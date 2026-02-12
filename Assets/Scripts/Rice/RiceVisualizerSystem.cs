using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using System.Collections.Generic;

namespace Vampire.Rice
{
    /// <summary>
    /// DISABLED - Replaced by RiceGPURenderer for performance
    /// This old system created GameObjects for each entity (very slow with 100k+ entities)
    /// </summary>
    [DisableAutoCreation]
    public partial class RiceVisualizerSystem : SystemBase
    {
        private GameObject ricePrefab;
        private Dictionary<Entity, GameObject> entityToGameObject = new Dictionary<Entity, GameObject>();

        protected override void OnCreate()
        {
            Debug.Log("[RiceVisualizerSystem] Created - will create GameObjects for rice entities");
            
            // Create a simple visual prefab
            ricePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ricePrefab.transform.localScale = Vector3.one * 0.1f;
            ricePrefab.name = "RiceVisual";
            
            // Make it bright so we can see it
            var renderer = ricePrefab.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.yellow;
            }
            
            ricePrefab.SetActive(false);
        }

        protected override void OnUpdate()
        {
            var entityManager = EntityManager;
            var ecb = new Unity.Entities.EntityCommandBuffer(Unity.Collections.Allocator.TempJob);
            var localEntityToGO = entityToGameObject; // Capture locally for lambda
            var localRicePrefab = ricePrefab;
            
            // Create GameObjects for new rice entities
            Entities
                .WithoutBurst()
                .WithNone<RiceVisualized>()
                .WithAll<RiceEntity>()
                .ForEach((Entity entity, in LocalTransform transform) =>
                {
                    if (!localEntityToGO.ContainsKey(entity))
                    {
                        var go = GameObject.Instantiate(localRicePrefab);
                        go.SetActive(true);
                        go.transform.position = transform.Position;
                        go.transform.rotation = transform.Rotation;
                        go.transform.localScale = Vector3.one * 0.1f;
                        
                        localEntityToGO[entity] = go;
                        ecb.AddComponent<RiceVisualized>(entity);
                    }
                }).Run();
            
            ecb.Playback(entityManager);
            ecb.Dispose();
            
            // Update positions of existing GameObjects
            foreach (var kvp in entityToGameObject)
            {
                if (entityManager.Exists(kvp.Key))
                {
                    var transform = entityManager.GetComponentData<LocalTransform>(kvp.Key);
                    kvp.Value.transform.position = transform.Position;
                }
            }
            
            // Clean up GameObjects for deleted entities
            var toRemove = new List<Entity>();
            foreach (var kvp in entityToGameObject)
            {
                if (!entityManager.Exists(kvp.Key))
                {
                    GameObject.Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var entity in toRemove)
            {
                entityToGameObject.Remove(entity);
            }
            
            // Debug log every 2 seconds
            if (World.Time.ElapsedTime % 2.0 < SystemAPI.Time.DeltaTime)
            {
                Debug.Log($"[RiceVisualizerSystem] Visualizing {entityToGameObject.Count} rice entities");
            }
        }

        protected override void OnDestroy()
        {
            // Clean up all GameObjects
            foreach (var go in entityToGameObject.Values)
            {
                if (go != null)
                {
                    GameObject.Destroy(go);
                }
            }
            entityToGameObject.Clear();
        }
    }

    // Tag component to mark that we've created a visualization for this entity
    public struct RiceVisualized : IComponentData { }
}
