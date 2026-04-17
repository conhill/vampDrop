using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Vorkin's Shop — sells DropPuzzle multiplier gate upgrades.
    /// Attach to a trigger collider in the FPS scene.
    /// Delegates all purchases to UpgradeShop (which already has the gate logic).
    /// </summary>
    public class VorkinShop : MonoBehaviour
    {
        private bool playerInZone = false;
        private bool shopOpen = false;
        private GUIStyle guiStyle;
        private Vampire.Player.FPSController fpsController;

        private PlayerDataManager PlayerData => PlayerDataManager.Instance;
        private UpgradeShop Shop => UpgradeShop.Instance;

        private void Start()
        {
            fpsController = FindObjectOfType<Vampire.Player.FPSController>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
                playerInZone = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                playerInZone = false;
                if (shopOpen) CloseShop();
            }
        }

        private void Update()
        {
            if (playerInZone && !shopOpen && Input.GetKeyDown(KeyCode.E))
                OpenShop();

            if (shopOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseShop();
        }

        private void OpenShop()
        {
            shopOpen = true;
            DayNightCycleManager.Instance?.Pause();
            if (fpsController != null) fpsController.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseShop()
        {
            shopOpen = false;
            DayNightCycleManager.Instance?.Resume();
            if (fpsController != null) fpsController.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnGUI()
        {
            if (shopOpen)
            {
                DrawShopUI();
                return;
            }

            if (!playerInZone) return;

            if (guiStyle == null)
            {
                guiStyle = new GUIStyle(GUI.skin.box);
                guiStyle.fontSize = 24;
                guiStyle.alignment = TextAnchor.MiddleCenter;
                guiStyle.normal.textColor = Color.white;
                guiStyle.normal.background = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));
            }

            float w = 420, h = 60;
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height - h - 100, w, h),
                "Press [E] to talk to Vorkin", guiStyle);
        }

        private void DrawShopUI()
        {
            if (PlayerData == null) return;

            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "",
                new GUIStyle { normal = { background = MakeTexture(2, 2, new Color(0, 0, 0, 0.8f)) } });

            float ww = 700, wh = 680;
            float wx = (Screen.width - ww) / 2f;
            float wy = (Screen.height - wh) / 2f;

            GUIStyle windowStyle = new GUIStyle(GUI.skin.box);
            windowStyle.normal.background = MakeTexture(2, 2, new Color(0.1f, 0.05f, 0.15f, 1f));
            GUI.Box(new Rect(wx, wy, ww, wh), "", windowStyle);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 30;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = new Color(1f, 0.6f, 0.2f);
            GUI.Label(new Rect(wx, wy + 15, ww, 45), "Vorkin's Drop Upgrades", titleStyle);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label);
            subStyle.fontSize = 14;
            subStyle.alignment = TextAnchor.MiddleCenter;
            subStyle.normal.textColor = new Color(0.9f, 0.7f, 0.5f);
            GUI.Label(new Rect(wx, wy + 55, ww, 25),
                "\"More multipliers. More chaos. More profit.\"", subStyle);

            GUIStyle currStyle = new GUIStyle(GUI.skin.label);
            currStyle.fontSize = 22;
            currStyle.alignment = TextAnchor.MiddleCenter;
            currStyle.normal.textColor = Color.green;
            GUI.Label(new Rect(wx, wy + 80, ww, 35),
                $"Currency: ${PlayerData.TotalCurrency * 0.01f:F2}", currStyle);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 17;
            labelStyle.normal.textColor = Color.white;

            GUIStyle smallStyle = new GUIStyle(GUI.skin.label);
            smallStyle.fontSize = 14;
            smallStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = 15;
            btnStyle.fontStyle = FontStyle.Bold;

            float y = wy + 125;
            float lx = wx + 30;
            float lw = ww - 60;

            // ─── x2 Gate Chance ───
            float x2 = PlayerData.DropPuzzle.x2GateChance;
            GUI.Label(new Rect(lx, y, lw, 28),
                $"━━━ x2 Multiplier Gate Chance: {x2 * 100f:F0}% (Max 50%) ━━━", labelStyle);
            y += 32;

            if (x2 >= 0.5f)
            {
                GUI.Label(new Rect(lx, y, lw, 24), "✅ MAXED",
                    new GUIStyle(smallStyle) { normal = { textColor = Color.cyan } });
                y += 28;
            }
            else
            {
                int cost = Shop != null ? Shop.GetUpgradeCost("x2Gate") : 0;
                GUI.Label(new Rect(lx, y, lw, 22),
                    $"x2 Gate Chance +5% — ${cost * 0.01f:F2}", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= cost;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Purchase" : "Not Enough $$$", btnStyle))
                {
                    Shop?.BuyX2GateUpgrade();
                }
                GUI.enabled = true;
                y += 50;
            }

            y += 8;

            // ─── x3 Gate Unlock / Upgrade ───
            float x3 = PlayerData.DropPuzzle.x3GateChance;
            GUI.Label(new Rect(lx, y, lw, 28),
                $"━━━ x3 Multiplier Gate Chance: {x3 * 100f:F0}% (Max 35%) ━━━", labelStyle);
            y += 32;

            if (x2 < 0.1f)
            {
                GUI.Label(new Rect(lx, y, lw, 24),
                    "Requires x2 Gate at 10%+ to unlock", smallStyle);
                y += 28;
            }
            else if (x3 == 0f)
            {
                GUI.Label(new Rect(lx, y, lw, 22),
                    "Unlock x3 Gates — $5.00", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= 500;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Unlock x3 Gates" : "Not Enough $$$", btnStyle))
                {
                    Shop?.UnlockX3Gates();
                }
                GUI.enabled = true;
                y += 50;
            }
            else if (x3 >= 0.35f)
            {
                GUI.Label(new Rect(lx, y, lw, 24), "✅ MAXED",
                    new GUIStyle(smallStyle) { normal = { textColor = Color.cyan } });
                y += 28;
            }
            else
            {
                int cost = Shop != null ? Shop.GetUpgradeCost("x3Gate") : 0;
                GUI.Label(new Rect(lx, y, lw, 22),
                    $"x3 Gate Chance +5% — ${cost * 0.01f:F2}", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= cost;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Purchase" : "Not Enough $$$", btnStyle))
                {
                    Shop?.BuyX3GateUpgrade();
                }
                GUI.enabled = true;
                y += 50;
            }

            y += 8;

            // ─── x4 Gate Unlock ───
            float x4 = PlayerData.DropPuzzle.x4GateChance;
            GUI.Label(new Rect(lx, y, lw, 28),
                $"━━━ x4 Multiplier Gate: {(x4 > 0 ? $"{x4 * 100f:F0}% chance" : "LOCKED")} ━━━", labelStyle);
            y += 32;

            if (x3 < 0.15f)
            {
                GUI.Label(new Rect(lx, y, lw, 24),
                    "Requires x3 Gate at 15%+ to unlock", smallStyle);
                y += 28;
            }
            else if (x4 > 0f)
            {
                GUI.Label(new Rect(lx, y, lw, 24), "✅ Unlocked (2% base spawn chance)",
                    new GUIStyle(smallStyle) { normal = { textColor = Color.cyan } });
                y += 28;
            }
            else
            {
                GUI.Label(new Rect(lx, y, lw, 22),
                    "Unlock x4 Gates (2% chance) — $20.00", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= 2000;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Unlock x4 Gates" : "Not Enough $$$", btnStyle))
                {
                    Shop?.UnlockX4Gates();
                }
                GUI.enabled = true;
                y += 50;
            }

            y += 8;

            // ─── Extra Starting Balls ───
            int balls = PlayerData.DropPuzzle.startingBalls;
            GUI.Label(new Rect(lx, y, lw, 28),
                $"━━━ Starting Balls: {balls} (Max 50) ━━━", labelStyle);
            y += 32;

            if (balls >= 50)
            {
                GUI.Label(new Rect(lx, y, lw, 24), "✅ MAXED",
                    new GUIStyle(smallStyle) { normal = { textColor = Color.cyan } });
                y += 28;
            }
            else
            {
                int level = (balls - 20) / 5;
                int cost = 200 + level * 100;
                GUI.Label(new Rect(lx, y, lw, 22),
                    $"+5 Starting Balls — ${cost * 0.01f:F2}", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= cost;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Purchase" : "Not Enough $$$", btnStyle))
                {
                    Shop?.BuyExtraBalls();
                }
                GUI.enabled = true;
                y += 50;
            }

            // Close
            if (GUI.Button(new Rect(wx + ww - 120, wy + wh - 60, 100, 45), "Close [ESC]", btnStyle))
                CloseShop();
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            Texture2D tex = new Texture2D(width, height);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
