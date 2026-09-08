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

        [Header("Always-On Gate")]
        [Tooltip("Guarantee at least one multiplier gate every run so the prep-phase spinner " +
                 "always has something to roll. Turn off for pure chance-based gates.")]
        public bool AlwaysGuaranteeGate = true;
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

            // Two different ideas wear the same "guarantee" word, and conflating them turns
            // every single drop into a scripted win:
            //   promisedToPlayer     — the tutorial / first-run promise. A contract: the gate
            //                          is stamped and the pre-drop slot is forced to land on it.
            //   AlwaysGuaranteeGate  — house-keeping filler so the spinner always has at least
            //                          one gate to roll for. NOT a promise; a losing spin is
            //                          still allowed to take it away.
            // Both place a gate; only the first is stamped as guaranteed.
            bool promisedToPlayer = guaranteeOneX2Gate;
            bool guarantee = promisedToPlayer || AlwaysGuaranteeGate;
            bool x2GuaranteeConsumed = false;
            int enhanced = 0;

            foreach (EnhancementMarker marker in markers)
            {
                // Force x2 on the first MultiplierZone marker if guarantee is still pending
                bool forceX2ThisMarker = guarantee
                    && !x2GuaranteeConsumed
                    && marker.EnhancementType == EnhancementType.MultiplierZone;

                bool spawned = ProcessEnhancementMarker(marker, gateStats, forceX2ThisMarker,
                                                        forceX2ThisMarker && promisedToPlayer);

                if (spawned)
                {
                    enhanced++;
                    if (forceX2ThisMarker)
                        x2GuaranteeConsumed = true;
                }
            }

            if (guarantee && !x2GuaranteeConsumed)
                Debug.LogWarning("[PuzzleEnhancer] Guaranteed gate could not be placed — no MultiplierZone markers in prefab. Add EnhancementMarker (type=MultiplierZone) to the puzzle.");

            Debug.Log($"[PuzzleEnhancer] Done: {enhanced}/{markers.Length} markers became gates");
        }

        /// <summary>
        /// Decide what (if anything) to spawn at this marker.
        /// MultiplierZone: rolls against DropPuzzle gate chances from PlayerDataManager.
        /// BonusZone: rolls against helper count threshold (unchanged).
        /// </summary>
        private bool ProcessEnhancementMarker(EnhancementMarker marker, DropPuzzleUpgrades gateStats,
                                              bool forceX2, bool stampPromise)
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
                GameObject spawned = ReplaceWithEnhancement(marker, prefab, label);

                // Stamp the promise onto the gate itself. GateRollController reads it as a
                // FLOOR: a winning spin may raise the gate, a losing one may not tear it down.
                // Carrying the guarantee on the object is what stops this decision and the
                // slot machine.s roll from silently contradicting each other.
                //
                // Only a gate the game actually PROMISED is stamped. The AlwaysGuaranteeGate
                // filler gate is deliberately left unstamped so it stays at the mercy of the
                // roll — stamping it would make every drop a forced win and flatten the slot
                // machine entirely.
                if (stampPromise && spawned != null)
                {
                    var guaranteed = spawned.GetComponent<MultiplierGate>();
                    if (guaranteed != null)
                        guaranteed.GuaranteedMultiplier = Mathf.Max(1, guaranteed.Multiplier);
                }
                return true;
            }

            // No enhancement — remove the placeholder
            string name = marker.name;
            DestroyImmediate(marker.gameObject);
            Debug.Log($"[PuzzleEnhancer] {name} → no enhancement (roll missed or prefab unassigned)");
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

        /// <summary>Swaps the marker for the real prefab and returns the object it spawned.</summary>
        private GameObject ReplaceWithEnhancement(EnhancementMarker marker, GameObject replacementPrefab, string enhancementType)
        {
            // Cache name before destroying
            string markerName = marker.name;

            Vector3 position = marker.transform.position;
            Quaternion rotation = marker.transform.rotation;
            Vector3 scale = marker.transform.localScale;
            Transform parent = marker.transform.parent;
            bool copyTagsAndLayers = marker.CopyTagsAndLayers;
            string markerTag = marker.gameObject.tag;
            int markerLayer = marker.gameObject.layer;

            // Create the enhanced zone
            GameObject enhanced = Instantiate(replacementPrefab, position, rotation, parent);
            enhanced.name = $"{markerName}_{enhancementType.Replace(" ", "")}";

            // Fit the gate INSIDE the marker's box preserving aspect, rather than copying the
            // marker's scale onto it. Markers are authored as flat boxes (puzzle1's is
            // 5 x 2 x 4) while a gate model is ~3.2 x 2.3 x 0.2 — assigning the scale directly
            // stretched gates ~18x in depth and smeared the posts into slabs.
            FitToMarkerBox(enhanced.transform, scale);

            if (copyTagsAndLayers)
            {
                enhanced.tag = markerTag;
                enhanced.layer = markerLayer;
            }

            DestroyImmediate(marker.gameObject);

            Debug.Log($"[PuzzleEnhancer] Enhanced {markerName} → {enhancementType} " +
                      $"pos={enhanced.transform.position} localScale={enhanced.transform.localScale}");
            return enhanced;
        }

        /// <summary>
        /// Scales <paramref name="t"/> uniformly so its rendered bounds fit inside the box the
        /// marker described, then re-centres it on the marker. Uniform scaling is the point —
        /// gates carry readable signage and text, and any non-uniform squash makes the number
        /// unreadable and the posts wrong.
        ///
        /// A prefab with no renderers (a plain trigger volume, like the old placeholder zones)
        /// keeps the marker's raw scale, so existing non-visual enhancements behave as before.
        /// </summary>
        private static void FitToMarkerBox(Transform t, Vector3 markerScale)
        {
            if (!TryMeasureLocalBounds(t, out Bounds b))
            {
                t.localScale = markerScale;   // no geometry to fit — legacy behaviour
                return;
            }

            var target = new Vector3(
                Mathf.Abs(markerScale.x), Mathf.Abs(markerScale.y), Mathf.Abs(markerScale.z));

            // X and Y only. A marker's Z is board THICKNESS, not gate size — every marker in
            // the project is authored 4 units deep while the gate model is ~0.2, so letting Z
            // into the fit lets board thickness decide how large the signage is.
            float s = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                if (b.size[i] < 0.0001f) continue;      // flat axis can't constrain the fit
                s = Mathf.Min(s, target[i] / b.size[i]);
            }
            if (s <= 0f || float.IsInfinity(s)) s = 1f;

            t.localScale = t.localScale * s;

            // Re-centre: the model's bounds centre may sit well off its pivot, so anchoring the
            // pivot alone would hang the gate off to one side of the marker.
            if (TryMeasureLocalBounds(t, out Bounds scaled))
                t.position += t.position - scaled.center;
        }

        /// <summary>
        /// World bounds from mesh data through each renderer's matrix. Avoids Renderer.bounds,
        /// which returns un-transformed mesh bounds in the same frame an object is instantiated.
        /// </summary>
        private static bool TryMeasureLocalBounds(Transform root, out Bounds bounds)
        {
            bool started = false;
            Bounds acc = default;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || mf.GetComponent<Renderer>() == null) continue;

                Bounds mb = mesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix;

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? mb.min.x : mb.max.x,
                        (c & 2) == 0 ? mb.min.y : mb.max.y,
                        (c & 4) == 0 ? mb.min.z : mb.max.z);

                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!started) { acc = new Bounds(p, Vector3.zero); started = true; }
                    else acc.Encapsulate(p);
                }
            }

            bounds = acc;
            return started;
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