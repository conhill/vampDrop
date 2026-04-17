using UnityEngine;
using UnityEngine.SceneManagement;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Handles entry to ball drop puzzle - only allows at night
    /// Place this on a trigger collider or door in FPS scene
    /// </summary>
    public class BallDropSceneEntry : MonoBehaviour
    {
        [Header("Scene")]
        public string dropPuzzleSceneName = "DropPuzzle";

        [Header("Comics")]
        [Tooltip("Comic to show the first time the player goes outside (tutorialStep 4). " +
                 "Set nextSceneName = 'DropPuzzle' on this asset.")]
        public Vampire.ComicSequenceConfig goingOutsideComic;

        [Tooltip("Comic to show on the second outside visit (after completing the tutorial drop). " +
                 "Set nextSceneName = 'DropPuzzle' on this asset.")]
        public Vampire.ComicSequenceConfig secondVisitComic;

        // Persists for the session; the tutorial step guard also prevents re-triggering after reload
        private static bool _goingOutsideComicShown = false;
        private static bool _secondOutsideComicShown = false;
        
        [Header("UI Messages")]
        public string nightOnlyMessage = "⚠️ Ball Drop only available at NIGHT!\nTime remaining: {0}";
        public string enterPromptMessage = "Press [F] to enter Ball Drop";
        public string noBallsMessage = "⚠️ Craft riceballs first!\nPress [E] to craft (need 5 rice)";
        
        private bool playerInRange = false;
        private DayNightCycleManager cycleManager => DayNightCycleManager.Instance;
        private PlayerDataManager playerData => PlayerDataManager.Instance;

        // ── OnGUI cache ───────────────────────────────────────────────────
        private GUIStyle _guiStyle;
        private bool     _guiStyleBuilt;

        // Track last state so we only rebuild strings on actual changes
        private int    _lastBallCount   = -1;
        private bool   _lastCanEnter    = false;
        private int    _lastTimeSecond  = -1;
        private string _guiMessage      = "";
        private Color  _guiColor        = Color.white;
        
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                playerInRange = true;
                // Debug.Log("[BallDropEntry] Player near ball drop entrance");
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                playerInRange = false;
            }
        }
        
        private void Update()
        {
            if (!playerInRange) return;
            
            // Check for input
            if (Input.GetKeyDown(KeyCode.F))
            {
                TryEnterBallDrop();
            }
        }
        
        private void TryEnterBallDrop()
        {
            if (cycleManager == null)
            {
                // No cycle manager - allow entry (fallback)
                // Debug.LogWarning("[BallDropEntry] No DayNightCycleManager found, allowing entry");
                LoadBallDropScene();
                return;
            }
            
            if (!cycleManager.CanEnterBallDrop())
            {
                // It's day time - deny entry
                float timeUntilNight = cycleManager.GetTimeRemaining();
                // Debug.LogWarning($"[BallDropEntry] ⚠️ Ball drop only at night! {timeUntilNight:F0}s until night");
                return;
            }
            
            // Check if player has riceballs
            if (playerData != null)
            {
                int totalBalls = playerData.Inventory.GetTotalBalls();
                if (totalBalls == 0)
                {
                    // Debug.LogWarning("[BallDropEntry] ⚠️ No riceballs! Craft some first (need 5 rice)");
                    return;
                }
            }
            
            // Show "going outside" comic the first time (check step BEFORE notifying, as notify advances it)
            bool isFirstTimeOutside = !_goingOutsideComicShown
                && goingOutsideComic != null
                && TutorialManager.Instance != null
                && TutorialManager.Instance.tutorialStep == 4;

            // Notify tutorial manager that we're visiting ball drop (completes "Go Outside" quest)
            if (TutorialManager.Instance != null)
                TutorialManager.Instance.NotifyBallDropVisit();

            // Comic #4: second outside visit — fires after the tutorial drop is done (TutorialCompleted=true)
            bool isSecondTimeOutside = !_secondOutsideComicShown
                && secondVisitComic != null
                && _goingOutsideComicShown
                && PlayerDataManager.Instance != null
                && PlayerDataManager.Instance.TutorialCompleted;

            if (isFirstTimeOutside)
            {
                _goingOutsideComicShown = true;
                // Override ensures the comic always lands in DropPuzzle regardless of
                // what nextSceneName the ScriptableObject asset has set.
                Vampire.ComicSceneManager.NextSceneOverride = dropPuzzleSceneName;
                Vampire.ComicSceneLoader.LoadComic(goingOutsideComic);
            }
            else if (isSecondTimeOutside)
            {
                _secondOutsideComicShown = true;
                Vampire.ComicSceneManager.NextSceneOverride = dropPuzzleSceneName;
                Vampire.ComicSceneLoader.LoadComic(secondVisitComic);
            }
            else
            {
                LoadBallDropScene();
            }
        }
        
        private void LoadBallDropScene()
        {
            // Prefer GameSceneManager so rice hiding and data-passing hooks fire correctly
            if (Vampire.GameSceneManager.Instance != null)
                Vampire.GameSceneManager.Instance.TransitionToDropPuzzle();
            else
                SceneManager.LoadScene(dropPuzzleSceneName);
        }
        
        private void OnGUI()
        {
            if (!playerInRange || cycleManager == null) return;

            // Build style once
            if (!_guiStyleBuilt)
            {
                _guiStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 20,
                    alignment = TextAnchor.MiddleCenter
                };
                _guiStyle.normal.textColor = Color.white;
                _guiStyleBuilt = true;
            }

            bool inEarlyTutorial = TutorialManager.Instance != null &&
                                   TutorialManager.Instance.tutorialActive &&
                                   TutorialManager.Instance.tutorialStep <= 2;

            bool canEnter  = cycleManager.CanEnterBallDrop();
            int  ballCount = playerData != null ? playerData.Inventory.GetTotalBalls() : 0;
            int  timeSec   = Mathf.CeilToInt(cycleManager.GetTimeRemaining());

            // Only rebuild the message string when something actually changed
            if (canEnter != _lastCanEnter || ballCount != _lastBallCount || timeSec != _lastTimeSecond)
            {
                _lastCanEnter   = canEnter;
                _lastBallCount  = ballCount;
                _lastTimeSecond = timeSec;

                if (!canEnter)
                {
                    if (!inEarlyTutorial)
                    {
                        _guiColor   = Color.yellow;
                        _guiMessage = string.Format(nightOnlyMessage, cycleManager.GetFormattedTimeRemaining());
                    }
                    else
                    {
                        _guiMessage = ""; // suppress in early tutorial
                    }
                }
                else if (ballCount == 0)
                {
                    bool show = !inEarlyTutorial || TutorialManager.Instance.tutorialStep >= 2;
                    _guiColor   = Color.red;
                    _guiMessage = show ? noBallsMessage : "";
                }
                else
                {
                    bool show = !inEarlyTutorial || TutorialManager.Instance.tutorialStep >= 3;
                    _guiColor   = Color.green;
                    _guiMessage = show ? $"{enterPromptMessage}\n({ballCount} riceballs ready)" : "";
                }
            }

            if (string.IsNullOrEmpty(_guiMessage)) return;

            _guiStyle.normal.textColor = _guiColor;
            GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height - 100, 500, 60),
                _guiMessage, _guiStyle);
        }
    }
}
