using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
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
    }

    /// <summary>
    /// MonoBehaviour responsible ONLY for wall discovery and caching.
    /// Per-frame collision is handled by RiceBallWallCollisionECSSystem below.
    /// </summary>
    public class RiceBallWallCollisionSystem : MonoBehaviour
    {
        private void Start()
        {
            // Wait a frame for walls to spawn, then cache them
            Invoke(nameof(CacheWalls), 0.5f);
        }

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
                        Bounciness = 0.3f
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
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RiceBallPhysicsSystem))]
    public partial struct RiceBallWallCollisionECSSystem : ISystem
    {
        private NativeArray<WallData> _walls;
        public  bool WallsReady;

        /// <summary>Called once by RiceBallWallCollisionSystem.CacheWalls().</summary>
        public void SetWalls(NativeArray<WallData> walls)
        {
            if (_walls.IsCreated) _walls.Dispose();
            _walls     = new NativeArray<WallData>(walls, Allocator.Persistent);
            WallsReady = true;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RiceBallTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!WallsReady || _walls.Length == 0) return;

            // Pass deltaTime for speculative CCD (tunnelling prevention)
            float dt = math.min(SystemAPI.Time.DeltaTime, 0.033f);
            state.Dependency = new WallCollisionJob { Walls = _walls, DeltaTime = dt }
                .ScheduleParallel(state.Dependency);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_walls.IsCreated) _walls.Dispose();
        }
    }

    [BurstCompile]
    [WithAll(typeof(RiceBallTag))]
    partial struct WallCollisionJob : IJobEntity
    {
        [ReadOnly] public NativeArray<WallData> Walls;
        public float DeltaTime; // needed for speculative CCD

        void Execute(ref RiceBallPhysics physics)
        {
            if (physics.IsSleeping) return;

            float3 ballPos    = physics.Position;
            float  ballRadius = physics.Radius;
            // Predict where the ball will be next frame — used for speculative tunnelling check
            float3 predictedPos = ballPos + physics.Velocity * DeltaTime;

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
                    physics.Velocity -= worldNormal * normalVelocity * (1f + wall.Bounciness);
                    float3 tangentVelocity = physics.Velocity
                        - worldNormal * math.dot(physics.Velocity, worldNormal);
                    physics.Velocity -= tangentVelocity * 0.02f * physics.Friction;
                }

                physics.IsSleeping = false;
            }
        }
    }
}
