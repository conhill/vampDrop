using UnityEngine;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    public class PuzzleEnhancer : MonoBehaviour
    {
        [Header("Enhancement Settings")]
        [Tooltip("Check user stats to determine enhancements")]
        public bool UseUserStats = true;
        
        [Tooltip("Use fake stats for testing (overrides UseUserStats)")]
        public bool UseTestStats = false;
        
        [Header("Test Stats (only used if UseTestStats is enabled)")]
        [Tooltip("Test value for rice collected")]
        public int TestRiceCollected = 5000;
        
        [Tooltip("Test value for helper count")]
        public int TestHelperCount = 5;
        
        [Header("Enhancement Prefabs")]
        [Tooltip("2x Multiplier drop zone prefab")]
        public GameObject Multiplier2xPrefab;
        
        [Tooltip("3x Multiplier drop zone prefab")]  
        public GameObject Multiplier3xPrefab;
        
        [Tooltip("Special bonus zone prefab")]
        public GameObject BonusZonePrefab;
        
        [Header("BonusZone Threshold")]
        [Tooltip("Helper count needed for bonus zones to appear")]
        public int HelpersForBonusZones = 3;
        // MultiplierZone spawn chances come from PlayerDataManager.DropPuzzle (x2/x3/x4GateChance)
        // and are upgraded via the shop — no static thresholds needed here.

        /// <param name="guaranteeOneX2Gate">
        /// When true (first post-tutorial run), forces at least one 2x gate regardless of player stats.
        /// Ensures the player has a good first impression of the multiplier system.
        /// </param>
        public void EnhancePuzzle(GameObject puzzleInstance, bool guaranteeOneX2Gate = false)
        {
            if (puzzleInstance == null)
            {
                Debug.LogError("[PuzzleEnhancer] Puzzle instance is NULL!");
                return;
            }

            Debug.Log($"[PuzzleEnhancer] Enhancing: {puzzleInstance.name} (guaranteeX2={guaranteeOneX2Gate})");

            EnhancementMarker[] markers = puzzleInstance.GetComponentsInChildren<EnhancementMarker>();

            if (markers.Length == 0)
            {
                Debug.LogWarning("[PuzzleEnhancer] No EnhancementMarkers found in puzzle prefab — add EnhancementMarker components to gate placeholder objects.");
                return;
            }

            // Get the player's current gate spawn chances
            DropPuzzleUpgrades gateStats = PlayerDataManager.Instance?.DropPuzzle;

            // Shuffle markers so the guaranteed gate lands on a random one, not always the first
            ShuffleArray(markers);

            bool x2GuaranteeConsumed = false;
            int enhanced = 0;

            foreach (EnhancementMarker marker in markers)
            {
                // Force x2 on the first MultiplierZone marker if guarantee is still pending
                bool forceX2ThisMarker = guaranteeOneX2Gate
                    && !x2GuaranteeConsumed
                    && marker.EnhancementType == EnhancementType.MultiplierZone;

                bool spawned = ProcessEnhancementMarker(marker, gateStats, forceX2ThisMarker);

                if (spawned)
                {
                    enhanced++;
                    if (forceX2ThisMarker)
                        x2GuaranteeConsumed = true;
                }
            }

            if (guaranteeOneX2Gate && !x2GuaranteeConsumed)
                Debug.LogWarning("[PuzzleEnhancer] Guaranteed x2 gate could not be placed — no MultiplierZone markers in prefab. Add EnhancementMarker (type=MultiplierZone) to the puzzle.");

            Debug.Log($"[PuzzleEnhancer] Done: {enhanced}/{markers.Length} markers became gates");
        }

        /// <summary>
        /// Decide what (if anything) to spawn at this marker.
        /// MultiplierZone: rolls against DropPuzzle gate chances from PlayerDataManager.
        /// BonusZone: rolls against helper count threshold (unchanged).
        /// </summary>
        private bool ProcessEnhancementMarker(EnhancementMarker marker, DropPuzzleUpgrades gateStats, bool forceX2)
        {
            GameObject prefab = null;
            string label = "none";

            switch (marker.EnhancementType)
            {
                case EnhancementType.MultiplierZone:
                    if (forceX2 && Multiplier2xPrefab != null)
                    {
                        prefab = Multiplier2xPrefab;
                        label = "2x (guaranteed)";
                    }
                    else if (gateStats != null)
                    {
                        // Roll cumulatively highest-to-lowest, matching ProgressionSystem.GenerateGateMultipliers
                        float roll = Random.Range(0f, 1f);
                        float cumulative = 0f;

                        cumulative += gateStats.x4GateChance;
                        if (roll < cumulative && Multiplier3xPrefab != null) // reuse 3x prefab for 4x until 4x prefab exists
                        {
                            prefab = Multiplier3xPrefab;
                            label = "4x→3x";
                        }
                        else
                        {
                            cumulative += gateStats.x3GateChance;
                            if (roll < cumulative && Multiplier3xPrefab != null)
                            {
                                prefab = Multiplier3xPrefab;
                                label = "3x";
                            }
                            else
                            {
                                cumulative += gateStats.x2GateChance;
                                if (roll < cumulative && Multiplier2xPrefab != null)
                                {
                                    prefab = Multiplier2xPrefab;
                                    label = "2x";
                                }
                            }
                        }
                    }
                    // else: all chances are 0 (new player, no upgrades) — marker is removed
                    break;

                case EnhancementType.BonusZone:
                    UserProgression stats = GetUserStats();
                    if (stats.ActiveHelperCount >= HelpersForBonusZones && BonusZonePrefab != null)
                    {
                        prefab = BonusZonePrefab;
                        label = "Bonus Zone";
                    }
                    break;

                case EnhancementType.ConditionalWall:
                    // Not yet implemented
                    break;
            }

            if (prefab != null)
            {
                ReplaceWithEnhancement(marker, prefab, label);
                Debug.Log($"[PuzzleEnhancer] {marker.name} → {label}");
                return true;
            }

            DestroyImmediate(marker.gameObject);
            return false;
        }

        private static void ShuffleArray(EnhancementMarker[] arr)
        {
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
        }

        private void ReplaceWithEnhancement(EnhancementMarker marker, GameObject replacementPrefab, string enhancementType)
        {
            // Store original transform data
            Vector3 position = marker.transform.position;
            Quaternion rotation = marker.transform.rotation;
            Vector3 scale = marker.transform.localScale;
            Transform parent = marker.transform.parent;
            
            // Create the enhanced zone
            GameObject enhanced = Instantiate(replacementPrefab, position, rotation, parent);
            enhanced.transform.localScale = scale;
            enhanced.name = $"{marker.name}_{enhancementType.Replace(" ", "")}";
            
            // Copy any special properties from the marker
            if (marker.CopyTagsAndLayers)
            {
                enhanced.tag = marker.gameObject.tag;
                enhanced.layer = marker.gameObject.layer;
            }
            
            // Destroy the original marker
            DestroyImmediate(marker.gameObject);
            
            Debug.Log($"[PuzzleEnhancer] ✨ Enhanced {marker.name} → {enhancementType}");
        }

        private UserProgression GetUserStats()
        {
            // Test mode override for debugging
            if (UseTestStats)
            {
                Debug.Log($"[PuzzleEnhancer] 🧪 Using TEST STATS: Rice={TestRiceCollected}, Helpers={TestHelperCount}");
                return new UserProgression
                {
                    TotalRiceCollected = TestRiceCollected,
                    ActiveHelperCount = TestHelperCount,
                    CompletedLevels = 10
                };
            }
            
            if (!UseUserStats)
            {
                // Return test stats for debugging
                return new UserProgression
                {
                    TotalRiceCollected = 2000,
                    ActiveHelperCount = 2,
                    CompletedLevels = 5
                };
            }
            
            // Connect to your existing PlayerDataManager system
            if (PlayerDataManager.Instance != null)
            {
                var pdm = PlayerDataManager.Instance;
                
                return new UserProgression
                {
                    TotalRiceCollected = pdm.RiceGrains,
                    ActiveHelperCount = pdm.Helpers.ownedGoblins + pdm.Helpers.ownedGhouls,
                    CompletedLevels = pdm.HighestLevelReached
                };
            }
            
            // Fallback if PlayerDataManager not found
            Debug.LogWarning("[PuzzleEnhancer] PlayerDataManager.Instance not found! Using defaults.");
            return new UserProgression
            {
                TotalRiceCollected = 0,
                ActiveHelperCount = 0,
                CompletedLevels = 0
            };
        }
    }

    // Data structure to hold user progression
    [System.Serializable]
    public struct UserProgression
    {
        public int TotalRiceCollected;
        public int ActiveHelperCount;
        public int CompletedLevels;
    }
}