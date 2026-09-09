using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Hybrid system — bridges ECS balls with MonoBehaviour gates.
    ///
    /// SYNC-POINT FIX:
    ///   All structural ECS changes (CreateEntity, DestroyEntity, SetComponentData)
    ///   are now batched into an EntityCommandBuffer and played back ONCE at the end
    ///   of FixedUpdate. This eliminates the ~1-second hiccup that occurred when
    ///   multiple balls hit a multiplier gate in the same frame, each triggering
    ///   a direct CreateEntity() call that forced a full ECS world sync.
    /// </summary>
    public class RiceBallGateInteractionSystem : MonoBehaviour
    {
        private EntityQuery     _ballQuery;
        private EntityManager   _em;
        private MultiplierGate[] _multiplierGates;
        private GoalGate[]       _goalGates;

        // ── Per-gate caches (all parallel to the gate arrays above) ───────────
        // Profiling at 2000 balls put this component at ~4.0 ms/frame — 5.5x the ECS
        // ball-to-ball collision system — because GetComponent<Collider>() and the
        // active/null checks ran INSIDE the per-ball x per-gate double loop, i.e.
        // 2000 x gateCount times per physics step. Colliders are now resolved once in
        // RefreshGates(), and bounds/usability are recomputed once per FixedUpdate
        // (gateCount times), so the inner loop is pure struct math.
        //
        // Indices must stay aligned with _multiplierGates: the gate's identity bit is
        // 1 << g, so these are parallel arrays with a validity flag rather than a
        // compacted list of active gates.
        private Collider[] _multiplierColliders;
        private Bounds[]   _multiplierBounds;
        private bool[]     _multiplierUsable;
        private Collider[] _goalColliders;
        private Bounds[]   _goalBounds;
        private bool[]     _goalUsable;

        // Active-scene name comparison allocated a string every FixedUpdate. The scene
        // handle is a cheap int, so the name is only re-read when the scene changes.
        private int  _cachedSceneHandle = -1;
        private bool _isDropPuzzleScene;

        // ── Cached archetype so CreateEntity doesn't re-resolve types every call ──
        private EntityArchetype _ballArchetype;

        // ── Spawn SMOOTHING (not a cap) ───────────────────────────────────────
        // A multiplier hit enqueues every ball it should create; each FixedUpdate we
        // create up to SpawnsPerFrame of them and carry the rest to the next frame.
        // This spreads a huge multiply (e.g. 300 balls × two x5 gates = ~7,500 balls)
        // across a few frames so the world never hitches on a giant archetype resize —
        // and, unlike the old cap, NOT A SINGLE BALL IS LOST. Raise for snappier
        // population, lower if a mega-drop ever hitches.
        [Tooltip("Max multiplier balls created per physics step; the rest carry to the next frame (nothing is dropped).")]
        public int SpawnsPerFrame = 150;

        [Tooltip("Give balls minted by a multiplier gate their own colour (hot green, strongly " +
                 "emissive) instead of inheriting the parent ball's. A clone otherwise looks " +
                 "identical to its parent, so there is no way to see whether a gate is actually " +
                 "multiplying. Only RiceBallType.TypeID is changed — the ball is worth exactly " +
                 "the same at the goal gate.")]
        public bool TintMultipliedBalls = true;

        [Tooltip("Log a line every time a multiplier gate fires, with how many balls it queued " +
                 "and the current backlog. Cheap — one line per gate hit, not per ball.")]
        public bool LogMultiplierHits = false;

        private struct PendingSpawn
        {
            public float3 BasePos;
            public float Radius;
            public RiceBallPhysics PhysicsTemplate;
            public RiceBallType Type;
            public RiceBallGateTracker Tracker;   // already includes the gate's bit
            public RiceBallLifetime Lifetime;
            public int StackIndex;                // fan-out offset so spawns don't overlap
        }

        private readonly Queue<PendingSpawn> _pendingSpawns = new Queue<PendingSpawn>();

        /// <summary>Balls still waiting to be created from earlier multiplier hits.</summary>
        public int PendingSpawnCount => _pendingSpawns.Count;

        private void Start()
        {
            _em = World.DefaultGameObjectInjectionWorld.EntityManager;

            _ballQuery = _em.CreateEntityQuery(
                typeof(LocalTransform),
                typeof(RiceBallPhysics),
                typeof(RiceBallGateTracker),
                typeof(RiceBallType),
                typeof(RiceBallTag)
            );

            _ballArchetype = _em.CreateArchetype(
                typeof(LocalTransform),
                typeof(RiceBallPhysics),
                typeof(RiceBallTag),
                typeof(RiceBallType),
                typeof(RiceBallGateTracker),
                typeof(RiceBallLifetime)
            );

            RefreshGates();
            Debug.Log($"[GateInteraction] Found {_multiplierGates.Length} multiplier gates, {_goalGates.Length} goal gates");
        }

        public void RefreshGates()
        {
            _multiplierGates = FindObjectsByType<MultiplierGate>(FindObjectsSortMode.None);
            _goalGates       = FindObjectsByType<GoalGate>(FindObjectsSortMode.None);

            // Resolve each gate's Collider ONCE here instead of per ball per step.
            // Called by PuzzlePrefabLoader.RefreshGateSystem() whenever a board is
            // (re)built, so the cache tracks gate lifetime exactly as the gate arrays
            // themselves always have — a gate destroyed later reads back as null and is
            // skipped by the usability prepass, same as before.
            _multiplierColliders = new Collider[_multiplierGates.Length];
            _multiplierBounds    = new Bounds[_multiplierGates.Length];
            _multiplierUsable    = new bool[_multiplierGates.Length];
            for (int i = 0; i < _multiplierGates.Length; i++)
                _multiplierColliders[i] = _multiplierGates[i] != null
                    ? _multiplierGates[i].GetComponent<Collider>() : null;

            _goalColliders = new Collider[_goalGates.Length];
            _goalBounds    = new Bounds[_goalGates.Length];
            _goalUsable    = new bool[_goalGates.Length];
            for (int i = 0; i < _goalGates.Length; i++)
                _goalColliders[i] = _goalGates[i] != null
                    ? _goalGates[i].GetComponent<Collider>() : null;
        }

        private void FixedUpdate()
        {
            // Scene name is only re-read when the active scene actually changes —
            // GetActiveScene().name allocates a managed string on every call.
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.handle != _cachedSceneHandle)
            {
                _cachedSceneHandle = activeScene.handle;
                _isDropPuzzleScene = activeScene.name == "DropPuzzle";
            }
            if (!_isDropPuzzleScene) return;

            // ── ECB via EndFixedStepSimulationEntityCommandBufferSystem ───────
            // Unity batches the playback with other ECS structural changes at the
            // end of the fixed-step group — no manual Playback/Dispose needed.
            var ecbSystem = World.DefaultGameObjectInjectionWorld
                .GetOrCreateSystemManaged<EndFixedStepSimulationEntityCommandBufferSystem>();
            var ecb = ecbSystem.CreateCommandBuffer();

            // Create balls still queued from earlier multiplier hits FIRST, so the backlog
            // always drains even on a frame where every live ball has already scored.
            DrainPendingSpawns(ref ecb);

            if (_ballQuery == null || _ballQuery.IsEmpty) return;

            // ── Per-gate prepass ──────────────────────────────────────────────
            // Resolve active state and world bounds ONCE per physics step (gateCount
            // iterations) rather than once per ball per gate (ballCount x gateCount).
            // Collider.bounds is re-read here every step, so gates that move or get
            // toggled by the pre-drop roll are still handled correctly.
            int usableGates = 0;
            for (int g = 0; g < _multiplierGates.Length; g++)
            {
                var gate = _multiplierGates[g];
                var col  = _multiplierColliders[g];
                bool ok  = gate != null && col != null && gate.gameObject.activeInHierarchy;
                _multiplierUsable[g] = ok;
                if (ok) { _multiplierBounds[g] = col.bounds; usableGates++; }
            }
            for (int g = 0; g < _goalGates.Length; g++)
            {
                var gate = _goalGates[g];
                var col  = _goalColliders[g];
                bool ok  = gate != null && col != null && gate.gameObject.activeInHierarchy;
                _goalUsable[g] = ok;
                if (ok) { _goalBounds[g] = col.bounds; usableGates++; }
            }

            // Nothing to test against — skip the three full-array ECS copies entirely.
            // Each of those is a sync point, so this is the difference between a board
            // with no live gates costing ~nothing and costing a full snapshot per step.
            if (usableGates == 0) return;

            // ── Snapshot ball data ────────────────────────────────────────────
            // RiceBallType is batched in with the rest: it used to be fetched per
            // scoring ball via _em.GetComponentData, and every ball eventually scores.
            var entities   = _ballQuery.ToEntityArray(Allocator.Temp);
            var transforms = _ballQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var trackers   = _ballQuery.ToComponentDataArray<RiceBallGateTracker>(Allocator.Temp);
            var types      = _ballQuery.ToComponentDataArray<RiceBallType>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity    = entities[i];
                float3 ballPos   = transforms[i].Position;
                int    hitMask   = trackers[i].HitGatesMask;
                bool   maskDirty = false;

                // ── Multiplier gates ──────────────────────────────────────────
                for (int g = 0; g < _multiplierGates.Length; g++)
                {
                    if (!_multiplierUsable[g]) continue;

                    int gateBit = 1 << g;
                    if ((hitMask & gateBit) != 0) continue; // already hit

                    if (_multiplierBounds[g].Contains((Vector3)ballPos))
                    {
                        var gate = _multiplierGates[g];
                        // Set the gate's bit BEFORE queuing so every spawned ball inherits it —
                        // they can chain onto OTHER gates but can't fall back through THIS one
                        // and multiply forever.
                        hitMask  |= gateBit;
                        EnqueueMultipliedBalls(entity, gate, ballPos, hitMask);
                        // Juice: chunky "x2!" popup + camera punch at the gate, plus a
                        // gate flash via MultiplierGateVisual (listens to Hit).
                        DropPuzzleJuice.MultiplierPop(gate.transform.position, gate.Multiplier);
                        DropPuzzleSparks.Burst(gate.transform.position, GatePalette.Base(gate.Multiplier), 16);
                        gate.RaiseHit();
                        maskDirty = true;
                    }
                }

                // ── Goal gates ────────────────────────────────────────────────
                bool ballDestroyed = false;
                for (int g = 0; g < _goalGates.Length; g++)
                {
                    if (!_goalUsable[g]) continue;

                    if (_goalBounds[g].Contains((Vector3)ballPos))
                    {
                        // Payout now scales with ball quality (Fine=1, Good=2, Excellent=5)
                        // instead of a flat +1 — so a gold ball actually pays off, and the
                        // popup shows the earned amount in the ball's colour.
                        var type   = types[i]; // batched above, not a per-ball ECS read
                        int payout = Mathf.Max(1, Mathf.RoundToInt(type.PointsMultiplier));

                        if (PlayerDataManager.Instance != null)
                            PlayerDataManager.Instance.AddCurrency(payout, "Goal scored");

                        DropPuzzleJuice.ScorePop((Vector3)ballPos, payout, type.TypeID);
                        DropPuzzleSparks.Burst((Vector3)ballPos, RiceBallPalette.ForType(type.TypeID));

                        ecb.DestroyEntity(entity);
                        ballDestroyed = true;
                        break;
                    }
                }

                // Write updated tracker back via ECB (no per-entity sync point)
                if (!ballDestroyed && maskDirty)
                    ecb.SetComponent(entity, new RiceBallGateTracker { HitGatesMask = hitMask });
            }

            // ECB owned by EndFixedStepSimulationEntityCommandBufferSystem —
            // do NOT call ecb.Playback() or ecb.Dispose() here.

            entities.Dispose();
            transforms.Dispose();
            trackers.Dispose();
            types.Dispose();
        }

        /// <summary>
        /// Queue every extra ball this multiplier owes (Multiplier − 1). They're created
        /// over the next frame(s) by DrainPendingSpawns — nothing is dropped, even for a
        /// mass hit. <paramref name="inheritedMask"/> already includes this gate's bit.
        /// </summary>
        private void EnqueueMultipliedBalls(Entity original, MultiplierGate gate,
                                            float3 ballPos, int inheritedMask)
        {
            var physics      = _em.GetComponentData<RiceBallPhysics>(original);
            var origType     = _em.GetComponentData<RiceBallType>(original);
            var origLifetime = _em.GetComponentData<RiceBallLifetime>(original);
            var tracker      = new RiceBallGateTracker { HitGatesMask = inheritedMask };

            int extra = gate.Multiplier - 1;
            for (int i = 0; i < extra; i++)
            {
                _pendingSpawns.Enqueue(new PendingSpawn
                {
                    BasePos         = ballPos,
                    Radius          = physics.Radius,
                    PhysicsTemplate = physics,
                    Type            = origType,
                    Tracker         = tracker,
                    Lifetime        = origLifetime,
                    StackIndex      = i
                });
            }

            if (LogMultiplierHits)
                Debug.Log($"[GateInteraction] x{gate.Multiplier} gate hit — queued {extra} extra " +
                          $"ball(s); backlog now {_pendingSpawns.Count}.");
        }

        /// <summary>Create up to SpawnsPerFrame queued balls; the remainder waits for later frames.</summary>
        private void DrainPendingSpawns(ref EntityCommandBuffer ecb)
        {
            int budget = SpawnsPerFrame;
            while (budget-- > 0 && _pendingSpawns.Count > 0)
            {
                var p = _pendingSpawns.Dequeue();
                float r = p.Radius;

                float spread    = UnityEngine.Random.Range(-r * 3f, r * 3f);
                float3 spawnPos = p.BasePos + new float3(spread, r * 2.2f * (p.StackIndex + 1), 0f);

                Entity newBall = ecb.CreateEntity(_ballArchetype);
                ecb.SetComponent(newBall, LocalTransform.FromPositionRotationScale(
                    spawnPos, quaternion.identity, r * 2f));
                ecb.SetComponent(newBall, new RiceBallPhysics
                {
                    Position               = spawnPos,
                    Velocity               = new float3(spread * 0.5f, 0f, 0f),
                    Radius                 = r,
                    Mass                   = p.PhysicsTemplate.Mass,
                    Bounciness             = p.PhysicsTemplate.Bounciness,
                    Friction               = p.PhysicsTemplate.Friction,
                    IsSleeping             = false,
                    SleepVelocityThreshold = 0.015f
                });
                // Colour gate-minted balls differently so the multiplier is visible in motion.
                // Only TypeID changes — PointsMultiplier and the rest of the struct carry over,
                // so what the ball is WORTH at the goal gate is untouched.
                var type = p.Type;
                if (TintMultipliedBalls) type.TypeID = RiceBallPalette.MultipliedTypeId;
                ecb.SetComponent(newBall, type);

                ecb.SetComponent(newBall, p.Tracker);

                // Lifetime restarts from now. Inheriting the parent's SpawnTime meant a clone
                // minted 20s into a drop was born 20s old against MaxLifetime 30 and got culled
                // after 10s — which reads exactly like "the gate isn't spawning anything".
                var lifetime = p.Lifetime;
                lifetime.SpawnTime = Time.time;
                ecb.SetComponent(newBall, lifetime);
            }
        }

        private void OnDestroy()
        {
            // On play-mode exit the ECS World can be torn down before this runs, which
            // makes disposing the query throw. Only dispose while the world is still alive.
            var world = World.DefaultGameObjectInjectionWorld;
            if (_ballQuery != default && world != null && world.IsCreated)
                _ballQuery.Dispose();
        }
    }
}
