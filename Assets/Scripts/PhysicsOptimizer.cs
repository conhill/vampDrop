using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Optimizes Unity physics for handling 1000+ balls
    /// Attach to a GameObject in the DropPuzzle scene
    /// </summary>
    public class PhysicsOptimizer : MonoBehaviour
    {
        [Header("Physics Performance Settings")]
        [Tooltip("Lower = faster but less accurate collisions")]
        [Range(1, 10)]
        public int SolverIterations = 3; // Default is 6
        
        [Tooltip("Physics update rate (Hz). Lower = better performance")]
        [Range(30, 120)]
        public int FixedTimestepFPS = 50; // Default is 50
        
        [Tooltip("Max timestep to prevent spiral of death")]
        public float MaximumTimestep = 0.1f;
        
        [Tooltip("Auto destroy balls that fall below this Y")]
        public float DestroyBelowY = -20f;
        
        private void Awake()
        {
            ApplyOptimizations();
        }
        
        private void ApplyOptimizations()
        {
            // Reduce physics solver iterations (faster but less accurate)
            Physics.defaultSolverIterations = SolverIterations;
            Physics.defaultSolverVelocityIterations = 1;
            
            // Adjust physics timestep
            Time.fixedDeltaTime = 1f / FixedTimestepFPS;
            Time.maximumDeltaTime = MaximumTimestep;
            
            // Disable auto-sync transforms (huge performance gain)
            Physics.autoSyncTransforms = false;
            
            // Reduce contact offset for simpler collisions
            Physics.defaultContactOffset = 0.01f; // Default is 0.01
            
            // Enable better collision detection
            Physics.defaultMaxAngularSpeed = 7f; // Limit rotation speed
            
            Debug.Log($"[PhysicsOptimizer] ✅ Applied optimizations:");
            Debug.Log($"  Solver Iterations: {Physics.defaultSolverIterations}");
            Debug.Log($"  Fixed Timestep: {Time.fixedDeltaTime:F4}s ({FixedTimestepFPS} FPS)");
            Debug.Log($"  Auto Sync Transforms: {Physics.autoSyncTransforms}");
        }
        
        private void Update()
        {
            // Periodic cleanup of fallen balls (backup to RiceBall's own cleanup)
            if (Time.frameCount % 120 == 0) // Every 2 seconds at 60fps
            {
                CleanupFallenBalls();
            }
        }
        
        private void CleanupFallenBalls()
        {
            var balls = FindObjectsOfType<RiceBall>();
            int cleaned = 0;
            
            foreach (var ball in balls)
            {
                if (ball.transform.position.y < DestroyBelowY)
                {
                    Destroy(ball.gameObject);
                    cleaned++;
                }
            }
            
            if (cleaned > 0)
            {
                Debug.Log($"[PhysicsOptimizer] Cleaned up {cleaned} fallen balls");
            }
        }
    }
}
