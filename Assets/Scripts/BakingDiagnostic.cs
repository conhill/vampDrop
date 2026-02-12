using Unity.Entities;
using UnityEngine;

namespace Vampire
{
    /// <summary>
    /// Manual diagnostic to check if ECS baking is working
    /// </summary>
    public class BakingDiagnostic : MonoBehaviour
    {
        void Start()
        {
            Debug.Log("=== BAKING DIAGNOSTIC ===");
            
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("❌ NO ECS WORLD EXISTS! This is a critical problem.");
                Debug.LogError("   Check: Window → Package Manager → Make sure 'Entities' package is installed");
                return;
            }
            
            Debug.Log($"✅ ECS World exists: {world.Name}");
            
            var allEntitiesQuery = world.EntityManager.UniversalQuery;
            var totalEntities = allEntitiesQuery.CalculateEntityCount();
            Debug.Log($"   Total entities in world: {totalEntities}");
            
            // Check for specific systems by checking what they produce
            Debug.Log("   Checking for system outputs...");
            
            var spawnerQuery = world.EntityManager.CreateEntityQuery(typeof(Rice.RiceSpawner));
            Debug.Log($"      RiceSpawner components: {spawnerQuery.CalculateEntityCount()}");
            spawnerQuery.Dispose();
            
            var spawnPointQuery = world.EntityManager.CreateEntityQuery(typeof(Rice.RiceSpawnPoint));
            Debug.Log($"      RiceSpawnPoint components: {spawnPointQuery.CalculateEntityCount()}");
            spawnPointQuery.Dispose();
            
            // Check if baking world exists
            var bakingWorlds = 0;
            foreach (var w in World.All)
            {
                if (w.Name.Contains("Baking") || w.Name.Contains("Conversion"))
                {
                    bakingWorlds++;
                    Debug.Log($"   Found baking world: {w.Name}");
                }
            }
            
            if (bakingWorlds == 0)
            {
                Debug.LogWarning("⚠️ No baking worlds found. Entities might not be converted from GameObjects.");
            }
            
            Debug.Log("=== END BAKING DIAGNOSTIC ===");
        }
    }
}
