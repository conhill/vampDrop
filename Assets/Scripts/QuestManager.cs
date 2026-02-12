using UnityEngine;
using System.Collections.Generic;
using System;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Manages quest progression and tracking
    /// </summary>
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }
        
        [System.Serializable]
        public class Quest
        {
            public string questId;
            public string title;
            public string description;
            public QuestType type;
            public int targetValue; // For collection quests
            public int currentValue;
            public bool isComplete;
            public bool isActive;
        }
        
        public enum QuestType
        {
            CollectRice,
            CraftRiceBalls,
            WaitForNight,
            VisitBallDrop,
            DropRiceBalls,
            CollectCurrency,
            Custom
        }
        
        public List<Quest> quests = new List<Quest>();
        public Quest currentQuest;
        
        // Events
        public event Action<Quest> OnQuestStarted;
        public event Action<Quest> OnQuestCompleted;
        public event Action<Quest> OnQuestProgress;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Clear any existing state on fresh load
            quests.Clear();
            currentQuest = null;
            
            Debug.Log("[QuestManager] Initialized - State cleared");
        }
        
        /// <summary>
        /// Reset all quest state (for tutorial/new game)
        /// </summary>
        public void ResetAllQuests()
        {
            quests.Clear();
            currentQuest = null;
            Debug.Log("[QuestManager] All quests reset");
        }
        
        /// <summary>
        /// Add a quest to the quest list
        /// </summary>
        public void AddQuest(string questId, string title, string description, QuestType type, int targetValue = 0)
        {
            Quest quest = new Quest
            {
                questId = questId,
                title = title,
                description = description,
                type = type,
                targetValue = targetValue,
                currentValue = 0,
                isComplete = false,
                isActive = false
            };
            
            quests.Add(quest);
            Debug.Log($"[QuestManager] Added quest: {questId} - Title:'{title}' Desc:'{description}' Target:{targetValue}");
        }
        
        /// <summary>
        /// Start a quest by ID
        /// </summary>
        public void StartQuest(string questId)
        {
            Quest quest = quests.Find(q => q.questId == questId);
            if (quest == null)
            {
                Debug.LogError($"[QuestManager] Quest not found: {questId}");
                return;
            }
            
            // If trying to start the same quest that's already active, ignore
            if (currentQuest != null && currentQuest.questId == questId && !currentQuest.isComplete)
            {
                Debug.Log($"[QuestManager] Quest already active: {questId}");
                return;
            }
            
            // If different quest is active and not complete, warn
            if (currentQuest != null && !currentQuest.isComplete && currentQuest.questId != questId)
            {
                Debug.LogWarning($"[QuestManager] Another quest already active: '{currentQuest.questId}' (trying to start '{questId}'). Complete it first.");
                return;
            }
            
            quest.isActive = true;
            currentQuest = quest;
            
            // Initialize quest progress from lifetime stats (don't reset to 0!)
            InitializeQuestProgressFromLifetimeStats(quest);
            
            Debug.Log($"[QuestManager] ✨ Quest Started: '{quest.title}' | Desc: '{quest.description}' | Progress: {quest.currentValue}/{quest.targetValue}");
            OnQuestStarted?.Invoke(quest);
        }
        
        /// <summary>
        /// Update quest progress
        /// </summary>
        public void UpdateQuestProgress(QuestType type, int amount = 1)
        {
            if (currentQuest == null || currentQuest.isComplete) return;
            if (currentQuest.type != type) return;
            
            currentQuest.currentValue += amount;
            
            Debug.Log($"[QuestManager] Quest Progress: {currentQuest.title} - {currentQuest.currentValue}/{currentQuest.targetValue}");
            OnQuestProgress?.Invoke(currentQuest);
            
            // Check if complete
            if (currentQuest.currentValue >= currentQuest.targetValue)
            {
                CompleteCurrentQuest();
            }
        }
        
        /// <summary>
        /// Initialize quest progress from lifetime stats instead of starting at 0
        /// This allows quests to reflect cumulative player progress
        /// </summary>
        private void InitializeQuestProgressFromLifetimeStats(Quest quest)
        {
            if (PlayerDataManager.Instance == null) return;
            
            var playerData = PlayerDataManager.Instance;
            
            switch (quest.type)
            {
                case QuestType.CraftRiceBalls:
                    quest.currentValue = playerData.TotalRiceBallsCrafted;
                    Debug.Log($"[QuestManager] Initialized CraftRiceBalls quest from lifetime stats: {quest.currentValue}/{quest.targetValue}");
                    break;
                    
                case QuestType.CollectCurrency:
                    quest.currentValue = playerData.TotalCurrencyEarned;
                    Debug.Log($"[QuestManager] Initialized CollectCurrency quest from lifetime stats: {quest.currentValue}/{quest.targetValue}");
                    break;
                    
                // Other quest types start from 0 (time-based or one-time actions)
                default:
                    quest.currentValue = 0;
                    break;
            }
            
            // Auto-complete if already at target
            if (quest.currentValue >= quest.targetValue)
            {
                Debug.Log($"[QuestManager] Quest '{quest.title}' already complete from prior progress!");
            }
        }
        
        /// <summary>
        /// Force complete the current quest
        /// </summary>
        public void CompleteCurrentQuest()
        {
            if (currentQuest == null || currentQuest.isComplete) return;
            
            currentQuest.isComplete = true;
            currentQuest.isActive = false;
            
            Debug.Log($"[QuestManager] ✅ Quest Complete: {currentQuest.title}");
            OnQuestCompleted?.Invoke(currentQuest);
        }
        
        /// <summary>
        /// Get current quest progress as string
        /// </summary>
        public string GetCurrentQuestProgress()
        {
            if (currentQuest == null) return "No active quest";
            
            if (currentQuest.targetValue > 0)
            {
                return $"{currentQuest.currentValue}/{currentQuest.targetValue}";
            }
            
            return "In Progress";
        }
        
        /// <summary>
        /// Check if a specific quest is complete
        /// </summary>
        public bool IsQuestComplete(string questId)
        {
            Quest quest = quests.Find(q => q.questId == questId);
            return quest != null && quest.isComplete;
        }
        
        /// <summary>
        /// Get quest by ID
        /// </summary>
        public Quest GetQuest(string questId)
        {
            return quests.Find(q => q.questId == questId);
        }
    }
}
