using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Manages the drop puzzle game state
    /// Tracks collected rice from FPS stage and currency earned from drops
    /// </summary>
    public class DropPuzzleManager : MonoBehaviour
    {
        [Header("Game State")]
        [Tooltip("Total rice balls available to drop (carried over from FPS stage)")]
        public int RiceBallsAvailable = 100;
        
        [Tooltip("Current currency earned (cents)")]
        public int Currency = 0;
        
        [Header("References")]
        public DropperController Dropper;
        // Note: GoalGate is created dynamically by PuzzleVariationLoader
        
        private static DropPuzzleManager instance;
        public static DropPuzzleManager Instance => instance;
        
        private void Awake()
        {
            Debug.Log("[DropPuzzleManager] Awake called");
            
            if (instance == null)
            {
                instance = this;
                Debug.Log($"[DropPuzzleManager] Instance created with {RiceBallsAvailable} rice balls (default)");
            }
            else
            {
                Debug.LogWarning("[DropPuzzleManager] Duplicate instance, destroying");
                Destroy(gameObject);
                return;
            }
        }
        
        private void Start()
        {
            Debug.Log($"[DropPuzzleManager] Start - RiceBallsAvailable = {RiceBallsAvailable}");
        }
        
        /// <summary>
        /// Called when player drops a rice ball
        /// </summary>
        public bool TryDropBall()
        {
            if (RiceBallsAvailable <= 0)
            {
                Debug.Log("[DropPuzzleManager] No rice balls left to drop!");
                return false;
            }
            
            RiceBallsAvailable--;
            Debug.Log($"[DropPuzzleManager] Dropped ball! {RiceBallsAvailable} remaining");
            return true;
        }
        
        /// <summary>
        /// Called when a rice ball enters the goal gate
        /// Awards 1 cent per ball
        /// </summary>
        public void OnBallScored()
        {
            Currency += 1; // 1 cent per rice ball
        }
        
        /// <summary>
        /// Load rice collected from FPS stage
        /// </summary>
        public void LoadFromFPSStage(int collectedRice)
        {
            Debug.Log($"[DropPuzzleManager] ✅ LoadFromFPSStage called! Setting RiceBallsAvailable from {RiceBallsAvailable} to {collectedRice}");
            RiceBallsAvailable = collectedRice;
            Debug.Log($"[DropPuzzleManager] Confirmed: RiceBallsAvailable is now {RiceBallsAvailable}");
        }
    }
}
