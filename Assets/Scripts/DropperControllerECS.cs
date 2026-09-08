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

        [Tooltip("How many balls are released per DropInterval tick. 1 = the original " +
                 "one-at-a-time trickle. Raise it to actually get a big inventory on screen " +
                 "at once: at DropInterval 0.1 a 2000-ball drop takes 200s one-at-a-time, and " +
                 "since a ball is culled a few seconds after it leaves the board, only ~66 are " +
                 "ever alive simultaneously. DebugUpgradeUI's load-balls key raises this on the " +
                 "scene's dropper; it is NOT restored afterwards, so exit play mode (or reset " +
                 "it by hand) before judging normal drop pacing.")]
        [Min(1)]
        public int BallsPerInterval = 1;
        
        /// <summary>Fired when the player commits to dropping (camera listens to zoom out).</summary>
        public static event System.Action OnAnyDropStarted;

        private bool isDropping = false;
        private bool hasDropped = false;
        private float _dropCenterX = 0f;

        // ── OnGUI cache — allocated once, not every frame ─────────────────
        private GUIStyle _guiStyle;
        private bool     _guiStyleBuilt;
        private int      _lastGuiBallCount = -1;
        private string   _guiLabelStr     = "";

        private EntityManager entityManager;
        private EntityArchetype ballArchetype;
        
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        private DayNightCycleManager cycleManager => DayNightCycleManager.Instance;
        private BallDropCompletionManager completionManager;
        private BallDropAudioManager audioManager;
        
        /// <summary>
        /// Called by PuzzlePrefabLoader after repositioning so the oscillation range
        /// is centred on the puzzle, not on world X=0.
        /// </summary>
        public void SetDropCenter(float x) => _dropCenterX = x;

        private void Start()
        {
            // CRITICAL: Force useInventorySystem=true for tutorial/proper gameplay
            useInventorySystem = true;

            _dropCenterX = transform.position.x;
            DropPoint = transform; // always self — PuzzlePrefabLoader repositions transform, not DropPoint
            
            // Get EntityManager
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            // Pre-create archetype once — reused for all spawns, avoids archetype lookup overhead
            ballArchetype = entityManager.CreateArchetype(
                typeof(LocalTransform),
                typeof(RiceBallPhysics),
                typeof(RiceBallTag),
                typeof(RiceBallType),
                typeof(RiceBallGateTracker),
                typeof(RiceBallLifetime)
            );

            // Find completion manager
            completionManager = FindObjectOfType<BallDropCompletionManager>();
            
            // Find audio manager
            audioManager = FindObjectOfType<BallDropAudioManager>();
            
            // Debug.Log("[DropperControllerECS] ECS Dropper ready! Press SPACE to drop (Night only)");
        }
        
        private void Update()
        {
            // Knox moves left/right with A/D — allowed at all times, even during drop
            HandleKnoxMovement();
            
            // Check for drop input — camera controller gates this during assessment
            if (Input.GetKeyDown(KeyCode.Space) && !hasDropped
                && DropPuzzleCameraController.AllowDrop)
            {
                // Set flag immediately to prevent re-triggering
                hasDropped = true;

                OnAnyDropStarted?.Invoke();

                // Notify audio manager
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
        
        private void HandleKnoxMovement()
        {
            if (!DropPuzzleFlowManager.AllowPlayerInput) return;

            float input = 0f;
            if (Input.GetKey(KeyCode.A)) input = 1f;
            else if (Input.GetKey(KeyCode.D)) input = -1f;
            if (input == 0f) return;

            float newX = Mathf.Clamp(
                transform.position.x + input * MoveSpeed * Time.deltaTime,
                _dropCenterX - MoveRange,
                _dropCenterX + MoveRange);
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
                // Debug.Log($"[DropperControllerECS] 🎯 Dropping {ballsToDrop} riceballs from inventory!");
            }
            else if (DropPuzzleManager.Instance != null)
            {
                ballsToDrop = DropPuzzleManager.Instance.RiceBallsAvailable;
                // Debug.Log($"[DropperControllerECS] 🎯 Dropping {ballsToDrop} balls (legacy mode)");
            }
            else
            {
                // Debug.LogError("[DropperControllerECS] No ball source available!");
                yield break;
            }
            
            if (ballsToDrop == 0)
            {
                // Debug.LogWarning("[DropperControllerECS] ⚠️ No balls to drop! Craft some riceballs first.");
                
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
            
            // Collect all quality data upfront — inventory consumed before any entity creation
            var qualities = new RiceBallQuality?[ballsToDrop];
            for (int i = 0; i < ballsToDrop; i++)
            {
                if (useInventorySystem && playerData != null)
                    qualities[i] = playerData.UseRiceBall();
                else if (DropPuzzleManager.Instance != null)
                    DropPuzzleManager.Instance.TryDropBall();
            }

            // Batch create ALL entities in a single call — 1 sync point instead of N
            var spawnedEntities = new Unity.Collections.NativeArray<Entity>(ballsToDrop, Unity.Collections.Allocator.Temp);
            entityManager.CreateEntity(ballArchetype, spawnedEntities);

            // Park all entities off-screen while we wait to activate them (match puzzle Z so no warp on activation)
            float3 parkedPos = new float3(0f, 100f, DropPoint.position.z);
            for (int i = 0; i < ballsToDrop; i++)
            {
                entityManager.SetComponentData(spawnedEntities[i], new LocalTransform
                {
                    Position = parkedPos,
                    Rotation = Unity.Mathematics.quaternion.identity,
                    Scale = BallRadius * 2f
                });
                entityManager.SetComponentData(spawnedEntities[i], new RiceBallPhysics
                {
                    Position = parkedPos,
                    Radius = BallRadius,
                    Mass = BallMass,
                    Bounciness = BallBounciness,
                    Friction = BallFriction,
                    IsSleeping = true
                });
                entityManager.SetComponentData(spawnedEntities[i], GetBallTypeFromQuality(qualities[i]));
                entityManager.SetComponentData(spawnedEntities[i], new RiceBallGateTracker { HitGatesMask = 0 });
                entityManager.SetComponentData(spawnedEntities[i], new RiceBallLifetime
                {
                    SpawnTime = Time.time,
                    MaxLifetime = 30f,
                    DestroyBelowY = -20f
                });
            }

            // Cache entity refs — NativeArray must be freed before yielding
            Entity[] entityList = spawnedEntities.ToArray();
            spawnedEntities.Dispose();

            // Debug.Log($"[DropperControllerECS] Dropping {ballsToDrop} riceballs");

            // Activate balls in BallsPerInterval-sized batches per DropInterval tick — only
            // SetComponentData calls from here, no more CreateEntity
            for (int i = 0; i < ballsToDrop; i++)
            {
                float randomX = UnityEngine.Random.Range(-BallRadius * 3f, BallRadius * 3f);
                float randomY = UnityEngine.Random.Range(0f, BallRadius * 0.5f);
                float3 spawnPos = new float3(
                    DropPoint.position.x + randomX,
                    DropPoint.position.y - 0.3f + randomY,
                    DropPoint.position.z
                );

                entityManager.SetComponentData(entityList[i], new LocalTransform
                {
                    Position = spawnPos,
                    Rotation = Unity.Mathematics.quaternion.identity,
                    Scale = BallRadius * 2f
                });
                entityManager.SetComponentData(entityList[i], new RiceBallPhysics
                {
                    Position = spawnPos,
                    Velocity = new Unity.Mathematics.float3(0, -DropForce, 0),
                    Radius = BallRadius,
                    Mass = BallMass,
                    Bounciness = BallBounciness,
                    Friction = BallFriction,
                    IsSleeping = false,
                    SleepVelocityThreshold = 0.015f
                });

                // Release BallsPerInterval balls per tick. At the default of 1 this is the
                // original one-ball-per-DropInterval trickle, exactly as before.
                if (BallsPerInterval <= 1 || (i + 1) % BallsPerInterval == 0)
                    yield return new WaitForSeconds(DropInterval);
            }

            // Debug.Log($"[DropperControllerECS] ✅ Dropped {ballsToDrop} balls!");
        }
        
        /// <summary>
        /// Spawn a ball from inventory (consumes 1 riceball)
        /// </summary>
        private void SpawnBallFromInventory()
        {
            RiceBallQuality? quality = playerData.UseRiceBall();
            
            if (quality == null)
            {
                // Debug.LogWarning("[DropperECS] No riceballs in inventory!");
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
            
            float3 spawnPos = new float3(
                DropPoint.position.x + randomX,
                DropPoint.position.y - 0.3f + randomY,
                DropPoint.position.z
            );
            
            // Create ECS entity using pre-built archetype (avoids archetype lookup)
            Entity ballEntity = entityManager.CreateEntity(ballArchetype);
            
            if (ballEntity == Entity.Null)
            {
                // Debug.LogError("[DropperECS] ❌ Failed to create entity!");
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
            if (hasDropped) return;
            if (!DropPuzzleFlowManager.AllowPlayerInput) return;

            // Build style once
            if (!_guiStyleBuilt)
            {
                _guiStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 20,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap  = true
                };
                _guiStyle.normal.textColor = Color.white;
                _guiStyleBuilt = true;
            }

            int ballCount = 0;
            if (useInventorySystem && playerData != null)
                ballCount = playerData.Inventory.GetTotalBalls();
            else if (DropPuzzleManager.Instance != null)
                ballCount = DropPuzzleManager.Instance.RiceBallsAvailable;

            if (ballCount != _lastGuiBallCount)
            {
                _lastGuiBallCount = ballCount;
                if (ballCount > 0)
                {
                    _guiStyle.normal.textColor = Color.green;
                    _guiLabelStr = $"[A/D] Move\n[SPACE] Drop {ballCount} balls";
                }
                else
                {
                    _guiStyle.normal.textColor = Color.red;
                    _guiLabelStr = "No riceballs!\n[Esc] to go back";
                }
            }

            // Draw in the left column (first 20% of screen), near the bottom
            float colW = Screen.width * 0.19f;
            GUI.Label(new Rect(5, Screen.height - 120f, colW, 80f), _guiLabelStr, _guiStyle);
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
