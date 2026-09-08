using UnityEngine;
using System;
using System.Collections.Generic;
using DataStructures.RandomSelector;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// MASTER PLAYER DATA MANAGER - Roguelite Progression
    /// Persists across scenes, handles all upgrades for both game modes
    /// </summary>
    public class PlayerDataManager : MonoBehaviour
    {
        public static PlayerDataManager Instance { get; private set; }
        
        [Header("Debug")]
        public bool debugFreeUpgrades = false; // Skip currency cost on all purchases

        [Header("Currency")]
        public int TotalCurrency = 0; // Earned across all runs
        public int CurrentRunCurrency = 0; // This session only
        
        [Header("Rice & RiceBall Inventory")]
        public int RiceGrains = 0; // Raw rice collected in FPS mode
        public RiceBallInventory Inventory = new RiceBallInventory();
        
        [Header("Game Mode Upgrades")]
        public DropPuzzleUpgrades DropPuzzle = new DropPuzzleUpgrades();
        public FPSCollectorUpgrades FPSCollector = new FPSCollectorUpgrades();
        public CraftingUpgrades Crafting = new CraftingUpgrades();
        public HelperSystemUpgrades Helpers = new HelperSystemUpgrades();
        
        [Header("Meta Progression")]
        public int TotalRunsCompleted = 0;
        public int HighestLevelReached = 1;
        public bool TutorialCompleted = false;

        [Header("Level Progression (Drop Puzzle maps)")]
        [Tooltip("Which map index the DropPuzzle scene loads. Set by the unlock flow / home select.")]
        public int SelectedLevel = 0;
        [Tooltip("Highest map index unlocked, inclusive. 0 = only the first map is playable.")]
        public int HighestLevelUnlocked = 0;
        [Tooltip("Skrilla accumulated toward each map's goal, indexed by level. Persists across " +
                 "drops on the same map; a new map has its own fresh entry.")]
        public List<int> LevelSkrillaProgress = new List<int>();

        [Tooltip("Set true once Snerd gives the player the rice crafting device. Gates E-key crafting.")]
        public bool HasCraftingDevice = false;
        
        [Header("Lifetime Stats (for Quest Progress)")]
        public int TotalRiceBallsCrafted = 0; // Cumulative riceballs crafted across all time
        public int TotalCurrencyEarned = 0; // Cumulative currency earned (for quest tracking)
        
        private void Awake()
        {
            // Singleton pattern - persist across scenes
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LoadPlayerData();
                // Debug.Log("[PlayerData] Manager initialized");
            }
            else
            {
                Destroy(gameObject);
            }
        }
        
        #region Currency Management

        /// <summary>
        /// Fired whenever currency changes. Args: (newTotal, delta, source).
        /// delta is positive for earnings, negative for spends. UIs subscribe to
        /// this to animate (e.g. DropPuzzleHUD rolls the money counter up on earn)
        /// instead of polling TotalCurrency every frame.
        /// </summary>
        public static event Action<int, int, string> OnCurrencyChanged;

        /// <summary>
        /// Award currency - both to current run and total pool
        /// </summary>
        public void AddCurrency(int amount, string source = "")
        {
            TotalCurrency += amount;
            CurrentRunCurrency += amount;
            TotalCurrencyEarned += amount; // Track lifetime stat for quest progress

            // Debug.Log($"[PlayerData] +{amount} currency from {source} | Run:{CurrentRunCurrency} Total:{TotalCurrency} | Lifetime earned: {TotalCurrencyEarned}");

            // Notify tutorial manager
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.NotifyCurrencyEarned(amount);
            }

            OnCurrencyChanged?.Invoke(TotalCurrency, amount, source);
        }

        #endregion

        #region Level Progression

        /// <summary>A map is playable if it's at or below the highest unlocked index.</summary>
        public bool IsLevelUnlocked(int level) => level >= 0 && level <= HighestLevelUnlocked;

        /// <summary>Skrilla accumulated toward a map's goal (0 if never played).</summary>
        public int GetLevelProgress(int level)
            => (level >= 0 && level < LevelSkrillaProgress.Count) ? LevelSkrillaProgress[level] : 0;

        /// <summary>Add skrilla toward a map's goal. Accumulates across drops on that map.</summary>
        public void AddLevelSkrilla(int level, int amount)
        {
            if (level < 0 || amount <= 0) return;
            while (LevelSkrillaProgress.Count <= level) LevelSkrillaProgress.Add(0);
            LevelSkrillaProgress[level] += amount;
            SavePlayerData();
        }

        /// <summary>Unlock a map. Returns true if this newly raised the unlock ceiling.</summary>
        public bool UnlockLevel(int level)
        {
            if (level > HighestLevelUnlocked)
            {
                HighestLevelUnlocked = level;
                SavePlayerData();
                return true;
            }
            return false;
        }

        #endregion

        #region Rice & RiceBall Management
        
        /// <summary>
        /// Add rice grains collected in FPS mode
        /// </summary>
        public void AddRice(int amount)
        {
            RiceGrains += amount;
            // Debug.Log($"[PlayerData] +{amount} rice | Total: {RiceGrains}");
        }
        
        /// <summary>
        /// Convert rice to riceballs (5 rice = 1 riceball)
        /// Returns number of riceballs crafted
        /// </summary>
        public int ConvertRiceToRiceBalls()
        {
            int riceBallsToMake = RiceGrains / 5;
            if (riceBallsToMake == 0)
            {
                // Debug.LogWarning("[PlayerData] Not enough rice! Need 5 rice per riceball.");
                return 0;
            }
            
            int riceUsed = riceBallsToMake * 5;
            RiceGrains -= riceUsed;
            
            // Roll quality for each riceball
            int fineCount = 0, goodCount = 0, greatCount = 0, excellentCount = 0;

            var qualitySelector = BuildQualitySelector();
            for (int i = 0; i < riceBallsToMake; i++)
            {
                RiceBallQuality quality = qualitySelector.SelectRandomItem(UnityEngine.Random.value);
                switch (quality)
                {
                    case RiceBallQuality.Fine:
                        fineCount++;
                        Inventory.FineBalls++;
                        break;
                    case RiceBallQuality.Good:
                        goodCount++;
                        Inventory.GoodBalls++;
                        break;
                    case RiceBallQuality.Great:
                        greatCount++;
                        Inventory.GreatBalls++;
                        break;
                    case RiceBallQuality.Excellent:
                        excellentCount++;
                        Inventory.ExcellentBalls++;
                        break;
                }
            }
            
            // Track lifetime stat for quest progress
            TotalRiceBallsCrafted += riceBallsToMake;
            
            // Debug.Log($"[PlayerData] Crafted {riceBallsToMake} riceballs! Fine:{fineCount} Good:{goodCount} Great:{greatCount} Excellent:{excellentCount}");
            // Debug.Log($"[PlayerData] Inventory: {Inventory.GetTotalBalls()} balls | Rice remaining: {RiceGrains} | Lifetime crafted: {TotalRiceBallsCrafted}");
            
            return riceBallsToMake;
        }
        
        /// <summary>
        /// Builds a weighted selector for riceball quality from current crafting upgrades.
        /// Built once per crafting batch (not per ball) since the chances don't change mid-batch.
        /// </summary>
        private DynamicRandomSelector<RiceBallQuality> BuildQualitySelector()
        {
            var selector = new DynamicRandomSelector<RiceBallQuality>();

            // Excellent/Great/Good chances come from upgrades; Fine takes whatever's left over
            // (floored above 0 so Build() always has at least one item to select from).
            float fineChance = Mathf.Max(0.0001f, 1f - Crafting.excellentChance - Crafting.greatChance - Crafting.goodChance);

            selector.Add(RiceBallQuality.Excellent, Crafting.excellentChance);
            selector.Add(RiceBallQuality.Great, Crafting.greatChance);
            selector.Add(RiceBallQuality.Good, Crafting.goodChance);
            selector.Add(RiceBallQuality.Fine, fineChance);
            selector.Build();

            return selector;
        }
        
        /// <summary>
        /// Use a riceball from inventory (called when launching in drop puzzle)
        /// Returns the quality of the ball used, or null if no balls available
        /// </summary>
        public RiceBallQuality? UseRiceBall()
        {
            // Use best quality first
            if (Inventory.ExcellentBalls > 0)
            {
                Inventory.ExcellentBalls--;
                return RiceBallQuality.Excellent;
            }
            if (Inventory.GreatBalls > 0)
            {
                Inventory.GreatBalls--;
                return RiceBallQuality.Great;
            }
            if (Inventory.GoodBalls > 0)
            {
                Inventory.GoodBalls--;
                return RiceBallQuality.Good;
            }
            if (Inventory.FineBalls > 0)
            {
                Inventory.FineBalls--;
                return RiceBallQuality.Fine;
            }
            
            return null; // No balls available
        }
        
        /// <summary>
        /// Spend currency on upgrades
        /// </summary>
        public bool SpendCurrency(int amount, string purchaseDescription)
        {
            if (debugFreeUpgrades) return true;

            if (TotalCurrency < amount)
            {
                // Debug.LogWarning($"[PlayerData] Not enough currency! Need {amount}, have {TotalCurrency}");
                return false;
            }

            TotalCurrency -= amount;
            // Debug.Log($"[PlayerData] Spent {amount} on: {purchaseDescription} | Remaining: {TotalCurrency}");
            OnCurrencyChanged?.Invoke(TotalCurrency, -amount, purchaseDescription);
            SavePlayerData();
            return true;
        }
        
        /// <summary>
        /// End current run - reset run currency
        /// </summary>
        public void EndRun()
        {
            TotalRunsCompleted++;
            CurrentRunCurrency = 0;
            SavePlayerData();
            // Debug.Log($"[PlayerData] Run #{TotalRunsCompleted} complete!");
        }
        
        #endregion
        
        #region Save/Load
        
        public void SavePlayerData()
        {
            // TODO: Implement JSON save to PlayerPrefs or file
            // For now, data persists in memory only (resets on app close)
            // Debug.Log("[PlayerData] Saved (in-memory only for now)");
        }
        
        public void LoadPlayerData()
        {
            // TODO: Load from PlayerPrefs/file
            // Debug.Log("[PlayerData] Loaded player data");
        }
        
        public void ResetAllProgress()
        {
            TotalCurrency = 0;
            CurrentRunCurrency = 0;
            RiceGrains = 0;
            Inventory = new RiceBallInventory();
            DropPuzzle = new DropPuzzleUpgrades();
            FPSCollector = new FPSCollectorUpgrades();
            Crafting = new CraftingUpgrades();
            Helpers = new HelperSystemUpgrades();
            TotalRunsCompleted = 0;
            HighestLevelReached = 1;
            TutorialCompleted = false;
            HasCraftingDevice = false;
            SelectedLevel = 0;
            HighestLevelUnlocked = 0;
            LevelSkrillaProgress = new List<int>();
            SavePlayerData();
            // Debug.Log("[PlayerData] ⚠️ All progress reset!");
        }
        
        #endregion
    }
    
    /// <summary>
    /// DROP PUZZLE MODE UPGRADES
    /// </summary>
    [System.Serializable]
    public class DropPuzzleUpgrades
    {
        [Header("Gate Spawn Chances (0-1)")]
        public float x2GateChance = 0.0f;  // Start locked
        public float x3GateChance = 0.0f;
        public float x4GateChance = 0.0f;
        public float x5GateChance = 0.0f;  // Ultra rare
        
        [Header("Weather Anomaly Chances (0-1)")]
        // Rolled each drop during the Preparation phase. A buy station will pump
        // these later. Independent rolls; if several hit, the rarest wins (see
        // WeatherAnomalyRoller). All 0 = always Clear Skies.
        public float radiationChance    = 0.0f;
        public float falloutChance      = 0.0f;
        public float thunderstormChance = 0.0f;

        [Header("Special Ball Chances")]
        public float bonusPointBallChance = 0.0f;     // 2x-5x points
        public float multiplierBoostBallChance = 0.0f; // +1 to gate multipliers
        public float luckyBallChance = 0.0f;           // Extra rewards
        
        [Header("Gate Roll Slot Machine")]
        [Tooltip("Which odds tier the 3-reel gate-roll slot uses. 0 = starting odds (a match " +
                 "is rare); each level shifts weight off No-Match and onto real multipliers. " +
                 "Raised by upgrades. See SlotMachine3Reel.Tiers for the actual tables.")]
        public int slotOddsLevel = 0;

        [Header("Guaranteed Features")]
        public int guaranteedHighMultiplierGates = 0; // Force spawn specific gates
        public bool canActivateGatesDuringRun = false; // Mid-run gate control
        
        [Header("Ball Count & Speed")]
        public int startingBalls = 20; // Start with more balls per level
        public float dropSpeedMultiplier = 1.0f; // Faster drops = more balls/sec
        
        /// <summary>
        /// Get total investment in drop puzzle upgrades (for UI display)
        /// </summary>
        public int GetTotalUpgradeLevel()
        {
            int total = 0;
            total += (int)(x2GateChance * 100);
            total += (int)(x3GateChance * 100);
            total += (int)(bonusPointBallChance * 100);
            total += guaranteedHighMultiplierGates * 10;
            return total;
        }
    }
    
    /// <summary>
    /// FPS COLLECTOR MODE UPGRADES
    /// </summary>
    [System.Serializable]
    public class FPSCollectorUpgrades
    {
        [Header("Pickup Abilities")]
        public float pickupRadius = 1.5f;          // Base: 1.5, Max: 5.0
        public int maxSimultaneousPickups = 1;     // Pick up 2, 3, 5+ rices at once
        public bool magneticPullEnabled = false;   // Rice auto-pulls toward player
        public float magneticPullRadius = 0f;      // Radius for magnetic pull
        
        [Header("Movement")]
        public float moveSpeedMultiplier = 1.0f;   // Run faster
        public bool canSprint = false;             // Hold shift to sprint
        public bool canDash = false;               // Quick dodge ability
        
        [Header("Collection Multipliers")]
        public float pointsPerRiceMultiplier = 1.0f; // Earn more per rice
        public bool hasComboSystem = false;          // Consecutive pickups = bonus
        public float comboMultiplier = 1.0f;         // 1.0x -> 2.0x -> 3.0x
        
        [Header("Special Abilities")]
        public bool canSlowTime = false;           // Bullet-time mode
        public bool hasXrayVision = false;         // See rice through walls
        public int extraLives = 0;                 // Respawn on death
        
        /// <summary>
        /// Get total investment in FPS collector upgrades
        /// </summary>
        public int GetTotalUpgradeLevel()
        {
            int total = 0;
            total += (int)((pickupRadius - 1.5f) * 10); // Each 0.1 radius = 1 point
            total += (maxSimultaneousPickups - 1) * 10;
            total += (int)(moveSpeedMultiplier * 10);
            total += extraLives * 20;
            return total;
        }
    }
    
    /// <summary>
    /// CRAFTING UPGRADES - Improve riceball quality chances
    /// </summary>
    [System.Serializable]
    public class CraftingUpgrades
    {
        [Header("Quality Chances (0-1)")]
        public float goodChance = 0.0f;       // Start locked (100% fine)
        public float greatChance = 0.0f;      // Requires good unlock
        public float excellentChance = 0.0f;  // Endgame tier
        
        [Header("Crafting Speed")]
        public float craftingSpeedMultiplier = 1.0f; // Faster animations
        
        [Header("Bonus Features")]
        public bool autoConvertEnabled = false;      // Auto-convert rice at threshold
        public int autoConvertThreshold = 100;       // Auto-convert when >= this rice
        public bool canRerollQuality = false;        // Spend currency to reroll
        
        /// <summary>
        /// Get value multiplier for quality
        /// Fine: 1x, Good: 2x, Great: 4x, Excellent: 8x
        /// </summary>
        public float GetQualityMultiplier(RiceBallQuality quality)
        {
            switch (quality)
            {
                case RiceBallQuality.Fine: return 1.0f;
                case RiceBallQuality.Good: return 2.0f;
                case RiceBallQuality.Great: return 4.0f;
                case RiceBallQuality.Excellent: return 8.0f;
                default: return 1.0f;
            }
        }
    }
    
    /// <summary>
    /// RICEBALL INVENTORY - Tracks crafted balls by quality
    /// </summary>
    [System.Serializable]
    public class RiceBallInventory
    {
        public int FineBalls = 0;
        public int GoodBalls = 0;
        public int GreatBalls = 0;
        public int ExcellentBalls = 0;
        
        public int GetTotalBalls()
        {
            return FineBalls + GoodBalls + GreatBalls + ExcellentBalls;
        }
        
        public int GetTotalValue()
        {
            return (FineBalls * 1) + (GoodBalls * 2) + (GreatBalls * 4) + (ExcellentBalls * 8);
        }
    }
    
    /// <summary>
    /// RICEBALL QUALITY TIERS
    /// </summary>
    public enum RiceBallQuality
    {
        Fine = 0,      // Common (start: 100%)
        Good = 1,      // Uncommon (unlock: ~20%)
        Great = 2,     // Rare (unlock: ~5%)
        Excellent = 3  // Epic (unlock: ~1%)
    }
    
    /// <summary>
    /// HELPER SYSTEM UPGRADES - Goblin/Ghoul helpers for automatic rice collection
    /// </summary>
    [System.Serializable]
    public class HelperSystemUpgrades
    {
        [Header("Helper Purchase & Inventory")]
        public int ownedGoblins = 0;          // Total goblin helpers purchased
        public int ownedGhouls = 0;           // Total ghoul helpers purchased
        
        [Header("Helper Efficiency")]
        public float ricePerSecond = 1.0f;    // Base collection rate per helper
        public float movementSpeed = 1.0f;    // How fast helpers move around
        public float collectRadius = 2.0f;    // Range helpers can collect from
        public bool canCollectRare = false;   // Can helpers collect special rice types
        
        [Header("Zone Management")]
        public List<string> unlockedZones = new List<string>(); // Zone IDs player has discovered
        public List<DeployedHelper> deployedHelpers = new List<DeployedHelper>(); // Currently active helpers
        
        [Header("Helper Upgrades")]
        public int helperCapacityBonus = 0;   // +X max helpers deployable
        public bool hasAutoScavenge = false;  // Helpers work when player offline
        public float offlineEfficiency = 0.5f; // 50% collection rate when offline
        public bool canUpgradeHelpers = false; // Individual helper upgrades
        
        [Header("Special Abilities")]
        public bool hasHelperStorage = false;  // Helpers can store rice before returning
        public int storageCapacity = 10;       // Rice each helper can hold
        public bool canCallHelpers = false;    // Summon all helpers to player location
        
        /// <summary>
        /// Get maximum helpers deployable (base + upgrades)
        /// </summary>
        public int GetMaxHelpers()
        {
            return (ownedGoblins + ownedGhouls) + helperCapacityBonus;
        }
        
        /// <summary>
        /// Get currently deployed helper count
        /// </summary>
        public int GetDeployedHelperCount()
        {
            return deployedHelpers.Count;
        }
        
        /// <summary>
        /// Check if player can deploy more helpers
        /// </summary>
        public bool CanDeployMoreHelpers()
        {
            return GetDeployedHelperCount() < GetMaxHelpers();
        }
        
        /// <summary>
        /// Get total investment in helper system upgrades
        /// </summary>
        public int GetTotalUpgradeLevel()
        {
            int total = 0;
            total += ownedGoblins * 10;                     // 10 points per goblin
            total += ownedGhouls * 20;                      // 20 points per ghoul (more expensive)
            total += (int)((ricePerSecond - 1.0f) * 50);   // 50 points per 0.1 rice/sec increase
            total += helperCapacityBonus * 25;             // 25 points per extra capacity
            total += unlockedZones.Count * 15;             // 15 points per zone unlocked
            return total;
        }
    }
    
    /// <summary>
    /// Data for a deployed helper in a specific zone
    /// </summary>
    [System.Serializable]
    public class DeployedHelper
    {
        public string helperId;           // Unique identifier for this helper
        public HelperType type;          // Goblin or Ghoul
        public string zoneId;            // Which zone this helper is deployed in
        public Vector3 lastKnownPosition; // Last position in the zone
        public float totalRiceCollected; // Lifetime rice collected by this helper
        public float deployedTimestamp;  // When this helper was deployed (for offline calculation)
        
        public DeployedHelper(string id, HelperType helperType, string zone)
        {
            helperId = id;
            type = helperType;
            zoneId = zone;
            lastKnownPosition = Vector3.zero;
            totalRiceCollected = 0f;
            deployedTimestamp = Time.time;
        }
    }
    
    /// <summary>
    /// Types of helpers available
    /// </summary>
    public enum HelperType
    {
        Goblin,    // Cheaper, slower, but reliable
        Ghoul      // More expensive, faster, special abilities
    }
}
