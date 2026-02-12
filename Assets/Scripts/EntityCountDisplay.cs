using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Vampire
{
    /// <summary>
    /// Adds basic visual representation to spawned rice entities
    /// This runs once after spawning to add renderers
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class RiceVisualizationSystem : SystemBase
    {
        protected override void OnCreate()
        {
            Debug.Log("[RiceVisualizationSystem] Created - will add visuals to rice entities");
        }

        protected override void OnUpdate()
        {
            // This is a temporary approach - normally you'd use hybrid renderer
            // For now, just count and report
        }
    }
    
    /// <summary>
    /// MonoBehaviour to display ECS entity counts on screen
    /// </summary>
    public class EntityCountDisplay : MonoBehaviour
    {
        private GUIStyle style;

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.fontSize = 24;
                style.normal.textColor = Color.white;
                style.alignment = TextAnchor.UpperLeft;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                // Only count visible rice (exclude hidden)
                var riceQuery = world.EntityManager.CreateEntityQuery(
                    Unity.Entities.ComponentType.ReadOnly<Rice.RiceEntity>(),
                    Unity.Entities.ComponentType.Exclude<Rice.RiceHidden>());
                var riceCount = riceQuery.CalculateEntityCount();
                riceQuery.Dispose();

                var playerQuery = world.EntityManager.CreateEntityQuery(typeof(Player.PlayerData));
                var playerCount = playerQuery.CalculateEntityCount();
                var collectedCount = 0;
                
                // Only get singleton if EXACTLY 1 player exists
                if (playerCount == 1)
                {
                    var playerEntity = playerQuery.GetSingletonEntity();
                    var playerData = world.EntityManager.GetComponentData<Player.PlayerData>(playerEntity);
                    collectedCount = playerData.RiceCollected;
                }
                else if (playerCount > 1)
                {
                    // Multiple players during scene transition - skip this frame
                    Debug.LogWarning($"[EntityCountDisplay] {playerCount} player entities detected during scene transition");
                }
                playerQuery.Dispose();

                GUI.Label(new Rect(10, 10, 500, 30), $"Rice in World: {riceCount}", style);
                GUI.Label(new Rect(10, 40, 500, 30), $"Rice Collected: {collectedCount}", style);
            }
        }
    }
}
