using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Vampire.Rice
{
    public class RiceSpawnPointAuthoring : MonoBehaviour
    {
        [Header("Zone Configuration")]
        [Tooltip("Name/ID for this spawn zone")]
        public string ZoneName = "Zone1";
        
        [Header("Floor Elements")]
        [Tooltip("Floor objects in this zone (rice will spawn on these)")]
        public GameObject[] FloorObjects;
        
        [Tooltip("Use raycasting to find floor height (if FloorObjects not set)")]
        public bool UseRaycastForFloor = false;
        
        [Tooltip("Layer mask for floor detection (when using raycast)")]
        public LayerMask FloorLayerMask = 1; // Default layer
        
        [Tooltip("Manual floor Y override (used if no floor objects and no raycast)")]
        public float ManualFloorY = 0f;
        
        [Header("Spawn Area Configuration")]
        [Tooltip("Inset from floor edges to prevent spawning near walls (in units)")]
        public float WallMargin = 0.2f;
        
        [Header("Spawn Settings")]
        [Tooltip("Number of rice to spawn in this area")]
        public int Count = 40000;
        
        [Tooltip("Height offset above floor to spawn rice (prevent z-fighting)")]
        public float SpawnHeightOffset = 0.01f;
        
        [Header("Advanced Settings")]
        [Tooltip("Randomize spawn height within this range")]
        public float HeightVariation = 0.5f;
        
        /// <summary>
        /// Calculate spawn area bounds from floor objects
        /// </summary>
        public Bounds GetSpawnBounds()
        {
            if (FloorObjects == null || FloorObjects.Length == 0)
            {
                // Fallback: use transform position with default size
                return new Bounds(transform.position, new Vector3(10f, 2f, 10f));
            }
            
            // Calculate combined bounds of all floor objects
            Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);
            bool initialized = false;
            
            foreach (var floor in FloorObjects)
            {
                if (floor == null) continue;
                
                Renderer renderer = floor.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (!initialized)
                    {
                        combinedBounds = renderer.bounds;
                        initialized = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            
            if (!initialized)
            {
                // No valid renderers found
                return new Bounds(transform.position, new Vector3(10f, 2f, 10f));
            }
            
            // Apply wall margin inset
            Vector3 insetSize = combinedBounds.size - new Vector3(WallMargin * 2f, 0, WallMargin * 2f);
            insetSize.x = Mathf.Max(0.5f, insetSize.x); // Minimum size
            insetSize.z = Mathf.Max(0.5f, insetSize.z);
            
            return new Bounds(combinedBounds.center, insetSize);
        }
        
        /// <summary>
        /// Get floor Y position at a specific XZ coordinate
        /// </summary>
        public float GetFloorYAt(Vector3 position)
        {
            // Priority 1: Use floor objects if available
            if (FloorObjects != null && FloorObjects.Length > 0)
            {
                float closestY = ManualFloorY;
                float closestDist = float.MaxValue;
                
                // Find the closest floor object
                foreach (var floor in FloorObjects)
                {
                    if (floor == null) continue;
                    
                    // Get floor bounds
                    Renderer renderer = floor.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        Bounds bounds = renderer.bounds;
                        
                        // Check if position is within floor bounds (XZ plane)
                        if (position.x >= bounds.min.x && position.x <= bounds.max.x &&
                            position.z >= bounds.min.z && position.z <= bounds.max.z)
                        {
                            return bounds.max.y; // Top of the floor
                        }
                        
                        // Track closest floor as fallback
                        float dist = Vector3.Distance(
                            new Vector3(position.x, 0, position.z), 
                            new Vector3(floor.transform.position.x, 0, floor.transform.position.z)
                        );
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestY = bounds.max.y;
                        }
                    }
                }
                
                return closestY;
            }
            
            // Priority 2: Use raycast if enabled
            if (UseRaycastForFloor)
            {
                RaycastHit hit;
                Vector3 rayOrigin = new Vector3(position.x, position.y + 10f, position.z);
                if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 100f, FloorLayerMask))
                {
                    return hit.point.y;
                }
            }
            
            // Priority 3: Use manual floor Y
            return ManualFloorY;
        }
        
        private void OnDrawGizmos()
        {
            // Draw spawn area bounds
            Bounds spawnBounds = GetSpawnBounds();
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(spawnBounds.center, spawnBounds.size);
            
            // Draw floor objects if available
            if (FloorObjects != null && FloorObjects.Length > 0)
            {
                Gizmos.color = Color.yellow;
                foreach (var floor in FloorObjects)
                {
                    if (floor != null)
                    {
                        Renderer renderer = floor.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            Gizmos.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
                        }
                    }
                }
            }
        }
    }

    public class RiceSpawnPointBaker : Baker<RiceSpawnPointAuthoring>
    {
        public override void Bake(RiceSpawnPointAuthoring authoring)
        {
            UnityEngine.Debug.Log($"[RiceSpawnPointBaker] BAKING Zone '{authoring.ZoneName}' at position {authoring.transform.position}, Count={authoring.Count}, FloorObjects={authoring.FloorObjects?.Length ?? 0}");
            
            var entity = GetEntity(TransformUsageFlags.None);
            
            // Calculate spawn bounds from floor objects
            Bounds spawnBounds = authoring.GetSpawnBounds();
            
            // Calculate average floor Y from floor objects if available
            float floorY = authoring.ManualFloorY;
            if (authoring.FloorObjects != null && authoring.FloorObjects.Length > 0)
            {
                float totalY = 0f;
                int validCount = 0;
                
                foreach (var floor in authoring.FloorObjects)
                {
                    if (floor != null)
                    {
                        Renderer renderer = floor.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            totalY += renderer.bounds.max.y;
                            validCount++;
                        }
                    }
                }
                
                if (validCount > 0)
                {
                    floorY = totalY / validCount;
                    UnityEngine.Debug.Log($"[RiceSpawnPointBaker] Calculated floor Y from {validCount} floor objects: {floorY}, Margin: {authoring.WallMargin}");
                }
            }
            
            AddComponent(entity, new RiceSpawnPoint
            {
                Center = (float3)spawnBounds.center,
                Size = (float3)spawnBounds.size,
                Count = authoring.Count,
                SpawnOnFloor = true,
                FloorY = floorY + authoring.SpawnHeightOffset
            });
            
            UnityEngine.Debug.Log($"[RiceSpawnPointBaker] ✅ RiceSpawnPoint component added for zone '{authoring.ZoneName}' - Size: {spawnBounds.size}");
        }
    }
}
