using UnityEngine;
using System;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// MASTER PLAYER DATA MANAGER - Roguelite Progression
    /// Persists across scenes, handles all upgrades for both game modes
    /// </summary>
    public class PlayerDataManager : MonoBehaviour
    {
        public static PlayerDataManager Instance { get; private set; }
        
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
        
        [Header("Meta Progression")]
        public int TotalRunsCompleted = 0;
        public int HighestLevelReached = 1;
        public bool TutorialCompleted = false;
        
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
                Debug.Log("[PlayerData] Manager initialized");
            }
            else
            {
                Destroy(gameObject);
            }
        }
        
        #region Currency Management
        
        /// <summary>
        /// Award currency - both to current run and total pool
        /// </summary>
        public void AddCurrency(int amount, string source = "")
        {
            TotalCurrency += amount;
            CurrentRunCurrency += amount;
            TotalCurrencyEarned += amount; // Track lifetime stat for quest progress
            
            Debug.Log($"[PlayerData] +{amount} currency from {source} | Run:{CurrentRunCurrency} Total:{TotalCurrency} | Lifetime earned: {TotalCurrencyEarned}");
            
            // Notify tutorial manager
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.NotifyCurrencyEarned(amount);
            }
            
            // TODO: UI update event
        }
        
        #endregion
        
        #region Rice & RiceBall Management
        
        /// <summary>
        /// Add rice grains collected in FPS mode
        /// </summary>
        public void AddRice(int amount)
        {
            RiceGrains += amount;
            Debug.Log($"[PlayerData] +{amount} rice | Total: {RiceGrains}");
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
                Debug.LogWarning("[PlayerData] Not enough rice! Need 5 rice per riceball.");
                return 0;
            }
            
            int riceUsed = riceBallsToMake * 5;
            RiceGrains -= riceUsed;
            
            // Roll quality for each riceball
            int fineCount = 0, goodCount = 0, greatCount = 0, excellentCount = 0;
            
            for (int i = 0; i < riceBallsToMake; i++)
            {
                RiceBallQuality quality = RollRiceBallQuality();
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
            
            Debug.Log($"[PlayerData] Crafted {riceBallsToMake} riceballs! Fine:{fineCount} Good:{goodCount} Great:{greatCount} Excellent:{excellentCount}");
            Debug.Log($"[PlayerData] Inventory: {Inventory.GetTotalBalls()} balls | Rice remaining: {RiceGrains} | Lifetime crafted: {TotalRiceBallsCrafted}");
            
            return riceBallsToMake;
        }
        
        /// <summary>
        /// Roll riceball quality based on crafting upgrades
        /// </summary>
        private RiceBallQuality RollRiceBallQuality()
        {
            float roll = UnityEngine.Random.Range(0f, 1f);
            float cumulative = 0f;
            
            // Excellent (rarest)
            cumulative += Crafting.excellentChance;
            if (roll < cumulative) return RiceBallQuality.Excellent;
            
            // Great
            cumulative += Crafting.greatChance;
            if (roll < cumulative) return RiceBallQuality.Great;
            
            // Good
            cumulative += Crafting.goodChance;
            if (roll < cumulative) return RiceBallQuality.Good;
            
            // Fine (default)
            return RiceBallQuality.Fine;
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
            if (TotalCurrency < amount)
            {
                Debug.LogWarning($"[PlayerData] Not enough currency! Need {amount}, have {TotalCurrency}");
                return false;
            }
            
            TotalCurrency -= amount;
            Debug.Log($"[PlayerData] Spent {amount} on: {purchaseDescription} | Remaining: {TotalCurrency}");
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
            Debug.Log($"[PlayerData] Run #{TotalRunsCompleted} complete!");
        }
        
        #endregion
        
        #region Save/Load
        
        public void SavePlayerData()
        {
            // TODO: Implement JSON save to PlayerPrefs or file
            // For now, data persists in memory only (resets on app close)
            Debug.Log("[PlayerData] Saved (in-memory only for now)");
        }
        
        public void LoadPlayerData()
        {
            // TODO: Load from PlayerPrefs/file
            Debug.Log("[PlayerData] Loaded player data");
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
            TotalRunsCompleted = 0;
            HighestLevelReached = 1;
            TutorialCompleted = false;
            SavePlayerData();
            Debug.Log("[PlayerData] ⚠️ All progress reset!");
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
        
        [Header("Special Ball Chances")]
        public float bonusPointBallChance = 0.0f;     // 2x-5x points
        public float multiplierBoostBallChance = 0.0f; // +1 to gate multipliers
        public float luckyBallChance = 0.0f;           // Extra rewards
        
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
}
