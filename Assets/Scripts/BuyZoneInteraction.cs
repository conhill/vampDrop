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
                Debug.Log("[BuyZone] Player entered shop zone");
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                playerInZone = false;
                Debug.Log("[BuyZone] Player left shop zone");
            }
        }
        
        private void Start()
        {
            // Find FPS controller in scene
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
            Debug.Log("[BuyZone] 🛒 Shop opened!");
            
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
            Debug.Log("[BuyZone] Shop closed");
            
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
                guiStyle.fontSize = 24;
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
            if (playerData == null) return;
            
            // Semi-transparent background
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", new GUIStyle { normal = { background = MakeTexture(2, 2, new Color(0, 0, 0, 0.8f)) } });
            
            // Shop window
            float windowWidth = 600;
            float windowHeight = 500;
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
            
            GUI.Label(new Rect(windowX, windowY + 20, windowWidth, 50), "Flink's Shop", titleStyle);
            
            // Currency display
            GUIStyle currencyStyle = new GUIStyle(GUI.skin.label);
            currencyStyle.fontSize = 24;
            currencyStyle.alignment = TextAnchor.MiddleCenter;
            currencyStyle.normal.textColor = Color.green;
            
            GUI.Label(new Rect(windowX, windowY + 70, windowWidth, 40), 
                $"Your Currency: ${playerData.TotalCurrency * 0.01f:F2}", currencyStyle);
            
            // Upgrade section
            float upgradeY = windowY + 130;
            
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 20;
            labelStyle.normal.textColor = Color.white;
            
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 18;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.fontStyle = FontStyle.Bold;
            
            // Collection Range Upgrade
            float currentRange = playerData.FPSCollector.pickupRadius;
            float nextRange = GetNextRangeUpgrade(currentRange);
            int upgradeCost = GetRangeUpgradeCost(currentRange);
            
            GUI.Label(new Rect(windowX + 30, upgradeY, windowWidth - 60, 30), 
                "━━━ Collection Range ━━━", labelStyle);
            
            GUI.Label(new Rect(windowX + 30, upgradeY + 40, windowWidth - 60, 30), 
                $"Current Range: {currentRange:F1} feet", labelStyle);
            
            if (nextRange > currentRange)
            {
                GUI.Label(new Rect(windowX + 30, upgradeY + 75, windowWidth - 60, 30), 
                    $"Next Upgrade: {nextRange:F1} feet", labelStyle);
                
                GUI.Label(new Rect(windowX + 30, upgradeY + 110, windowWidth - 60, 30), 
                    $"Cost: ${upgradeCost * 0.01f:F2}", 
                    playerData.TotalCurrency >= upgradeCost ? labelStyle : new GUIStyle(labelStyle) { normal = { textColor = Color.red } });
                
                bool canAfford = playerData.TotalCurrency >= upgradeCost;
                GUI.enabled = canAfford;
                
                if (GUI.Button(new Rect(windowX + 200, upgradeY + 150, 200, 50), 
                    canAfford ? "Purchase Upgrade" : "Not Enough $$$", buttonStyle))
                {
                    PurchaseRangeUpgrade();
                }
                
                GUI.enabled = true;
            }
            else
            {
                GUI.Label(new Rect(windowX + 30, upgradeY + 75, windowWidth - 60, 30), 
                    "MAX LEVEL ACHIEVED!", new GUIStyle(labelStyle) { normal = { textColor = Color.cyan } });
            }
            
            // Close button
            if (GUI.Button(new Rect(windowX + windowWidth - 120, windowY + windowHeight - 70, 100, 50), 
                "Close [ESC]", buttonStyle))
            {
                CloseShop();
            }
        }
        
        private float GetNextRangeUpgrade(float currentRange)
        {
            // Upgrade path: 1.5 -> 2.5 -> 4.0 -> 6.0 -> 10.0
            if (currentRange < 2.5f) return 2.5f;
            if (currentRange < 4.0f) return 4.0f;
            if (currentRange < 6.0f) return 6.0f;
            if (currentRange < 10.0f) return 10.0f;
            return currentRange; // Max level
        }
        
        private int GetRangeUpgradeCost(float currentRange)
        {
            // Costs in cents: 1.5->2.5 = $0.50, 2.5->4.0 = $1.00, 4.0->6.0 = $2.00, 6.0->10.0 = $5.00
            if (currentRange < 2.5f) return 50;   // $0.50
            if (currentRange < 4.0f) return 100;  // $1.00
            if (currentRange < 6.0f) return 200;  // $2.00
            if (currentRange < 10.0f) return 500; // $5.00
            return 0; // Max level
        }
        
        private void PurchaseRangeUpgrade()
        {
            var playerData = PlayerDataManager.Instance;
            if (playerData == null) return;
            
            float currentRange = playerData.FPSCollector.pickupRadius;
            float nextRange = GetNextRangeUpgrade(currentRange);
            int cost = GetRangeUpgradeCost(currentRange);
            
            if (playerData.SpendCurrency(cost, $"Collection Range Upgrade ({currentRange:F1} -> {nextRange:F1})"))
            {
                playerData.FPSCollector.pickupRadius = nextRange;
                Debug.Log($"[BuyZone] ✅ Upgraded collection range to {nextRange:F1} feet!");
            }
            else
            {
                Debug.LogWarning("[BuyZone] ❌ Not enough currency for upgrade!");
            }
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
