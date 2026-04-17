using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// UI for ball drop completion and daylight warnings
    /// Shows "Complete" message and "Go Back Inside" button
    /// </summary>
    public class BallDropUI : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Panel shown when drop is complete")]
        public GameObject completionPanel;
        
        [Tooltip("Text showing completion message")]
        public TextMeshProUGUI completionText;
        
        [Tooltip("Button to return to FPS scene")]
        public Button goBackButton;
        
        [Tooltip("Panel shown for daylight warning")]
        public GameObject warningPanel;
        
        [Tooltip("Text showing warning message")]
        public TextMeshProUGUI warningText;
        
        [Tooltip("Time display (top of screen)")]
        public TextMeshProUGUI timeDisplay;
        
        [Header("Settings")]
        public string fpsSceneName = "FPS_Collect";

        [Header("Comics")]
        [Tooltip("Comic shown after completing the first (tutorial) drop. nextSceneName should be FPS_Collect.")]
        public Vampire.ComicSequenceConfig afterFirstDropComic;

        private static bool _afterFirstDropComicShown = false;

        private BallDropCompletionManager completionManager;
        private DayNightCycleManager cycleManager;
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        private int currencyEarned = 0;
        private int startingCurrency = 0;

        // ── Change-detection cache ────────────────────────────────────────
        private int    _lastTimeSecond = -1;
        private string _lastTimeStr    = "";

        // ── Strings built ONCE at event time, displayed statically ────────
        private string _completionStatsStr = "";   // set in ShowCompletionUI
        private string _warningStr         = "";   // set in ShowDaylightWarning
        private bool   _showWarning        = false;

        // ── OnGUI cached styles (allocated once, not every frame) ─────────
        private GUIStyle _guiBigStyle;
        private GUIStyle _guiWarningStyle;
        private GUIStyle _guiStatStyle;
        private bool     _guiStylesBuilt;
        
        private void Start()
        {
            // Clean up rice entities from FPS scene (ECS entities persist across scenes)
            CleanupRiceEntities();
            
            // Capture starting currency AFTER cleanup but BEFORE any scoring
            if (playerData != null)
            {
                startingCurrency = playerData.TotalCurrency;
                // Debug.Log($"[BallDropUI] Starting currency: ${startingCurrency / 100f:F2}");
            }
            
            // Setup puzzle (uses TutorialCompleted flag to pick tutorial vs normal)
            if (PuzzleManager.Instance != null)
                PuzzleManager.Instance.SetupPuzzleForCurrentScene();
            else if (TutorialManager.Instance != null)
                TutorialManager.Instance.SetupTutorialPuzzle();

            // Notify tutorial manager that we visited ball drop
            if (TutorialManager.Instance != null)
                TutorialManager.Instance.NotifyBallDropVisit();
            
            // Find managers
            completionManager = FindObjectOfType<BallDropCompletionManager>();
            if (completionManager != null)
            {
                // IMPORTANT: Reset completion state at scene start
                completionManager.isComplete = false;
                completionManager.isDropActive = false;
                
                completionManager.OnDropComplete += ShowCompletionUI;
                // Debug.Log("[BallDropUI] Subscribed to OnDropComplete event and reset completion state");
            }
            else
            {
                // Debug.LogError("[BallDropUI] BallDropCompletionManager not found!");
            }
            
            cycleManager = DayNightCycleManager.Instance;
            if (cycleManager != null)
            {
                cycleManager.OnDaylightWarning += ShowDaylightWarning;
                cycleManager.OnNightEnd += HideWarning;
            }
            
            // Wire up button
            if (goBackButton != null)
            {
                goBackButton.onClick.AddListener(GoBackToFPS);
            }
            
            // Hide panels initially
            if (completionPanel != null) completionPanel.SetActive(false);
            if (warningPanel != null) warningPanel.SetActive(false);
            
            // Debug.Log("[BallDropUI] Initialized");
        }
        
        private void OnDestroy()
        {
            if (completionManager != null)
            {
                completionManager.OnDropComplete -= ShowCompletionUI;
            }
            
            if (cycleManager != null)
            {
                cycleManager.OnDaylightWarning -= ShowDaylightWarning;
                cycleManager.OnNightEnd -= HideWarning;
            }
            
            if (goBackButton != null)
            {
                goBackButton.onClick.RemoveListener(GoBackToFPS);
            }
        }
        
        private void Update()
        {
            // ── During the drop phase the player is just watching balls fall.
            //    No UI polling needed until completion fires via event.
            //    Only handle input and the once-per-second time display. ──────

            // ESC is handled by EscapeMenuManager when it exists.
            // Fall back to direct return only if there is no ESC manager.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Vampire.EscapeMenuManager.Instance != null)
                    return; // EscapeMenuManager.Update() owns ESC
                GoBackToFPS();
                return;
            }

            // Post-completion: E key to return
            if (completionManager != null && completionManager.isComplete)
            {
                if (Input.GetKeyDown(KeyCode.E))
                    GoBackToFPS();
                return; // Nothing else to update — completion screen is static
            }

            // Time display — only update once per second, only if TMP ref is assigned
            if (timeDisplay != null && cycleManager != null)
            {
                int sec = Mathf.CeilToInt(cycleManager.GetTimeRemaining());
                if (sec != _lastTimeSecond)
                {
                    _lastTimeSecond  = sec;
                    string phase     = cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day
                                       ? "DAY" : "NIGHT";
                    _lastTimeStr     = $"{phase} - {cycleManager.GetFormattedTimeRemaining()}";
                    timeDisplay.text = _lastTimeStr;
                }
            }
        }
        
        /// <summary>
        /// Hide rice entities from FPS scene (they persist in ECS, just make them invisible/non-interactive)
        /// Also DESTROY any riceball entities from ball drop
        /// </summary>
        private void CleanupRiceEntities()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                // Debug.LogWarning("[BallDropUI] No ECS world found");
                return;
            }
            
            var entityManager = world.EntityManager;
            
            // HIDE FPS rice entities by adding Disabled — the only flag the DOTS renderer respects.
            // RiceHidden was a custom tag that nothing checked for visibility, so it had no effect.
            var riceQuery = entityManager.CreateEntityQuery(new Unity.Entities.EntityQueryDesc
            {
                All     = new[] { Unity.Entities.ComponentType.ReadOnly<Vampire.Rice.RiceEntity>() },
                Options = Unity.Entities.EntityQueryOptions.Default // exclude already-Disabled
            });
            if (!riceQuery.IsEmpty)
                entityManager.AddComponent<Unity.Entities.Disabled>(riceQuery);
            riceQuery.Dispose();
            
            // DESTROY riceball entities from ball drop (they shouldn't persist)
            var ballQuery = entityManager.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<DropPuzzle.RiceBallTag>());
            var ballEntities = ballQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            int destroyed = ballEntities.Length;
            if (destroyed > 0)
            {
                foreach (var entity in ballEntities)
                {
                    entityManager.DestroyEntity(entity);
                }
                // Debug.Log($"[BallDropUI] ♻️ Destroyed {destroyed} riceball entities from ball drop");
            }
            
            ballEntities.Dispose();
            ballQuery.Dispose();
        }
        
        /// <summary>
        /// Show completion UI
        /// </summary>
        private void ShowCompletionUI()
        {
            // Debug.Log("[BallDropUI] ShowCompletionUI called!");
            
            // Pause day/night cycle while viewing completion screen
            if (cycleManager != null)
            {
                cycleManager.Pause();
            }
            
            // Enable cursor for clicking button
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            // Notify tutorial manager that riceballs were dropped
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.NotifyRiceBallsDropped();
            }

            // Mark tutorial complete after first successful drop
            if (playerData != null && !playerData.TutorialCompleted)
            {
                playerData.TutorialCompleted = true;
                playerData.SavePlayerData();
                Debug.Log("[BallDropUI] Tutorial marked complete — puzzle 2+ will load on next visit");
            }
            
            if (completionPanel == null)
            {
                // Debug.LogError("[BallDropUI] ⚠️ completionPanel is NULL! Assign it in the Inspector.");
                return;
            }
            
            // Calculate currency earned
            if (playerData != null)
            {
                currencyEarned = playerData.TotalCurrency - startingCurrency;
            }
            
            completionPanel.SetActive(true);
            
            if (completionText != null)
            {
                if (completionManager != null)
                {
                    string message = "🎉 Drop Complete!\n\n";
                    
                    // Show currency earned
                    if (playerData != null && currencyEarned > 0)
                    {
                        message += $"💰 Earned: ${currencyEarned / 100f:F2}\n";
                    }
                    else
                    {
                        message += "✅ Complete!\n";
                    }
                    
                    message += "\n[E] Return to FPS mode";
                    
                    completionText.text = message;
                }
                else
                {
                    completionText.text = "Drop Complete!\n\n[E] Return";
                }
            }
            
            // Debug.Log($"[BallDropUI] Completion - Earned ${currencyEarned} - Press [E] to return");
        }
        
        /// <summary>
        /// Show daylight warning (10s before day)
        /// </summary>
        private void ShowDaylightWarning()
        {
            if (warningPanel == null) return;
            
            warningPanel.SetActive(true);
            
            if (warningText != null && cycleManager != null)
            {
                float timeLeft = cycleManager.GetTimeRemaining();
                warningText.text = $"⚠️ DAYLIGHT IN {Mathf.CeilToInt(timeLeft)}s!\nFinish up or salvage!";
            }
            
            // Debug.LogWarning("[BallDropUI] Showing daylight warning");
        }
        
        /// <summary>
        /// Hide warning panel
        /// </summary>
        private void HideWarning()
        {
            if (warningPanel != null)
            {
                warningPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// Go back to FPS scene
        /// </summary>
        private void GoBackToFPS()
        {
            // Resume day/night cycle when leaving completion screen
            if (cycleManager != null)
                cycleManager.Resume();

            // Unhide rice entities before returning to FPS scene
            UnhideRiceEntities();

            // Comic #3: play after the first (tutorial) drop, once only
            bool showAfterDropComic = !_afterFirstDropComicShown
                && afterFirstDropComic != null
                && playerData != null
                && playerData.TutorialCompleted;

            if (showAfterDropComic)
            {
                _afterFirstDropComicShown = true;
                // Comic's nextSceneName must be "FPS_Collect" — set in the Inspector
                Vampire.ComicSceneLoader.LoadComic(afterFirstDropComic);
                return;
            }

            SceneManager.LoadScene(fpsSceneName);
        }
        
        /// <summary>
        /// Unhide rice entities when returning to FPS scene
        /// </summary>
        private void UnhideRiceEntities()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                // Debug.LogWarning("[BallDropUI] No ECS world found for unhiding rice");
                return;
            }
            
            var entityManager = world.EntityManager;
            
            // Re-enable FPS rice entities (were disabled via Unity.Entities.Disabled)
            var query = entityManager.CreateEntityQuery(new Unity.Entities.EntityQueryDesc
            {
                All     = new[] { Unity.Entities.ComponentType.ReadOnly<Vampire.Rice.RiceEntity>() },
                Options = Unity.Entities.EntityQueryOptions.IncludeDisabledEntities
            });
            if (!query.IsEmpty)
                entityManager.RemoveComponent<Unity.Entities.Disabled>(query);
            query.Dispose();
        }
        
        /// <summary>
        /// Create basic UI on-screen (for scenes without canvas setup)
        /// Always show completion screen here as a fallback
        /// </summary>
        private void OnGUI()
        {
            // Build styles once
            if (!_guiStylesBuilt)
            {
                _guiBigStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                _guiBigStyle.normal.textColor = Color.white;

                _guiWarningStyle = new GUIStyle(_guiBigStyle);
                _guiWarningStyle.normal.textColor = Color.yellow;

                _guiStatStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 18, alignment = TextAnchor.UpperLeft };
                _guiStatStyle.normal.textColor = Color.white;

                _guiStylesBuilt = true;
            }

            bool isComplete = completionManager != null && completionManager.isComplete;

            // ── COMPLETION SCREEN (shown once, fully static after ShowCompletionUI fires) ──
            if (isComplete)
            {
                // Canvas panel is handling it — don't duplicate
                if (completionPanel != null && completionPanel.activeSelf) return;

                // OnGUI fallback completion screen — strings built once in ShowCompletionUI,
                // just display the cached values here
                GUI.Box(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 150, 500, 300), "");

                _guiBigStyle.fontSize = 32;
                _guiBigStyle.normal.textColor = Color.green;
                GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 120, 500, 50),
                    "Drop Complete!", _guiBigStyle);

                _guiBigStyle.fontSize = 24;
                _guiBigStyle.normal.textColor = Color.yellow;
                GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 50, 500, 80),
                    _completionStatsStr, _guiBigStyle);

                _guiBigStyle.fontSize = 28;
                GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 + 50, 500, 40),
                    "[E] Return to FPS mode", _guiBigStyle);

                if (GUI.Button(new Rect(Screen.width / 2 - 100, Screen.height / 2 + 95, 200, 40), "or Click Here"))
                    GoBackToFPS();

                return;
            }

            // ── DURING DROP PHASE — minimal UI, no inventory polling ──────────
            // Time display fallback (only if no TMP timeDisplay assigned)
            if (completionPanel == null && cycleManager != null && !string.IsNullOrEmpty(_lastTimeStr))
                GUI.Label(new Rect(10, 10, 300, 40), _lastTimeStr, _guiBigStyle);

            // Daylight warning (event-driven text, just display the cached string)
            if (_showWarning && !string.IsNullOrEmpty(_warningStr))
                GUI.Label(new Rect(Screen.width / 2 - 200, 60, 400, 40), _warningStr, _guiWarningStyle);

            // Go Back button (no inventory query needed)
            if (completionPanel == null)
            {
                if (GUI.Button(new Rect(Screen.width - 220, Screen.height - 60, 200, 50), "Go Back [Esc]"))
                    GoBackToFPS();
            }
        }
    }
}
