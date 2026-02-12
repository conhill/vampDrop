using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Detects when ball drop puzzle is complete
    /// - All balls scored/deleted
    /// - Balls stuck (not moved in 3.5s)
    /// </summary>
    public class BallDropCompletionManager : MonoBehaviour
    {
        [Header("Completion Settings")]
        [Tooltip("Time in seconds a ball must be still to be considered stuck")]
        public float stuckThreshold = 3.5f;
        
        [Tooltip("Check interval in seconds")]
        public float checkInterval = 0.5f;
        
        [Header("State")]
        public bool isDropActive = false;
        public bool isComplete = false;
        public int ballsRemaining = 0;
        public int ballsStuck = 0;
        
        // Track ball positions to detect stuck balls
        private Dictionary<Entity, BallTrackingData> trackedBalls = new Dictionary<Entity, BallTrackingData>();
        private float nextCheckTime = 0f;
        
        private struct BallTrackingData
        {
            public Vector3 lastPosition;
            public float timeSinceLastMove;
        }
        
        // Events
        public event System.Action OnDropComplete;
        
        private void Start()
        {
            // Subscribe to day/night events
            if (DayNightCycleManager.Instance != null)
            {
                DayNightCycleManager.Instance.OnDaylightWarning += HandleDaylightWarning;
                DayNightCycleManager.Instance.OnNightEnd += HandleNightEnd;
            }
        }
        
        private void OnDestroy()
        {
            if (DayNightCycleManager.Instance != null)
            {
                DayNightCycleManager.Instance.OnDaylightWarning -= HandleDaylightWarning;
                DayNightCycleManager.Instance.OnNightEnd -= HandleNightEnd;
            }
        }
        
        private void Update()
        {
            if (!isDropActive || isComplete) return;
            
            // Check periodically
            if (Time.time >= nextCheckTime)
            {
                nextCheckTime = Time.time + checkInterval;
                CheckCompletion();
            }
        }
        
        /// <summary>
        /// Start tracking a new drop session
        /// </summary>
        public void StartDropSession()
        {
            isDropActive = true;
            isComplete = false;
            ballsRemaining = 0;
            ballsStuck = 0;
            trackedBalls.Clear();
            
            Debug.Log("[BallDropCompletion] Drop session started");
        }
        
        /// <summary>
        /// Check if drop is complete
        /// </summary>
        private void CheckCompletion()
        {
            // Get ECS world
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            
            var entityManager = world.EntityManager;
            
            // Query all active balls
            var query = entityManager.CreateEntityQuery(typeof(RiceBallTag), typeof(RiceBallPhysics), typeof(LocalTransform));
            var entities = query.ToEntityArray(Allocator.Temp);
            
            ballsRemaining = entities.Length;
            ballsStuck = 0;
            
            // If no balls exist, we're complete
            if (ballsRemaining == 0)
            {
                entities.Dispose();
                query.Dispose();
                CompleteDropSession("All balls scored/deleted");
                return;
            }
            
            // Track ball positions to detect stuck balls
            var newTrackedBalls = new Dictionary<Entity, BallTrackingData>();
            
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var physics = entityManager.GetComponentData<RiceBallPhysics>(entity);
                var transform = entityManager.GetComponentData<LocalTransform>(entity);
                
                Vector3 currentPos = transform.Position;
                
                // Check if we've tracked this ball before
                if (trackedBalls.TryGetValue(entity, out var oldData))
                {
                    // Check if ball has moved
                    float distance = Vector3.Distance(currentPos, oldData.lastPosition);
                    
                    if (distance < 0.01f) // Ball hasn't moved significantly
                    {
                        // Increment stuck time
                        var newData = oldData;
                        newData.timeSinceLastMove += checkInterval;
                        
                        if (newData.timeSinceLastMove >= stuckThreshold)
                        {
                            ballsStuck++;
                        }
                        
                        newTrackedBalls[entity] = newData;
                    }
                    else
                    {
                        // Ball moved, reset timer
                        newTrackedBalls[entity] = new BallTrackingData
                        {
                            lastPosition = currentPos,
                            timeSinceLastMove = 0f
                        };
                    }
                }
                else
                {
                    // New ball, start tracking
                    newTrackedBalls[entity] = new BallTrackingData
                    {
                        lastPosition = currentPos,
                        timeSinceLastMove = 0f
                    };
                }
            }
            
            entities.Dispose();
            query.Dispose();
            
            trackedBalls = newTrackedBalls;
            
            // Check if all remaining balls are stuck
            if (ballsRemaining > 0 && ballsStuck == ballsRemaining)
            {
                CompleteDropSession($"All {ballsRemaining} balls stuck");
            }
        }
        
        /// <summary>
        /// Mark drop session as complete
        /// </summary>
        private void CompleteDropSession(string reason)
        {
            if (isComplete) return;
            
            isComplete = true;
            isDropActive = false;
            
            Debug.Log($"[BallDropCompletion] ✅ DROP COMPLETE: {reason}");
            
            if (OnDropComplete != null)
            {
                Debug.Log($"[BallDropCompletion] Invoking OnDropComplete event ({OnDropComplete.GetInvocationList().Length} subscribers)");
                OnDropComplete.Invoke();
            }
            else
            {
                Debug.LogWarning("[BallDropCompletion] OnDropComplete has no subscribers!");
            }
        }
        
        /// <summary>
        /// Handle daylight warning (10s before day)
        /// </summary>
        private void HandleDaylightWarning()
        {
            if (isDropActive && !isComplete)
            {
                Debug.LogWarning("[BallDropCompletion] ⚠️ Daylight approaching! Finish up!");
                // UI will display warning
            }
        }
        
        /// <summary>
        /// Handle night end - force completion and salvage
        /// </summary>
        private void HandleNightEnd()
        {
            if (isDropActive && !isComplete)
            {
                Debug.LogWarning("[BallDropCompletion] ☀️ DAY TIME! Forcing salvage...");
                ForceSalvage();
            }
        }
        
        /// <summary>
        /// Force salvage remaining balls when daylight hits
        /// </summary>
        private void ForceSalvage()
        {
            // Destroy all remaining balls
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            
            var entityManager = world.EntityManager;
            var query = entityManager.CreateEntityQuery(typeof(RiceBallTag));
            var entities = query.ToEntityArray(Allocator.Temp);
            
            int salvaged = entities.Length;
            
            entityManager.DestroyEntity(query);
            
            entities.Dispose();
            query.Dispose();
            
            Debug.Log($"[BallDropCompletion] Salvaged {salvaged} balls due to daylight");
            
            CompleteDropSession($"Daylight salvage - {salvaged} balls destroyed");
        }
        
        /// <summary>
        /// Manual completion (for testing or button press)
        /// </summary>
        public void ManualComplete()
        {
            if (!isDropActive) return;
            
            CompleteDropSession("Manual completion");
        }
    }
}
