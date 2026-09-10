using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Handles player interaction with the shop BuyZone
    /// Shows "Press E" prompt when player enters
    /// </summary>
    public class BuyZoneInteraction : MonoBehaviour
    {
        [Header("UI Settings")]
        [Tooltip("Prompt message shown to player")]
        public string promptMessage = "Press [E] to view wares";
        
        private bool playerInZone = false;
        private bool shopOpen = false;
        private GUIStyle guiStyle;
        private Vampire.Player.FPSController fpsController;
        
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                playerInZone = true;
                enabled = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                playerInZone = false;
                if (shopOpen) CloseShop();
                enabled = false;
            }
        }

        private void Start()
        {
            enabled = false; // re-enabled by OnTriggerEnter
            fpsController = FindObjectOfType<Vampire.Player.FPSController>();
        }
        
        private void Update()
        {
            if (playerInZone && !shopOpen && Input.GetKeyDown(KeyCode.E))
            {
                OpenShop();
            }
            
            // Allow ESC to close shop from Update as well
            if (shopOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseShop();
            }
        }
        
        private void OpenShop()
        {
            shopOpen = true;
            Vampire.EscapeMenuManager.PushEscBlock();
            
            // Pause day/night cycle while shopping
            if (DayNightCycleManager.Instance != null)
            {
                DayNightCycleManager.Instance.Pause();
            }
            
            // Disable FPS controller
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }
            
            // Enable cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        
        private void CloseShop()
        {
            shopOpen = false;
            Vampire.EscapeMenuManager.PopEscBlock();
            
            // Resume day/night cycle
            if (DayNightCycleManager.Instance != null)
            {
                DayNightCycleManager.Instance.Resume();
            }
            
            // Re-enable FPS controller
            if (fpsController != null)
            {
                fpsController.enabled = true;
            }
            
            // Lock cursor again
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        
        private void OnGUI()
        {
            // Show shop UI if open
            if (shopOpen)
            {
                DrawShopUI();
                return;
            }
            
            // Show "Press E" prompt when in zone
            if (!playerInZone) return;
            
            // Initialize style
            if (guiStyle == null)
            {
                guiStyle = new GUIStyle(GUI.skin.box);
                guiStyle.fontSize = 16;
                guiStyle.alignment = TextAnchor.MiddleCenter;
                guiStyle.normal.textColor = Color.white;
                guiStyle.normal.background = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));
            }
            
            // Display prompt at bottom center of screen
            float width = 400;
            float height = 60;
            float x = (Screen.width - width) / 2f;
            float y = Screen.height - height - 100; // 100px from bottom
            
            GUI.Box(new Rect(x, y, width, height), promptMessage, guiStyle);
        }
        
        private void DrawShopUI()
        {
            var playerData = PlayerDataManager.Instance;
            var shop = UpgradeShop.Instance;
            if (playerData == null) return;
            
            // Semi-transparent background
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", new GUIStyle { normal = { background = MakeTexture(2, 2, new Color(0, 0, 0, 0.8f)) } });
            
            // Shop window (taller for more upgrades)
            float windowWidth = 700;
            float windowHeight = 700;
            float windowX = (Screen.width - windowWidth) / 2f;
            float windowY = (Screen.height - windowHeight) / 2f;
            
            GUIStyle windowStyle = new GUIStyle(GUI.skin.box);
            windowStyle.fontSize = 20;
            windowStyle.normal.textColor = Color.white;
            windowStyle.normal.background = MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.15f, 1f));
            
            GUI.Box(new Rect(windowX, windowY, windowWidth, windowHeight), "", windowStyle);
            
            // Shop title
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 32;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = Color.yellow;
            
            GUI.Label(new Rect(windowX, windowY + 20, windowWidth, 50), "Flink's Shop - Crafting Quality", titleStyle);
            
            // Currency display
            GUIStyle currencyStyle = new GUIStyle(GUI.skin.label);
            currencyStyle.fontSize = 24;
            currencyStyle.alignment = TextAnchor.MiddleCenter;
            currencyStyle.normal.textColor = Color.green;
            
            GUI.Label(new Rect(windowX, windowY + 70, windowWidth, 40), 
                $"Your Currency: ${playerData.TotalCurrency * 0.01f:F2}", currencyStyle);
            
            // Styles
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 18;
            labelStyle.normal.textColor = Color.white;
            
            GUIStyle smallLabelStyle = new GUIStyle(GUI.skin.label);
            smallLabelStyle.fontSize = 16;
            smallLabelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 16;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.fontStyle = FontStyle.Bold;
            
            float yPos = windowY + 130;
            float leftMargin = windowX + 30;
            float rightMargin = windowWidth - 60;
            
            var craft = playerData.Crafting;

            // ━━━ Good Quality (2×) ━━━
            GUI.Label(new Rect(leftMargin, yPos, rightMargin, 30),
                $"━━━ Good Quality (2×): {(craft.goodChance > 0 ? $"{craft.goodChance * 100:F0}%" : "Locked")} (Max: 50%) ━━━", labelStyle);
            yPos += 35;

            if (craft.goodChance == 0)
            {
                DrawUpgradeButton(leftMargin, ref yPos, rightMargin, buttonStyle, smallLabelStyle, playerData,
                    true, () => shop != null && shop.UnlockGoodQuality(),
                    "🔓 Unlock Good Quality (2× value)", 250);
            }
            else
            {
                int lvl = (int)((craft.goodChance - 0.2f) * 100 / 5);
                DrawUpgradeButton(leftMargin, ref yPos, rightMargin, buttonStyle, smallLabelStyle, playerData,
                    craft.goodChance < 0.5f, () => shop != null && shop.BuyGoodQualityUpgrade(),
                    "Good Chance +5%", 150 + lvl * 50);
            }

            yPos += 10;

            // ━━━ Great Quality (4×) ━━━
            GUI.Label(new Rect(leftMargin, yPos, rightMargin, 30),
                $"━━━ Great Quality (4×): {(craft.greatChance > 0 ? $"{craft.greatChance * 100:F0}%" : "Locked")} (Max: 20%) ━━━", labelStyle);
            yPos += 35;

            if (craft.greatChance == 0)
            {
                if (craft.goodChance >= 0.3f)
                    DrawUpgradeButton(leftMargin, ref yPos, rightMargin, buttonStyle, smallLabelStyle, playerData,
                        true, () => shop != null && shop.UnlockGreatQuality(),
                        "🔓 Unlock Great Quality (4× value)", 600);
                else
                    DrawLocked(leftMargin, ref yPos, rightMargin, smallLabelStyle, "🔒 Reach 30% Good Quality first");
            }
            else
            {
                int lvl = (int)((craft.greatChance - 0.05f) * 100 / 2);
                DrawUpgradeButton(leftMargin, ref yPos, rightMargin, buttonStyle, smallLabelStyle, playerData,
                    craft.greatChance < 0.2f, () => shop != null && shop.BuyGreatQualityUpgrade(),
                    "Great Chance +2%", 300 + lvl * 100);
            }

            yPos += 10;

            // ━━━ Excellent Quality (5×) ━━━
            GUI.Label(new Rect(leftMargin, yPos, rightMargin, 30),
                $"━━━ Excellent Quality (5×): {(craft.excellentChance > 0 ? $"{craft.excellentChance * 100:F0}%" : "Locked")} (Max: 10%) ━━━", labelStyle);
            yPos += 35;

            if (craft.excellentChance == 0)
            {
                if (craft.greatChance >= 0.1f)
                    DrawUpgradeButton(leftMargin, ref yPos, rightMargin, buttonStyle, smallLabelStyle, playerData,
                        true, () => shop != null && shop.UnlockExcellentQuality(),
                        "🔓 Unlock Excellent Quality (5× value!)", 2000);
                else
                    DrawLocked(leftMargin, ref yPos, rightMargin, smallLabelStyle, "🔒 Reach 10% Great Quality first");
            }
            else
            {
                int lvl = (int)((craft.excellentChance - 0.01f) * 100);
                DrawUpgradeButton(leftMargin, ref yPos, rightMargin, buttonStyle, smallLabelStyle, playerData,
                    craft.excellentChance < 0.1f, () => shop != null && shop.BuyExcellentQualityUpgrade(),
                    "Excellent Chance +1%", 1000 + lvl * 500);
            }

            yPos += 10;

            // ━━━ Crafting Speed ━━━
            GUI.Label(new Rect(leftMargin, yPos, rightMargin, 30),
                $"━━━ Crafting Speed: {craft.craftingSpeedMultiplier:F1}x (Max: 3.0x) ━━━", labelStyle);
            yPos += 35;

            {
                int lvl = (int)((craft.craftingSpeedMultiplier - 1.0f) * 5);
                DrawUpgradeButton(leftMargin, ref yPos, rightMargin, buttonStyle, smallLabelStyle, playerData,
                    craft.craftingSpeedMultiplier < 3.0f, () => shop != null && shop.BuyCraftingSpeedUpgrade(),
                    "Crafting Speed +20%", 200 + lvl * 100);
            }
            
            // Close button
            if (GUI.Button(new Rect(windowX + windowWidth - 120, windowY + windowHeight - 70, 100, 50), 
                "Close [ESC]", buttonStyle))
            {
                CloseShop();
            }
        }
        
        /// <summary>
        /// Helper to draw an upgrade button. Returns true if purchase succeeded.
        /// </summary>
        private bool DrawUpgradeButton(float x, ref float y, float maxWidth, GUIStyle buttonStyle, GUIStyle labelStyle, 
            PlayerDataManager playerData, bool isAvailable, System.Func<bool> purchaseFunc, string upgradeText, int costCents)
        {
            if (!isAvailable)
            {
                GUI.Label(new Rect(x, y, maxWidth, 25), 
                    "✅ MAXED OUT", new GUIStyle(labelStyle) { normal = { textColor = Color.cyan } });
                y += 30;
                return false;
            }
            
            bool canAfford = playerData.debugFreeUpgrades || playerData.TotalCurrency >= costCents;
            
            // Show cost
            GUI.Label(new Rect(x, y, maxWidth, 25), 
                $"{upgradeText} — ${costCents * 0.01f:F2}", 
                canAfford ? labelStyle : new GUIStyle(labelStyle) { normal = { textColor = Color.red } });
            y += 30;
            
            // Purchase button
            GUI.enabled = canAfford;
            if (GUI.Button(new Rect(x + 150, y, 200, 40), 
                canAfford ? "Purchase" : "Not Enough $$$", buttonStyle))
            {
                bool success = purchaseFunc();
                GUI.enabled = true;
                y += 50;
                return success;
            }
            GUI.enabled = true;
            y += 50;
            return false;
        }

        /// <summary>Draws a greyed-out "locked / prerequisite" line and advances y.</summary>
        private void DrawLocked(float x, ref float y, float maxWidth, GUIStyle labelStyle, string message)
        {
            GUI.Label(new Rect(x, y, maxWidth, 25), message,
                new GUIStyle(labelStyle) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } });
            y += 40;
        }

        private int GetPickupRadiusCost(float currentRadius)
        {
            int currentLevel = (int)((currentRadius - 1.5f) / 0.25f);
            return 100 + (currentLevel * 50);
        }
        
        private int GetMultiPickupCost(int currentPickups)
        {
            int currentLevel = currentPickups - 2;
            return 300 + (currentLevel * 150);
        }
        
        private int GetMoveSpeedCost(float currentSpeed)
        {
            int currentLevel = (int)((currentSpeed - 1.0f) * 10);
            return 150 + (currentLevel * 75);
        }
        
        /// <summary>
        /// Helper to create a solid color texture for GUI backgrounds
        /// </summary>
        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
