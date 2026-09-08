using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace Vampire.DropPuzzle
{
    // Procedurally creates a 3-column HUD Canvas for the DropPuzzle scene.
    //   Left  (20%): ball inventory by quality + current money
    //   Center(60%): transparent — 3D puzzle shows through
    //   Right (20%): exposed as RightPanel for GateRollController content
    //
    // Add this MonoBehaviour to any scene GameObject in DropPuzzle.
    public class DropPuzzleHUD : MonoBehaviour
    {
        public static DropPuzzleHUD Instance { get; private set; }

        public RectTransform LeftPanel  { get; private set; }
        public RectTransform RightPanel { get; private set; }

        private TextMeshProUGUI _moneyLabel;

        // Per-quality riceball count labels (right column of each row)
        private TextMeshProUGUI _fineCount;
        private TextMeshProUGUI _goodCount;
        private TextMeshProUGUI _greatCount;
        private TextMeshProUGUI _excellentCount;
        private TextMeshProUGUI _totalCount;

        // Palette shared with the row symbols so a count matches its bullet colour.
        private const string HexFine      = "#C9C9D4";
        private const string HexGood      = "#5FE08A";
        private const string HexGreat     = "#5FA8FF";
        private const string HexExcellent = "#FFCE3D";
        private static readonly Color AccentGold = new Color(1f, 0.82f, 0.28f);
        private static readonly Color CaptionCol = new Color(0.55f, 0.57f, 0.66f);

        // Money roll-up state — the label shows _displayedCents, which chases the real
        // total so earnings visibly count up instead of snapping. Driven by the
        // PlayerDataManager.OnCurrencyChanged event, not by polling.
        private RectTransform _moneyRect;
        private float _displayedCents;
        private float _targetCents;
        private float _moneyPunch;              // current extra scale from the last earn
        private float _moneyFlash;              // 0..1 color-flash amount, decays to 0
        private static readonly Color MoneyBase  = new Color(0.42f, 1f, 0.55f);
        private static readonly Color MoneyFlash = new Color(1f, 0.95f, 0.5f);

        private PlayerDataManager PDM => PlayerDataManager.Instance;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCanvas();
        }

        private void Start()
        {
            // Disable the entire old UI canvas — disabling individual text fields
            // leaves placeholder "New Text" entries still rendering.
            var oldUI = FindObjectOfType<DropPuzzleUI>(true);
            if (oldUI != null)
            {
                // Disable the legacy DropPuzzleUI component and hide its Canvas by disabling the
                // Canvas COMPONENT (not the GameObject) — this hides the old placeholder UI but
                // keeps sibling MonoBehaviours on that GameObject (e.g. BallDropUI, which owns the
                // completion screen + [E] return) alive and running.
                oldUI.enabled = false;
                var oldCanvas = oldUI.GetComponentInParent<Canvas>();
                if (oldCanvas != null) oldCanvas.enabled = false;
            }

            // Seed the money counter to the current total (no roll-up on first show).
            if (PDM != null)
            {
                _targetCents = _displayedCents = PDM.TotalCurrency;
                if (_moneyLabel != null) _moneyLabel.text = $"${_displayedCents / 100f:F2}";
            }
            PlayerDataManager.OnCurrencyChanged += OnCurrencyChanged;

            StartCoroutine(RefreshLoop());
        }

        private void OnDestroy()
        {
            PlayerDataManager.OnCurrencyChanged -= OnCurrencyChanged;
            if (Instance == this) Instance = null;
        }

        // ── Money roll-up (event-driven) ──────────────────────────────────────

        private void OnCurrencyChanged(int newTotal, int delta, string source)
        {
            _targetCents = newTotal;
            if (delta > 0)
            {
                // Punch + flash on earnings; scale the kick to how big the earn was.
                _moneyPunch = Mathf.Min(0.6f, _moneyPunch + 0.18f + Mathf.Min(0.3f, delta * 0.01f));
                _moneyFlash = 1f;
            }
        }

        private void Update()
        {
            if (_moneyLabel == null) return;

            // Chase the target — fast enough to feel responsive, slow enough to read
            // as a count-up. Snap when within a cent to avoid float dithering.
            if (!Mathf.Approximately(_displayedCents, _targetCents))
            {
                _displayedCents = Mathf.MoveTowards(
                    _displayedCents, _targetCents,
                    Mathf.Max(120f, Mathf.Abs(_targetCents - _displayedCents) * 6f) * Time.deltaTime);
                if (Mathf.Abs(_displayedCents - _targetCents) < 1f) _displayedCents = _targetCents;
                _moneyLabel.text = $"${_displayedCents / 100f:F2}";
            }

            // Decay the punch scale and colour flash back to rest.
            if (_moneyPunch > 0f || _moneyRect.localScale != Vector3.one)
            {
                _moneyPunch = Mathf.MoveTowards(_moneyPunch, 0f, 2.5f * Time.deltaTime);
                float s = 1f + _moneyPunch;
                _moneyRect.localScale = new Vector3(s, s, 1f);
            }
            if (_moneyFlash > 0f)
            {
                _moneyFlash = Mathf.MoveTowards(_moneyFlash, 0f, 3f * Time.deltaTime);
                _moneyLabel.color = Color.Lerp(MoneyBase, MoneyFlash, _moneyFlash);
            }
        }

        // ── Canvas construction ───────────────────────────────────────────────

        private void BuildCanvas()
        {
            var root = new GameObject("DropPuzzle_HUD");
            root.transform.SetParent(transform, false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight   = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            LeftPanel  = MakePanel(root, "LeftPanel",  new Vector2(0f,   0f), new Vector2(0.2f, 1f));
            RightPanel = MakePanel(root, "RightPanel", new Vector2(0.8f, 0f), new Vector2(1f,   1f));

            BuildLeftPanel();
        }

        // Left column: INVENTORY header ▸ SKRILLA ▸ RICEBALLS breakdown.
        // Grouped into sections with divider rules and consistent vertical rhythm
        // so numbers are instantly scannable instead of a wall of text.
        private void BuildLeftPanel()
        {
            // Gold accent stripe down the panel's left edge — frames the column.
            var stripe = MakeRect(LeftPanel, "AccentStripe",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(5f, 0f));
            stripe.gameObject.AddComponent<Image>().color = AccentGold;

            // ── Header ─────────────────────────────────────────────────────────
            var header = MakeLabel(LeftPanel, "Header",
                new Vector2(0f, 0.93f), new Vector2(1f, 0.985f),
                "INVENTORY", 34, FontStyles.Bold, TextAlignmentOptions.Center);
            header.color = AccentGold;
            header.characterSpacing = 8f;
            MakeDivider(0.922f);

            // ── SKRILLA (money) — the hero number ───────────────────────────────
            var moneyCaption = MakeLabel(LeftPanel, "MoneyCaption",
                new Vector2(0f, 0.876f), new Vector2(1f, 0.914f),
                "SKRILLA", 17, FontStyles.Bold, TextAlignmentOptions.Center);
            moneyCaption.color = CaptionCol;
            moneyCaption.characterSpacing = 6f;

            _moneyLabel = MakeLabel(LeftPanel, "Money",
                new Vector2(0f, 0.788f), new Vector2(1f, 0.876f),
                "$0.00", 50, FontStyles.Bold, TextAlignmentOptions.Center);
            _moneyLabel.color = MoneyBase;
            _moneyRect = _moneyLabel.rectTransform;
            MakeDivider(0.775f);

            // ── RICEBALLS breakdown ─────────────────────────────────────────────
            var ballsCaption = MakeLabel(LeftPanel, "BallsCaption",
                new Vector2(0f, 0.728f), new Vector2(1f, 0.766f),
                "RICEBALLS", 17, FontStyles.Bold, TextAlignmentOptions.Center);
            ballsCaption.color = CaptionCol;
            ballsCaption.characterSpacing = 6f;

            // One tidy row per quality: coloured symbol + name (left), count (right).
            _fineCount      = MakeRow("Fine",      0.638f, 0.700f, "●", HexFine,      "Fine");
            _goodCount      = MakeRow("Good",      0.568f, 0.630f, "●", HexGood,      "Good");
            _greatCount     = MakeRow("Great",     0.498f, 0.560f, "◆", HexGreat,     "Great");
            _excellentCount = MakeRow("Excellent", 0.428f, 0.490f, "★", HexExcellent, "Excellent");

            MakeDivider(0.408f);
            _totalCount = MakeRow("Total", 0.338f, 0.400f, "", "#FFFFFF", "TOTAL", bold: true, size: 27);
        }

        // Builds a left name (symbol + text) + right-aligned count; returns the count label.
        private TextMeshProUGUI MakeRow(string id, float yMin, float yMax,
                                        string symbol, string hex, string label,
                                        bool bold = false, int size = 24)
        {
            var style = bold ? FontStyles.Bold : FontStyles.Normal;
            string prefix = string.IsNullOrEmpty(symbol) ? "" : $"<color={hex}>{symbol}</color>  ";
            var name = MakeLabel(LeftPanel, $"Row_{id}",
                new Vector2(0f, yMin), new Vector2(0.66f, yMax),
                $"{prefix}{label}", size, style, TextAlignmentOptions.MidlineLeft);
            name.color = bold ? Color.white : new Color(0.82f, 0.83f, 0.88f);

            var count = MakeLabel(LeftPanel, $"Count_{id}",
                new Vector2(0.5f, yMin), new Vector2(1f, yMax),
                "0", size, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            count.color = bold ? AccentGold : ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
            return count;
        }

        // Thin horizontal rule at normalized height y, inset from the panel edges.
        private void MakeDivider(float y)
        {
            var rt = MakeRect(LeftPanel, "Divider",
                new Vector2(0.08f, y), new Vector2(0.92f, y),
                new Vector2(0f, -1f), new Vector2(0f, 1f));
            rt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        }

        private RectTransform MakeRect(RectTransform parent, string name,
                                       Vector2 anchorMin, Vector2 anchorMax,
                                       Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private RectTransform MakePanel(GameObject parent, string name,
                                        Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin  = anchorMin;
            rt.anchorMax  = anchorMax;
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.05f, 0.06f, 0.10f, 0.86f);
            return rt;
        }

        private TextMeshProUGUI MakeLabel(RectTransform parent, string name,
                                          Vector2 anchorMin, Vector2 anchorMax,
                                          string text, float size,
                                          FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(18f, 6f);
            rt.offsetMax = new Vector2(-18f, -6f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = size;
            tmp.fontStyle = style;
            tmp.color     = Color.white;
            tmp.alignment = align;
            return tmp;
        }

        // ── Data refresh ──────────────────────────────────────────────────────

        private IEnumerator RefreshLoop()
        {
            while (true)
            {
                Refresh();
                yield return new WaitForSeconds(0.25f);
            }
        }

        public void Refresh()
        {
            if (PDM == null) return;

            // Money is now driven by OnCurrencyChanged + the roll-up in Update() — not
            // polled here, so the count-up animation isn't overwritten.

            var inv = PDM.Inventory;
            if (_fineCount      != null) _fineCount.text      = inv.FineBalls.ToString();
            if (_goodCount      != null) _goodCount.text      = inv.GoodBalls.ToString();
            if (_greatCount     != null) _greatCount.text     = inv.GreatBalls.ToString();
            if (_excellentCount != null) _excellentCount.text = inv.ExcellentBalls.ToString();
            if (_totalCount     != null) _totalCount.text     = inv.GetTotalBalls().ToString();
        }
    }
}
