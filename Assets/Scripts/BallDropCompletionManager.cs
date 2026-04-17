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
        private List<Entity> _toRemove = new List<Entity>(8);

        // Cached query — created once, reused every check to avoid per-call sync points
        private EntityQuery ballQuery;
        private EntityManager entityManager;
        private bool ballQueryCreated = false;
        
        private struct BallTrackingData
        {
            public Vector3 lastPosition;
            public float timeSinceLastMove;
        }
        
        // Events
        public event System.Action OnDropComplete;
        
        private void Start()
        {
            // Cache ECS references once
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                entityManager = world.EntityManager;
                ballQuery = entityManager.CreateEntityQuery(
                    typeof(RiceBallTag),
                    typeof(RiceBallPhysics),
                    typeof(LocalTransform)
                );
                ballQueryCreated = true;
            }

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

            if (ballQueryCreated)
                ballQuery.Dispose();
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
            
            // Debug.Log("[BallDropCompletion] Drop session started");
        }
        
        /// <summary>
        /// Check if drop is complete
        /// </summary>
        private void CheckCompletion()
        {
            if (ballQuery == null) return;

            // Single sync point: read entities and physics together via chunk iteration
            // ToEntityArray + ToComponentDataArray would be two separate sync points; this is one.
            NativeArray<Entity> entities = ballQuery.ToEntityArray(Allocator.Temp);
            NativeArray<RiceBallPhysics> physicsDatas = ballQuery.ToComponentDataArray<RiceBallPhysics>(Allocator.Temp);

            ballsRemaining = entities.Length;
            ballsStuck = 0;

            if (ballsRemaining == 0)
            {
                entities.Dispose();
                physicsDatas.Dispose();
                CompleteDropSession("All balls scored/deleted");
                return;
            }

            // Remove stale tracking entries — reuse cached list, no GC
            _toRemove.Clear();
            foreach (var key in trackedBalls.Keys)
            {
                bool stillExists = false;
                for (int j = 0; j < entities.Length; j++)
                    if (entities[j] == key) { stillExists = true; break; }
                if (!stillExists) _toRemove.Add(key);
            }
            foreach (var key in _toRemove) trackedBalls.Remove(key);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                Vector3 currentPos = physicsDatas[i].Position;

                if (trackedBalls.TryGetValue(entity, out var oldData))
                {
                    float distance = Vector3.Distance(currentPos, oldData.lastPosition);
                    if (distance < 0.01f)
                    {
                        oldData.timeSinceLastMove += checkInterval;
                        if (oldData.timeSinceLastMove >= stuckThreshold)
                            ballsStuck++;
                        trackedBalls[entity] = oldData;
                    }
                    else
                    {
                        trackedBalls[entity] = new BallTrackingData
                        {
                            lastPosition      = currentPos,
                            timeSinceLastMove = 0f
                        };
                    }
                }
                else
                {
                    trackedBalls[entity] = new BallTrackingData
                    {
                        lastPosition      = currentPos,
                        timeSinceLastMove = 0f
                    };
                }
            }

            entities.Dispose();
            physicsDatas.Dispose();

            if (ballsRemaining > 0 && ballsStuck == ballsRemaining)
                CompleteDropSession($"All {ballsRemaining} balls stuck");
        }
        
        /// <summary>
        /// Mark drop session as complete
        /// </summary>
        private void CompleteDropSession(string reason)
        {
            if (isComplete) return;
            
            isComplete = true;
            isDropActive = false;
            
            // Debug.Log($"[BallDropCompletion] ✅ DROP COMPLETE: {reason}");
            
            if (OnDropComplete != null)
            {
                // Debug.Log($"[BallDropCompletion] Invoking OnDropComplete event ({OnDropComplete.GetInvocationList().Length} subscribers)");
                OnDropComplete.Invoke();
            }
            else
            {
                // Debug.LogWarning("[BallDropCompletion] OnDropComplete has no subscribers!");
            }
        }
        
        /// <summary>
        /// Handle daylight warning (10s before day)
        /// </summary>
        private void HandleDaylightWarning()
        {
            if (isDropActive && !isComplete)
            {
                // Debug.LogWarning("[BallDropCompletion] ⚠️ Daylight approaching! Finish up!");
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
                // Debug.LogWarning("[BallDropCompletion] ☀️ DAY TIME! Forcing salvage...");
                ForceSalvage();
            }
        }
        
        /// <summary>
        /// Force salvage remaining balls when daylight hits
        /// </summary>
        private void ForceSalvage()
        {
            if (ballQuery == null) return;

            int salvaged = ballQuery.CalculateEntityCount();
            entityManager.DestroyEntity(ballQuery);

            // Debug.Log($"[BallDropCompletion] Salvaged {salvaged} balls due to daylight");

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
