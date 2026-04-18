using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Manages puzzle selection based on player progress
    /// </summary>
    public class PuzzleManager : MonoBehaviour
    {
        public static PuzzleManager Instance { get; private set; }
        
        [Header("Puzzle Files")]
        [Tooltip("Tutorial puzzle (puzzle_level1.json)")]
        public TextAsset TutorialPuzzle;
        [Tooltip("Level 2 puzzle (puzzle_level2.json)")]
        public TextAsset Level2Puzzle;
        [Tooltip("Level 3 puzzle (puzzle_level3.json)")]  
        public TextAsset Level3Puzzle;
        
        private PlayerDataManager playerData;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        
        private void Start()
        {
            playerData = PlayerDataManager.Instance;
        }
        
        /// <summary>
        /// Get the appropriate puzzle based on player progress
        /// </summary>
        public TextAsset GetCurrentPuzzle()
        {
            if (playerData == null)
            {
                // Debug.LogWarning("[PuzzleManager] No PlayerDataManager found, using tutorial puzzle");
                return TutorialPuzzle;
            }
            
            // Tutorial logic: Only play tutorial once
            if (!playerData.TutorialCompleted)
            {
                // Debug.Log("[PuzzleManager] Tutorial not completed - loading tutorial puzzle");
                return TutorialPuzzle;
            }
            
            // Post-tutorial: Load level 2 (or level 3 based on some criteria)
            if (playerData.TotalRunsCompleted < 10) // Example: Level 2 for first 10 runs
            {
                // Debug.Log("[PuzzleManager] Loading Level 2 puzzle (post-tutorial)");
                return Level2Puzzle;
            }
            else
            {
                // Debug.Log("[PuzzleManager] Loading Level 3 puzzle (advanced)");
                return Level3Puzzle ?? Level2Puzzle; // Fallback to Level 2 if Level 3 missing
            }
        }
        
        /// <summary>
        /// Setup puzzle in current scene based on player progress
        /// </summary>
        public void SetupPuzzleForCurrentScene()
        {
            var gridLoader = FindObjectOfType<GridPuzzleLoader>();
            if (gridLoader == null)
            {
                // Debug.LogWarning("[PuzzleManager] No GridPuzzleLoader found in scene!");
                return;
            }
            
            TextAsset targetPuzzle = GetCurrentPuzzle();
            if (targetPuzzle == null)
            {
                // Debug.LogError("[PuzzleManager] No puzzle assigned for current progress level!");
                return;
            }
            
            // Debug.Log($"[PuzzleManager] Setting puzzle: {targetPuzzle.name}");
            gridLoader.PuzzleJsonFile = targetPuzzle;
            gridLoader.LoadAndBuildPuzzle();
            
            // Mark tutorial as completed after first tutorial run
            if (!playerData.TutorialCompleted && targetPuzzle == TutorialPuzzle)
            {
                playerData.TutorialCompleted = true;
                playerData.SavePlayerData();
                // Debug.Log("[PuzzleManager] ✅ Tutorial marked as completed!");
            }
        }
    }
}
