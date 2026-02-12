using UnityEngine;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Multiplier gate - multiplies balls that pass through
    /// Like Cup Heroes multiplier zones (x2, x4, x5, x6, etc.)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MultiplierGate : MonoBehaviour
    {
        [Header("Multiplier Settings")]
        [Tooltip("Multiplier value (2 = x2, 4 = x4, etc.)")]
        public int Multiplier = 2;
        
        [Tooltip("Rice ball prefab to spawn (assign the PREFAB, not scene instance)")]
        public GameObject RiceBallPrefab;
        
        [Header("Visual Settings")]
        public Color GateColor = Color.yellow;
        
        [Header("Optional Text Display")]
        public TMPro.TextMeshPro MultiplierText;
        
        private void Start()
        {
            Debug.Log($"[MultiplierGate x{Multiplier}] === FULL DIAGNOSTICS ===");
            Debug.Log($"  Position: {transform.position}");
            Debug.Log($"  Scale: {transform.localScale}");
            Debug.Log($"  Layer: {LayerMask.LayerToName(gameObject.layer)} (ID: {gameObject.layer})");
            
            // Ensure trigger
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                if (!col.isTrigger)
                {
                    col.isTrigger = true;
                    Debug.LogWarning($"[MultiplierGate] Collider was not trigger, fixed!");
                }
                Debug.Log($"  Collider: {col.GetType().Name}, IsTrigger: {col.isTrigger}, Enabled: {col.enabled}");
                Debug.Log($"  Collider Bounds: Center={col.bounds.center}, Size={col.bounds.size}");
            }
            else
            {
                Debug.LogError($"[MultiplierGate] ❌ NO COLLIDER FOUND!");
            }
            
            // Validate prefab
            if (RiceBallPrefab == null)
            {
                Debug.LogError($"[MultiplierGate] ❌ No RiceBallPrefab assigned! Drag the rice ball prefab here.");
            }
            else
            {
                Debug.Log($"  ✅ RiceBallPrefab assigned: {RiceBallPrefab.name}");
            }
            
            // Update text if present
            if (MultiplierText != null)
            {
                MultiplierText.text = $"x{Multiplier}";
            }
            
            Debug.Log($"[MultiplierGate x{Multiplier}] === READY ===");
        }
        
        private HashSet<int> processedBalls = new HashSet<int>(); // Track which balls we've already processed
        
        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[MultiplierGate x{Multiplier}] ❗ TRIGGER ENTER! Object: {other.name}, Tag: {other.tag}, HasRigidbody: {other.attachedRigidbody != null}");
            ProcessBallHit(other);
        }
        
        private void OnTriggerStay(Collider other)
        {
            // Backup in case OnTriggerEnter is missed
            ProcessBallHit(other);
        }
        
        private void ProcessBallHit(Collider other)
        {
            // Prevent processing same ball multiple times
            int ballInstanceId = other.gameObject.GetInstanceID();
            if (processedBalls.Contains(ballInstanceId))
            {
                return; // Already processed this ball
            }
            
            // Check if it's a rice ball
            if (!other.CompareTag("RiceBall"))
            {
                return;
            }
            
            // Mark as processed immediately
            processedBalls.Add(ballInstanceId);
            
            Debug.Log($"[MultiplierGate x{Multiplier}] ✅ Tag verified! Checking RiceBall component...");
            
            // Check if this ball has already hit THIS specific gate
            RiceBall riceBall = other.GetComponent<RiceBall>();
            if (riceBall == null)
            {
                Debug.LogWarning($"[MultiplierGate] Ball missing RiceBall component!");
                return;
            }
            
            // Use instance ID to uniquely identify this gate
            int gateId = GetInstanceID();
            if (riceBall.HasHitGate(gateId))
            {
                // Already hit this gate, ignore
                Debug.Log($"[MultiplierGate] Ball already hit this gate, ignoring");
                return;
            }
            
            // Mark this gate as hit FIRST
            riceBall.MarkGateHit(gateId);
            
            if (RiceBallPrefab == null)
            {
                Debug.LogError($"[MultiplierGate] Cannot spawn - no prefab assigned!");
                return;
            }
            
            // Spawn ADDITIONAL balls from PREFAB ABOVE the ball's current position
            // Original ball continues, spawned balls = (Multiplier - 1)
            // So x5 gate: 1 original + 4 new = 5 total balls
            // Stack them vertically above so physics pushes balls away naturally
            int additionalBalls = Multiplier - 1;
            
            Debug.Log($"[MultiplierGate] x{Multiplier} triggered! Spawning {additionalBalls} additional balls ABOVE");
            
            // Get ball size for proper stacking
            float ballRadius = 0.25f; // Default sphere radius
            Collider ballCollider = other.GetComponent<Collider>();
            if (ballCollider != null)
            {
                ballRadius = ballCollider.bounds.extents.y;
            }
            
            // Spread balls in random horizontal positions above
            for (int i = 0; i < additionalBalls; i++)
            {
                // Random horizontal spread (left or right of center)
                float randomSpread = Random.Range(-ballRadius * 3f, ballRadius * 3f);
                
                // Stack vertically with random horizontal offset
                Vector3 spawnPos = other.transform.position + new Vector3(
                    randomSpread,                    // Random horizontal position
                    ballRadius * 2.2f * (i + 1),     // Vertical stacking
                    0f                                // No Z movement
                );
                
                GameObject newBall = Instantiate(RiceBallPrefab, spawnPos, Quaternion.identity);
                
                // Ensure physics is enabled for pushing
                Rigidbody newRb = newBall.GetComponent<Rigidbody>();
                if (newRb != null)
                {
                    newRb.linearVelocity = Vector3.zero; // Start at rest, gravity pulls down
                    newRb.angularVelocity = Vector3.zero;
                    newRb.useGravity = true;
                    newRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                }
                
                // Mark this new ball as having hit THIS gate already
                RiceBall newRiceBall = newBall.GetComponent<RiceBall>();
                if (newRiceBall != null)
                {
                    newRiceBall.MarkGateHit(gateId);
                }
            }
            
            Debug.Log($"[MultiplierGate] Spawned {additionalBalls} additional balls, original continues (total {Multiplier} balls)");
            
            // Original ball continues - can hit other gates!
        }
        
        private void OnDrawGizmos()
        {
            Gizmos.color = GateColor;
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                Gizmos.DrawWireCube(transform.position, col.bounds.size);
            }
        }
    }
}
