using UnityEngine;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// UPGRADE SHOP - Purchase upgrades between runs
    /// This is the UI interface for spending currency on permanent upgrades
    /// </summary>
    public class UpgradeShop : MonoBehaviour
    {
        public static UpgradeShop Instance { get; private set; }
        
        [Header("Configuration")]
        public bool debugLogPurchases = true;
        
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }
        
        #region Drop Puzzle Upgrades
        
        /// <summary>
        /// Increase x2 gate spawn chance by 5%
        /// Cost scales: 100, 150, 225, 337...
        /// </summary>
        public bool BuyX2GateUpgrade()
        {
            int currentLevel = (int)(playerData.DropPuzzle.x2GateChance * 100 / 5);
            int cost = 100 + (currentLevel * 50);
            
            if (playerData.DropPuzzle.x2GateChance >= 0.5f)
            {
                Debug.LogWarning("[Shop] x2 Gate chance maxed at 50%!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "x2 Gate Chance +5%"))
            {
                playerData.DropPuzzle.x2GateChance += 0.05f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased x2 Gate +5% | Now: {playerData.DropPuzzle.x2GateChance * 100}%");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Unlock x3 gates (must buy x2 first)
        /// </summary>
        public bool UnlockX3Gates()
        {
            if (playerData.DropPuzzle.x2GateChance < 0.1f)
            {
                Debug.LogWarning("[Shop] Unlock x2 gates first!");
                return false;
            }
            
            if (playerData.DropPuzzle.x3GateChance > 0)
            {
                Debug.LogWarning("[Shop] x3 gates already unlocked!");
                return false;
            }
            
            int cost = 500;
            if (playerData.SpendCurrency(cost, "Unlock x3 Gates"))
            {
                playerData.DropPuzzle.x3GateChance = 0.05f; // Start at 5%
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked x3 Gates!");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Increase x3 gate spawn chance by 5%
        /// </summary>
        public bool BuyX3GateUpgrade()
        {
            if (playerData.DropPuzzle.x3GateChance == 0)
            {
                Debug.LogWarning("[Shop] Unlock x3 gates first!");
                return false;
            }
            
            int currentLevel = (int)(playerData.DropPuzzle.x3GateChance * 100 / 5) - 1;
            int cost = 200 + (currentLevel * 75);
            
            if (playerData.DropPuzzle.x3GateChance >= 0.35f)
            {
                Debug.LogWarning("[Shop] x3 Gate chance maxed at 35%!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "x3 Gate Chance +5%"))
            {
                playerData.DropPuzzle.x3GateChance += 0.05f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased x3 Gate +5% | Now: {playerData.DropPuzzle.x3GateChance * 100}%");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Unlock x4 gates (endgame content)
        /// </summary>
        public bool UnlockX4Gates()
        {
            if (playerData.DropPuzzle.x3GateChance < 0.15f)
            {
                Debug.LogWarning("[Shop] Unlock more x3 gates first!");
                return false;
            }
            
            if (playerData.DropPuzzle.x4GateChance > 0)
            {
                Debug.LogWarning("[Shop] x4 gates already unlocked!");
                return false;
            }
            
            int cost = 2000;
            if (playerData.SpendCurrency(cost, "Unlock x4 Gates"))
            {
                playerData.DropPuzzle.x4GateChance = 0.02f; // Start at 2%
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked x4 Gates!");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Unlock bonus point balls (gold balls worth 2x-5x)
        /// </summary>
        public bool UnlockBonusPointBalls()
        {
            if (playerData.DropPuzzle.bonusPointBallChance > 0)
            {
                Debug.LogWarning("[Shop] Bonus balls already unlocked!");
                return false;
            }
            
            int cost = 300;
            if (playerData.SpendCurrency(cost, "Unlock Bonus Point Balls"))
            {
                playerData.DropPuzzle.bonusPointBallChance = 0.05f; // 5% spawn chance
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked Bonus Point Balls!");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Increase bonus ball spawn rate
        /// </summary>
        public bool BuyBonusPointBallUpgrade()
        {
            if (playerData.DropPuzzle.bonusPointBallChance == 0)
            {
                Debug.LogWarning("[Shop] Unlock bonus balls first!");
                return false;
            }
            
            int currentLevel = (int)(playerData.DropPuzzle.bonusPointBallChance * 100 / 5) - 1;
            int cost = 150 + (currentLevel * 50);
            
            if (playerData.DropPuzzle.bonusPointBallChance >= 0.3f)
            {
                Debug.LogWarning("[Shop] Bonus ball chance maxed at 30%!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Bonus Ball Chance +5%"))
            {
                playerData.DropPuzzle.bonusPointBallChance += 0.05f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Bonus Ball +5% | Now: {playerData.DropPuzzle.bonusPointBallChance * 100}%");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Start each level with +5 extra balls
        /// </summary>
        public bool BuyExtraBalls()
        {
            int currentLevel = (playerData.DropPuzzle.startingBalls - 20) / 5;
            int cost = 200 + (currentLevel * 100);
            
            if (playerData.DropPuzzle.startingBalls >= 50)
            {
                Debug.LogWarning("[Shop] Starting balls maxed at 50!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "+5 Starting Balls"))
            {
                playerData.DropPuzzle.startingBalls += 5;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased +5 Balls | Now: {playerData.DropPuzzle.startingBalls} per level");
                return true;
            }
            return false;
        }
        
        #endregion
        
        #region FPS Collector Upgrades
        
        /// <summary>
        /// Increase pickup radius by 0.25
        /// </summary>
        public bool BuyPickupRadiusUpgrade()
        {
            int currentLevel = (int)((playerData.FPSCollector.pickupRadius - 1.5f) / 0.25f);
            int cost = 100 + (currentLevel * 50);
            
            if (playerData.FPSCollector.pickupRadius >= 5.0f)
            {
                Debug.LogWarning("[Shop] Pickup radius maxed at 5.0!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Pickup Radius +0.25"))
            {
                playerData.FPSCollector.pickupRadius += 0.25f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Pickup Radius | Now: {playerData.FPSCollector.pickupRadius}");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Unlock multi-pickup (pick up 2 rices at once)
        /// </summary>
        public bool UnlockMultiPickup()
        {
            if (playerData.FPSCollector.maxSimultaneousPickups > 1)
            {
                Debug.LogWarning("[Shop] Multi-pickup already unlocked!");
                return false;
            }
            
            int cost = 400;
            if (playerData.SpendCurrency(cost, "Unlock Multi-Pickup"))
            {
                playerData.FPSCollector.maxSimultaneousPickups = 2;
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked Multi-Pickup (2 rices)");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Upgrade multi-pickup capacity (+1)
        /// </summary>
        public bool BuyMultiPickupUpgrade()
        {
            if (playerData.FPSCollector.maxSimultaneousPickups == 1)
            {
                Debug.LogWarning("[Shop] Unlock multi-pickup first!");
                return false;
            }
            
            int currentLevel = playerData.FPSCollector.maxSimultaneousPickups - 2;
            int cost = 300 + (currentLevel * 150);
            
            if (playerData.FPSCollector.maxSimultaneousPickups >= 5)
            {
                Debug.LogWarning("[Shop] Multi-pickup maxed at 5!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Multi-Pickup +1"))
            {
                playerData.FPSCollector.maxSimultaneousPickups++;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Multi-Pickup | Now: {playerData.FPSCollector.maxSimultaneousPickups} rices");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Unlock magnetic pull (rice flies toward you)
        /// </summary>
        public bool UnlockMagneticPull()
        {
            if (playerData.FPSCollector.magneticPullEnabled)
            {
                Debug.LogWarning("[Shop] Magnetic pull already unlocked!");
                return false;
            }
            
            int cost = 800;
            if (playerData.SpendCurrency(cost, "Unlock Magnetic Pull"))
            {
                playerData.FPSCollector.magneticPullEnabled = true;
                playerData.FPSCollector.magneticPullRadius = 3.0f;
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked Magnetic Pull!");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Increase move speed by 10%
        /// </summary>
        public bool BuyMoveSpeedUpgrade()
        {
            int currentLevel = (int)((playerData.FPSCollector.moveSpeedMultiplier - 1.0f) * 10);
            int cost = 150 + (currentLevel * 75);
            
            if (playerData.FPSCollector.moveSpeedMultiplier >= 2.0f)
            {
                Debug.LogWarning("[Shop] Move speed maxed at 2.0x!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Move Speed +10%"))
            {
                playerData.FPSCollector.moveSpeedMultiplier += 0.1f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Move Speed | Now: {playerData.FPSCollector.moveSpeedMultiplier:F1}x");
                return true;
            }
            return false;
        }
        
        #endregion
        
        #region Crafting Upgrades
        
        /// <summary>
        /// Unlock Good quality riceballs (20% chance)
        /// </summary>
        public bool UnlockGoodQuality()
        {
            if (playerData.Crafting.goodChance > 0)
            {
                Debug.LogWarning("[Shop] Good quality already unlocked!");
                return false;
            }
            
            int cost = 250;
            if (playerData.SpendCurrency(cost, "Unlock Good Quality Riceballs"))
            {
                playerData.Crafting.goodChance = 0.2f; // 20% chance
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked Good Quality Riceballs (2x value)!");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Increase Good quality chance by 5%
        /// </summary>
        public bool BuyGoodQualityUpgrade()
        {
            if (playerData.Crafting.goodChance == 0)
            {
                Debug.LogWarning("[Shop] Unlock Good quality first!");
                return false;
            }
            
            int currentLevel = (int)((playerData.Crafting.goodChance - 0.2f) * 100 / 5);
            int cost = 150 + (currentLevel * 50);
            
            if (playerData.Crafting.goodChance >= 0.5f)
            {
                Debug.LogWarning("[Shop] Good quality maxed at 50%!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Good Quality +5%"))
            {
                playerData.Crafting.goodChance += 0.05f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Good Quality +5% | Now: {playerData.Crafting.goodChance * 100:F0}%");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Unlock Great quality riceballs (5% chance)
        /// </summary>
        public bool UnlockGreatQuality()
        {
            if (playerData.Crafting.goodChance < 0.3f)
            {
                Debug.LogWarning("[Shop] Need 30%+ Good quality first!");
                return false;
            }
            
            if (playerData.Crafting.greatChance > 0)
            {
                Debug.LogWarning("[Shop] Great quality already unlocked!");
                return false;
            }
            
            int cost = 600;
            if (playerData.SpendCurrency(cost, "Unlock Great Quality Riceballs"))
            {
                playerData.Crafting.greatChance = 0.05f; // 5% chance
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked Great Quality Riceballs (4x value)!");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Increase Great quality chance by 2%
        /// </summary>
        public bool BuyGreatQualityUpgrade()
        {
            if (playerData.Crafting.greatChance == 0)
            {
                Debug.LogWarning("[Shop] Unlock Great quality first!");
                return false;
            }
            
            int currentLevel = (int)((playerData.Crafting.greatChance - 0.05f) * 100 / 2);
            int cost = 300 + (currentLevel * 100);
            
            if (playerData.Crafting.greatChance >= 0.2f)
            {
                Debug.LogWarning("[Shop] Great quality maxed at 20%!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Great Quality +2%"))
            {
                playerData.Crafting.greatChance += 0.02f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Great Quality +2% | Now: {playerData.Crafting.greatChance * 100:F0}%");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Unlock Excellent quality riceballs (1% chance, endgame)
        /// </summary>
        public bool UnlockExcellentQuality()
        {
            if (playerData.Crafting.greatChance < 0.1f)
            {
                Debug.LogWarning("[Shop] Need 10%+ Great quality first!");
                return false;
            }
            
            if (playerData.Crafting.excellentChance > 0)
            {
                Debug.LogWarning("[Shop] Excellent quality already unlocked!");
                return false;
            }
            
            int cost = 2000;
            if (playerData.SpendCurrency(cost, "Unlock Excellent Quality Riceballs"))
            {
                playerData.Crafting.excellentChance = 0.01f; // 1% chance
                if (debugLogPurchases) Debug.Log("[Shop] ✅ Unlocked Excellent Quality Riceballs (8x value)!");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Increase Excellent quality chance by 1%
        /// </summary>
        public bool BuyExcellentQualityUpgrade()
        {
            if (playerData.Crafting.excellentChance == 0)
            {
                Debug.LogWarning("[Shop] Unlock Excellent quality first!");
                return false;
            }
            
            int currentLevel = (int)((playerData.Crafting.excellentChance - 0.01f) * 100);
            int cost = 1000 + (currentLevel * 500);
            
            if (playerData.Crafting.excellentChance >= 0.1f)
            {
                Debug.LogWarning("[Shop] Excellent quality maxed at 10%!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Excellent Quality +1%"))
            {
                playerData.Crafting.excellentChance += 0.01f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Excellent Quality +1% | Now: {playerData.Crafting.excellentChance * 100:F0}%");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Increase crafting speed by 20%
        /// </summary>
        public bool BuyCraftingSpeedUpgrade()
        {
            int currentLevel = (int)((playerData.Crafting.craftingSpeedMultiplier - 1.0f) * 5);
            int cost = 200 + (currentLevel * 100);
            
            if (playerData.Crafting.craftingSpeedMultiplier >= 3.0f)
            {
                Debug.LogWarning("[Shop] Crafting speed maxed at 3.0x!");
                return false;
            }
            
            if (playerData.SpendCurrency(cost, "Crafting Speed +20%"))
            {
                playerData.Crafting.craftingSpeedMultiplier += 0.2f;
                if (debugLogPurchases) Debug.Log($"[Shop] ✅ Purchased Crafting Speed | Now: {playerData.Crafting.craftingSpeedMultiplier:F1}x");
                return true;
            }
            return false;
        }
        
        #endregion
        
        #region Utility
        
        /// <summary>
        /// Get cost for any upgrade (for UI display)
        /// </summary>
        public int GetUpgradeCost(string upgradeType)
        {
            // Calculate without purchasing
            switch (upgradeType)
            {
                case "x2Gate":
                    return 100 + ((int)(playerData.DropPuzzle.x2GateChance * 100 / 5) * 50);
                case "x3Unlock":
                    return 500;
                case "x3Gate":
                    return 200 + (((int)(playerData.DropPuzzle.x3GateChance * 100 / 5) - 1) * 75);
                case "pickupRadius":
                    return 100 + ((int)((playerData.FPSCollector.pickupRadius - 1.5f) / 0.25f) * 50);
                case "multiPickupUnlock":
                    return 400;
                case "moveSpeed":
                    return 150 + ((int)((playerData.FPSCollector.moveSpeedMultiplier - 1.0f) * 10) * 75);
                default:
                    return 999999;
            }
        }
        
        /// <summary>
        /// Check if upgrade is available for purchase
        /// </summary>
        public bool CanBuyUpgrade(string upgradeType)
        {
            int cost = GetUpgradeCost(upgradeType);
            return playerData.TotalCurrency >= cost;
        }
        
        #endregion
    }
}
