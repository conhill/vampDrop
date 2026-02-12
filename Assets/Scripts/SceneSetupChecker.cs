using UnityEngine;
using Unity.Entities;

namespace Vampire
{
    /// <summary>
    /// Debug helper to check scene setup on startup
    /// </summary>
    public class SceneSetupChecker : MonoBehaviour
    {
        void Start()
        {
            Debug.Log("=== VAMPIRE GAME - SCENE SETUP CHECK ===");
            
            var allGood = true;
            
            // Check for RiceSpawnerAuthoring
            var spawner = FindObjectOfType<Rice.RiceSpawnerAuthoring>();
            if (spawner != null)
            {
                var prefabName = (spawner.RicePrefab != null) ? spawner.RicePrefab.name : "NULL";
                Debug.Log($"✅ RiceSpawner found: Count={spawner.Count}, Prefab={prefabName}");
                Debug.Log($"   GameObject: {spawner.gameObject.name}, Active: {spawner.gameObject.activeInHierarchy}");
                Debug.Log($"   Component enabled: {spawner.enabled}");
                
                if (spawner.RicePrefab == null)
                {
                    Debug.LogError("❌ RiceSpawner has NO PREFAB assigned! Drag your Rice prefab into the RiceSpawner component.");
                    allGood = false;
                }
                
                // Check if the prefab has RiceAuthoring
                if (spawner.RicePrefab != null)
                {
                    var riceAuthoring = spawner.RicePrefab.GetComponent<Rice.RiceAuthoring>();
                    if (riceAuthoring == null)
                    {
                        Debug.LogError("❌ Rice Prefab is MISSING RiceAuthoring component! Add it to the prefab.");
                        allGood = false;
                    }
                    else
                    {
                        Debug.Log($"   ✅ Rice prefab has RiceAuthoring component");
                    }
                }
            }
            else
            {
                Debug.LogError("❌ No RiceSpawnerAuthoring found in scene! Add it to a GameObject.");
                Debug.LogError("   HOW TO FIX: Create empty GameObject → Add Component → RiceSpawnerAuthoring");
                allGood = false;
            }

            // Check for RiceSpawnPointAuthoring
            var spawnPoints = FindObjectsOfType<Rice.RiceSpawnPointAuthoring>();
            if (spawnPoints.Length > 0)
            {
                Debug.Log($"✅ Found {spawnPoints.Length} spawn point(s)");
            }
            else
            {
                Debug.LogError("❌ No RiceSpawnPointAuthoring found! Add it to mark where rice should spawn.");
                Debug.LogError("   HOW TO FIX: Create empty GameObject → Add Component → RiceSpawnPointAuthoring");
                allGood = false;
            }

            // Check for Player
            var player = FindObjectOfType<Player.PlayerAuthoring>();
            if (player != null)
            {
                Debug.Log($"✅ Player found at position {player.transform.position}");
            }
            else
            {
                Debug.LogError("❌ No PlayerAuthoring found! Add it to your player GameObject.");
            }

            // Check for Camera
            var camera = Camera.main;
            if (camera != null)
            {
                Debug.Log($"✅ Main Camera found at position {camera.transform.position}");
            }
            else
            {
                Debug.LogWarning("⚠️ No Main Camera found! Tag a camera as 'MainCamera'.");
            }

            Debug.Log("=== END SETUP CHECK ===");
            
            if (!allGood)
            {
                Debug.LogError("⚠️⚠️⚠️ SCENE IS NOT READY! Fix the errors above. ⚠️⚠️⚠️");
            }
            else
            {
                Debug.Log("🎉 Scene setup looks good! Rice should spawn...");
            }
        }
        void Update()
        {
            // Check ECS world every 2 seconds
            if (Time.frameCount % 120 == 0)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world != null)
                {
                    Debug.Log("=== ECS WORLD STATUS ===");
                    
                    // Check for RiceSpawner components
                    var spawnerQuery = world.EntityManager.CreateEntityQuery(typeof(Rice.RiceSpawner));
                    var spawnerCount = spawnerQuery.CalculateEntityCount();
                    Debug.Log($"[ECS] RiceSpawner components: {spawnerCount}");
                    spawnerQuery.Dispose();
                    
                    // Check for RiceSpawnPoint components
                    var spawnPointQuery = world.EntityManager.CreateEntityQuery(typeof(Rice.RiceSpawnPoint));
                    var spawnPointCount = spawnPointQuery.CalculateEntityCount();
                    if (spawnPointCount == 0)
                    {
                        Debug.LogError($"[ECS] ❌ NO RiceSpawnPoint components found! Rice will NOT spawn. Add RiceSpawnPointAuthoring GameObjects to your scene.");
                    }
                    else
                    {
                        Debug.Log($"[ECS] ✅ RiceSpawnPoint components: {spawnPointCount}");
                    }
                    spawnPointQuery.Dispose();
                    
                    // Check for Rice entities
                    var riceQuery = world.EntityManager.CreateEntityQuery(typeof(Rice.RiceEntity));
                    var riceCount = riceQuery.CalculateEntityCount();
                    Debug.Log($"[ECS] Rice entities in world: {riceCount}");
                    riceQuery.Dispose();
                    
                    // Check for Player
                    var playerQuery = world.EntityManager.CreateEntityQuery(typeof(Player.PlayerData));
                    var playerCount = playerQuery.CalculateEntityCount();
                    Debug.Log($"[ECS] Player entities: {playerCount}");
                    playerQuery.Dispose();
                    
                    Debug.Log("=== END ECS STATUS ===");
                }
            }
        }    }
}
