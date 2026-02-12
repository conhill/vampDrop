using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// ECS system for collision between balls and static GameObject walls
    /// Much faster than Unity's physics for simple sphere-box collisions
    /// </summary>
    public class RiceBallWallCollisionSystem : MonoBehaviour
    {
        private EntityQuery ballQuery;
        private EntityManager entityManager;
        
        // Cached wall colliders
        private struct WallData
        {
            public float3 Center;
            public float3 Size;
            public quaternion Rotation;
            public float Bounciness;
        }
        
        private NativeArray<WallData> walls;
        private bool wallsInitialized = false;
        
        private void Start()
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            
            ballQuery = entityManager.CreateEntityQuery(
                typeof(LocalTransform),
                typeof(RiceBallPhysics),
                typeof(RiceBallTag)
            );
            
            // Wait a frame for walls to spawn, then cache them
            Invoke(nameof(CacheWalls), 0.5f);
        }
        
        private void CacheWalls()
        {
            // Find all wall objects with "Wall" tag
            GameObject[] wallObjects = GameObject.FindGameObjectsWithTag("Wall");
            
            Debug.Log($"[WallCollision] Found {wallObjects.Length} GameObjects with 'Wall' tag");
            
            if (wallObjects.Length == 0)
            {
                Debug.LogError("[WallCollision] ❌ NO WALLS FOUND! You MUST create 'Wall' tag: Edit → Project Settings → Tags → Add 'Wall'");
                walls = new NativeArray<WallData>(0, Allocator.Persistent);
                wallsInitialized = true;
                return;
            }
            
            NativeList<WallData> wallList = new NativeList<WallData>(Allocator.Temp);
            
            foreach (GameObject wallObj in wallObjects)
            {
                // CRITICAL: Use BoxCollider local size MULTIPLIED by transform scale!
                // boxCollider.size is unscaled (1,1,1 for cube primitive)
                // We need actual world size for collision math
                BoxCollider boxCol = wallObj.GetComponent<BoxCollider>();
                if (boxCol != null)
                {
                    // Get ACTUAL size: localSize * localScale
                    Vector3 localSize = boxCol.size;
                    Vector3 scale = wallObj.transform.localScale;
                    float3 actualSize = new float3(
                        localSize.x * scale.x,
                        localSize.y * scale.y,
                        localSize.z * scale.z
                    );
                    
                    float3 worldCenter = boxCol.bounds.center;
                    
                    wallList.Add(new WallData
                    {
                        Center = worldCenter,
                        Size = actualSize, // SCALED size!
                        Rotation = wallObj.transform.rotation,
                        Bounciness = 0.3f
                    });
                    
                    Debug.Log($"[WallCollision] Wall '{wallObj.name}' | Center:{worldCenter} | Size:{actualSize} | Rot:{wallObj.transform.eulerAngles}");
                }
            }
            
            walls = new NativeArray<WallData>(wallList.AsArray(), Allocator.Persistent);
            wallList.Dispose();
            
            wallsInitialized = true;
            Debug.Log($"[WallCollision] Cached {walls.Length} walls for ECS collision");
            
            if (walls.Length == 0)
            {
                Debug.LogWarning("[WallCollision] ❌ No walls found! Did you create the 'Wall' tag and apply it to walls?");
            }
        }
        
        private void FixedUpdate()
        {
            // Only run in DropPuzzle scene
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "DropPuzzle") return;
            
            if (!wallsInitialized || ballQuery == null || walls.Length == 0) return;
            
            // ONLY run collision in FixedUpdate for physics consistency
            PerformCollisionCheck();
        }
        
        // REMOVED Update() - was checking collision twice per frame!
        
        private void PerformCollisionCheck()
        {
            // PERFORMANCE: Only check moving balls - critical optimization!
            // Get all balls but filter to only awake ones
            NativeArray<Entity> entities = ballQuery.ToEntityArray(Allocator.Temp);
            
            int checkedCount = 0;
            
            foreach (Entity entity in entities)
            {
                if (!entityManager.Exists(entity)) continue;
                
                RiceBallPhysics physics = entityManager.GetComponentData<RiceBallPhysics>(entity);
                
                // PERFORMANCE: Skip sleeping balls - they're not moving!
                if (physics.IsSleeping) continue;
                
                checkedCount++;
                
                float3 ballPos = physics.Position;
                float ballRadius = physics.Radius;
                bool collided = false;
                
                // Check against all walls
                for (int i = 0; i < walls.Length; i++)
                {
                    WallData wall = walls[i];
                    
                    // LOCAL SPACE COLLISION - Handles rotated walls correctly!
                    // 1. Convert ball position to wall's local space
                    float3 localBallPos = math.mul(math.inverse(wall.Rotation), ballPos - wall.Center);
                    
                    // 2. Clamp in local space (AABB works here because wall is "upright" in its own space)
                    float3 halfSize = wall.Size * 0.5f;
                    float3 localClosest = math.clamp(localBallPos, -halfSize, halfSize);
                    
                    // 3. Calculate distance in local space
                    float3 localDelta = localBallPos - localClosest;
                    float distance = math.length(localDelta);
                    
                    // Collision detected
                    if (distance < ballRadius)
                    {
                        // 4. Convert collision normal back to world space
                        // FIX #4: Safe normalize to prevent infinity
                        float3 localNormal = (distance > 0.0001f) ? (localDelta / distance) : new float3(0, 1, 0);
                        float3 worldNormal = math.mul(wall.Rotation, localNormal);
                        float overlap = ballRadius - distance;
                        
                        // FIX #1: Clamp overlap to prevent launching (max 50% of radius per frame)
                        float safeOverlap = math.min(overlap, ballRadius * 0.5f);
                        
                        // FIX #1: Reduced multiplier (1.1f instead of 1.5f)
                        physics.Position += worldNormal * safeOverlap * 1.1f;
                        
                        // Only affect velocity component perpendicular to wall
                        float normalVelocity = math.dot(physics.Velocity, worldNormal);
                        
                        if (normalVelocity < 0) // Ball moving into wall
                        {
                            // Reflect normal velocity with bounce
                            physics.Velocity -= worldNormal * normalVelocity * (1f + wall.Bounciness);
                            
                            // Extra dampening if ball is deep inside wall (prevents tunneling)
                            if (overlap > ballRadius * 0.5f)
                            {
                                physics.Velocity *= 0.5f; // Cut velocity in half to prevent phase-through
                            }
                            
                            // CRITICAL: Minimal friction for sliding!
                            // For diagonal walls (slopes), we want balls to slide smoothly
                            // Only apply 2% friction on tangent velocity (not 10%)
                            float3 tangentVelocity = physics.Velocity - worldNormal * math.dot(physics.Velocity, worldNormal);
                            physics.Velocity -= tangentVelocity * 0.02f * physics.Friction;
                        }
                        
                        collided = true;
                        physics.IsSleeping = false;
                    }
                }
                
                // Update entity
                if (collided)
                {
                    entityManager.SetComponentData(entity, physics);
                    
                    // Also update transform
                    LocalTransform transform = entityManager.GetComponentData<LocalTransform>(entity);
                    transform.Position = physics.Position;
                    entityManager.SetComponentData(entity, transform);
                }
            }
            
            entities.Dispose();
            
            // Performance logging every 5 seconds
            if (Time.frameCount % 300 == 0 && checkedCount > 0)
            {
                Debug.Log($"[WallCollision] Checked {checkedCount} awake balls vs {walls.Length} walls");
            }
        }
        
        private void OnDestroy()
        {
            if (ballQuery != null)
            {
                ballQuery.Dispose();
            }
            
            if (walls.IsCreated)
            {
                walls.Dispose();
            }
        }
    }
}
