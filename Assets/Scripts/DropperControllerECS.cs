using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// ECS-based dropper controller - spawns ECS entities instead of GameObjects
    /// MUCH faster for 1000+ balls
    /// </summary>
    public class DropperControllerECS : MonoBehaviour
    {
        [Header("Movement")]
        public float MoveSpeed = 2f;
        public float MoveRange = 5f;
        
        [Header("Dropping")]
        public Transform DropPoint;
        public float DropForce = 0f;
        
        [Header("Ball Settings")]
        public float BallRadius = 0.17f; // Reduced from 0.5 - much smaller balls
        public float BallMass = 1f;
        public float BallBounciness = 0.3f;
        public float BallFriction = 0.1f;
        
        [Header("Visual")]
        public GameObject BallVisualPrefab; // For rendering only
        
        [Header("Settings")]
        public bool DropAllAtOnce = true;
        public float DropInterval = 0.25f; // Slower spawn to prevent stacking
        public bool useInventorySystem = true; // Use crafted riceballs from inventory
        
        private float moveDirection = 1f;
        private bool isDropping = false;
        private bool hasDropped = false;
        
        private EntityManager entityManager;
        private Entity ballArchetype;
        
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        private DayNightCycleManager cycleManager => DayNightCycleManager.Instance;
        private BallDropCompletionManager completionManager;
        private BallDropAudioManager audioManager;
        
        private void Start()
        {
            // CRITICAL: Force useInventorySystem=true for tutorial/proper gameplay
            useInventorySystem = true;
            
            if (DropPoint == null)
            {
                DropPoint = transform;
            }
            
            // Get EntityManager
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            
            // Find completion manager
            completionManager = FindObjectOfType<BallDropCompletionManager>();
            
            // Find audio manager
            audioManager = FindObjectOfType<BallDropAudioManager>();
            
            Debug.Log("[DropperControllerECS] ECS Dropper ready! Press SPACE to drop (Night only)");
        }
        
        private void Update()
        {
            // Move dropper back and forth
            if (!isDropping)
            {
                MoveDropper();
            }
            
            // Check for drop input
            if (Input.GetKeyDown(KeyCode.Space) && !hasDropped)
            {
                // Set flag immediately to prevent re-triggering
                hasDropped = true;
                
                // Check if it's night time (can only drop at night)
                if (cycleManager != null && !cycleManager.CanEnterBallDrop())
                {
                    Debug.LogWarning("[DropperControllerECS] ⚠️ Can only drop balls at NIGHT!");
                    hasDropped = false; // Reset flag since we didn't actually drop
                    return;
                }
                
                // Notify audio manager (only after night check passes)
                if (audioManager != null)
                {
                    audioManager.OnDropStarted();
                }
                
                if (DropAllAtOnce)
                {
                    StartCoroutine(DropAllBallsECS());
                }
                else
                {
                    if (useInventorySystem && playerData != null)
                    {
                        SpawnBallFromInventory();
                    }
                    else
                    {
                        SpawnBallEntity(null);
                    }
                }
            }
        }
        
        private void MoveDropper()
        {
            float newX = transform.position.x + (moveDirection * MoveSpeed * Time.deltaTime);
            
            if (newX > MoveRange)
            {
                newX = MoveRange;
                moveDirection = -1f;
            }
            else if (newX < -MoveRange)
            {
                newX = -MoveRange;
                moveDirection = 1f;
            }
            
            transform.position = new Vector3(newX, transform.position.y, transform.position.z);
        }
        
        private System.Collections.IEnumerator DropAllBallsECS()
        {
            isDropping = true;
            // Note: hasDropped is already set to true in Update() when space is pressed
            
            // Start completion tracking
            if (completionManager != null)
            {
                completionManager.StartDropSession();
            }
            
            // Determine how many balls to drop
            int ballsToDrop;
            if (useInventorySystem && playerData != null)
            {
                ballsToDrop = playerData.Inventory.GetTotalBalls();
                Debug.Log($"[DropperControllerECS] 🎯 Dropping {ballsToDrop} riceballs from inventory!");
            }
            else if (DropPuzzleManager.Instance != null)
            {
                ballsToDrop = DropPuzzleManager.Instance.RiceBallsAvailable;
                Debug.Log($"[DropperControllerECS] 🎯 Dropping {ballsToDrop} balls (legacy mode)");
            }
            else
            {
                Debug.LogError("[DropperControllerECS] No ball source available!");
                yield break;
            }
            
            if (ballsToDrop == 0)
            {
                Debug.LogWarning("[DropperControllerECS] ⚠️ No balls to drop! Craft some riceballs first.");
                
                // Trigger completion immediately so player can return
                if (completionManager != null)
                {
                    completionManager.isDropActive = true;
                    completionManager.isComplete = true;
                }
                
                isDropping = false;
                hasDropped = false; // Allow trying again
                yield break;
            }
            
            int successCount = 0;
            
            for (int i = 0; i < ballsToDrop; i++)
            {
                // Use inventory system if enabled
                if (useInventorySystem && playerData != null)
                {
                    SpawnBallFromInventory();
                }
                else
                {
                    // Legacy mode
                    if (DropPuzzleManager.Instance != null)
                    {
                        DropPuzzleManager.Instance.TryDropBall();
                    }
                    SpawnBallEntity(null); // Standard ball
                }
                
                successCount++;
                
                // Log first 5 spawns
                if (successCount <= 5)
                {
                    Debug.Log($"[DropperControllerECS] Ball {successCount} spawned at X={DropPoint.position.x:F2}");
                }
                
                yield return new WaitForSeconds(DropInterval);
            }
            
            Debug.Log($"[DropperControllerECS] ✅ Dropped {successCount} balls!");
            
            // Wait then verify
            yield return new WaitForSeconds(1f);
            
            var query = entityManager.CreateEntityQuery(typeof(RiceBallTag));
            int actualCount = query.CalculateEntityCount();
            query.Dispose();
            
            Debug.Log($"[DropperControllerECS] 🔍 Verification: {actualCount} entities exist in ECS world");
        }
        
        /// <summary>
        /// Spawn a ball from inventory (consumes 1 riceball)
        /// </summary>
        private void SpawnBallFromInventory()
        {
            RiceBallQuality? quality = playerData.UseRiceBall();
            
            if (quality == null)
            {
                Debug.LogWarning("[DropperECS] No riceballs in inventory!");
                return;
            }
            
            SpawnBallEntity(quality.Value);
        }
        
        /// <summary>
        /// Spawn ball entity with specific quality (or null for standard)
        /// </summary>
        private void SpawnBallEntity(RiceBallQuality? quality)
        {
            // Add random offset to prevent stacking
            float randomX = UnityEngine.Random.Range(-BallRadius * 3f, BallRadius * 3f);
            float randomY = UnityEngine.Random.Range(0f, BallRadius * 0.5f);
            
            // CRITICAL: Force Z=0 to match wall positions (walls are at Z=0 in GridPuzzleLoader)
            float3 spawnPos = new float3(
                DropPoint.position.x + randomX,
                DropPoint.position.y - 0.3f + randomY,
                0f  // ← Must be 0 to match walls!
            );
            
            // Create ECS entity
            Entity ballEntity = entityManager.CreateEntity(
                typeof(LocalTransform),
                typeof(RiceBallPhysics),
                typeof(RiceBallTag),
                typeof(RiceBallType),
                typeof(RiceBallGateTracker),
                typeof(RiceBallLifetime)
            );
            
            if (ballEntity == Entity.Null)
            {
                Debug.LogError("[DropperECS] ❌ Failed to create entity!");
                return;
            }
            
            // Set transform
            entityManager.SetComponentData(ballEntity, new LocalTransform
            {
                Position = spawnPos,
                Rotation = quaternion.identity,
                Scale = BallRadius * 2f
            });
            
            // Convert quality to ball type
            RiceBallType ballType = GetBallTypeFromQuality(quality);
            entityManager.SetComponentData(ballEntity, ballType);
            
            // Set physics
            entityManager.SetComponentData(ballEntity, new RiceBallPhysics
            {
                Position = spawnPos,
                Velocity = new float3(0, -DropForce, 0),
                Radius = BallRadius,
                Mass = BallMass,
                Bounciness = BallBounciness,
                Friction = BallFriction,
                IsSleeping = false,
                SleepVelocityThreshold = 0.015f // Very aggressive sleep for 1000+ ball performance
            });
            
            // Set gate tracker
            entityManager.SetComponentData(ballEntity, new RiceBallGateTracker
            {
                HitGatesMask = 0
            });
            
            // Set lifetime
            entityManager.SetComponentData(ballEntity, new RiceBallLifetime
            {
                SpawnTime = Time.time,
                MaxLifetime = 30f,
                DestroyBelowY = -20f
            });
        }
        
        /// <summary>
        /// Map riceball quality to ball type properties
        /// Fine: Standard (1x)
        /// Good: Bonus Points (2x)
        /// Great: Multiplier Booster (+1)
        /// Excellent: Lucky Super Ball (5x)
        /// </summary>
        private RiceBallType GetBallTypeFromQuality(RiceBallQuality? quality)
        {
            if (!quality.HasValue)
            {
                // Standard ball (no quality)
                return new RiceBallType
                {
                    TypeID = 0,
                    PointsMultiplier = 1.0f,
                    MultiplierBoost = 0f,
                    IsHarmful = false
                };
            }
            
            switch (quality.Value)
            {
                case RiceBallQuality.Fine:
                    // Standard ball (1x value)
                    return new RiceBallType
                    {
                        TypeID = 0,
                        PointsMultiplier = 1.0f,
                        MultiplierBoost = 0f,
                        IsHarmful = false
                    };
                    
                case RiceBallQuality.Good:
                    // Bonus points ball (2x value)
                    return new RiceBallType
                    {
                        TypeID = 1,
                        PointsMultiplier = 2.0f,
                        MultiplierBoost = 0f,
                        IsHarmful = false
                    };
                    
                case RiceBallQuality.Great:
                    // Multiplier booster (+1 to gates, 4x value)
                    return new RiceBallType
                    {
                        TypeID = 2,
                        PointsMultiplier = 1.0f,
                        MultiplierBoost = 1f,
                        IsHarmful = false
                    };
                    
                case RiceBallQuality.Excellent:
                    // Lucky super ball (5x points, 8x value)
                    return new RiceBallType
                    {
                        TypeID = 4,
                        PointsMultiplier = 5.0f,
                        MultiplierBoost = 0f,
                        IsHarmful = false
                    };
                    
                default:
                    return new RiceBallType
                    {
                        TypeID = 0,
                        PointsMultiplier = 1.0f,
                        MultiplierBoost = 0f,
                        IsHarmful = false
                    };
            }
        }
        
        private void OnGUI()
        {
            // Show instruction and ball count
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 18;
            style.alignment = TextAnchor.MiddleLeft;
            style.normal.textColor = Color.white;
            
            if (!hasDropped)
            {
                // Show instruction
                int ballCount = 0;
                if (useInventorySystem && playerData != null)
                {
                    ballCount = playerData.Inventory.GetTotalBalls();
                }
                else if (DropPuzzleManager.Instance != null)
                {
                    ballCount = DropPuzzleManager.Instance.RiceBallsAvailable;
                }
                
                if (ballCount > 0)
                {
                    style.normal.textColor = Color.green;
                    GUI.Label(new Rect(10, 60, 400, 30), 
                        $"Press SPACE to drop {ballCount} riceballs", style);
                }
                else
                {
                    style.normal.textColor = Color.red;
                    GUI.Label(new Rect(10, 60, 400, 30), 
                        "⚠️ No riceballs! Press [Esc] to go back", style);
                }
            }
        }
        
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Vector3 leftPos = new Vector3(-MoveRange, transform.position.y, transform.position.z);
            Vector3 rightPos = new Vector3(MoveRange, transform.position.y, transform.position.z);
            Gizmos.DrawLine(leftPos, rightPos);
            Gizmos.DrawWireSphere(leftPos, 0.2f);
            Gizmos.DrawWireSphere(rightPos, 0.2f);
        }
    }
}
