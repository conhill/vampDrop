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
        }

        private void FixedUpdate()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "DropPuzzle") return;

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

            // ── Snapshot ball data ────────────────────────────────────────────
            var entities   = _ballQuery.ToEntityArray(Allocator.Temp);
            var transforms = _ballQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var trackers   = _ballQuery.ToComponentDataArray<RiceBallGateTracker>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity    = entities[i];
                float3 ballPos   = transforms[i].Position;
                int    hitMask   = trackers[i].HitGatesMask;
                bool   maskDirty = false;

                // ── Multiplier gates ──────────────────────────────────────────
                for (int g = 0; g < _multiplierGates.Length; g++)
                {
                    var gate = _multiplierGates[g];
                    if (gate == null || !gate.gameObject.activeInHierarchy) continue;

                    int gateBit = 1 << g;
                    if ((hitMask & gateBit) != 0) continue; // already hit

                    var col = gate.GetComponent<Collider>();
                    if (col == null) continue;

                    if (col.bounds.Contains((Vector3)ballPos))
                    {
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
                foreach (var gate in _goalGates)
                {
                    if (gate == null || !gate.gameObject.activeInHierarchy) continue;

                    var col = gate.GetComponent<Collider>();
                    if (col == null) continue;

                    if (col.bounds.Contains((Vector3)ballPos))
                    {
                        // Payout now scales with ball quality (Fine=1, Good=2, Excellent=5)
                        // instead of a flat +1 — so a gold ball actually pays off, and the
                        // popup shows the earned amount in the ball's colour.
                        var type   = _em.GetComponentData<RiceBallType>(entity);
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
                ecb.SetComponent(newBall, p.Type);
                ecb.SetComponent(newBall, p.Tracker);
                ecb.SetComponent(newBall, p.Lifetime);
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
