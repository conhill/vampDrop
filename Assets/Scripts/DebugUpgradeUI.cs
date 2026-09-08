using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// DEBUG UI - Quick test interface for upgrades (not final UI)
    /// Press keys to test upgrades without building full UI
    /// </summary>
    public class DebugUpgradeUI : MonoBehaviour
    {
        private PlayerDataManager playerData => PlayerDataManager.Instance;
        private UpgradeShop shop => UpgradeShop.Instance;
        
        [Header("Debug Settings")]
        public bool showOnScreenHelp = true;
        public KeyCode addCurrencyKey = KeyCode.C;
        public KeyCode addRiceKey = KeyCode.G; // G for Grains
        public KeyCode buyX2GateKey = KeyCode.Alpha1;
        public KeyCode unlockX3Key = KeyCode.Alpha2;
        public KeyCode buyBonusBallKey = KeyCode.Alpha3;
        public KeyCode buyCraftingKey = KeyCode.Alpha5;
        public KeyCode buyPickupRadiusKey = KeyCode.Alpha4;
        public KeyCode resetProgressKey = KeyCode.R;

        [Header("Stress Drop (P)")]
        [Tooltip("Fills the riceball inventory with loadBallCount Fine balls so a big drop can " +
                 "be triggered with SPACE, without grinding out the rice by hand.")]
        public KeyCode loadBallsKey = KeyCode.P;

        [Tooltip("How many Fine riceballs the load key grants.")]
        public int loadBallCount = 2000;

        [Tooltip("BallsPerInterval to force on the scene's dropper when the load key is used. " +
                 "Without this the drop trickles out one ball per DropInterval — 2000 balls at " +
                 "DropInterval 0.1 would take 200s, and because balls are culled shortly after " +
                 "leaving the board only ~66 would be alive at once, so you would never see the " +
                 "load on screen. Set to 0 to leave the dropper untouched.")]
        public int loadBallsPerInterval = 50;
        
        private GUIStyle headerStyle;
        private GUIStyle textStyle;
        private bool initialized = false;
        
        /// <summary>
        /// Fills the inventory for a stress drop and makes that drop actually releasable.
        ///
        /// Granting the balls alone is not enough to see them: DropperControllerECS releases
        /// one ball per DropInterval, so 2000 balls at DropInterval 0.1 dribble out over 200
        /// seconds, and a ball is culled a few seconds after it leaves the board — the live
        /// count would plateau near 66 and never approach the load. Raising BallsPerInterval
        /// for the dropper is what turns the grant into a drop you can watch.
        /// </summary>
        private void LoadBallsForStressDrop()
        {
            playerData.Inventory.FineBalls += loadBallCount;

            string dropperNote = "no DropperControllerECS in this scene (enter DropPuzzle to drop)";
            if (loadBallsPerInterval > 0)
            {
                var dropper = FindObjectOfType<DropperControllerECS>();
                if (dropper != null)
                {
                    dropper.BallsPerInterval = loadBallsPerInterval;
                    float seconds = loadBallCount / (float)loadBallsPerInterval * dropper.DropInterval;
                    dropperNote = $"BallsPerInterval={loadBallsPerInterval} " +
                                  $"(~{seconds:F1}s to release all {loadBallCount})";
                }
            }

            Debug.Log($"[Debug] Loaded {loadBallCount} Fine riceballs — " +
                      $"inventory total {playerData.Inventory.GetTotalBalls()}. {dropperNote}. Press SPACE to drop.");
        }

        private void InitStyles()
        {
            if (initialized) return;
            
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 18;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = Color.yellow;
            
            textStyle = new GUIStyle(GUI.skin.label);
            textStyle.fontSize = 14;
            textStyle.normal.textColor = Color.white;
            
            initialized = true;
        }
        
        private void Update()
        {
            if (playerData == null || shop == null) return;
            
            // Add currency for testing
            if (Input.GetKeyDown(addCurrencyKey))
            {
                playerData.AddCurrency(100, "Debug Cheat");
            }
            
            // Add rice for testing
            if (Input.GetKeyDown(addRiceKey))
            {
                playerData.AddRice(50);
            }

            // Load a big riceball inventory for a stress drop
            if (Input.GetKeyDown(loadBallsKey))
            {
                LoadBallsForStressDrop();
            }
            
            // Buy x2 gate upgrade
            if (Input.GetKeyDown(buyX2GateKey))
            {
                shop.BuyX2GateUpgrade();
            }
            
            // Unlock x3 gates
            if (Input.GetKeyDown(unlockX3Key))
            {
                if (playerData.DropPuzzle.x3GateChance == 0)
                {
                    shop.UnlockX3Gates();
                }
                else
                {
                    shop.BuyX3GateUpgrade();
                }
            }
            
            // Buy bonus ball chance
            if (Input.GetKeyDown(buyBonusBallKey))
            {
                if (playerData.DropPuzzle.bonusPointBallChance == 0)
                {
                    shop.UnlockBonusPointBalls();
                }
                else
                {
                    shop.BuyBonusPointBallUpgrade();
                }
            }
            
            // Buy pickup radius (FPS Collector)
            if (Input.GetKeyDown(buyPickupRadiusKey))
            {
                shop.BuyPickupRadiusUpgrade();
            }
            
            // Buy crafting upgrade
            if (Input.GetKeyDown(buyCraftingKey))
            {
                // Try to unlock/upgrade quality tiers
                if (playerData.Crafting.goodChance == 0)
                {
                    shop.UnlockGoodQuality();
                }
                else if (playerData.Crafting.greatChance == 0)
                {
                    shop.UnlockGreatQuality();
                }
                else if (playerData.Crafting.excellentChance == 0)
                {
                    shop.UnlockExcellentQuality();
                }
                else
                {
                    shop.BuyGoodQualityUpgrade();
                }
            }
            
            // Reset all progress
            if (Input.GetKeyDown(resetProgressKey))
            {
                Debug.LogWarning("[Debug] Resetting ALL progress!");
                playerData.ResetAllProgress();
            }
        }
        
        private void OnGUI()
        {
            if (!showOnScreenHelp || playerData == null) return;
            
            InitStyles();
            
            float x = 10;
            float y = Screen.height - 450;
            float lineHeight = 20;
            
            // Background box
            GUI.Box(new Rect(x - 5, y - 5, 450, 440), "");
            
            GUI.Label(new Rect(x, y, 450, 30), "DEBUG UPGRADE SHOP", headerStyle);
            y += 30;
            
            // Currency & Rice display
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Currency: ${playerData.TotalCurrency} (Run: ${playerData.CurrentRunCurrency})", textStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Rice: {playerData.RiceGrains} | Inventory: {playerData.Inventory.GetTotalBalls()} balls (Value:{playerData.Inventory.GetTotalValue()}x)", textStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Runs Completed: {playerData.TotalRunsCompleted} | Level: {playerData.HighestLevelReached}", textStyle);
            y += lineHeight;
            
            // Riceball inventory breakdown
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"  Fine:{playerData.Inventory.FineBalls} Good:{playerData.Inventory.GoodBalls} Great:{playerData.Inventory.GreatBalls} Excellent:{playerData.Inventory.ExcellentBalls}", textStyle);
            y += lineHeight + 10;
            
            // Drop puzzle upgrades
            GUI.Label(new Rect(x, y, 450, lineHeight), "--- DROP PUZZLE ---", headerStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"x2 Gates: {playerData.DropPuzzle.x2GateChance * 100:F0}% | x3: {playerData.DropPuzzle.x3GateChance * 100:F0}% | x4: {playerData.DropPuzzle.x4GateChance * 100:F0}%", textStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Starting Balls: {playerData.DropPuzzle.startingBalls}", textStyle);
            y += lineHeight + 10;
            
            // Crafting upgrades
            GUI.Label(new Rect(x, y, 450, lineHeight), "--- CRAFTING ---", headerStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Good: {playerData.Crafting.goodChance * 100:F0}% | Great: {playerData.Crafting.greatChance * 100:F0}% | Excellent: {playerData.Crafting.excellentChance * 100:F0}%", textStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Crafting Speed: {playerData.Crafting.craftingSpeedMultiplier:F1}x", textStyle);
            y += lineHeight + 10;
            
            // FPS Collector upgrades
            GUI.Label(new Rect(x, y, 450, lineHeight), "--- FPS COLLECTOR ---", headerStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Pickup Radius: {playerData.FPSCollector.pickupRadius:F2}", textStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), 
                $"Max Pickups: {playerData.FPSCollector.maxSimultaneousPickups} | Move Speed: {playerData.FPSCollector.moveSpeedMultiplier:F1}x", textStyle);
            y += lineHeight + 10;
            
            // Controls
            GUI.Label(new Rect(x, y, 450, lineHeight), "--- CONTROLS ---", headerStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), $"[C] +$100  [G] +50Rice  [1] x2Gate  [2] x3Gate  [3] BonusBall", textStyle);
            y += lineHeight;
            
            GUI.Label(new Rect(x, y, 450, lineHeight), $"[4] Radius  [5] Crafting  [R] Reset All", textStyle);
            y += lineHeight;

            GUI.Label(new Rect(x, y, 450, lineHeight),
                $"[{loadBallsKey}] Load {loadBallCount} riceballs, then SPACE to drop", textStyle);
        }
    }
}
