using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// ECS Component for rice ball physics data
    /// </summary>
    public struct RiceBallPhysics : IComponentData
    {
        public float3 Velocity;
        public float3 Position;
        public float Radius;
        public float Mass;
        public float Bounciness;
        public float Friction;
        public bool IsSleeping;
        public float SleepVelocityThreshold;
    }

    /// <summary>
    /// NEW: Ball type for special abilities (upgrades, perks, etc.)
    /// </summary>
    public struct RiceBallType : IComponentData
    {
        public int TypeID; // 0=Standard, 1=BonusPoints, 2=DoubleMultiplier, 3=Harmful, etc.
        public float PointsMultiplier; // 1.0=normal, 2.0=double points, etc.
        public float MultiplierBoost; // 0=normal, 1.0=+1 to gate multipliers, etc.
        public bool IsHarmful; // True for negative effects
    }

    /// <summary>
    /// Tag component to identify rice balls
    /// </summary>
    public struct RiceBallTag : IComponentData { }

    /// <summary>
    /// Track which gates this ball has hit (bitmask up to 32 gates)
    /// </summary>
    public struct RiceBallGateTracker : IComponentData
    {
        public int HitGatesMask;
    }

    /// <summary>
    /// Lifetime tracking
    /// </summary>
    public struct RiceBallLifetime : IComponentData
    {
        public float SpawnTime;
        public float MaxLifetime;
        public float DestroyBelowY;
    }

    /// <summary>
    /// Simple ECS physics simulation - MUCH faster than Unity physics
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct RiceBallPhysicsSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RiceBallTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = math.min(SystemAPI.Time.DeltaTime, 0.033f);
            float3 gravity = new float3(0, -5f, 0);

            foreach (var (physics, transform) in
                SystemAPI.Query<RefRW<RiceBallPhysics>, RefRW<LocalTransform>>()
                .WithAll<RiceBallTag>())
            {
                if (physics.ValueRO.IsSleeping) continue;

                physics.ValueRW.Velocity += gravity * deltaTime;
                physics.ValueRW.Velocity *= (1f - physics.ValueRO.Friction * deltaTime);

                float velocityMag = math.length(physics.ValueRW.Velocity);
                if (velocityMag > 15f)
                    physics.ValueRW.Velocity = math.normalize(physics.ValueRW.Velocity) * 15f;

                float3 newPosition = physics.ValueRO.Position + physics.ValueRW.Velocity * deltaTime;

                // NaN protection — reset silently, no managed call
                if (math.isnan(newPosition.x) || math.isnan(newPosition.y) || math.isnan(newPosition.z))
                {
                    newPosition = new float3(0, 10, 0);
                    physics.ValueRW.Velocity = float3.zero;
                }

                // Sleep balls that fall below kill zone (large enough for big puzzle layouts)
                if (newPosition.y < -200f)
                {
                    physics.ValueRW.Velocity = float3.zero;
                    physics.ValueRW.IsSleeping = true;
                    continue;
                }

                physics.ValueRW.Position = newPosition;

                // Cosmetic spin: roll about Z (screen-facing axis) proportional to
                // horizontal speed, so balls visibly tumble instead of sliding.
                float spinRadius = physics.ValueRO.Radius;
                if (spinRadius > 0.001f)
                {
                    float angularSpeed = -physics.ValueRO.Velocity.x / spinRadius; // rad/sec
                    transform.ValueRW.Rotation = math.mul(
                        transform.ValueRO.Rotation,
                        quaternion.RotateZ(angularSpeed * deltaTime));
                }

                float velocityMagnitude = math.length(physics.ValueRO.Velocity);
                if (velocityMagnitude < 0.015f && math.abs(physics.ValueRO.Velocity.y) < 0.03f)
                {
                    physics.ValueRW.IsSleeping = true;
                    physics.ValueRW.Velocity = float3.zero;
                }
            }
        }
    }

    /// <summary>
    /// Delete balls that fell below the kill zone (Y < -10)
    /// Runs after physics to remove fallen balls
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RiceBallPhysicsSystem))]
    public partial struct RiceBallDeletionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RiceBallTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (physics, entity) in
                SystemAPI.Query<RefRO<RiceBallPhysics>>()
                .WithAll<RiceBallTag>()
                .WithEntityAccess())
            {
                if (physics.ValueRO.Position.y < -200f)
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    /// <summary>
    /// FAST ball-to-ball collision using spatial hash grid - O(n) instead of O(n²)!
    /// TEMPORARILY DISABLED FOR PERFORMANCE TESTING
    /// </summary>
    // [BurstCompile]
    // [UpdateInGroup(typeof(SimulationSystemGroup))]
    // [UpdateAfter(typeof(RiceBallPhysicsSystem))]
    public partial struct RiceBallCollisionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float cellSize = 0.75f;
            NativeParallelMultiHashMap<int, int> spatialHash = new NativeParallelMultiHashMap<int, int>(1000, Allocator.Temp);

            NativeList<float3> positions = new NativeList<float3>(Allocator.Temp);
            NativeList<float3> velocities = new NativeList<float3>(Allocator.Temp);
            NativeList<float> radii = new NativeList<float>(Allocator.Temp);
            NativeList<bool> sleeping = new NativeList<bool>(Allocator.Temp);

            int ballIndex = 0;
            foreach (var physics in SystemAPI.Query<RefRO<RiceBallPhysics>>().WithAll<RiceBallTag>())
            {
                positions.Add(physics.ValueRO.Position);
                velocities.Add(physics.ValueRO.Velocity);
                radii.Add(physics.ValueRO.Radius);
                sleeping.Add(physics.ValueRO.IsSleeping);

                if (!physics.ValueRO.IsSleeping)
                {
                    int cellX = (int)math.floor(physics.ValueRO.Position.x / cellSize);
                    int cellY = (int)math.floor(physics.ValueRO.Position.y / cellSize);
                    int cellKey = cellX + cellY * 10000;
                    spatialHash.Add(cellKey, ballIndex);
                }
                ballIndex++;
            }

            int idx = 0;
            foreach (var physics in SystemAPI.Query<RefRW<RiceBallPhysics>>().WithAll<RiceBallTag>())
            {
                if (physics.ValueRO.IsSleeping) { idx++; continue; }

                float3 pos = positions[idx];
                float radius = radii[idx];

                int cellX = (int)math.floor(pos.x / cellSize);
                int cellY = (int)math.floor(pos.y / cellSize);

                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        int checkKey = (cellX + offsetX) + (cellY + offsetY) * 10000;

                        if (spatialHash.TryGetFirstValue(checkKey, out int otherIdx, out var iterator))
                        {
                            do
                            {
                                if (otherIdx == idx) continue;

                                float3 otherPos = positions[otherIdx];
                                float otherRadius = radii[otherIdx];
                                float3 delta = pos - otherPos;
                                float distance = math.length(delta);
                                float minDistance = radius + otherRadius;

                                if (distance < minDistance && distance > 0.001f)
                                {
                                    float3 pushDir = (distance > 0.0001f) ? (delta / distance) : new float3(0, 1, 0);
                                    float overlap = minDistance - distance;
                                    float safePush = math.min(overlap, radius * 0.2f);
                                    float3 separationForce = pushDir * safePush * 1.01f;

                                    if (math.abs(delta.y) > 0.1f)
                                        separationForce.y += safePush * 0.2f;

                                    physics.ValueRW.Position += separationForce;

                                    float velAlongNormal = math.dot(physics.ValueRO.Velocity, pushDir);
                                    if (velAlongNormal < 0)
                                        physics.ValueRW.Velocity -= pushDir * velAlongNormal * 0.5f;

                                    physics.ValueRW.IsSleeping = false;
                                }
                            }
                            while (spatialHash.TryGetNextValue(out otherIdx, ref iterator));
                        }
                    }
                }

                idx++;
            }

            spatialHash.Dispose();
            positions.Dispose();
            velocities.Dispose();
            radii.Dispose();
            sleeping.Dispose();
        }
    }

    /// <summary>
    /// Cleanup old balls by lifetime
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial struct RiceBallCleanupSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RiceBallTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float currentTime = (float)SystemAPI.Time.ElapsedTime;
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (lifetime, entity) in
                SystemAPI.Query<RefRO<RiceBallLifetime>>()
                .WithAll<RiceBallTag>()
                .WithEntityAccess())
            {
                if (currentTime - lifetime.ValueRO.SpawnTime > lifetime.ValueRO.MaxLifetime)
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    /// <summary>
    /// Dedicated system to sync physics Position → LocalTransform.Position
    /// Runs at the very end to prevent double-writing and renderer glitches
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct SyncBallTransformSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RiceBallTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (physics, transform) in
                SystemAPI.Query<RefRO<RiceBallPhysics>, RefRW<LocalTransform>>().WithAll<RiceBallTag>())
            {
                transform.ValueRW.Position = physics.ValueRO.Position;
            }
        }
    }
}
