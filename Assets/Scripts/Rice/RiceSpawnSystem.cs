using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Vampire.Rice
{
    [BurstCompile]
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
                    UnityEngine.Debug.LogWarning($"[RiceSpawnSystem] Spawn point {pointIdx} has Count={point.Count}, skipping");
                    continue;
                }
                
                // UnityEngine.Debug.Log($"[RiceSpawnSystem] Spawning {point.Count} rice in spawn point {pointIdx}");

                for (var i = 0; i < point.Count; i++)
                {
                    var offset = random.NextFloat3(-0.5f, 0.5f) * point.Size;
                    
                    // If spawning on floor, use fixed Y
                    if (point.SpawnOnFloor)
                    {
                        offset.y = 0; // Don't randomize Y
                    }
                    
                    var position = point.Center + offset;
                    
                    // Set floor Y if enabled
                    if (point.SpawnOnFloor)
                    {
                        position.y = point.FloorY;
                    }

                    // Random rotation - rice lying flat on ground
                    // Only Y axis (spin) is fully random, X/Z have small tilts so rice lies flat
                    var rotationAngles = new float3(
                        random.NextFloat(-10f, 10f),    // X: slight forward/back tilt
                        random.NextFloat(-180f, 180f),  // Y: full spin/twist for variety
                        random.NextFloat(-10f, 10f)     // Z: slight side tilt
                    );
                    var rotation = quaternion.EulerXYZ(math.radians(rotationAngles));

                    var instance = ecb.Instantiate(spawner.Prefab);
                    ecb.SetComponent(instance, LocalTransform.FromPositionRotation(position, rotation));
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

            // UnityEngine.Debug.Log($"[RiceSpawnSystem] ✅ Successfully spawned {totalSpawned} rice entities across spawn areas!");
        }
    }
}
