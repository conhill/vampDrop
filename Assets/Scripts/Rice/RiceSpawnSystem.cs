using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Vampire.Rice
{
    // [BurstCompile] - Removed to allow Physics.CheckSphere for obstacle avoidance
    public partial struct RiceSpawnSystem : ISystem
    {
        private bool hasCheckedRequirements;
        private EntityQuery spawnerQuery;

        public void OnCreate(ref SystemState state)
        {
            // UnityEngine.Debug.Log("[RiceSpawnSystem] System created! Waiting for RiceSpawner and RiceSpawnPoint...");
            state.RequireForUpdate<RiceSpawner>();
            state.RequireForUpdate<RiceSpawnPoint>();
            
            // Create query for spawner singleton checks
            spawnerQuery = state.GetEntityQuery(ComponentType.ReadOnly<RiceSpawner>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!hasCheckedRequirements)
            {
                // UnityEngine.Debug.Log("[RiceSpawnSystem] ✅ Requirements met! Starting to check spawner...");
                hasCheckedRequirements = true;
            }
            
            // Check spawner count BEFORE trying to access singleton
            // (TryGetSingletonEntity throws if count != 1)
            int spawnerCount = spawnerQuery.CalculateEntityCount();
            if (spawnerCount != 1)
            {
                // 0 or 2+ spawners during scene transition - skip this frame
                return;
            }
            
            // Now safe to get singleton
            Entity spawnerEntity = SystemAPI.GetSingletonEntity<RiceSpawner>();

            if (SystemAPI.HasComponent<RiceSpawned>(spawnerEntity))
            {
                // UnityEngine.Debug.Log("[RiceSpawnSystem] Already spawned, skipping");
                return;
            }

            // UnityEngine.Debug.Log("[RiceSpawnSystem] Starting spawn process...");

            var spawner = SystemAPI.GetSingleton<RiceSpawner>();
            if (spawner.Prefab == Entity.Null)
            {
                UnityEngine.Debug.LogError("[RiceSpawnSystem] Prefab is NULL! Assign the Rice prefab to RiceSpawnerAuthoring");
                state.EntityManager.AddComponent<RiceSpawned>(spawnerEntity);
                return;
            }

            // UnityEngine.Debug.Log($"[RiceSpawnSystem] Using prefab, checking spawn points...");

            var spawnQuery = SystemAPI.QueryBuilder().WithAll<RiceSpawnPoint>().Build();
            var spawnPoints = spawnQuery.ToComponentDataArray<RiceSpawnPoint>(Allocator.Temp);
            if (spawnPoints.Length == 0)
            {
                UnityEngine.Debug.LogError("[RiceSpawnSystem] No RiceSpawnPoint found! Add RiceSpawnPointAuthoring to a GameObject in the scene");
                spawnPoints.Dispose();
                return;
            }

            // UnityEngine.Debug.Log($"[RiceSpawnSystem] Found {spawnPoints.Length} spawn points");

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var random = new Random(spawner.Seed == 0 ? 1u : spawner.Seed);

            // Spawn rice for EACH spawn point with its own count
            for (int pointIdx = 0; pointIdx < spawnPoints.Length; pointIdx++)
            {
                var point = spawnPoints[pointIdx];
                
                if (point.Count <= 0)
                {
                    UnityEngine.Debug.LogError($"[RiceSpawnSystem] ❌ Spawn point {pointIdx} has Count={point.Count}! Set Count > 0 in RiceSpawnPointAuthoring Inspector");
                    continue;
                }
                
                UnityEngine.Debug.Log($"[RiceSpawnSystem] ✅ Spawning {point.Count} rice in spawn point {pointIdx} (Center: {point.Center}, Size: {point.Size}, ObstacleLayerMask: {point.ObstacleLayerMask}, CheckRadius: {point.CheckRadius}, MaxRetries: {point.MaxRetries})");

                int successfulSpawns = 0;
                int skippedDueToObstacles = 0;
                
                // If ObstacleLayerMask is 0, disable obstacle checking entirely
                bool checkObstacles = point.ObstacleLayerMask != 0;
                
                // CRITICAL FIX: If MaxRetries is 0, set it to 1 so at least one attempt is made!
                int maxRetries = math.max(1, point.MaxRetries);
                
                UnityEngine.Debug.Log($"[RiceSpawnSystem] Obstacle checking: {checkObstacles}, MaxRetries: {maxRetries}");
                
                // Debug: Check what's being detected at spawn center
                if (checkObstacles)
                {
                    var testColliders = UnityEngine.Physics.OverlapSphere(point.Center, point.CheckRadius, point.ObstacleLayerMask);
                    if (testColliders.Length > 0)
                    {
                        UnityEngine.Debug.LogWarning($"[RiceSpawnSystem] ⚠️ OBSTACLE DETECTED at spawn center! Found {testColliders.Length} colliders:");
                        for (int i = 0; i < math.min(5, testColliders.Length); i++)
                        {
                            var col = testColliders[i];
                            UnityEngine.Debug.LogWarning($"  - {col.gameObject.name} (Layer: {col.gameObject.layer})");
                        }
                        UnityEngine.Debug.LogWarning($"[RiceSpawnSystem] 💡 SOLUTION: Either set ObstacleLayerMask to 'Nothing', or move floor objects to a different layer (not in the mask).");
                    }
                }
                
                for (var i = 0; i < point.Count; i++)
                {
                    bool foundValidPosition = false;
                    float3 position = float3.zero;
                    float3 rotationAngles = float3.zero;
                    
                    // Try to find a valid spawn position (avoiding obstacles)
                    for (int attempt = 0; attempt < maxRetries; attempt++)
                    {
                        var offset = random.NextFloat3(-0.5f, 0.5f) * point.Size;
                        
                        // If spawning on floor, use fixed Y
                        if (point.SpawnOnFloor)
                        {
                            offset.y = 0; // Don't randomize Y
                        }
                        
                        var testPosition = point.Center + offset;
                        
                        // Set floor Y if enabled
                        if (point.SpawnOnFloor)
                        {
                            testPosition.y = point.FloorY;
                        }
                        
                        // Check for obstacles at this position (skip if ObstacleLayerMask is 0)
                        bool hasObstacle = false;
                        if (checkObstacles)
                        {
                            hasObstacle = UnityEngine.Physics.CheckSphere(
                                testPosition, 
                                point.CheckRadius, 
                                point.ObstacleLayerMask
                            );
                        }
                        
                        if (!hasObstacle)
                        {
                            // Found a clear spot!
                            position = testPosition;
                            foundValidPosition = true;
                            
                            // Generate rotation for this valid position
                            rotationAngles = new float3(
                                random.NextFloat(-10f, 10f),    // X: slight forward/back tilt
                                random.NextFloat(-180f, 180f),  // Y: full spin/twist for variety
                                random.NextFloat(-10f, 10f)     // Z: slight side tilt
                            );
                            
                            break; // Exit retry loop
                        }
                    }
                    
                    // Skip this rice if no valid position found after all retries
                    if (!foundValidPosition)
                    {
                        skippedDueToObstacles++;
                        continue;
                    }

                    var rotation = quaternion.EulerXYZ(math.radians(rotationAngles));

                    var instance = ecb.Instantiate(spawner.Prefab);
                    ecb.SetComponent(instance, LocalTransform.FromPositionRotation(position, rotation));
                    successfulSpawns++;
                }
                
                if (skippedDueToObstacles > 0)
                {
                    UnityEngine.Debug.LogWarning($"[RiceSpawnSystem] ⚠️ Zone {pointIdx}: Spawned {successfulSpawns}/{point.Count} rice ({skippedDueToObstacles} skipped due to obstacles). Consider adjusting obstacle layer mask or increasing spawn area.");
                }
                else if (checkObstacles)
                {
                    UnityEngine.Debug.Log($"[RiceSpawnSystem] ✅ Zone {pointIdx}: Successfully spawned all {successfulSpawns} rice grains (obstacle checking enabled)!");
                }
                else
                {
                    UnityEngine.Debug.Log($"[RiceSpawnSystem] ✅ Zone {pointIdx}: Successfully spawned all {successfulSpawns} rice grains (obstacle checking DISABLED)!");
                }
            }

            ecb.AddComponent<RiceSpawned>(spawnerEntity);
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            
            // Calculate total spawned
            var totalSpawned = 0;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                totalSpawned += spawnPoints[i].Count;
            }
            
            spawnPoints.Dispose();

            UnityEngine.Debug.Log($"[RiceSpawnSystem] ✅ Requested spawn of {totalSpawned} rice entities. Check if they appear (some may be rejected by obstacle avoidance).");
        }
    }
}
