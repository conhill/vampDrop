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

            // ScheduleParallel — each entity is processed by exactly one worker thread,
            // no write races. Walls array is read-only.
            state.Dependency = new WallCollisionJob { Walls = _walls }
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

        void Execute(ref RiceBallPhysics physics)
        {
            if (physics.IsSleeping) return;

            float3 ballPos    = physics.Position;
            float  ballRadius = physics.Radius;

            for (int i = 0; i < Walls.Length; i++)
            {
                WallData wall = Walls[i];

                float3 localBallPos = math.mul(math.inverse(wall.Rotation), ballPos - wall.Center);
                float3 halfSize     = wall.Size * 0.5f;
                float3 localClosest = math.clamp(localBallPos, -halfSize, halfSize);
                float3 localDelta   = localBallPos - localClosest;
                float  distance     = math.length(localDelta);

                if (distance < ballRadius)
                {
                    float3 localNormal = (distance > 0.0001f)
                        ? (localDelta / distance)
                        : new float3(0, 1, 0);
                    float3 worldNormal = math.mul(wall.Rotation, localNormal);
                    float  overlap     = ballRadius - distance;

                    physics.Position += worldNormal * overlap * 1.01f;
                    ballPos           = physics.Position;

                    float normalVelocity = math.dot(physics.Velocity, worldNormal);
                    if (normalVelocity < 0)
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
}
