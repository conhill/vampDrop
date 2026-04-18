using UnityEngine;
using Unity.Entities;
using Vampire.DropPuzzle;

namespace Vampire.DropPuzzle
{
    public class PuzzlePrefabLoader : MonoBehaviour
    {
        [Header("Puzzle Prefabs")]
        [Tooltip("Add your manually designed puzzle prefabs here")]
        public GameObject[] PuzzlePrefabs;
        
        [Header("Puzzle Positioning")]
        [Tooltip("Offset position for loaded puzzles (to fix centering)")]
        public Vector3 PuzzlePositionOffset = Vector3.zero;
        
        [Tooltip("Rotation for loaded puzzles")]
        public Vector3 PuzzleRotation = Vector3.zero;
        
        [Tooltip("Scale multiplier for loaded puzzles")]
        public Vector3 PuzzleScale = Vector3.one;
        
        [Header("Puzzle Selection")]
        [Tooltip("Auto-select puzzle based on player level")]
        public bool UsePlayerLevel = true;
        
        [Tooltip("Manual puzzle index (only used if UsePlayerLevel is false)")]
        public int ManualPuzzleIndex = 0;
        
        [Header("Background")]
        [Tooltip("Optional background prefab (can be image plane or quad)")]
        public GameObject BackgroundPrefab;
        
        [Tooltip("Optional background sprite/texture for image backgrounds")]
        public Sprite BackgroundSprite;
        
        public Vector3 BackgroundPosition = new Vector3(-2f, 1f, -50f);
        public Vector3 BackgroundScale = new Vector3(50f, 50f, 1f);
        
        [Header("Dynamic Enhancement")]
        [Tooltip("Enable dynamic zone enhancement based on user stats")]
        public bool EnableDynamicEnhancement = true;
        
        private GameObject currentPuzzleInstance;
        private GameObject backgroundInstance;
        private PuzzleEnhancer puzzleEnhancer;
        private int currentPuzzleIndex = 0; // Track currently loaded puzzle

        private void Start()
        {
            puzzleEnhancer = GetComponent<PuzzleEnhancer>();
            if (puzzleEnhancer == null)
                puzzleEnhancer = gameObject.AddComponent<PuzzleEnhancer>();

            int puzzleToLoad = UsePlayerLevel ? SelectPuzzleIndex() : ManualPuzzleIndex;
            bool guaranteeX2 = IsFirstPostTutorialRun();

            currentPuzzleIndex = puzzleToLoad;
            LoadPuzzle(puzzleToLoad, guaranteeX2);
        }

        /// <summary>
        /// Returns true if the tutorial is done (runtime flag or persisted flag).
        /// </summary>
        private bool IsTutorialComplete()
        {
            if (TutorialManager.Instance != null)
                return !TutorialManager.Instance.tutorialActive;

            if (PlayerDataManager.Instance != null)
                return PlayerDataManager.Instance.TutorialCompleted;

            return true; // No manager present — assume complete
        }

        /// <summary>
        /// Returns true when we're on the very first non-tutorial puzzle run.
        /// Used to guarantee the player sees at least one x2 gate.
        /// </summary>
        private bool IsFirstPostTutorialRun()
        {
            if (!IsTutorialComplete()) return false;
            if (PlayerDataManager.Instance == null) return false;
            return PlayerDataManager.Instance.TotalRunsCompleted == 0;
        }

        /// <summary>
        /// Chooses puzzle index:
        ///   Tutorial active → 0
        ///   Post-tutorial   → random from 1..N-1 (skips tutorial puzzle)
        /// </summary>
        private int SelectPuzzleIndex()
        {
            if (PuzzlePrefabs == null || PuzzlePrefabs.Length == 0) return 0;

            if (!IsTutorialComplete())
            {
                Debug.Log("[PuzzlePrefabLoader] Tutorial active — loading tutorial puzzle (index 0)");
                return 0;
            }

            if (PuzzlePrefabs.Length <= 1)
            {
                Debug.LogWarning("[PuzzlePrefabLoader] Only one puzzle prefab assigned — add more to PuzzlePrefabs array for post-tutorial variety");
                return 0;
            }

            int chosen = Random.Range(1, PuzzlePrefabs.Length);
            Debug.Log($"[PuzzlePrefabLoader] Post-tutorial: randomly selected puzzle index {chosen} ({PuzzlePrefabs[chosen]?.name})");
            return chosen;
        }

