using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Vampire.Rice
{
    public class RiceSpawnerAuthoring : MonoBehaviour
    {
        public GameObject RicePrefab;
        public int Count = 100000;
        public uint Seed = 1;
    }

    public class RiceSpawnerBaker : Baker<RiceSpawnerAuthoring>
    {
        public override void Bake(RiceSpawnerAuthoring authoring)
        {
            UnityEngine.Debug.Log($"[RiceSpawnerBaker] BAKING! Prefab={authoring.RicePrefab?.name ?? "NULL"}, Count={authoring.Count}");
            
            var entity = GetEntity(TransformUsageFlags.None);
            var prefabEntity = authoring.RicePrefab != null
                ? GetEntity(authoring.RicePrefab, TransformUsageFlags.Dynamic)
                : Entity.Null;

            if (prefabEntity == Entity.Null)
            {
                UnityEngine.Debug.LogError("[RiceSpawnerBaker] ❌ Prefab entity is NULL after baking!");
            }
            else
            {
                UnityEngine.Debug.Log($"[RiceSpawnerBaker] ✅ Prefab entity created successfully");
            }

            AddComponent(entity, new RiceSpawner
            {
                Prefab = prefabEntity,
                Count = math.max(0, authoring.Count),
                Seed = authoring.Seed == 0 ? 1u : authoring.Seed
            });
            
            UnityEngine.Debug.Log($"[RiceSpawnerBaker] ✅ RiceSpawner component added to entity");
        }
    }
}
