using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Vampire.Player
{
    /// <summary>
    /// Syncs the player GameObject transform to its ECS entity
    /// so the rice collection system can track the player position
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(Rice.RiceCollectionSystem))]
    public partial class PlayerTransformSyncSystem : SystemBase
    {
        // Cached reference — FindObjectOfType only called once, not every frame
        private PlayerAuthoring _cachedPlayerAuthoring;
        private Transform _cachedPlayerTransform;

        protected override void OnCreate()
        {
            Debug.Log("[PlayerTransformSyncSystem] Created - will sync player GameObject transform to ECS entity");
        }

        protected override void OnUpdate()
        {
            // Only search if cache is stale (scene load, first frame, etc.)
            if (_cachedPlayerAuthoring == null)
            {
                _cachedPlayerAuthoring = GameObject.FindFirstObjectByType<PlayerAuthoring>();
                _cachedPlayerTransform = _cachedPlayerAuthoring != null ? _cachedPlayerAuthoring.transform : null;
            }

            if (_cachedPlayerTransform == null) return;

            var pos = (Unity.Mathematics.float3)_cachedPlayerTransform.position;
            var rot = (Unity.Mathematics.quaternion)_cachedPlayerTransform.rotation;

            // Burst-compiled: the lambda only writes pos/rot captured from outside, no managed calls inside.
            Entities
                .WithAll<PlayerData>()
                .ForEach((Entity entity, ref LocalTransform transform) =>
                {
                    transform.Position = pos;
                    transform.Rotation = rot;
                    // NOTE: Do NOT touch transform.Scale here — rice scale is managed by RiceGPURenderer
                }).Run();
        }
    }
}
