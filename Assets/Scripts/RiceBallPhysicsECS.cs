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
        public void OnUpdate(ref SystemState state)
        {
            // Only run in DropPuzzle scene
            #if !UNITY_EDITOR && !UNITY_STANDALONE
            return;
            #endif
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene != "DropPuzzle") return;
            
            // FIX #3: Cap deltaTime to prevent huge time steps on lag spikes
            float deltaTime = math.min(SystemAPI.Time.DeltaTime, 0.033f); // Max 30fps worth
            float3 gravity = new float3(0, -5f, 0);
            
            int ballCount = 0;
            int sleepingCount = 0;
            int movedCount = 0;
            
            // Track first 3 ball positions for debugging
            NativeList<float3> firstPositions = new NativeList<float3>(3, Unity.Collections.Allocator.Temp);
            
            // Update all ball physics
            foreach (var (physics, transform) in 
                SystemAPI.Query<RefRW<RiceBallPhysics>, RefRW<LocalTransform>>()
                .WithAll<RiceBallTag>())
            {
                ballCount++;
                
                // Log first 3 positions
                if (firstPositions.Length < 3)
                {
                    firstPositions.Add(physics.ValueRO.Position);
                }
                
                // Skip if sleeping
                if (physics.ValueRO.IsSleeping)
                {
                    sleepingCount++;
                    continue;
                }
                
                movedCount++;
                
                // Apply gravity
                physics.ValueRW.Velocity += gravity * deltaTime;
                
                // Apply friction (air resistance)
                physics.ValueRW.Velocity *= (1f - physics.ValueRO.Friction * deltaTime);
                
                // CLAMP MAX VELOCITY - Prevents tunneling through walls at extreme speeds
                float velocityMag = math.length(physics.ValueRW.Velocity);
                if (velocityMag > 15f) // Terminal velocity to prevent tunneling
                {
                    physics.ValueRW.Velocity = math.normalize(physics.ValueRW.Velocity) * 15f;
                }
                
                // Update position
                float3 newPosition = physics.ValueRO.Position + physics.ValueRW.Velocity * deltaTime;
                
                // FIX #2: Infinity protection - catch balls that glitched out
                if (math.abs(newPosition.x) > 50f || math.abs(newPosition.y) > 50f || 
                    math.isnan(newPosition.x) || math.isnan(newPosition.y) || math.isnan(newPosition.z))
                {
                    // Ball glitched to infinity - reset to top or destroy
                    UnityEngine.Debug.LogWarning($"[PhysicsSystem] Ball at infinity! Pos:{newPosition} Vel:{physics.ValueRO.Velocity}");
                    newPosition = new float3(0, 10, 0);
                    physics.ValueRW.Velocity = float3.zero;
                }
                
                // FIX #6: X-axis guardrails - hard clamp within play area
                // Adjust 8f to match your actual puzzle width
                if (newPosition.x > 8f) newPosition.x = 8f;
                if (newPosition.x < -8f) newPosition.x = -8f;
                
                // Final NaN check before rendering
                if (math.any(math.isnan(newPosition)))
                {
                    newPosition = physics.ValueRO.Position; // Revert to last known good
                }
                
                // KILL ZONE: Delete balls that fall below Y=-10 (they fell off the puzzle)
                if (newPosition.y < -10f)
                {
                    // Mark for deletion by EntityManager (can't delete during query iteration)
                    physics.ValueRW.Velocity = float3.zero;
                    physics.ValueRW.IsSleeping = true;
                    // Entity will be cleaned up by separate deletion system
                    continue; // Skip transform update for deleted balls
                }
                
                // FIX #7: Don't update transform here - let SyncBallTransformSystem do it
                // This prevents double-writing and renderer seeing ball in two places
                physics.ValueRW.Position = newPosition;
                
                // Check if should sleep (performance optimization)
                // BUT: Don't sleep if ball has significant downward potential (on a slope)
                float velocityMagnitude = math.length(physics.ValueRO.Velocity);
                
                // AGGRESSIVE SLEEP: Very low threshold for high ball counts
                if (velocityMagnitude < 0.015f && math.abs(physics.ValueRO.Velocity.y) < 0.03f)
                {
                    physics.ValueRW.IsSleeping = true;
                    physics.ValueRW.Velocity = float3.zero;
                }
            }
            
            // Debug: Log ball count periodically
            if (state.WorldUnmanaged.Time.ElapsedTime % 5.0 < deltaTime)
            {
                if (ballCount > 0)
                {
                    float activePercent = (movedCount / (float)ballCount) * 100f;
                    UnityEngine.Debug.Log($"[PhysicsSystem] Total:{ballCount} Moving:{movedCount} ({activePercent:F0}%) Sleeping:{sleepingCount} | First 3 Pos: {(firstPositions.Length > 0 ? $"({firstPositions[0].x:F2},{firstPositions[0].y:F2})" : "none")}{(firstPositions.Length > 1 ? $" ({firstPositions[1].x:F2},{firstPositions[1].y:F2})" : "")}{(firstPositions.Length > 2 ? $" ({firstPositions[2].x:F2},{firstPositions[2].y:F2})" : "")}");
                }
                else
                {
                    UnityEngine.Debug.LogWarning("[PhysicsSystem] ❌ NO BALLS FOUND IN QUERY!");
                }
            }
            
            firstPositions.Dispose();
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
        public void OnUpdate(ref SystemState state)
        {
            // Only run in DropPuzzle scene
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene != "DropPuzzle") return;
            
            var ecb = new Unity.Entities.EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            
            int deletedCount = 0;
            
            foreach (var (physics, transform, entity) in
                SystemAPI.Query<RefRO<RiceBallPhysics>, RefRO<LocalTransform>>()
                .WithAll<RiceBallTag>()
                .WithEntityAccess())
            {
                if (transform.ValueRO.Position.y < -10f)
                {
                    ecb.DestroyEntity(entity);
                    deletedCount++;
                }
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            
            if (deletedCount > 0)
            {
                UnityEngine.Debug.Log($"[BallDeletion] ♻️ Deleted {deletedCount} balls below Y=-10");
            }
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
            // Only run in DropPuzzle scene
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene != "DropPuzzle") return;
            
            // Spatial hash grid - LARGER cells = fewer checks for high ball counts
            float cellSize = 0.75f; // Increased from 0.5 for better performance with 1000+ balls
            NativeParallelMultiHashMap<int, int> spatialHash = new NativeParallelMultiHashMap<int, int>(1000, Allocator.Temp);
            
            // Collect all ball data
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
                
                // Add to spatial hash ONLY for awake balls - huge performance gain!
                if (!physics.ValueRO.IsSleeping)
                {
                    int cellX = (int)math.floor(physics.ValueRO.Position.x / cellSize);
                    int cellY = (int)math.floor(physics.ValueRO.Position.y / cellSize);
                    int cellKey = cellX + cellY * 10000; // Simple hash
                    spatialHash.Add(cellKey, ballIndex);
                }
                ballIndex++;
            }
            
            // Now check collisions using spatial hash (only awake balls check against nearby)
            int idx = 0;
            int collisionCount = 0;
            foreach (var physics in SystemAPI.Query<RefRW<RiceBallPhysics>>().WithAll<RiceBallTag>())
            {
                // CRITICAL: Skip sleeping balls entirely - massive performance boost
                if (physics.ValueRO.IsSleeping)
                {
                    idx++;
                    continue;
                }
                
                float3 pos = positions[idx];
                float radius = radii[idx];
                
                // Check current cell and 8 neighbors (3x3 grid)
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
                                if (otherIdx == idx) continue; // Skip self
                                
                                float3 otherPos = positions[otherIdx];
                                float otherRadius = radii[otherIdx];
                                
                                float3 delta = pos - otherPos;
                                float distance = math.length(delta);
                                float minDistance = radius + otherRadius;
                                
                                // Collision!
                                if (distance < minDistance && distance > 0.001f)
                                {
                                    collisionCount++;
                                    
                                    // FIX #4: Safe normalize to prevent infinity
                                    float3 pushDir = (distance > 0.0001f) ? (delta / distance) : new float3(0, 1, 0);
                                    float overlap = minDistance - distance;
                                    
                                    // FIX #5: Clamp push to prevent explosion (max 20% of radius per frame)
                                    float safePush = math.min(overlap, radius * 0.2f);
                                    
                                    // FIX #5: Use lower multiplier (1.01 instead of 1.1) to reduce jitter
                                    float3 separationForce = pushDir * safePush * 1.01f;
                                    
                                    // If balls are on a slope (Y difference suggests diagonal), add upward push
                                    // FIX #5: Reduced upward push (0.2f instead of 0.5f) to prevent popcorn
                                    if (math.abs(delta.y) > 0.1f)
                                    {
                                        separationForce.y += safePush * 0.2f;
                                    }
                                    
                                    physics.ValueRW.Position += separationForce;
                                    
                                    // Minimal bounce to prevent bouncing back into each other
                                    float velAlongNormal = math.dot(physics.ValueRO.Velocity, pushDir);
                                    if (velAlongNormal < 0)
                                    {
                                        physics.ValueRW.Velocity -= pushDir * velAlongNormal * 0.5f; // Very soft bounce
                                    }
                                    
                                    // Wake up this ball
                                    physics.ValueRW.IsSleeping = false;
                                }
                            }
                            while (spatialHash.TryGetNextValue(out otherIdx, ref iterator));
                        }
                    }
                }
                
                idx++;
            }
            
            // Debug periodically
            if (collisionCount > 0 && state.WorldUnmanaged.Time.ElapsedTime % 5.0 < SystemAPI.Time.DeltaTime)
            {
                UnityEngine.Debug.Log($"[BallCollision] Resolved {collisionCount} ball overlaps");
            }
            
            spatialHash.Dispose();
            positions.Dispose();
            velocities.Dispose();
            radii.Dispose();
            sleeping.Dispose();
        }
    }
    
    /// <summary>
    /// Cleanup old balls
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial struct RiceBallCleanupSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Only run in DropPuzzle scene
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene != "DropPuzzle") return;
            
            float currentTime = (float)SystemAPI.Time.ElapsedTime;
            int destroyedCount = 0;
            
            // Collect entities to destroy
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            
            foreach (var (lifetime, physics, entity) in 
                SystemAPI.Query<RefRO<RiceBallLifetime>, RefRO<RiceBallPhysics>>()
                .WithAll<RiceBallTag>()
                .WithEntityAccess())
            {
                bool shouldDestroy = false;
                
                // Check lifetime
                if (currentTime - lifetime.ValueRO.SpawnTime > lifetime.ValueRO.MaxLifetime)
                {
                    shouldDestroy = true;
                }
                
                // Check fell too far - DISABLED FOR NOW TO TEST
                // if (physics.ValueRO.Position.y < lifetime.ValueRO.DestroyBelowY)
                // {
                //     shouldDestroy = true;
                // }
                
                if (shouldDestroy)
                {
                    destroyedCount++;
                    ecb.DestroyEntity(entity);
                }
            }
            
            if (destroyedCount > 0)
            {
                UnityEngine.Debug.Log($"[CleanupSystem] Destroyed {destroyedCount} balls");
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
    
    /// <summary>
    /// FIX #7: Dedicated system to sync physics Position to transform Position
    /// Runs at the very end to prevent double-writing and renderer glitches
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct SyncBallTransformSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Only run in DropPuzzle scene
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene != "DropPuzzle") return;
            
            // Sync all ball transforms from physics component
            foreach (var (physics, transform) in 
                SystemAPI.Query<RefRO<RiceBallPhysics>, RefRW<LocalTransform>>().WithAll<RiceBallTag>())
            {
                transform.ValueRW.Position = physics.ValueRO.Position;
            }
        }
    }
}
