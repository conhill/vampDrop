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
        protected override void OnCreate()
        {
            Debug.Log("[PlayerTransformSyncSystem] Created - will sync player GameObject transform to ECS entity");
        }

        protected override void OnUpdate()
        {
            // Find player GameObject and sync its transform to the ECS entity
            var playerAuthoring = GameObject.FindObjectOfType<PlayerAuthoring>();
            
            if (playerAuthoring != null)
            {
                // Update the ECS entity's transform to match the GameObject
                Entities
                    .WithoutBurst()
                    .WithAll<PlayerData>()
                    .ForEach((Entity entity, ref LocalTransform transform) =>
                    {
                        transform.Position = playerAuthoring.transform.position;
                        transform.Rotation = playerAuthoring.transform.rotation;
                    }).Run();
            }
        }
    }
}
