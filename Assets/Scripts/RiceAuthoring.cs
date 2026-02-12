using Unity.Entities;
using UnityEngine;

namespace Vampire.Rice
{
    /// <summary>
    /// Component to tag an entity as a rice grain
    /// </summary>
    public struct RiceEntity : IComponentData
    {
        public float CollectionRadius;
    }
    
    /// <summary>
    /// Tag component to hide rice (don't render, interact, or collect)
    /// Used when switching to ball drop scene to avoid destroying/respawning
    /// </summary>
    public struct RiceHidden : IComponentData { }

    public class RiceAuthoring : MonoBehaviour
    {
        [Tooltip("Radius at which the player can collect this rice")]
        public float CollectionRadius = 1f;
    }

    public class RiceEntityBaker : Baker<RiceAuthoring>
    {
        public override void Bake(RiceAuthoring authoring)
        {
            UnityEngine.Debug.Log($"[RiceEntityBaker] BAKING Rice prefab with CollectionRadius={authoring.CollectionRadius}");
            
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new RiceEntity
            {
                CollectionRadius = authoring.CollectionRadius
            });
            
            // Add RiceHighlighted component (disabled by default) for zero-cost highlighting
            AddComponent(entity, new RiceHighlighted());
            SetComponentEnabled<RiceHighlighted>(entity, false);
            
            UnityEngine.Debug.Log($"[RiceEntityBaker] ✅ RiceEntity + RiceHighlighted components added");
        }
    }
}
