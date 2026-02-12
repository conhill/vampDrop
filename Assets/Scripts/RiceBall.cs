using UnityEngine;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Simple component to identify rice balls in the drop puzzle
    /// Attach to the rice ball prefab
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RiceBall : MonoBehaviour
    {
        [Header("Physics")]
        [Tooltip("Ball will auto-destroy after this many seconds")]
        public float Lifetime = 30f;
        
        [Tooltip("Destroy if ball falls below this Y position")]
        public float DestroyBelowY = -20f;
        
        private float spawnTime;
        private HashSet<int> hitGates = new HashSet<int>(); // Track which gate instances have been hit
        
        private void Awake()
        {
            // Set tag IMMEDIATELY in Awake (before Start, before any physics)
            if (!gameObject.CompareTag("RiceBall"))
            {
                gameObject.tag = "RiceBall";
            }
            Debug.Log($"[RiceBall] {gameObject.name} - Tag: {gameObject.tag}, Layer: {LayerMask.LayerToName(gameObject.layer)} (ID: {gameObject.layer})");
        }
        
        private void Start()
        {
            spawnTime = Time.time;
            
            // Tag already set in Awake()
            
            // Ensure Rigidbody exists and configure for MAXIMUM PERFORMANCE
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            
            // AGGRESSIVE PERFORMANCE SETTINGS for 1000+ balls
            rb.linearDamping = 0.1f; // Minimal air resistance
            rb.angularDamping = 0.9f; // Reduce spinning heavily
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete; // FASTEST (not Continuous)
            rb.interpolation = RigidbodyInterpolation.None; // NO interpolation for speed
            rb.sleepThreshold = 0.1f; // Sleep faster to save CPU
            rb.maxAngularVelocity = 5f; // Limit rotation speed
            
            // FREEZE Z-AXIS for 2D physics (huge performance gain!)
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            
            // Reduce solver iterations (game-wide setting affects all rigidbodies)
            Physics.defaultSolverIterations = 3; // Default is 6, lower = faster but less accurate
            Physics.defaultSolverVelocityIterations = 1; // Default is 1
        }
        
        private void Update()
        {
            // Auto-destroy after lifetime
            if (Time.time - spawnTime > Lifetime)
            {
                Destroy(gameObject);
                return;
            }
            
            // Destroy if fell too far
            if (transform.position.y < DestroyBelowY)
            {
                Destroy(gameObject);
                return;
            }
            
            // PERFORMANCE: Put ball to sleep if barely moving
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.IsSleeping())
            {
                if (rb.linearVelocity.sqrMagnitude < 0.01f) // Almost stopped
                {
                    rb.Sleep(); // Force sleep to save CPU
                }
            }
        }
        
        /// <summary>
        /// Check if this ball has already hit a specific gate instance
        /// </summary>
        public bool HasHitGate(int gateInstanceId)
        {
            return hitGates.Contains(gateInstanceId);
        }
        
        /// <summary>
        /// Mark that this ball has hit a specific gate instance
        /// </summary>
        public void MarkGateHit(int gateInstanceId)
        {
            hitGates.Add(gateInstanceId);
        }
    }
}
