using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Snerd's Shop — sells Helper Workers and helper upgrades.
    /// Attach to a trigger collider in the FPS scene.
    /// Assign helperWorkerPrefab (must have HelperWorker + NavMeshAgent).
    /// Max 2 helpers purchasable total.
    /// Workers auto-respawn on scene load for already-purchased count.
    /// </summary>
    public class SnerdShop : MonoBehaviour
    {
        public const int MAX_HELPERS = 2;

        [Header("Helper Spawning")]
        [Tooltip("Prefab with HelperWorker component and NavMeshAgent")]
        public GameObject helperWorkerPrefab;

        [Tooltip("Where workers spawn. Falls back to random near shop if empty.")]
        public Transform[] spawnPoints;

        [Header("Upgrade Costs")]
        public int helperCost = 0;
        public int speedUpgradeCost = 200;
        public int efficiencyUpgradeCost = 250;
        public int maxSpeedLevel = 5;
        public int maxEfficiencyLevel = 5;

        // Runtime
        private bool playerInZone = false;
        private bool shopOpen = false;
        private GUIStyle guiStyle;
        private Vampire.Player.FPSController fpsController;
        private readonly List<Helpers.HelperWorker> activeWorkers = new List<Helpers.HelperWorker>();

        private PlayerDataManager PlayerData => PlayerDataManager.Instance;

        private int SpeedLevel => Mathf.RoundToInt((PlayerData.Helpers.movementSpeed - 1f) / 0.2f);
        private int EfficiencyLevel => Mathf.RoundToInt((PlayerData.Helpers.ricePerSecond - 1f) / 0.5f);

        private void Start()
        {
            fpsController = FindObjectOfType<Vampire.Player.FPSController>();
            RespawnPurchasedWorkers();
        }

        /// <summary>
        /// On scene load, re-instantiate workers for helpers already bought.
        /// </summary>
        private void RespawnPurchasedWorkers()
        {
            if (helperWorkerPrefab == null) return;

            int count = Mathf.Min(PlayerData != null ? PlayerData.Helpers.ownedGoblins : 0, MAX_HELPERS);
            for (int i = 0; i < count; i++)
            {
                SpawnWorker(i);
            }
        }

        private void SpawnWorker(int workerIndex)
        {
            if (helperWorkerPrefab == null) return;

            Vector3 spawnPos = GetSpawnPosition(workerIndex);
            GameObject go = Instantiate(helperWorkerPrefab, spawnPos, Quaternion.identity);
            go.name = $"SnerdWorker_{workerIndex}";

            var worker = go.GetComponent<Helpers.HelperWorker>();
            if (worker != null)
            {
                worker.workerIndex = workerIndex;
                // Name is assigned in HelperWorker.Start() from the pool if not already set
                activeWorkers.Add(worker);

                // Register with HUD
                if (Helpers.HelperManagerUI.Instance != null)
                    Helpers.HelperManagerUI.Instance.RegisterWorker(worker);
            }
        }

        private Vector3 GetSpawnPosition(int index)
        {
            if (spawnPoints != null && index < spawnPoints.Length && spawnPoints[index] != null)
                return spawnPoints[index].position;

            // Fallback: random offset near shop
            Vector3 offset = UnityEngine.Random.insideUnitSphere * 4f;
            offset.y = 0f;
            Vector3 candidate = transform.position + offset;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                return hit.position;

            return transform.position;
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

            float w = 400, h = 60;
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height - h - 100, w, h),
                "Press [E] to talk to Snerd", guiStyle);
        }

        private void DrawShopUI()
        {
            if (PlayerData == null) return;

            // Dim overlay
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "",
                new GUIStyle { normal = { background = MakeTexture(2, 2, new Color(0, 0, 0, 0.8f)) } });

            float ww = 680, wh = 580;
            float wx = (Screen.width - ww) / 2f;
            float wy = (Screen.height - wh) / 2f;

            GUIStyle windowStyle = new GUIStyle(GUI.skin.box);
            windowStyle.normal.background = MakeTexture(2, 2, new Color(0.08f, 0.12f, 0.08f, 1f));
            GUI.Box(new Rect(wx, wy, ww, wh), "", windowStyle);

            // Title
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 30;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = new Color(0.4f, 1f, 0.4f);
            GUI.Label(new Rect(wx, wy + 15, ww, 45), "Snerd's Workers & Upgrades", titleStyle);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label);
            subStyle.fontSize = 14;
            subStyle.alignment = TextAnchor.MiddleCenter;
            subStyle.normal.textColor = new Color(0.7f, 0.9f, 0.7f);
            GUI.Label(new Rect(wx, wy + 55, ww, 25),
                "\"My workers collect rice so you don't have to. Mostly.\"", subStyle);

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

            // ─── Hire Worker ───
            int owned = PlayerData.Helpers.ownedGoblins;
            GUI.Label(new Rect(lx, y, lw, 28),
                $"━━━ Workers: {owned} / {MAX_HELPERS} (1 rice/sec each) ━━━", labelStyle);
            y += 32;

            if (owned >= MAX_HELPERS)
            {
                GUI.Label(new Rect(lx, y, lw, 24), "✅ All workers hired",
                    new GUIStyle(smallStyle) { normal = { textColor = Color.cyan } });
                y += 28;
            }
            else
            {
                GUI.Label(new Rect(lx, y, lw, 22),
                    $"Hire Worker — ${helperCost * 0.01f:F2}  (roams and picks up rice for you)", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= helperCost;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Hire Worker" : "Not Enough $$$", btnStyle))
                {
                    PurchaseWorker();
                }
                GUI.enabled = true;
                y += 50;
            }

            y += 8;

            // ─── Speed Upgrade ───
            int sLevel = SpeedLevel;
            GUI.Label(new Rect(lx, y, lw, 28),
                $"━━━ Worker Speed: {PlayerData.Helpers.movementSpeed:F1}x  (Level {sLevel}/{maxSpeedLevel}) ━━━", labelStyle);
            y += 32;

            if (sLevel >= maxSpeedLevel)
            {
                GUI.Label(new Rect(lx, y, lw, 24), "✅ MAXED",
                    new GUIStyle(smallStyle) { normal = { textColor = Color.cyan } });
                y += 28;
            }
            else
            {
                int sCost = speedUpgradeCost + sLevel * 100;
                GUI.Label(new Rect(lx, y, lw, 22),
                    $"Speed +20% — ${sCost * 0.01f:F2}", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= sCost;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Purchase" : "Not Enough $$$", btnStyle))
                {
                    BuySpeedUpgrade(sCost);
                }
                GUI.enabled = true;
                y += 50;
            }

            y += 8;

            // ─── Efficiency Upgrade ───
            int eLevel = EfficiencyLevel;
            GUI.Label(new Rect(lx, y, lw, 28),
                $"━━━ Collection Rate: {PlayerData.Helpers.ricePerSecond:F1}/sec  (Level {eLevel}/{maxEfficiencyLevel}) ━━━", labelStyle);
            y += 32;

            if (eLevel >= maxEfficiencyLevel)
            {
                GUI.Label(new Rect(lx, y, lw, 24), "✅ MAXED",
                    new GUIStyle(smallStyle) { normal = { textColor = Color.cyan } });
                y += 28;
            }
            else
            {
                int eCost = efficiencyUpgradeCost + eLevel * 125;
                GUI.Label(new Rect(lx, y, lw, 22),
                    $"Collection Rate +0.5/sec — ${eCost * 0.01f:F2}", smallStyle);
                y += 26;

                bool canAfford = PlayerData.TotalCurrency >= eCost;
                GUI.enabled = canAfford;
                if (GUI.Button(new Rect(lx + 130, y, 200, 38),
                    canAfford ? "Purchase" : "Not Enough $$$", btnStyle))
                {
                    BuyEfficiencyUpgrade(eCost);
                }
                GUI.enabled = true;
                y += 50;
            }

            // Close button
            if (GUI.Button(new Rect(wx + ww - 120, wy + wh - 60, 100, 45),
                "Close [ESC]", btnStyle))
            {
                CloseShop();
            }
        }

        private void PurchaseWorker()
        {
            if (PlayerData == null) return;
            if (PlayerData.Helpers.ownedGoblins >= MAX_HELPERS) return;

            if (PlayerData.SpendCurrency(helperCost, "Hired Snerd Worker"))
            {
                int newIndex = PlayerData.Helpers.ownedGoblins;
                PlayerData.Helpers.ownedGoblins++;
                SpawnWorker(newIndex);
            }
        }

        private void BuySpeedUpgrade(int cost)
        {
            if (PlayerData == null) return;
            if (PlayerData.SpendCurrency(cost, "Worker Speed Upgrade"))
            {
                PlayerData.Helpers.movementSpeed += 0.2f;
                foreach (var w in activeWorkers)
                    if (w != null) w.RefreshStats();
            }
        }

        private void BuyEfficiencyUpgrade(int cost)
        {
            if (PlayerData == null) return;
            if (PlayerData.SpendCurrency(cost, "Worker Efficiency Upgrade"))
            {
                PlayerData.Helpers.ricePerSecond += 0.5f;
                // Workers read ricePerSecond dynamically; no refresh needed
            }
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
