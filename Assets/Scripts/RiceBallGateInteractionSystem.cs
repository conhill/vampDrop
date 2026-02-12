using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Hybrid system - bridges ECS balls with MonoBehaviour gates
    /// Checks ECS ball positions against GameObject trigger zones
    /// </summary>
    public class RiceBallGateInteractionSystem : MonoBehaviour
    {
        private EntityQuery ballQuery;
        private EntityManager entityManager;
        
        // Cache all gates in scene
        private MultiplierGate[] multiplierGates;
        private GoalGate[] goalGates;
        
        private void Start()
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            
            ballQuery = entityManager.CreateEntityQuery(
                typeof(LocalTransform),
                typeof(RiceBallPhysics),
                typeof(RiceBallGateTracker),
                typeof(RiceBallTag)
            );
            
            // Find all gates in scene
            RefreshGates();
            
            Debug.Log($"[GateInteraction] Found {multiplierGates.Length} multiplier gates, {goalGates.Length} goal gates");
        }
        
        public void RefreshGates()
        {
            multiplierGates = FindObjectsOfType<MultiplierGate>();
            goalGates = FindObjectsOfType<GoalGate>();
        }
        
        private void FixedUpdate()
        {
            // Only run in DropPuzzle scene
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "DropPuzzle") return;
            
            if (ballQuery == null) return;
            
            // PERFORMANCE: Only check gates every 3rd physics frame
            if (Time.frameCount % 3 != 0) return;
            
            // Get all ball data
            NativeArray<Entity> entities = ballQuery.ToEntityArray(Allocator.Temp);
            NativeArray<LocalTransform> transforms = ballQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            NativeArray<RiceBallGateTracker> trackers = ballQuery.ToComponentDataArray<RiceBallGateTracker>(Allocator.Temp);
            
            // Debug: Log first ball position and first gate bounds
            if (Time.frameCount % 300 == 0 && entities.Length > 0 && multiplierGates.Length > 0)
            {
                var firstGate = multiplierGates[0];
                if (firstGate != null)
                {
                    var col = firstGate.GetComponent<Collider>();
                    if (col != null)
                    {
                        Debug.Log($"[GateInteraction] Ball[0] pos: {transforms[0].Position} | Gate[0] bounds: {col.bounds}");
                    }
                }
            }
            
            // Check each ball against gates
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                float3 ballPos = transforms[i].Position;
                int hitMask = trackers[i].HitGatesMask;
                
                // Check multiplier gates
                for (int g = 0; g < multiplierGates.Length; g++)
                {
                    MultiplierGate gate = multiplierGates[g];
                    if (gate == null || !gate.gameObject.activeInHierarchy) continue;
                    
                    // Check if already hit this gate
                    int gateBit = 1 << g;
                    if ((hitMask & gateBit) != 0) continue; // Already hit
                    
                    // Check if ball is inside gate trigger zone
                    Collider gateCollider = gate.GetComponent<Collider>();
                    if (gateCollider == null) continue;
                    
                    // Convert float3 to Vector3 for bounds check
                    Vector3 ballPosV3 = new Vector3(ballPos.x, ballPos.y, ballPos.z);
                    
                    if (gateCollider.bounds.Contains(ballPosV3))
                    {
                        // Trigger hit!
                        Debug.Log($"[GateInteraction] 🎯 Ball hit gate {g} (x{gate.Multiplier}) at {ballPos}!");
                        OnBallHitMultiplierGate(entity, gate, g, ballPos);
                        
                        // Update mask
                        hitMask |= gateBit;
                    }
                }
                
                // Check goal gates
                foreach (GoalGate gate in goalGates)
                {
                    if (gate == null || !gate.gameObject.activeInHierarchy) continue;
                    
                    Collider gateCollider = gate.GetComponent<Collider>();
                    if (gateCollider == null) continue;
                    
                    Vector3 ballPosV3 = new Vector3(ballPos.x, ballPos.y, ballPos.z);
                    
                    if (gateCollider.bounds.Contains(ballPosV3))
                    {
                        // Goal scored!
                        Debug.Log($"[GateInteraction] ⭐ Ball scored at goal! Pos: {ballPos}");
                        OnBallHitGoalGate(entity, gate, ballPos);
                        break; // Ball is destroyed after goal
                    }
                }
                
                // Update tracker
                if (entityManager.Exists(entity))
                {
                    entityManager.SetComponentData(entity, new RiceBallGateTracker { HitGatesMask = hitMask });
                }
            }
            
            entities.Dispose();
            transforms.Dispose();
            trackers.Dispose();
        }
        
        private void OnBallHitMultiplierGate(Entity ballEntity, MultiplierGate gate, int gateIndex, float3 ballPos)
        {
            Debug.Log($"[GateInteraction] Ball hit x{gate.Multiplier} gate at {ballPos}");
            
            // Get ball physics
            RiceBallPhysics ballPhysics = entityManager.GetComponentData<RiceBallPhysics>(ballEntity);
            float ballRadius = ballPhysics.Radius;
            
            // Spawn additional balls
            int additionalBalls = gate.Multiplier - 1;
            
            for (int i = 0; i < additionalBalls; i++)
            {
                // Random spread pattern
                float randomSpread = UnityEngine.Random.Range(-ballRadius * 3f, ballRadius * 3f);
                float3 spawnPos = ballPos + new float3(
                    randomSpread,
                    ballRadius * 2.2f * (i + 1),
                    0f
                );
                
                // Create new entity
                Entity newBall = entityManager.CreateEntity(
                    typeof(LocalTransform),
                    typeof(RiceBallPhysics),
                    typeof(RiceBallTag),
                    typeof(RiceBallType),
                    typeof(RiceBallGateTracker),
                    typeof(RiceBallLifetime)
                );
                
                // Copy properties from original ball
                entityManager.SetComponentData(newBall, new LocalTransform
                {
                    Position = spawnPos,
                    Rotation = quaternion.identity,
                    Scale = ballRadius * 2f
                });
                
                // Copy ball type from original (inherits perks!)
                RiceBallType originalType = entityManager.GetComponentData<RiceBallType>(ballEntity);
                entityManager.SetComponentData(newBall, originalType);
                
                // Set physics with small velocity
                entityManager.SetComponentData(newBall, new RiceBallPhysics
                {
                    Position = spawnPos,
                    Velocity = new float3(randomSpread * 0.5f, 0f, 0f), // Slight horizontal push
                    Radius = ballRadius,
                    Mass = ballPhysics.Mass,
                    Bounciness = ballPhysics.Bounciness,
                    Friction = ballPhysics.Friction,
                    IsSleeping = false,
                    SleepVelocityThreshold = 0.015f // Very aggressive sleep for high ball counts
                });
                
                // Inherit gate tracker (so spawned balls don't re-trigger same gates)
                RiceBallGateTracker originalTracker = entityManager.GetComponentData<RiceBallGateTracker>(ballEntity);
                entityManager.SetComponentData(newBall, originalTracker);
                
                // Set lifetime
                RiceBallLifetime originalLifetime = entityManager.GetComponentData<RiceBallLifetime>(ballEntity);
                entityManager.SetComponentData(newBall, originalLifetime);
            }
        }
        
        private void OnBallHitGoalGate(Entity ballEntity, GoalGate gate, float3 ballPos)
        {
            Debug.Log($"[GateInteraction] Ball scored at goal!");
            
            // Award currency through new system (1 cent per ball)
            if (PlayerDataManager.Instance != null)
            {
                PlayerDataManager.Instance.AddCurrency(1, "Goal scored");
            }
            else
            {
                Debug.LogWarning("[GateInteraction] PlayerDataManager not found!");
            }
            
            // Destroy ball entity
            entityManager.DestroyEntity(ballEntity);
        }
        
        private void OnDestroy()
        {
            if (ballQuery != null)
            {
                ballQuery.Dispose();
            }
        }
    }
}
