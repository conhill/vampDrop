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
        
        [Header("UI Messages")]
        public string nightOnlyMessage = "⚠️ Ball Drop only available at NIGHT!\nTime remaining: {0}";
        public string enterPromptMessage = "Press [F] to enter Ball Drop";
        public string noBallsMessage = "⚠️ Craft riceballs first!\nPress [E] to craft (need 5 rice)";
        
        private bool playerInRange = false;
        private DayNightCycleManager cycleManager => DayNightCycleManager.Instance;
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                playerInRange = true;
                Debug.Log("[BallDropEntry] Player near ball drop entrance");
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
                Debug.LogWarning("[BallDropEntry] No DayNightCycleManager found, allowing entry");
                LoadBallDropScene();
                return;
            }
            
            if (!cycleManager.CanEnterBallDrop())
            {
                // It's day time - deny entry
                float timeUntilNight = cycleManager.GetTimeRemaining();
                Debug.LogWarning($"[BallDropEntry] ⚠️ Ball drop only at night! {timeUntilNight:F0}s until night");
                return;
            }
            
            // Check if player has riceballs
            if (playerData != null)
            {
                int totalBalls = playerData.Inventory.GetTotalBalls();
                if (totalBalls == 0)
                {
                    Debug.LogWarning("[BallDropEntry] ⚠️ No riceballs! Craft some first (need 5 rice)");
                    return;
                }
            }
            
            // It's night time and have balls - allow entry
            Debug.Log("[BallDropEntry] Entering ball drop puzzle (Night time)");
            
            // Notify tutorial manager that we're visiting ball drop (completes "Go Outside" quest)
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.NotifyBallDropVisit();
            }
            
            LoadBallDropScene();
        }
        
        private void LoadBallDropScene()
        {
            SceneManager.LoadScene(dropPuzzleSceneName);
        }
        
        private void OnGUI()
        {
            if (!playerInRange) return;
            
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = Color.white;
            
            if (cycleManager == null) return;
            
            // Check if we're in tutorial and before quest 3 (Wait for Night)
            // tutorialStep: 1=CollectRice, 2=CraftRiceballs, 3=WaitNight, 4=GoOutside
            bool inEarlyTutorial = TutorialManager.Instance != null && 
                                   TutorialManager.Instance.tutorialActive && 
                                   TutorialManager.Instance.tutorialStep <= 2;
            
            if (!cycleManager.CanEnterBallDrop())
            {
                // Day time - show warning ONLY if past quest 2 (or not in tutorial)
                if (!inEarlyTutorial)
                {
                    style.normal.textColor = Color.yellow;
                    float timeUntilNight = cycleManager.GetTimeRemaining();
                    string message = string.Format(nightOnlyMessage, cycleManager.GetFormattedTimeRemaining());
                    GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height - 100, 500, 60), 
                        message, style);
                }
                // else: In early tutorial (quests 1-2), don't show anything about nighttime
            }
            else
            {
                // Night time - check if have riceballs
                if (playerData != null && playerData.Inventory.GetTotalBalls() == 0)
                {
                    // No riceballs - show craft warning (only if past quest 1)
                    if (!inEarlyTutorial || TutorialManager.Instance.tutorialStep >= 2)
                    {
                        style.normal.textColor = Color.red;
                        GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height - 100, 500, 60), 
                            noBallsMessage, style);
                    }
                }
                else
                {
                    // Have riceballs - show enter prompt (only if quest 3+)
                    if (!inEarlyTutorial || TutorialManager.Instance.tutorialStep >= 3)
                    {
                        style.normal.textColor = Color.green;
                        int ballCount = playerData != null ? playerData.Inventory.GetTotalBalls() : 0;
                        string message = $"{enterPromptMessage}\n({ballCount} riceballs ready)";
                        GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height - 100, 400, 60), 
                            message, style);
                    }
                }
            }
        }
    }
}
