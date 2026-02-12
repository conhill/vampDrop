using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Vampire.Rice
{
    /// <summary>
    /// Manually converts authoring components to ECS since automatic baking isn't working
    /// This is a workaround for when Sub Scenes aren't being used
    /// </summary>
    public class ManualBakingBootstrap : MonoBehaviour
    {
        void Start()
        {
            // Debug.Log("[ManualBakingBootstrap] Starting manual baking process...");
            
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[ManualBakingBootstrap] No ECS world found!");
                return;
            }

            var entityManager = world.EntityManager;

            // Convert RiceSpawner
            var spawnerAuthoring = FindObjectOfType<RiceSpawnerAuthoring>();
            if (spawnerAuthoring != null)
            {
                Debug.Log($"[ManualBakingBootstrap] Converting RiceSpawner: Count={spawnerAuthoring.Count}");
                
                var spawnerEntity = entityManager.CreateEntity();
                entityManager.SetName(spawnerEntity, "RiceSpawner");
                
                // Convert prefab to entity PREFAB (not a regular entity)
                Entity prefabEntity = Entity.Null;
                if (spawnerAuthoring.RicePrefab != null)
                {
                    // Create a proper ECS prefab entity
                    var riceAuthoring = spawnerAuthoring.RicePrefab.GetComponent<RiceAuthoring>();
                    if (riceAuthoring != null)
                    {
                        prefabEntity = entityManager.CreateEntity();
                        
                        // Add the RiceEntity component
                        entityManager.AddComponentData(prefabEntity, new RiceEntity
                        {
                            CollectionRadius = riceAuthoring.CollectionRadius
                        });
                        
                        // Add Transform at origin (will be set during spawn)
                        entityManager.AddComponentData(prefabEntity, Unity.Transforms.LocalTransform.FromPosition(0, 0, 0));
                        
                        // Mark as Prefab so it doesn't get processed by systems
                        entityManager.AddComponentData(prefabEntity, new Unity.Entities.Prefab());
                        
                        Debug.Log($"[ManualBakingBootstrap] ✅ Created ECS prefab entity: {prefabEntity}");
                    }
                    else
                    {
                        Debug.LogError("[ManualBakingBootstrap] RicePrefab is missing RiceAuthoring component!");
                    }
                }
                else
                {
                    Debug.LogError("[ManualBakingBootstrap] RicePrefab is NULL!");
                }
                
                entityManager.AddComponentData(spawnerEntity, new RiceSpawner
                {
                    Prefab = prefabEntity,
                    Count = spawnerAuthoring.Count,
                    Seed = spawnerAuthoring.Seed == 0 ? 1u : spawnerAuthoring.Seed
                });
                
                Debug.Log("[ManualBakingBootstrap] ✅ RiceSpawner component added");
            }
            else
            {
                Debug.LogError("[ManualBakingBootstrap] No RiceSpawnerAuthoring found!");
            }

            // Convert RiceSpawnPoints
            var spawnPointAuthorings = FindObjectsOfType<RiceSpawnPointAuthoring>();
            Debug.Log($"[ManualBakingBootstrap] Found {spawnPointAuthorings.Length} spawn points");
            
            foreach (var spawnPointAuthoring in spawnPointAuthorings)
            {
                var spawnPointEntity = entityManager.CreateEntity();
                entityManager.SetName(spawnPointEntity, "RiceSpawnPoint");
                
                // Calculate spawn bounds from floor objects
                Bounds spawnBounds = spawnPointAuthoring.GetSpawnBounds();
                
                // Calculate floor Y from floor objects if available
                float floorY = spawnPointAuthoring.ManualFloorY;
                if (spawnPointAuthoring.FloorObjects != null && spawnPointAuthoring.FloorObjects.Length > 0)
                {
                    float totalY = 0f;
                    int validCount = 0;
                    
                    foreach (var floor in spawnPointAuthoring.FloorObjects)
                    {
                        if (floor != null)
                        {
                            Renderer renderer = floor.GetComponent<Renderer>();
                            if (renderer != null)
                            {
                                totalY += renderer.bounds.max.y;
                                validCount++;
                            }
                        }
                    }
                    
                    if (validCount > 0)
                    {
                        floorY = totalY / validCount;
                    }
                }
                
                entityManager.AddComponentData(spawnPointEntity, new RiceSpawnPoint
                {
                    Center = (float3)spawnBounds.center,
                    Size = (float3)spawnBounds.size,
                    Count = spawnPointAuthoring.Count,
                    SpawnOnFloor = true,
                    FloorY = floorY + spawnPointAuthoring.SpawnHeightOffset
                });
                
                Debug.Log($"[ManualBakingBootstrap] ✅ RiceSpawnPoint added for zone '{spawnPointAuthoring.ZoneName}' - Size: {spawnBounds.size}, FloorY={floorY}, Margin={spawnPointAuthoring.WallMargin}");
            }

            // Convert Player
            var playerAuthoring = FindObjectOfType<Player.PlayerAuthoring>();
            if (playerAuthoring != null)
            {
                var playerEntity = GameObjectEntity.GetEntity(entityManager, playerAuthoring.gameObject);
                
                entityManager.AddComponentData(playerEntity, new Player.PlayerData
                {
                    MoveSpeed = 0, // Movement handled by FPSController now
                    CollectionRadius = playerAuthoring.CollectionRadius,
                    RiceCollected = 0
                });
                
                // Make sure it has a proper transform
                if (!entityManager.HasComponent<LocalTransform>(playerEntity))
                {
                    entityManager.AddComponentData(playerEntity, LocalTransform.FromPositionRotationScale(
                        playerAuthoring.transform.position,
                        playerAuthoring.transform.rotation,
                        1f
                    ));
                }
                
                Debug.Log($"[ManualBakingBootstrap] ✅ Player entity created at {playerAuthoring.transform.position}");
            }

            Debug.Log("[ManualBakingBootstrap] 🎉 Manual conversion complete!");
        }
    }

    /// <summary>
    /// Helper to convert GameObjects to entities
    /// </summary>
    public static class GameObjectEntity
    {
        public static Entity GetEntity(EntityManager entityManager, GameObject gameObject)
        {
            // Check if GameObject already has an entity component
            var entityHolder = gameObject.GetComponent<EntityHolder>();
            if (entityHolder != null && entityManager.Exists(entityHolder.Entity))
            {
                return entityHolder.Entity;
            }

            // Create new entity with Transform
            var entity = entityManager.CreateEntity();
            
            // Add Transform component
            entityManager.AddComponentData(entity, Unity.Transforms.LocalTransform.FromPositionRotationScale(
                gameObject.transform.position,
                gameObject.transform.rotation,
                gameObject.transform.localScale.x
            ));

            // Store entity reference on GameObject
            if (entityHolder == null)
            {
                entityHolder = gameObject.AddComponent<EntityHolder>();
            }
            entityHolder.Entity = entity;

            return entity;
        }
    }

    /// <summary>
    /// Hold entity reference on GameObject
    /// </summary>
    public class EntityHolder : MonoBehaviour
    {
        public Entity Entity;
    }
}
