using UnityEngine;
using System.Collections;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Manages tutorial progression and quest flow
    /// Controls day/night cycle during tutorial
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }
        
        [Header("Tutorial State")]
        public bool tutorialActive = true;
        public int tutorialStep = 0;
        
        [Header("Settings")]
        public float nightTransitionDelay = 10f; // Wait 10s before forcing night
        
        [Header("Tutorial Puzzles")]
        [Tooltip("Tutorial puzzle JSON file (puzzle_level1.json)")]
        public TextAsset TutorialPuzzle;
        
        [Header("Shop Configuration")]
        [Tooltip("BuyZone GameObject already in scene (will be enabled after tutorial)")]
        public GameObject BuyZone;
        [Tooltip("Flink character GameObject already in scene (will be enabled after tutorial)")]
        public GameObject FlinkCharacter;
        
        private QuestManager questManager;
        private DayNightCycleManager cycleManager;
        private PlayerDataManager playerData;
        
        // Tutorial quest IDs
        private const string QUEST_COLLECT_RICE = "tutorial_rice";
        private const string QUEST_CRAFT_RICEBALLS = "tutorial_craft";
        private const string QUEST_WAIT_NIGHT = "tutorial_night";
        private const string QUEST_GO_OUTSIDE = "tutorial_outside";
        private const string QUEST_DROP_RICEBALLS = "tutorial_drop";
        private const string QUEST_COLLECT_MONEY = "tutorial_money";
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Disable shop elements during tutorial
            if (BuyZone != null)
            {
                BuyZone.SetActive(false);
            }
            if (FlinkCharacter != null)
            {
                FlinkCharacter.SetActive(false);
            }
        }
        
        private void Start()
        {
            questManager = QuestManager.Instance;
            cycleManager = DayNightCycleManager.Instance;
            playerData = PlayerDataManager.Instance;
            
            if (questManager != null)
            {
                questManager.OnQuestCompleted += OnQuestCompleted;
            }
            
            // Start tutorial
            if (tutorialActive)
            {
                StartCoroutine(StartTutorial());
            }
            
            Debug.Log("[TutorialManager] Initialized");
        }
        
        private void OnDestroy()
        {
            if (questManager != null)
            {
                questManager.OnQuestCompleted -= OnQuestCompleted;
            }
        }
        
        private IEnumerator StartTutorial()
        {
            yield return new WaitForSeconds(1f); // Wait for all managers to init
            
            Debug.Log("[TutorialManager] Starting tutorial setup...");
            
            // Ensure quest manager is ready and cleared
            if (questManager == null)
            {
                Debug.LogError("[TutorialManager] QuestManager is NULL!");
                yield break;
            }
            
            // Reset quest manager to ensure clean state
            questManager.ResetAllQuests();
            Debug.Log("[TutorialManager] Quest manager reset for tutorial");
            
            // Pause day/night cycle during tutorial
            if (cycleManager != null)
            {
                cycleManager.enabled = false;
                cycleManager.currentTime = DayNightCycleManager.TimeOfDay.Day;
                Debug.Log("[TutorialManager] Day/Night cycle PAUSED for tutorial");
            }
            
            // Setup tutorial quests
            SetupTutorialQuests();
            
            // Start first quest
            tutorialStep = 1;
            Debug.Log($"[TutorialManager] About to start first quest: {QUEST_COLLECT_RICE}");
            questManager.StartQuest(QUEST_COLLECT_RICE);
        }
        
        private void SetupTutorialQuests()
        {
            // Quest 1: Collect 50 rice
            questManager.AddQuest(
                QUEST_COLLECT_RICE,
                "Collect Rice",
                "Collect 50 rice grains from the field",
                QuestManager.QuestType.CollectRice,
                50
            );
            
            // Quest 2: Craft riceballs
            questManager.AddQuest(
                QUEST_CRAFT_RICEBALLS,
                "Craft Rice Balls",
                "Craft 5 riceballs (Press E to craft)",
                QuestManager.QuestType.CraftRiceBalls,
                5
            );
            
            // Quest 3: Wait for night
            questManager.AddQuest(
                QUEST_WAIT_NIGHT,
                "Wait for Nighttime",
                "Wait for nighttime to arrive",
                QuestManager.QuestType.WaitForNight,
                1
            );
            
            // Quest 4: Go outside (visit ball drop)
            questManager.AddQuest(
                QUEST_GO_OUTSIDE,
                "Check Outside",
                "Go outside and check out what's there!",
                QuestManager.QuestType.VisitBallDrop,
                1
            );
            
            // Quest 5: Drop riceballs
            questManager.AddQuest(
                QUEST_DROP_RICEBALLS,
                "Drop the Riceballs",
                "Complete a ball drop puzzle",
                QuestManager.QuestType.DropRiceBalls,
                1
            );
            
            // Quest 6: Collect currency
            questManager.AddQuest(
                QUEST_COLLECT_MONEY,
                "Earn Money",
                "Collect $0.50 from ball drops",
                QuestManager.QuestType.CollectCurrency,
                50 // 50 cents
            );
            
            Debug.Log("[TutorialManager] Tutorial quests created");
        }
        
        private void OnQuestCompleted(QuestManager.Quest quest)
        {
            Debug.Log($"[TutorialManager] Quest completed: {quest.questId}");
            
            // Handle quest completion and start next
            if (quest.questId == QUEST_COLLECT_RICE)
            {
                // Start craft riceballs quest
                tutorialStep = 2;
                questManager.StartQuest(QUEST_CRAFT_RICEBALLS);
            }
            else if (quest.questId == QUEST_CRAFT_RICEBALLS)
            {
                // Start wait for night quest
                tutorialStep = 3;
                StartCoroutine(StartNightWaitQuest());
            }
            else if (quest.questId == QUEST_WAIT_NIGHT)
            {
                // Start go outside quest
                tutorialStep = 4;
                questManager.StartQuest(QUEST_GO_OUTSIDE);
            }
            else if (quest.questId == QUEST_GO_OUTSIDE)
            {
                // Start drop riceballs quest
                tutorialStep = 5;
                questManager.StartQuest(QUEST_DROP_RICEBALLS);
            }
            else if (quest.questId == QUEST_DROP_RICEBALLS)
            {
                // Start collect money quest
                tutorialStep = 6;
                Debug.Log($"[TutorialManager] ✅ Quest 5 complete! Starting Quest 6: Collect Money");
                questManager.StartQuest(QUEST_COLLECT_MONEY);
                Debug.Log($"[TutorialManager] Quest 6 started. Current quest: {questManager.currentQuest?.title}");
            }
            else if (quest.questId == QUEST_COLLECT_MONEY)
            {
                // Tutorial complete! Spawn shop
                tutorialStep = 7;
                CompleteTutorial();
            }
        }
        
        private IEnumerator StartNightWaitQuest()
        {
            questManager.StartQuest(QUEST_WAIT_NIGHT);
            
            // Wait 10 seconds, then force night
            Debug.Log("[TutorialManager] Waiting 10 seconds before night...");
            yield return new WaitForSeconds(nightTransitionDelay);
            
            // Force night time
            if (cycleManager != null)
            {
                cycleManager.ForcePhase(DayNightCycleManager.TimeOfDay.Night);
                Debug.Log("[TutorialManager] 🌙 Forced NIGHT for tutorial");
            }
            
            // Complete the quest
            questManager.CompleteCurrentQuest();
        }
        
        /// <summary>
        /// Called when player visits ball drop scene
        /// </summary>
        public void NotifyBallDropVisit()
        {
            if (tutorialStep == 4 && questManager.currentQuest?.questId == QUEST_GO_OUTSIDE)
            {
                questManager.UpdateQuestProgress(QuestManager.QuestType.VisitBallDrop, 1);
            }
        }
        
        /// <summary>
        /// Setup tutorial puzzle in ball drop scene
        /// </summary>
        public void SetupTutorialPuzzle()
        {
            if (!tutorialActive) return;
            
            // Find GridPuzzleLoader in scene
            var gridLoader = Object.FindObjectOfType<GridPuzzleLoader>();
            if (gridLoader != null && TutorialPuzzle != null)
            {
                Debug.Log($"[TutorialManager] Setting tutorial puzzle: {TutorialPuzzle.name}");
                gridLoader.PuzzleJsonFile = TutorialPuzzle;
                gridLoader.LoadAndBuildPuzzle();
            }
            else if (gridLoader == null)
            {
                Debug.LogWarning("[TutorialManager] No GridPuzzleLoader found in scene!");
            }
            else if (TutorialPuzzle == null)
            {
                Debug.LogWarning("[TutorialManager] No TutorialPuzzle assigned! Assign puzzle_level1.json in Inspector");
            }
        }
        
        /// <summary>
        /// Track rice collection
        /// </summary>
        public void NotifyRiceCollected(int amount = 1)
        {
            if (tutorialStep == 1 && questManager.currentQuest?.questId == QUEST_COLLECT_RICE)
            {
                questManager.UpdateQuestProgress(QuestManager.QuestType.CollectRice, amount);
            }
        }
        
        /// <summary>
        /// Track riceballs crafted
        /// </summary>
        public void NotifyRiceBallsCrafted(int amount = 1)
        {
            if (tutorialStep == 2 && questManager.currentQuest?.questId == QUEST_CRAFT_RICEBALLS)
            {
                questManager.UpdateQuestProgress(QuestManager.QuestType.CraftRiceBalls, amount);
            }
        }
        
        /// <summary>
        /// Track riceballs dropped (puzzle completed)
        /// </summary>
        public void NotifyRiceBallsDropped()
        {
            Debug.Log($"[TutorialManager] NotifyRiceBallsDropped called. tutorialStep:{tutorialStep}, currentQuest:{questManager.currentQuest?.questId}");
            
            if (tutorialStep == 5 && questManager.currentQuest?.questId == QUEST_DROP_RICEBALLS)
            {
                Debug.Log("[TutorialManager] Updating Quest 5 progress (Drop Rice Balls)");
                questManager.UpdateQuestProgress(QuestManager.QuestType.DropRiceBalls, 1);
            }
            else
            {
                Debug.LogWarning($"[TutorialManager] NotifyRiceBallsDropped called but conditions not met. Step:{tutorialStep}, Expected:5, Quest:{questManager.currentQuest?.questId}, Expected:{QUEST_DROP_RICEBALLS}");
            }
        }
        
        /// <summary>
        /// Track currency collection
        /// </summary>
        public void NotifyCurrencyEarned(int amount)
        {
            if (tutorialStep == 6 && questManager.currentQuest?.questId == QUEST_COLLECT_MONEY)
            {
                questManager.UpdateQuestProgress(QuestManager.QuestType.CollectCurrency, amount);
            }
        }
        
        private void CompleteTutorial()
        {
            tutorialActive = false;
            
            Debug.Log("[TutorialManager] ✅ TUTORIAL COMPLETE!");
            
            // Enable day/night cycle
            if (cycleManager != null)
            {
                cycleManager.enabled = true;
                Debug.Log("[TutorialManager] Day/Night cycle ENABLED");
            }
            
            // Enable the shop/BuyZone
            EnableBuyZone();
            
            Debug.Log("[TutorialManager] You're now ready to explore!");
        }
        
        /// <summary>
        /// Enable BuyZone and Flink character
        /// </summary>
        private void EnableBuyZone()
        {
            if (BuyZone != null)
            {
                BuyZone.SetActive(true);
                Debug.Log("[TutorialManager] 🛒 BuyZone ENABLED");
            }
            else
            {
                Debug.LogWarning("[TutorialManager] BuyZone not assigned! Drag BuyZone GameObject to TutorialManager in Inspector");
            }
            
            if (FlinkCharacter != null)
            {
                FlinkCharacter.SetActive(true);
                Debug.Log("[TutorialManager] 👤 Flink character ENABLED");
            }
        }
        
        /// <summary>
        /// Skip tutorial (for testing)
        /// </summary>
        public void SkipTutorial()
        {
            tutorialActive = false;
            
            if (cycleManager != null)
            {
                cycleManager.enabled = true;
            }
            
            Debug.Log("[TutorialManager] Tutorial SKIPPED");
        }
    }
}
