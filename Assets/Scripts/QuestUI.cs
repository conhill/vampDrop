using UnityEngine;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Displays current quest on screen
    /// </summary>
    public class QuestUI : MonoBehaviour
    {
        [Header("UI References")]
        public TextMeshProUGUI questTitleText;
        public TextMeshProUGUI questDescriptionText;
        public TextMeshProUGUI questProgressText;
        public GameObject questPanel;
        
        private QuestManager questManager;
        
        private void Start()
        {
            questManager = QuestManager.Instance;
            
            if (questManager != null)
            {
                questManager.OnQuestStarted += OnQuestStarted;
                questManager.OnQuestCompleted += OnQuestCompleted;
                questManager.OnQuestProgress += OnQuestProgress;
                
                RefreshQuestDisplay();
            }
            else
            {
                // Hide initially if no quest manager
                if (questPanel != null)
                {
                    questPanel.SetActive(false);
                }
            }
            
            Debug.Log("[QuestUI] Initialized");
        }
        
        private void OnEnable()
        {
            // Refresh quest UI when returning to this scene (e.g., from ball drop)
            // Re-get instance in case it wasn't set yet
            if (questManager == null)
            {
                questManager = QuestManager.Instance;
            }
            
            if (questManager != null)
            {
                RefreshQuestDisplay();
                Debug.Log("[QuestUI] OnEnable - Refreshed quest display");
            }
            else
            {
                Debug.LogWarning("[QuestUI] OnEnable - No QuestManager found!");
            }
        }
        
        private void RefreshQuestDisplay()
        {
            // Re-check manager instance
            if (questManager == null)
            {
                questManager = QuestManager.Instance;
            }
            
            if (questManager == null)
            {
                Debug.LogError("[QuestUI] RefreshQuestDisplay - QuestManager.Instance is NULL!");
                return;
            }
            
            // If there's an active quest with valid data, show it
            if (questManager.currentQuest != null && 
                !questManager.currentQuest.isComplete &&
                !string.IsNullOrEmpty(questManager.currentQuest.title))
            {
                Debug.Log($"[QuestUI] Refreshing display for active quest: '{questManager.currentQuest.title}' | Progress: {questManager.currentQuest.currentValue}/{questManager.currentQuest.targetValue} | Complete: {questManager.currentQuest.isComplete}");
                OnQuestStarted(questManager.currentQuest);
            }
            else
            {
                if (questManager.currentQuest == null)
                {
                    Debug.Log("[QuestUI] No active quest to display (currentQuest is null)");
                }
                else if (questManager.currentQuest.isComplete)
                {
                    Debug.Log($"[QuestUI] Current quest '{questManager.currentQuest.title}' is already complete");
                }
                else if (string.IsNullOrEmpty(questManager.currentQuest.title))
                {
                    Debug.LogWarning($"[QuestUI] Current quest has empty title! ID: {questManager.currentQuest.questId}");
                }
            }
        }
        
        private void OnDestroy()
        {
            if (questManager != null)
            {
                questManager.OnQuestStarted -= OnQuestStarted;
                questManager.OnQuestCompleted -= OnQuestCompleted;
                questManager.OnQuestProgress -= OnQuestProgress;
            }
        }
        
        private void OnQuestStarted(QuestManager.Quest quest)
        {
            Debug.Log($"[QuestUI] OnQuestStarted called - Title:'{quest.title}' Desc:'{quest.description}'");
            
            // Cancel any pending hide operations (from previous quest completion)
            CancelInvoke(nameof(HideQuestPanel));
            
            if (questPanel != null)
            {
                questPanel.SetActive(true);
                Debug.Log("[QuestUI] Quest panel activated");
            }
            else
            {
                Debug.Log("[QuestUI] questPanel is NULL - using OnGUI fallback");
            }
            
            UpdateQuestDisplay(quest);
        }
        
        private void OnQuestProgress(QuestManager.Quest quest)
        {
            UpdateQuestDisplay(quest);
        }
        
        private void OnQuestCompleted(QuestManager.Quest quest)
        {
            // Show completion briefly with checkmark
            if (questTitleText != null)
            {
                questTitleText.text = "✅ " + quest.title;
            }
            
            // Don't auto-hide - wait for next quest to start
            // The tutorial manager will start the next quest automatically
            Debug.Log($"[QuestUI] Quest completed: {quest.title} - Waiting for next quest...");
        }
        
        private void UpdateQuestDisplay(QuestManager.Quest quest)
        {
            Debug.Log($"[QuestUI] UpdateQuestDisplay - Title:'{quest.title}' Desc:'{quest.description}' Type:{quest.type} Target:{quest.targetValue}");
            
            if (questTitleText != null)
            {
                questTitleText.text = quest.title;
            }
            
            if (questDescriptionText != null)
            {
                questDescriptionText.text = quest.description;
            }
            
            if (questProgressText != null && questManager != null)
            {
                questProgressText.text = questManager.GetCurrentQuestProgress();
            }
        }
        
        private void HideQuestPanel()
        {
            if (questPanel != null)
            {
                questPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// Fallback OnGUI display
        /// </summary>
        private void OnGUI()
        {
            // Only show if no proper UI set up
            if (questPanel != null) return;
            if (questManager == null || questManager.currentQuest == null) return;
            
            var quest = questManager.currentQuest;
            
            // Debug: Check quest data
            if (string.IsNullOrEmpty(quest.title) || string.IsNullOrEmpty(quest.description))
            {
                Debug.LogWarning($"[QuestUI] OnGUI - Quest has empty fields! Title:'{quest.title}' Desc:'{quest.description}' ID:'{quest.questId}' Type:{quest.type} Target:{quest.targetValue}");
            }
            
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 16;
            boxStyle.alignment = TextAnchor.UpperLeft;
            boxStyle.normal.textColor = Color.white;
            boxStyle.padding = new RectOffset(10, 10, 10, 10);
            
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 20;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.yellow;
            
            GUIStyle descStyle = new GUIStyle(GUI.skin.label);
            descStyle.fontSize = 16;
            descStyle.normal.textColor = Color.white;
            
            GUIStyle progressStyle = new GUIStyle(GUI.skin.label);
            progressStyle.fontSize = 18;
            progressStyle.fontStyle = FontStyle.Bold;
            progressStyle.normal.textColor = Color.green;
            
            // Quest box in top-right corner
            GUI.Box(new Rect(Screen.width - 320, 10, 300, 120), "", boxStyle);
            
            // Title
            GUI.Label(new Rect(Screen.width - 310, 20, 280, 30), quest.title, titleStyle);
            
            // Description
            GUI.Label(new Rect(Screen.width - 310, 50, 280, 40), quest.description, descStyle);
            
            // Progress
            string progress = questManager.GetCurrentQuestProgress();
            GUI.Label(new Rect(Screen.width - 310, 95, 280, 30), progress, progressStyle);
        }
    }
}
