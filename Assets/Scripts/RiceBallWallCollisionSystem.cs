using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    // Namespace-scoped so both the MonoBehaviour and the ISystem can reference it
    // (was a private nested struct — moving it out is the only structural change needed)
    public struct WallData
    {
        public float3 Center;
        public float3 Size;
        public quaternion Rotation;
        public float Bounciness;

        /// <summary>
        /// Which BallImpactSurface this collider sounds like. Resolved once on the main
        /// thread in CacheWalls() (Burst jobs cannot touch tags / components / strings),
        /// then carried into the job as a plain int so an impact event can report *what*
        /// was hit without any managed data crossing the job boundary.
        /// </summary>
        public int SurfaceType;
    }

    /// <summary>
    /// A single ball-vs-obstacle impact, produced inside the Burst collision job and
    /// consumed on the main thread by RiceBallImpactAudio.
    ///
    /// Deliberately blittable and tiny (32 bytes) — hundreds of these can be written per
    /// frame by parallel worker threads with nothing but a lock-free block append.
    /// </summary>
    public struct BallImpactEvent
    {
        public float3 Position;    // world-space contact point on the obstacle surface
        public float  Speed;       // approach speed along the surface normal, m/s (always positive)
        public int    SurfaceType; // WallData.SurfaceType → BallImpactSurface enum
        public int    BallTypeId;  // RiceBallType.TypeID → RiceBallPalette colour
    }

    /// <summary>
    /// MonoBehaviour responsible ONLY for wall discovery and caching.
    /// Per-frame collision is handled by RiceBallWallCollisionECSSystem below.
    /// </summary>
    public class RiceBallWallCollisionSystem : MonoBehaviour
    {
        private void Start()
        {
            // Bootstrap the impact-feedback hub. This is the one MonoBehaviour guaranteed to
            // exist wherever ball-vs-obstacle collision runs, so it is the natural owner of
            // that bootstrap — the feature therefore needs no scene wiring at all. Touching
            // Instance returns an existing authored component if the scene has one, and
            // otherwise spawns a scene-scoped default (which dies with the scene).
            _ = RiceBallImpactAudio.Instance;

            // Wait a frame for walls to spawn, then cache them
            Invoke(nameof(CacheWalls), 0.5f);
        }

        /// <summary>Call after instantiating a new puzzle to rebuild the wall cache.</summary>
        public void RefreshWalls() => Invoke(nameof(CacheWalls), 0.1f);

        private void CacheWalls()
        {
            GameObject[] wallObjects = GameObject.FindGameObjectsWithTag("Wall");

            NativeList<WallData> wallList = new NativeList<WallData>(Allocator.Temp);

            foreach (GameObject wallObj in wallObjects)
            {
                BoxCollider boxCol = wallObj.GetComponent<BoxCollider>();
                if (boxCol != null)
                {
                    Vector3 localSize = boxCol.size;
                    Vector3 scale = wallObj.transform.lossyScale;
                    float3 actualSize = new float3(
                        localSize.x * scale.x,
                        localSize.y * scale.y,
                        localSize.z * scale.z
                    );

                    wallList.Add(new WallData
                    {
                        Center    = boxCol.bounds.center,
                        Size      = actualSize,
                        Rotation  = wallObj.transform.rotation,
                        Bounciness = 0.3f,
                        // Resolved here (main thread, once per cache) because it needs
                        // GetComponent / name lookups that are illegal inside a Burst job.
                        SurfaceType = (int)ImpactSurfaceTag.Resolve(wallObj)
                    });
                }
            }

            // Hand the wall data to the ECS system; it owns the persistent array from here.
            var world  = World.DefaultGameObjectInjectionWorld;
            var handle = world.GetOrCreateSystem<RiceBallWallCollisionECSSystem>();
            ref var sys = ref world.Unmanaged.GetUnsafeSystemRef<RiceBallWallCollisionECSSystem>(handle);
            sys.SetWalls(wallList.AsArray());

            wallList.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ECS system — Burst-compiled, parallel IJobEntity, no per-frame managed allocs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the per-frame MonoBehaviour loop that called ToEntityArray/
    /// ToComponentDataArray (sync point + GC pressure) with a Burst-compiled
    /// parallel IJobEntity that operates directly on chunk memory.
    ///
    /// It also produces impact events for RiceBallImpactAudio. That handoff is
    /// DOUBLE-BUFFERED: the job writes into one NativeQueue while the main thread drains
    /// the queue that was filled last frame. The drain therefore never has to Complete()
    /// a running job, so the audio feature adds zero sync points to the frame.
    /// See the ping-pong block in OnUpdate.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RiceBallPhysicsSystem))]
    public partial struct RiceBallWallCollisionECSSystem : ISystem
    {
        private NativeArray<WallData> _walls;
        public  bool WallsReady;

        // ── Impact-event handoff ──────────────────────────────────────────────
        // Two queues, ping-ponged each frame. _writeIsA says which one this frame's job
        // is filling; the other holds last frame's finished events and is safe for the
        // main thread to read.
        private NativeQueue<BallImpactEvent> _impactsA;
        private NativeQueue<BallImpactEvent> _impactsB;
        // A third, never-read queue used purely as the job's writer target while capture is
        // off. The job struct must always hold a *constructed* container (the safety system
        // rejects a default one), but pointing it at A or B while disabled would leave stale
        // write-dependencies on the buffers the main thread Clear()s. This keeps them clean.
        private NativeQueue<BallImpactEvent> _impactsIdle;
        private bool      _writeIsA;
        private JobHandle _lastImpactJob;

        private EntityQuery _ballQuery;
        private ComponentLookup<RiceBallType> _ballTypeLookup;
        private int _frameSalt;

        // Config pushed in from RiceBallImpactAudio (managed side). Defaults are "off" so
        // the feature genuinely costs nothing until something explicitly turns it on.
        private bool  _captureImpacts;
        private float _minImpactSpeed;
        private int   _targetEventsPerFrame;

        /// <summary>Called once by RiceBallWallCollisionSystem.CacheWalls().</summary>
        public void SetWalls(NativeArray<WallData> walls)
        {
            if (_walls.IsCreated) _walls.Dispose();
            _walls     = new NativeArray<WallData>(walls, Allocator.Persistent);
            WallsReady = true;
        }

        /// <summary>
        /// Called from RiceBallImpactAudio (managed) to arm/disarm impact capture and push
        /// its tuning values in. When <paramref name="enabled"/> is false the job skips the
        /// entire event path — one perfectly-predicted bool test per resolved bounce.
        /// </summary>
        public void ConfigureImpactCapture(bool enabled, float minImpactSpeed, int targetEventsPerFrame)
        {
            _captureImpacts       = enabled;
            _minImpactSpeed       = math.max(0.01f, minImpactSpeed);
            _targetEventsPerFrame = math.max(1, targetEventsPerFrame);
        }

        /// <summary>
        /// The queue holding LAST frame's impact events. Its producing job was completed at
        /// the top of this frame's OnUpdate, so the main thread may drain it freely.
        /// Returns a default (uncreated) queue if the system has not initialised yet.
        /// </summary>
        public NativeQueue<BallImpactEvent> GetReadableImpacts()
        {
            if (!_impactsA.IsCreated || !_impactsB.IsCreated) return default;
            return _writeIsA ? _impactsB : _impactsA;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RiceBallTag>();

            _impactsA    = new NativeQueue<BallImpactEvent>(Allocator.Persistent);
            _impactsB    = new NativeQueue<BallImpactEvent>(Allocator.Persistent);
            _impactsIdle = new NativeQueue<BallImpactEvent>(Allocator.Persistent);
            _writeIsA    = true;

            _ballQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RiceBallTag>()
                .Build(ref state);

            _ballTypeLookup = state.GetComponentLookup<RiceBallType>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!WallsReady || _walls.Length == 0) return;

            // Pass deltaTime for speculative CCD (tunnelling prevention)
            float dt = math.min(SystemAPI.Time.DeltaTime, 0.033f);

            // Refreshed every frame whether or not capture is on — a ComponentLookup handed
            // to a job must always be the current frame's version.
            _ballTypeLookup.Update(ref state);

            var job = new WallCollisionJob
            {
                Walls     = _walls,
                DeltaTime = dt,
                Capture   = false,
                Stride    = 1
            };

            if (_captureImpacts)
            {
                // Last frame's writer job is guaranteed finished by now (a whole frame plus
                // the end-of-SimulationSystemGroup ECB barrier has elapsed), so this
                // Complete() is a no-op in practice. It exists so correctness does not
                // *depend* on that assumption.
                _lastImpactJob.Complete();

                // Ping-pong: this frame writes into the buffer the main thread just finished.
                _writeIsA = !_writeIsA;
                var write = _writeIsA ? _impactsA : _impactsB;

                // Drop-don't-queue: anything the drainer left behind is thrown away here,
                // so a backlog can never build up across frames.
                write.Clear();

                // Event thinning. A mega-drop puts thousands of balls in simultaneous
                // contact; enqueueing every one is pointless (we can only *play* a handful)
                // and would balloon the queue's block memory. Stride subsamples the ball set
                // so the expected event count stays near _targetEventsPerFrame no matter how
                // many balls are in flight — this is what makes the handoff O(budget) rather
                // than O(balls). _frameSalt rotates *which* balls are eligible each frame so
                // no individual ball is ever permanently muted.
                int ballCount = _ballQuery.CalculateEntityCount();
                int stride    = math.max(1, ballCount / _targetEventsPerFrame);

                _frameSalt = (_frameSalt + 1) & 0x03FFFFFF;

                job.Capture        = true;
                job.MinImpactSpeed = _minImpactSpeed;
                job.Stride         = stride;
                job.FrameSalt      = _frameSalt;
                job.BallTypes      = _ballTypeLookup;
                job.Impacts        = write.AsParallelWriter();
            }
            else
            {
                // Disabled: the fields still need valid handles so the job struct is
                // well-formed, but Capture == false means neither is ever touched — the
                // idle queue therefore stays empty forever and costs one allocation total.
                job.BallTypes = _ballTypeLookup;
                job.Impacts   = _impactsIdle.AsParallelWriter();
            }

            state.Dependency = job.ScheduleParallel(state.Dependency);

            // Stored unconditionally: state.Dependency chains, so the newest handle already
            // implies every job this system scheduled before it. Completing it in OnDestroy
            // therefore covers the idle queue as well as A/B.
            _lastImpactJob = state.Dependency;
        }

        public void OnDestroy(ref SystemState state)
        {
            // The writer job must finish before the container it writes into goes away.
            _lastImpactJob.Complete();

            if (_walls.IsCreated)       _walls.Dispose();
            if (_impactsA.IsCreated)    _impactsA.Dispose();
            if (_impactsB.IsCreated)    _impactsB.Dispose();
            if (_impactsIdle.IsCreated) _impactsIdle.Dispose();
        }
    }

    [BurstCompile]
    [WithAll(typeof(RiceBallTag))]
    partial struct WallCollisionJob : IJobEntity
    {
        [ReadOnly] public NativeArray<WallData> Walls;
        public float DeltaTime; // needed for speculative CCD

        // ── Impact reporting (entirely inert when Capture == false) ───────────
        public bool  Capture;
        public float MinImpactSpeed;  // grazes slower than this never make a sound
        public int   Stride;          // 1 = every ball is eligible, N = every Nth
        public int   FrameSalt;       // rotates which balls are eligible each frame

        [ReadOnly] public ComponentLookup<RiceBallType> BallTypes;

        // ParallelWriter is atomic-write-only: each worker thread appends into its own
        // block, so there is no cross-thread contention even with thousands of balls.
        public NativeQueue<BallImpactEvent>.ParallelWriter Impacts;

        void Execute(Entity entity, ref RiceBallPhysics physics)
        {
            if (physics.IsSleeping) return;

            float3 ballPos    = physics.Position;
            float  ballRadius = physics.Radius;
            // Predict where the ball will be next frame — used for speculative tunnelling check
            float3 predictedPos = ballPos + physics.Velocity * DeltaTime;

            // Eligibility is decided ONCE, up front, so the hot collision loop below pays at
            // most one already-loaded bool test. A ball is ineligible when capture is off or
            // when the stride subsample skips it this frame.
            bool eligible = Capture && ((entity.Index + FrameSalt) % Stride == 0);

            // A ball can resolve against several walls in one Execute (corners, tight
            // channels, stacked pegs). We keep only the hardest contact and emit ONE event —
            // the cheapest and most effective coalescing available, done at the source.
            float  bestSpeed   = 0f;
            float3 bestPos     = ballPos;
            int    bestSurface = 0;

            for (int i = 0; i < Walls.Length; i++)
            {
                WallData    wall   = Walls[i];
                quaternion  invRot = math.inverse(wall.Rotation);
                float3      halfSize = wall.Size * 0.5f;

                // ── Current-frame overlap ─────────────────────────────────────
                float3 localPos     = math.mul(invRot, ballPos - wall.Center);
                float3 localClosest = math.clamp(localPos, -halfSize, halfSize);
                float3 localDelta   = localPos - localClosest;
                float  distance     = math.length(localDelta);
                bool   overlapNow   = distance < ballRadius;

                // ── Speculative next-frame check (tunnelling prevention) ───────
                // If the ball will penetrate the wall next frame AND is approaching,
                // resolve it now before it has a chance to pass through.
                float3 localPredicted   = math.mul(invRot, predictedPos - wall.Center);
                float3 predClosest      = math.clamp(localPredicted, -halfSize, halfSize);
                float3 predDelta        = localPredicted - predClosest;
                float  predDist         = math.length(predDelta);

                // Compute the surface normal from whichever sample gives a cleaner direction
                float3 rawNormalLocal = (distance > 0.0001f) ? (localDelta / distance) : new float3(0, 1, 0);
                float3 worldNormal    = math.mul(wall.Rotation, rawNormalLocal);
                float  normalVelocity = math.dot(physics.Velocity, worldNormal);

                bool willTunnel = !overlapNow && predDist < ballRadius && normalVelocity < 0f;

                if (!overlapNow && !willTunnel) continue;

                // ── Resolve ───────────────────────────────────────────────────
                if (overlapNow)
                {
                    // Push ball fully out of wall
                    float overlap = ballRadius - distance;
                    physics.Position += worldNormal * overlap * 1.01f;
                    ballPos           = physics.Position;
                    predictedPos      = ballPos + physics.Velocity * DeltaTime;
                }

                // Reflect velocity off wall surface
                if (normalVelocity < 0f)
                {
                    // ── Impact reporting hook ─────────────────────────────────
                    // normalVelocity is negative while approaching, so -normalVelocity is the
                    // closing speed — exactly the quantity that should drive how loud and how
                    // bright the hit reads. Record the hardest contact of the frame.
                    if (eligible)
                    {
                        float approach = -normalVelocity;
                        if (approach > bestSpeed)
                        {
                            bestSpeed   = approach;
                            // Put the event on the obstacle surface rather than at the ball
                            // centre, so sparks spawn where the eye expects the hit.
                            bestPos     = ballPos - worldNormal * ballRadius;
                            bestSurface = wall.SurfaceType;
                        }
                    }

                    physics.Velocity -= worldNormal * normalVelocity * (1f + wall.Bounciness);
                    float3 tangentVelocity = physics.Velocity
                        - worldNormal * math.dot(physics.Velocity, worldNormal);
                    physics.Velocity -= tangentVelocity * 0.02f * physics.Friction;
                }

                physics.IsSleeping = false;
            }

            // Exactly one enqueue per ball per frame, and only for hits worth hearing.
            if (eligible && bestSpeed >= MinImpactSpeed)
            {
                int ballTypeId = BallTypes.TryGetComponent(entity, out RiceBallType t) ? t.TypeID : 0;
                Impacts.Enqueue(new BallImpactEvent
                {
                    Position    = bestPos,
                    Speed       = bestSpeed,
                    SurfaceType = bestSurface,
                    BallTypeId  = ballTypeId
                });
            }
        }
    }
}
