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
        
        private BallDropCompletionManager completionManager;
        private DayNightCycleManager cycleManager;
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        private int currencyEarned = 0; // Track currency earned this session
        private int startingCurrency = 0;
        
        private void Start()
        {
            // Clean up rice entities from FPS scene (ECS entities persist across scenes)
            CleanupRiceEntities();
            
            // Capture starting currency AFTER cleanup but BEFORE any scoring
            if (playerData != null)
            {
                startingCurrency = playerData.TotalCurrency;
                Debug.Log($"[BallDropUI] Starting currency: ${startingCurrency / 100f:F2}");
            }
            
            // Setup tutorial puzzle if in tutorial mode
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.SetupTutorialPuzzle();
                TutorialManager.Instance.NotifyBallDropVisit();
            }
            
            // Find managers
            completionManager = FindObjectOfType<BallDropCompletionManager>();
            if (completionManager != null)
            {
                completionManager.OnDropComplete += ShowCompletionUI;
                Debug.Log("[BallDropUI] Subscribed to OnDropComplete event");
            }
            else
            {
                Debug.LogError("[BallDropUI] BallDropCompletionManager not found!");
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
            
            Debug.Log("[BallDropUI] Initialized");
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
            // Update time display
            if (timeDisplay != null && cycleManager != null)
            {
                string timeStr = cycleManager.GetFormattedTimeRemaining();
                string phase = cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day ? "☀️ DAY" : "🌙 NIGHT";
                timeDisplay.text = $"{phase} - {timeStr}";
            }
            
            // Allow escape key to go back anytime
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                GoBackToFPS();
            }
            
            // Allow E key when complete
            if (completionManager != null && completionManager.isComplete && Input.GetKeyDown(KeyCode.E))
            {
                GoBackToFPS();
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
                Debug.LogWarning("[BallDropUI] No ECS world found");
                return;
            }
            
            var entityManager = world.EntityManager;
            
            // HIDE FPS rice entities (only RiceEntity, NOT riceballs)
            var riceQuery = entityManager.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<Vampire.Rice.RiceEntity>(),
                Unity.Entities.ComponentType.Exclude<Vampire.Rice.RiceHidden>());
            var riceEntities = riceQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            int hidden = riceEntities.Length;
            if (hidden > 0)
            {
                foreach (var entity in riceEntities)
                {
                    if (!entityManager.HasComponent<Vampire.Rice.RiceHidden>(entity))
                    {
                        entityManager.AddComponent<Vampire.Rice.RiceHidden>(entity);
                    }
                }
                Debug.Log($"[BallDropUI] 👁️‍🗨️ Hidden {hidden} FPS rice entities (not destroyed)");
            }
            
            riceEntities.Dispose();
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
                Debug.Log($"[BallDropUI] ♻️ Destroyed {destroyed} riceball entities from ball drop");
            }
            
            ballEntities.Dispose();
            ballQuery.Dispose();
        }
        
        /// <summary>
        /// Show completion UI
        /// </summary>
        private void ShowCompletionUI()
        {
            Debug.Log("[BallDropUI] ShowCompletionUI called!");
            
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
            
            if (completionPanel == null)
            {
                Debug.LogError("[BallDropUI] ⚠️ completionPanel is NULL! Assign it in the Inspector.");
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
            
            Debug.Log($"[BallDropUI] Completion - Earned ${currencyEarned} - Press [E] to return");
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
            
            Debug.LogWarning("[BallDropUI] Showing daylight warning");
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
            Debug.Log($"[BallDropUI] Loading scene: {fpsSceneName}");
            
            // Resume day/night cycle when leaving completion screen
            if (cycleManager != null)
            {
                cycleManager.Resume();
            }
            
            // Unhide rice entities before returning to FPS scene
            UnhideRiceEntities();
            
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
                Debug.LogWarning("[BallDropUI] No ECS world found for unhiding rice");
                return;
            }
            
            var entityManager = world.EntityManager;
            
            // Only unhide FPS rice entities (RiceEntity with RiceHidden)
            var query = entityManager.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<Vampire.Rice.RiceEntity>(),
                Unity.Entities.ComponentType.ReadOnly<Vampire.Rice.RiceHidden>());
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            int unhidden = entities.Length;
            if (unhidden > 0)
            {
                foreach (var entity in entities)
                {
                    if (entityManager.Exists(entity))
                    {
                        entityManager.RemoveComponent<Vampire.Rice.RiceHidden>(entity);
                    }
                }
                Debug.Log($"[BallDropUI] 👁️ Unhidden {unhidden} FPS rice entities (ready to collect)");
            }
            else
            {
                Debug.LogWarning("[BallDropUI] No hidden rice entities found to unhide!");
            }
            
            entities.Dispose();
            query.Dispose();
        }
        
        /// <summary>
        /// Create basic UI on-screen (for scenes without canvas setup)
        /// Always show completion screen here as a fallback
        /// </summary>
        private void OnGUI()
        {
            // Simple on-screen UI
            GUIStyle bigStyle = new GUIStyle(GUI.skin.label);
            bigStyle.fontSize = 24;
            bigStyle.normal.textColor = Color.white;
            bigStyle.alignment = TextAnchor.MiddleCenter;
            
            GUIStyle warningStyle = new GUIStyle(bigStyle);
            warningStyle.normal.textColor = Color.yellow;
            
            // Time display (only if proper canvas doesn't exist)
            if (completionPanel == null && cycleManager != null)
            {
                string timeStr = cycleManager.GetFormattedTimeRemaining();
                string phase = cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day ? "☀️ DAY" : "🌙 NIGHT";
                GUI.Label(new Rect(10, 10, 300, 40), $"{phase} - {timeStr}", bigStyle);
            }
            
            // COMPLETION SCREEN - Always show when complete (critical for progression!)
            if (completionManager != null && completionManager.isComplete)
            {
                // Recalculate currency earned for UI
                if (playerData != null && currencyEarned == 0)
                {
                    currencyEarned = playerData.TotalCurrency - startingCurrency;
                }
                
                // If proper canvas UI exists and is active, don't duplicate
                if (completionPanel != null && completionPanel.activeSelf)
                {
                    return; // Canvas UI is handling it
                }
                
                // FALLBACK: OnGUI completion screen
                GUI.Box(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 150, 500, 300), "");
                
                // Title
                bigStyle.fontSize = 32;
                bigStyle.normal.textColor = Color.green;
                GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 120, 500, 50), 
                    "🎉 Drop Complete!", bigStyle);
                
                // Stats - Show money earned
                bigStyle.fontSize = 24;
                bigStyle.normal.textColor = Color.yellow;
                string statsText = "";
                
                // Show currency earned
                if (currencyEarned > 0)
                {
                    statsText = $"💰 Earned: ${currencyEarned / 100f:F2}";
                }
                else
                {
                    statsText = "✅ Complete!";
                }
                
                GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 50, 500, 80), 
                    statsText, bigStyle);
                
                // Return prompt
                bigStyle.fontSize = 28;
                bigStyle.normal.textColor = Color.yellow;
                GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 + 50, 500, 40), 
                    "✅ [E] Return to FPS mode", bigStyle);
                
                // Button alternative
                if (GUI.Button(new Rect(Screen.width / 2 - 100, Screen.height / 2 + 95, 200, 40), "or Click Here"))
                {
                    GoBackToFPS();
                }
                
                return; // Don't show other UI when complete
            }
            
            // Only show these if no proper canvas exists
            if (completionPanel == null)
            {
                // Show rice count and currency at top (replaces old DropPuzzleUI)
                GUIStyle statStyle = new GUIStyle(GUI.skin.label);
                statStyle.fontSize = 18;
                statStyle.normal.textColor = Color.white;
                statStyle.alignment = TextAnchor.UpperLeft;
                
                if (playerData != null)
                {
                    int totalBalls = playerData.Inventory.GetTotalBalls();
                    GUI.Label(new Rect(10, 40, 300, 30), $"Rice Balls: {totalBalls}", statStyle);
                    GUI.Label(new Rect(10, 70, 300, 30), $"Currency: ${playerData.TotalCurrency / 100f:F2}", statStyle);
                }
                
                // Show "Go Back" button even before completion (in case they want to leave early)
                if (GUI.Button(new Rect(Screen.width - 220, Screen.height - 60, 200, 50), "Go Back Inside [Esc]"))
                {
                    GoBackToFPS();
                }
                
                // Warning
                if (cycleManager != null && cycleManager.isWarningActive && completionManager != null && !completionManager.isComplete)
                {
                    float timeLeft = cycleManager.GetTimeRemaining();
                    GUI.Label(new Rect(Screen.width / 2 - 200, 60, 400, 40), 
                        $"⚠️ DAYLIGHT IN {Mathf.CeilToInt(timeLeft)}s!", warningStyle);
                }
            }
        }
    }
}
