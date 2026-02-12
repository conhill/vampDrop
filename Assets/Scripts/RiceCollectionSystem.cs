using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Vampire.Rice
{
    /// <summary>
    /// Tag component for highlighted rice
    /// </summary>
    public struct RiceHighlighted : IComponentData
    {
        public float OriginalScale;
    }
    
    /// <summary>
    /// Hover highlight system - shows which rice can be collected
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(RiceCollectionSystem))]
    public partial class RiceHoverHighlightSystem : SystemBase
    {
        private Entity currentHoveredEntity = Entity.Null;
        private EntityQuery playerQuery;
        
        /// <summary>
        /// Public flag for UI to check if rice is being hovered
        /// </summary>
        public static bool IsHoveringRice { get; private set; }
        
        protected override void OnCreate()
        {
            RequireForUpdate<Player.PlayerData>();
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<Player.PlayerData>());
        }
        
        protected override void OnUpdate()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                IsHoveringRice = false;
                return;
            }
            
            // Check player entity count BEFORE trying to access singleton
            // (TryGetSingletonEntity throws if count != 1)
            int playerCount = playerQuery.CalculateEntityCount();
            if (playerCount != 1)
            {
                // 0 or 2+ players during scene transition - skip this frame
                IsHoveringRice = false;
                return;
            }
            
            // Now safe to get singleton
            Entity playerEntity = SystemAPI.GetSingletonEntity<Player.PlayerData>();
            
            // Raycast from mouse position
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            
            Entity newHoveredEntity = Entity.Null;
            float closestDistance = float.MaxValue;
            float3 rayOrigin = ray.origin;
            float3 rayDirection = ray.direction;
            
            // Get player position and pickup radius for distance check
            var playerTransform = EntityManager.GetComponentData<LocalTransform>(playerEntity);
            float3 playerPosition = playerTransform.Position;
            
            float pickupRadius = 1.5f; // Default
            if (DropPuzzle.PlayerDataManager.Instance != null)
            {
                pickupRadius = DropPuzzle.PlayerDataManager.Instance.FPSCollector.pickupRadius;
            }
            
            // Find closest rice under mouse (exclude hidden rice and out-of-range rice)
            Entities
                .WithoutBurst()
                .WithNone<RiceHidden>()
                .ForEach((Entity entity, in RiceEntity riceData, in LocalTransform transform) =>
                {
                    // Check if rice is within pickup range
                    float distanceToPlayer = math.distance(playerPosition, transform.Position);
                    if (distanceToPlayer > pickupRadius) return;
                    
                    float3 toSphere = transform.Position - rayOrigin;
                    float t = math.dot(toSphere, rayDirection);
                    
                    if (t < 0) return;
                    
                    float3 closestPoint = rayOrigin + rayDirection * t;
                    float distanceToRay = math.distance(closestPoint, transform.Position);
                    
                    float hoverRadius = 0.15f;
                    if (distanceToRay < hoverRadius && t < closestDistance)
                    {
                        closestDistance = t;
                        newHoveredEntity = entity;
                    }
                }).Run();
            
            // Remove highlight from previous entity
            if (currentHoveredEntity != Entity.Null && currentHoveredEntity != newHoveredEntity)
            {
                if (EntityManager.Exists(currentHoveredEntity) && EntityManager.HasComponent<RiceHighlighted>(currentHoveredEntity))
                {
                    var highlighted = EntityManager.GetComponentData<RiceHighlighted>(currentHoveredEntity);
                    var transform = EntityManager.GetComponentData<LocalTransform>(currentHoveredEntity);
                    transform.Scale = highlighted.OriginalScale;
                    EntityManager.SetComponentData(currentHoveredEntity, transform);
                    EntityManager.RemoveComponent<RiceHighlighted>(currentHoveredEntity);
                }
            }
            
            // Add highlight to new entity
            if (newHoveredEntity != Entity.Null && newHoveredEntity != currentHoveredEntity)
            {
                if (EntityManager.Exists(newHoveredEntity) && !EntityManager.HasComponent<RiceHighlighted>(newHoveredEntity))
                {
                    var transform = EntityManager.GetComponentData<LocalTransform>(newHoveredEntity);
                    EntityManager.AddComponentData(newHoveredEntity, new RiceHighlighted
                    {
                        OriginalScale = transform.Scale
                    });
                    transform.Scale = transform.Scale * 1.3f; // 30% larger when hovered
                    EntityManager.SetComponentData(newHoveredEntity, transform);
                }
            }
            
            currentHoveredEntity = newHoveredEntity;
            IsHoveringRice = (newHoveredEntity != Entity.Null);
        }
    }
    
    /// <summary>
    /// Click-to-collect system - raycast from camera and collect rice on click
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Player.PlayerTransformSyncSystem))]
    public partial class RiceCollectionSystem : SystemBase
    {
        private EntityQuery playerQuery;
        
        protected override void OnCreate()
        {
            RequireForUpdate<Player.PlayerData>();
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<Player.PlayerData>());
            // Debug.Log("[RiceCollectionSystem] Created - click rice to collect!");
        }

        protected override void OnUpdate()
        {
            // Only check on mouse click
            if (!Input.GetMouseButtonDown(0)) return;

            var camera = Camera.main;
            if (camera == null) return;
            
            // Check player entity count BEFORE trying to access singleton
            // (TryGetSingletonEntity throws if count != 1)
            int playerCount = playerQuery.CalculateEntityCount();
            if (playerCount != 1)
            {
                // 0 or 2+ players during scene transition - skip this click
                return;
            }
            
            // Now safe to get singleton
            Entity playerEntity = SystemAPI.GetSingletonEntity<Player.PlayerData>();

            // Raycast from camera
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            
            // Find which rice entity was clicked (if any)
            Entity clickedEntity = Entity.Null;
            float closestDistance = float.MaxValue;
            float3 rayOrigin = ray.origin;
            float3 rayDirection = ray.direction;

            // Check all rice entities (exclude hidden rice)
            Entities
                .WithoutBurst()
                .WithNone<RiceHidden>()
                .ForEach((Entity entity, in RiceEntity riceData, in LocalTransform transform) =>
                {
                    // Simple sphere-ray intersection
                    float3 toSphere = transform.Position - rayOrigin;
                    float t = math.dot(toSphere, rayDirection);
                    
                    if (t < 0) return; // Behind camera
                    
                    float3 closestPoint = rayOrigin + rayDirection * t;
                    float distanceToRay = math.distance(closestPoint, transform.Position);
                    
                    float clickRadius = 0.15f; // Generous click radius
                    if (distanceToRay < clickRadius && t < closestDistance)
                    {
                        closestDistance = t;
                        clickedEntity = entity;
                    }
                }).Run();

            // Collect the clicked rice
            if (clickedEntity != Entity.Null)
            {
                // Check distance to player - must be within pickup radius
                var playerTransform = EntityManager.GetComponentData<LocalTransform>(playerEntity);
                var riceTransform = EntityManager.GetComponentData<LocalTransform>(clickedEntity);
                float distance = math.distance(playerTransform.Position, riceTransform.Position);
                
                // Get pickup radius from upgrades (default 1.5, can be upgraded)
                float pickupRadius = 1.5f; // Default
                if (DropPuzzle.PlayerDataManager.Instance != null)
                {
                    pickupRadius = DropPuzzle.PlayerDataManager.Instance.FPSCollector.pickupRadius;
                }
                
                if (distance > pickupRadius)
                {
                    Debug.Log($"[RiceCollection] Rice too far! Distance: {distance:F2} > Max: {pickupRadius:F2}");
                    return;
                }
                
                // Within range - collect it!
                EntityManager.DestroyEntity(clickedEntity);
                
                // Update player collected count (ECS component for display)
                var playerData = EntityManager.GetComponentData<Player.PlayerData>(playerEntity);
                playerData.RiceCollected++;
                EntityManager.SetComponentData(playerEntity, playerData);
                
                // ALSO update the new roguelite progression system
                if (DropPuzzle.PlayerDataManager.Instance != null)
                {
                    DropPuzzle.PlayerDataManager.Instance.AddRice(1);
                }
                
                // Notify tutorial manager
                if (DropPuzzle.TutorialManager.Instance != null)
                {
                    DropPuzzle.TutorialManager.Instance.NotifyRiceCollected(1);
                }
            }
        }
    }
}