        public void LoadPuzzle(int puzzleIndex, bool guaranteeX2Gate = false)
        {
            Debug.Log($"[PuzzlePrefabLoader] Loading puzzle {puzzleIndex} (guaranteeX2={guaranteeX2Gate})");

            ClearPuzzle();
            CreateBackground();

            if (puzzleIndex >= 0 && puzzleIndex < PuzzlePrefabs.Length && PuzzlePrefabs[puzzleIndex] != null)
            {
                currentPuzzleInstance = Instantiate(PuzzlePrefabs[puzzleIndex], transform);
                currentPuzzleInstance.name = $"Puzzle_{puzzleIndex}";

                currentPuzzleInstance.transform.localPosition = PuzzlePositionOffset;
                currentPuzzleInstance.transform.localRotation = Quaternion.Euler(PuzzleRotation);
                currentPuzzleInstance.transform.localScale = PuzzleScale;

                Debug.Log($"[PuzzlePrefabLoader] Loaded puzzle prefab: {PuzzlePrefabs[puzzleIndex].name}");

                if (EnableDynamicEnhancement && puzzleEnhancer != null)
                    puzzleEnhancer.EnhancePuzzle(currentPuzzleInstance, guaranteeX2Gate);
                else if (!EnableDynamicEnhancement)
                    Debug.LogWarning("[PuzzlePrefabLoader] Dynamic enhancement is DISABLED");
                else
                    Debug.LogError("[PuzzlePrefabLoader] PuzzleEnhancer component is NULL!");

                RefreshGateSystem();
            }
            else
            {
                Debug.LogError($"[PuzzlePrefabLoader] Invalid puzzle index {puzzleIndex} or null prefab");
            }
        }

        private void CreateBackground()
        {
            if (BackgroundPrefab != null)
            {
                // Use custom prefab
                backgroundInstance = Instantiate(BackgroundPrefab, transform);
                backgroundInstance.transform.position = BackgroundPosition;
                backgroundInstance.transform.localScale = BackgroundScale;
            }
            else if (BackgroundSprite != null)
            {
                // Create image plane with sprite
                backgroundInstance = new GameObject("Background_ImagePlane");
                backgroundInstance.transform.SetParent(transform);
                backgroundInstance.transform.position = BackgroundPosition;
                backgroundInstance.transform.localScale = BackgroundScale;
                
                // Add SpriteRenderer for 2D image
                var spriteRenderer = backgroundInstance.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = BackgroundSprite;
                spriteRenderer.sortingOrder = -100; // Behind everything
                
                Debug.Log("[PuzzlePrefabLoader] Created sprite-based background");
            }
            else
            {
                // Create default quad background
                backgroundInstance = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backgroundInstance.name = "Background_Default";
                backgroundInstance.transform.position = BackgroundPosition;
                backgroundInstance.transform.localScale = BackgroundScale;
                backgroundInstance.transform.SetParent(transform);
                
                Debug.Log("[PuzzlePrefabLoader] Created default quad background");
            }
        }

        private void RefreshGateSystem()
        {
            var gateSystem = FindObjectOfType<RiceBallGateInteractionSystem>();
            if (gateSystem != null)
            {
                gateSystem.RefreshGates();
                Debug.Log("[PuzzlePrefabLoader] Refreshed gate interaction system");
            }
        }

        public void ClearPuzzle()
        {
            if (currentPuzzleInstance != null)
            {
                DestroyImmediate(currentPuzzleInstance);
                currentPuzzleInstance = null;
            }
            
            if (backgroundInstance != null)
            {
                DestroyImmediate(backgroundInstance);
                backgroundInstance = null;
            }
        }

        // Method to switch puzzles at runtime
        public void SwitchToPuzzle(int newIndex)
        {
            if (newIndex != currentPuzzleIndex)
            {
                currentPuzzleIndex = newIndex;
                LoadPuzzle(currentPuzzleIndex);
            }
        }

        // Inspector buttons for easy testing
        [ContextMenu("Reload Current Puzzle")]
        public void ReloadCurrentPuzzle()
        {
            LoadPuzzle(currentPuzzleIndex);
        }

        [ContextMenu("Load Next Puzzle")]
        public void LoadNextPuzzle()
        {
            int nextIndex = (currentPuzzleIndex + 1) % PuzzlePrefabs.Length;
            SwitchToPuzzle(nextIndex);
        }

        [ContextMenu("Load Previous Puzzle")]
        public void LoadPreviousPuzzle()
        {
            int prevIndex = currentPuzzleIndex - 1;
            if (prevIndex < 0) prevIndex = PuzzlePrefabs.Length - 1;
            SwitchToPuzzle(prevIndex);
        }
    }
}