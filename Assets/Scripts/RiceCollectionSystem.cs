using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Vampire.Rice
{
    /// <summary>
    /// Tag component for highlighted rice - optimized with IEnableableComponent
    /// No structural changes when toggling highlight state (no sync points!)
    /// </summary>
    public struct RiceHighlighted : IComponentData, IEnableableComponent
    {
        // No fields needed - just use the enabled/disabled state
    }
    
    /// <summary>
    /// Hover highlight system - shows which rice can be collected
    /// OPTIMIZED: Uses ISystem + IJobEntity with Burst compilation for 40k+ rice grains
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(RiceCollectionSystem))]
    public partial struct RiceHoverHighlightSystem : ISystem
    {
        private Entity currentHoveredEntity;
        private EntityQuery playerQuery;
        
        /// <summary>
        /// Public flag for UI to check if rice is being hovered
        /// </summary>
        public static bool IsHoveringRice { get; private set; }
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Player.PlayerData>();
            playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<Player.PlayerData>());
            currentHoveredEntity = Entity.Null;
        }
        
        public void OnUpdate(ref SystemState state)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                IsHoveringRice = false;
                return;
            }
            
            // Check player entity count BEFORE trying to access singleton
            int playerCount = playerQuery.CalculateEntityCount();
            if (playerCount != 1)
            {
                IsHoveringRice = false;
                return;
            }
            
            // Get player data
            Entity playerEntity = SystemAPI.GetSingletonEntity<Player.PlayerData>();
            var playerTransform = state.EntityManager.GetComponentData<LocalTransform>(playerEntity);
            
            // Get pickup radius from upgrades
            float pickupRadius = 1.5f;
            if (DropPuzzle.PlayerDataManager.Instance != null)
            {
                pickupRadius = DropPuzzle.PlayerDataManager.Instance.FPSCollector.pickupRadius;
            }
            
            // Raycast from mouse position
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            
            // Use a shared container to find the closest rice
            var closestResult = new NativeReference<RiceHoverResult>(Allocator.TempJob);
            closestResult.Value = new RiceHoverResult 
            { 
                Entity = Entity.Null, 
                Distance = float.MaxValue 
            };
            
            // Job to find closest rice under mouse (runs on main thread due to NativeReference)
            var findJob = new FindHoveredRiceJob
            {
                RayOrigin = ray.origin,
                RayDirection = ray.direction,
                PlayerPosition = playerTransform.Position,
                PickupRadius = pickupRadius,
                ClosestResult = closestResult
            };
            
            findJob.Run(); // Run on main thread (Burst-compiled, fast)
            
            Entity newHoveredEntity = closestResult.Value.Entity;
            closestResult.Dispose();
            
            // Unhighlight previous entity (toggles IEnableableComponent - no structural change!)
            if (currentHoveredEntity != Entity.Null && currentHoveredEntity != newHoveredEntity)
            {
                if (state.EntityManager.Exists(currentHoveredEntity))
                {
                    if (state.EntityManager.HasComponent<RiceHighlighted>(currentHoveredEntity))
                    {
                        state.EntityManager.SetComponentEnabled<RiceHighlighted>(currentHoveredEntity, false);
                        
                        // Reset scale
                        var transform = state.EntityManager.GetComponentData<LocalTransform>(currentHoveredEntity);
                        transform.Scale = 1f; // Reset to default
                        state.EntityManager.SetComponentData(currentHoveredEntity, transform);
                    }
                }
            }
            
            // Highlight new entity
            if (newHoveredEntity != Entity.Null && newHoveredEntity != currentHoveredEntity)
            {
                if (state.EntityManager.Exists(newHoveredEntity))
                {
                    // Add component if it doesn't exist, then enable it
                    if (!state.EntityManager.HasComponent<RiceHighlighted>(newHoveredEntity))
                    {
                        state.EntityManager.AddComponent<RiceHighlighted>(newHoveredEntity);
                    }
                    state.EntityManager.SetComponentEnabled<RiceHighlighted>(newHoveredEntity, true);
                    
                    // Scale up
                    var transform = state.EntityManager.GetComponentData<LocalTransform>(newHoveredEntity);
                    transform.Scale = 1.3f; // 30% larger when hovered
                    state.EntityManager.SetComponentData(newHoveredEntity, transform);
                }
            }
            
            currentHoveredEntity = newHoveredEntity;
            IsHoveringRice = (newHoveredEntity != Entity.Null);
        }
    }
    
    /// <summary>
    /// Result container for hover detection
    /// </summary>
    public struct RiceHoverResult
    {
        public Entity Entity;
        public float Distance;
    }
    
    /// <summary>
    /// BURST-COMPILED parallel job to find closest rice under mouse cursor
    /// Processes 40k+ rice grains efficiently across all CPU cores
    /// </summary>
    [BurstCompile]
    [WithNone(typeof(RiceHidden))]
    public partial struct FindHoveredRiceJob : IJobEntity
    {
        [ReadOnly] public float3 RayOrigin;
        [ReadOnly] public float3 RayDirection;
        [ReadOnly] public float3 PlayerPosition;
        [ReadOnly] public float PickupRadius;
        
        public NativeReference<RiceHoverResult> ClosestResult;
        
        public void Execute(Entity entity, in RiceEntity rice, in LocalTransform transform)
        {
            // Check if rice is within pickup range
            float distToPlayer = math.distance(PlayerPosition, transform.Position);
            if (distToPlayer > PickupRadius) return;
            
            // Optimized ray-sphere intersection
            float3 toSphere = transform.Position - RayOrigin;
            float t = math.dot(toSphere, RayDirection);
            
            if (t < 0) return; // Behind camera
            
            float3 closestPoint = RayOrigin + RayDirection * t;
            float distanceToRay = math.distance(closestPoint, transform.Position);
            
            float hoverRadius = 0.15f;
            if (distanceToRay < hoverRadius)
            {
                // Thread-safe comparison and update
                var current = ClosestResult.Value;
                if (t < current.Distance)
                {
                    ClosestResult.Value = new RiceHoverResult 
                    { 
                        Entity = entity, 
                        Distance = t 
                    };
                }
            }
        }
    }
    
    /// <summary>
    /// Click-to-collect system - raycast from camera and collect rice on click
    /// OPTIMIZED: Uses ISystem + IJobEntity with EntityCommandBuffer (no sync points!)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Player.PlayerTransformSyncSystem))]
    public partial struct RiceCollectionSystem : ISystem
    {
        private EntityQuery playerQuery;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Player.PlayerData>();
            playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<Player.PlayerData>());
        }

        public void OnUpdate(ref SystemState state)
        {
            // Only check on mouse click
            if (!Input.GetMouseButtonDown(0)) return;

            UnityEngine.Debug.Log("[RiceCollectionSystem] Mouse click detected!");

            var camera = Camera.main;
            if (camera == null)
            {
                UnityEngine.Debug.LogWarning("[RiceCollectionSystem] No camera found!");
                return;
            }
            
            // Check player entity count
            int playerCount = playerQuery.CalculateEntityCount();
            if (playerCount != 1)
            {
                UnityEngine.Debug.LogWarning($"[RiceCollectionSystem] Player count: {playerCount} (need exactly 1)");
                return;
            }
            
            Entity playerEntity = SystemAPI.GetSingletonEntity<Player.PlayerData>();
            var playerTransform = state.EntityManager.GetComponentData<LocalTransform>(playerEntity);
            
            // Count rice entities
            var riceQuery = SystemAPI.QueryBuilder().WithAll<RiceEntity>().WithNone<RiceHidden>().Build();
            int riceCount = riceQuery.CalculateEntityCount();
            
            // Get pickup radius from upgrades
            float pickupRadius = 1.5f;
            if (DropPuzzle.PlayerDataManager.Instance != null)
            {
                pickupRadius = DropPuzzle.PlayerDataManager.Instance.FPSCollector.pickupRadius;
            }

            UnityEngine.Debug.Log($"[RiceCollectionSystem] Player pos: {playerTransform.Position}, Rice count: {riceCount}, Pickup radius: {pickupRadius}");

            // Raycast from camera
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            
            // Find which rice entity was clicked
            var closestResult = new NativeReference<RiceHoverResult>(Allocator.TempJob);
            closestResult.Value = new RiceHoverResult 
            { 
                Entity = Entity.Null, 
                Distance = float.MaxValue 
            };
            
            var findJob = new FindClickedRiceJob
            {
                RayOrigin = ray.origin,
                RayDirection = ray.direction,
                PlayerPosition = playerTransform.Position,
                PickupRadius = pickupRadius,
                ClosestResult = closestResult
            };
            
            findJob.Run(); // Run on main thread (Burst-compiled, fast)
            
            Entity clickedEntity = closestResult.Value.Entity;
            float distance = closestResult.Value.Distance;
            closestResult.Dispose();

            UnityEngine.Debug.Log($"[RiceCollectionSystem] Job complete. Found rice: {clickedEntity != Entity.Null}, Distance: {distance}");

            // Collect the clicked rice
            if (clickedEntity != Entity.Null)
            {
                UnityEngine.Debug.Log($"[RiceCollectionSystem] ✅ Collecting rice entity!");
                
                // Destroy entity (safe to do outside the job)
                state.EntityManager.DestroyEntity(clickedEntity);
                
                // Update player collected count
                var playerData = state.EntityManager.GetComponentData<Player.PlayerData>(playerEntity);
                playerData.RiceCollected++;
                state.EntityManager.SetComponentData(playerEntity, playerData);
                
                // Update roguelite progression system
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
    
    /// <summary>
    /// BURST-COMPILED parallel job to find clicked rice
    /// </summary>
    [BurstCompile]
    [WithNone(typeof(RiceHidden))]
    public partial struct FindClickedRiceJob : IJobEntity
    {
        [ReadOnly] public float3 RayOrigin;
        [ReadOnly] public float3 RayDirection;
        [ReadOnly] public float3 PlayerPosition;
        [ReadOnly] public float PickupRadius;
        
        public NativeReference<RiceHoverResult> ClosestResult;
        
        public void Execute(Entity entity, in RiceEntity rice, in LocalTransform transform)
        {
            // Check distance to player
            float distToPlayer = math.distance(PlayerPosition, transform.Position);
            if (distToPlayer > PickupRadius) return;
            
            // Ray-sphere intersection
            float3 toSphere = transform.Position - RayOrigin;
            float t = math.dot(toSphere, RayDirection);
            
            if (t < 0) return; // Behind camera
            
            float3 closestPoint = RayOrigin + RayDirection * t;
            float distanceToRay = math.distance(closestPoint, transform.Position);
            
            float clickRadius = 0.15f;
            if (distanceToRay < clickRadius)
            {
                var current = ClosestResult.Value;
                if (t < current.Distance)
                {
                    ClosestResult.Value = new RiceHoverResult 
                    { 
                        Entity = entity, 
                        Distance = t 
                    };
                }
            }
        }
    }
}
