using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Vampire.Player
{
    public struct PlayerData : IComponentData
    {
        public float MoveSpeed;
        public float CollectionRadius;
        public int RiceCollected;
    }

    public class PlayerAuthoring : MonoBehaviour
    {
        [Header("ECS Collection Settings")]
        [Tooltip("These values are used by the ECS rice collection system")]
        public float CollectionRadius = 2f;
        
        [Header("Note:")]
        [TextArea(2, 4)]
        public string info = "Movement is handled by FPSController component. This component only provides data for ECS rice collection.";
    }

    public class PlayerBaker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerData
            {
                MoveSpeed = 0, // Not used anymore, movement handled by FPSController
                CollectionRadius = authoring.CollectionRadius,
                RiceCollected = 0
            });
        }
    }
}
