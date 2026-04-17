using UnityEngine;
using System.Collections.Generic;
using Vampire.Rice;
using Vampire.DropPuzzle;

namespace Vampire.Helpers
{
    /// <summary>
    /// Tab-key worker management screen.
    /// Shows each HelperWorker with status, zone assignment, and controls.
    /// Opens/closes with Tab; ESC also closes when the panel is active.
    /// Workers self-register via RegisterWorker().
    /// </summary>
    public class HelperManagerUI : MonoBehaviour
    {
        public static HelperManagerUI Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("Key to open / close this screen.")]
        public KeyCode toggleKey = KeyCode.Tab;

        // ── State ─────────────────────────────────────────────────────────────
        private bool panelOpen        = false;
        private bool inUIMode         = false;
        private int  zonePickerFor    = -1;   // which worker's zone picker is expanded
        private Vector2 scrollPos     = Vector2.zero;

        public bool InUIMode => inUIMode;

        // ── Data ──────────────────────────────────────────────────────────────
        private readonly List<HelperWorker>    workers  = new List<HelperWorker>();
        private          RiceSpawnPointAuthoring[] zones = new RiceSpawnPointAuthoring[0];
        private          Vampire.Player.FPSController fps;

        // ── Layout constants ──────────────────────────────────────────────────
        private const float PanelW    = 680f;
        private const float HeaderH   = 48f;
        private const float StatsH    = 36f;
        private const float CardH     = 96f;   // height of one worker card (no picker)
        private const float PickerRowH = 32f;
        private const float Pad       = 12f;
        private const float MaxPanelH = 560f;

        // ── Styles ────────────────────────────────────────────────────────────
        private GUIStyle _bg, _cardBg, _cardBgWorking, _cardBgPaused, _cardBgIdle;
        private GUIStyle _header, _workerName, _statusBadge, _small, _label;
        private GUIStyle _btn, _btnDanger, _btnAssign, _zoneBtn, _zoneBtnActive;
        private bool     _stylesBuilt;

        // ── Textures cached for status badge colours ───────────────────────────
        private Texture2D _texWorking, _texPaused, _texIdle, _texDark, _texCard,
                          _texCardWorking, _texCardPaused, _texCardIdle, _texZoneActive;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            fps = FindObjectOfType<Vampire.Player.FPSController>();
            RefreshZones();
        }

        // ── Worker registration (called by SnerdShop) ──────────────────────────

        public void RegisterWorker(HelperWorker w)
        {
            if (!workers.Contains(w))
            {
                workers.Add(w);
                w.OnZoneDepleted += OnZoneDepleted;
            }
        }

        public void UnregisterWorker(HelperWorker w)
        {
            workers.Remove(w);
            w.OnZoneDepleted -= OnZoneDepleted;
            if (zonePickerFor >= workers.Count) zonePickerFor = -1;
        }

        public void RefreshZones()
        {
            zones = FindObjectsOfType<RiceSpawnPointAuthoring>();
        }

        private void OnZoneDepleted(HelperWorker w)
        {
            Debug.Log($"[Workers] {w.WorkerName}'s zone depleted — reassign in the Workers tab.");
        }

        // ── UI Mode (cursor lock / FPS controller) ────────────────────────────

        private void SetUIMode(bool on)
        {
            inUIMode = on;
            if (on)
            {
                if (fps != null) fps.enabled = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
                DropPuzzle.DayNightCycleManager.Instance?.Pause();
            }
            else
            {
                panelOpen     = false;
                zonePickerFor = -1;
                scrollPos     = Vector2.zero;
                if (fps != null) fps.enabled = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
                DropPuzzle.DayNightCycleManager.Instance?.Resume();
            }
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // Key handling via Event.current — bypasses UI Toolkit focus.
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape && inUIMode)
                {
                    SetUIMode(false);
                    Event.current.Use();
                    return;
                }
                if (Event.current.keyCode == toggleKey)
                {
                    bool escOpen = Vampire.EscapeMenuManager.Instance != null
                                && Vampire.EscapeMenuManager.Instance.IsOpen;
                    if (!escOpen)
                    {
                        if (panelOpen) SetUIMode(false);
                        else { panelOpen = true; SetUIMode(true); }
                        Event.current.Use();
                        return;
                    }
                }
            }

            if (!panelOpen) return;

            BuildStyles();

            // ── Full-screen dim ──────────────────────────────────────────────
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // ── Calculate panel height ───────────────────────────────────────
            float contentH = workers.Count == 0
                ? 60f
                : CalculateContentHeight();

            float panelH = Mathf.Min(HeaderH + StatsH + contentH + Pad * 2f, MaxPanelH);
            float panelX = (Screen.width  - PanelW) * 0.5f;
            float panelY = (Screen.height - panelH) * 0.5f;

            // ── Panel background ─────────────────────────────────────────────
            GUI.Box(new Rect(panelX, panelY, PanelW, panelH), GUIContent.none, _bg);

            float y = panelY + Pad;

            // ── Header ───────────────────────────────────────────────────────
            DrawHeader(panelX, ref y);

            // ── Global stats row ─────────────────────────────────────────────
            DrawStatsRow(panelX, ref y);

            // ── Divider ──────────────────────────────────────────────────────
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(panelX + Pad, y, PanelW - Pad * 2f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += 6f;

            // ── Worker cards (scrollable if many workers) ────────────────────
            float scrollAreaH = panelH - (y - panelY) - Pad;
            Rect  scrollView  = new Rect(panelX + Pad, y, PanelW - Pad * 2f, scrollAreaH);
            float innerH      = workers.Count == 0 ? 60f : CalculateContentHeight();
            Rect  inner       = new Rect(0, 0, PanelW - Pad * 2f - 16f, innerH);

            scrollPos = GUI.BeginScrollView(scrollView, scrollPos, inner);
            float iy = 0f;

            if (workers.Count == 0)
            {
                GUI.Label(new Rect(0, iy, inner.width, 40f),
                    "No workers deployed yet.  Purchase workers from the shop.", _small);
            }
            else
            {
                for (int i = 0; i < workers.Count; i++)
                {
                    var w = workers[i];
                    if (w == null) continue;
                    DrawWorkerCard(w, i, inner.width, ref iy);
                }
            }

            GUI.EndScrollView();
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void DrawHeader(float panelX, ref float y)
        {
            float closeW = 110f;

            GUI.Label(new Rect(panelX + Pad, y, PanelW - closeW - Pad * 2f, HeaderH - 4f),
                $"WORKERS  ({workers.Count})", _header);

            if (GUI.Button(new Rect(panelX + PanelW - closeW - Pad, y + 6f, closeW, 28f),
                "Close  [Esc]", _btn))
                SetUIMode(false);

            y += HeaderH;
        }

        // ── Global stats row ──────────────────────────────────────────────────

        private void DrawStatsRow(float panelX, ref float y)
        {
            var pd = PlayerDataManager.Instance;
            if (pd == null) { y += StatsH; return; }

            float speed     = pd.Helpers.movementSpeed;
            float rate      = pd.Helpers.ricePerSecond;
            int   deployed  = pd.Helpers.GetDeployedHelperCount();
            int   maxH      = pd.Helpers.GetMaxHelpers();

            string stats = $"Speed  {speed:F1}×      Rice / sec  {rate:F1}      Deployed  {deployed} / {maxH}";
            GUI.Label(new Rect(panelX + Pad, y, PanelW - Pad * 2f, StatsH), stats, _label);
            y += StatsH;
        }

        // ── Worker card ───────────────────────────────────────────────────────

        private void DrawWorkerCard(HelperWorker w, int idx, float width, ref float y)
        {
            bool pickerOpen = zonePickerFor == idx;
            float pickerH   = pickerOpen ? CalculatePickerHeight() : 0f;
            float cardTotal = CardH + pickerH + 6f;

            // Card background (colour by status)
            GUIStyle cardStyle = w.IsPaused ? _cardBgPaused
                               : w.AssignedZone != null ? _cardBgWorking
                               : _cardBgIdle;
            GUI.Box(new Rect(0, y, width, CardH - 4f), GUIContent.none, cardStyle);

            float cx = Pad;
            float cy = y + 8f;

            // ── Worker name ──────────────────────────────────────────────────
            GUI.Label(new Rect(cx, cy, width * 0.55f, 26f), w.WorkerName, _workerName);

            // ── Status badge ─────────────────────────────────────────────────
            string statusText;
            GUIStyle badgeStyle = _statusBadge;
            if (w.IsPaused)           { statusText = "  PAUSED";  GUI.color = new Color(1f, 0.8f, 0.2f); }
            else if (w.AssignedZone != null) { statusText = "● WORKING"; GUI.color = new Color(0.3f, 1f, 0.4f); }
            else                      { statusText = "○  IDLE";   GUI.color = new Color(0.6f, 0.6f, 0.6f); }

            GUI.Label(new Rect(width - 110f, cy, 108f, 26f), statusText, _statusBadge);
            GUI.color = Color.white;

            cy += 26f;

            // ── Zone label ───────────────────────────────────────────────────
            string zoneLine = w.AssignedZone != null
                ? $"Zone:  {w.AssignedZone.ZoneName}"
                : "Zone:  —  unassigned";
            GUI.Label(new Rect(cx, cy, width - Pad, 20f), zoneLine, _small);
            cy += 24f;

            // ── Buttons ───────────────────────────────────────────────────────
            float bW = 150f, bH = 26f, bGap = 8f;
            float bx = cx;

            // Assign zone toggle
            string assignLabel = pickerOpen ? "▲ Cancel" : "▼ Assign Zone";
            if (GUI.Button(new Rect(bx, cy, bW, bH), assignLabel, _btnAssign))
            {
                zonePickerFor = pickerOpen ? -1 : idx;
                scrollPos = Vector2.zero;
            }
            bx += bW + bGap;

            // Pause / Resume
            if (GUI.Button(new Rect(bx, cy, 90f, bH), w.IsPaused ? "Resume" : "Pause", _btn))
                w.SetPaused(!w.IsPaused);
            bx += 90f + bGap;

            // Clear zone
            GUI.enabled = w.AssignedZone != null;
            if (GUI.Button(new Rect(bx, cy, 100f, bH), "Clear Zone", _btnDanger))
            {
                w.UnassignZone();
                if (zonePickerFor == idx) zonePickerFor = -1;
            }
            GUI.enabled = true;

            y += CardH + 2f;

            // ── Inline zone picker ────────────────────────────────────────────
            if (pickerOpen)
            {
                DrawZonePicker(w, idx, width, ref y);
                y += 4f;
            }
        }

        // ── Zone picker ───────────────────────────────────────────────────────

        private void DrawZonePicker(HelperWorker w, int idx, float width, ref float y)
        {
            // Picker background
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            float pickerH = CalculatePickerHeight();
            GUI.DrawTexture(new Rect(0, y, width, pickerH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float cy = y + 6f;

            if (zones.Length == 0)
            {
                GUI.Label(new Rect(Pad, cy, width - Pad * 2f, 24f),
                    "No zones found in scene (add RiceSpawnPointAuthoring components).", _small);
                y += pickerH;
                return;
            }

            GUI.Label(new Rect(Pad, cy, 200f, 22f), "Select a zone:", _label);
            cy += 26f;

            float btnW = (width - Pad * 2f - 8f) / 2f;
            int col = 0;

            foreach (var zone in zones)
            {
                if (zone == null) continue;

                bool isActive = w.AssignedZone == zone;
                float bx = Pad + col * (btnW + 8f);

                GUIStyle zs = isActive ? _zoneBtnActive : _zoneBtn;
                string zlabel = isActive ? $"✓  {zone.ZoneName}" : zone.ZoneName;

                if (GUI.Button(new Rect(bx, cy, btnW, PickerRowH - 4f), zlabel, zs))
                {
                    if (isActive)
                        w.UnassignZone();
                    else
                        w.AssignZone(zone);
                    zonePickerFor = -1;   // close picker after assigning
                }

                col++;
                if (col >= 2) { col = 0; cy += PickerRowH; }
            }

            y += pickerH;
        }

        // ── Height helpers ────────────────────────────────────────────────────

        private float CalculateContentHeight()
        {
            float h = 0f;
            for (int i = 0; i < workers.Count; i++)
            {
                h += CardH + 2f;
                if (zonePickerFor == i) h += CalculatePickerHeight() + 4f;
            }
            return h;
        }

        private float CalculatePickerHeight()
        {
            if (zones.Length == 0) return 60f;
            int rows = Mathf.CeilToInt(zones.Length / 2f);
            return 32f + rows * PickerRowH + 8f;
        }

        // ── Style builder ─────────────────────────────────────────────────────

        private void BuildStyles()
        {
            if (_stylesBuilt) return;

            _texDark        = MakeTex(new Color(0.06f, 0.06f, 0.10f, 0.97f));
            _texCard        = MakeTex(new Color(0.12f, 0.12f, 0.18f, 1f));
            _texCardWorking = MakeTex(new Color(0.06f, 0.18f, 0.10f, 1f));
            _texCardPaused  = MakeTex(new Color(0.20f, 0.17f, 0.04f, 1f));
            _texCardIdle    = MakeTex(new Color(0.12f, 0.12f, 0.18f, 1f));
            _texWorking     = MakeTex(new Color(0.15f, 0.55f, 0.20f, 1f));
            _texPaused      = MakeTex(new Color(0.55f, 0.40f, 0.05f, 1f));
            _texIdle        = MakeTex(new Color(0.25f, 0.25f, 0.32f, 1f));
            _texZoneActive  = MakeTex(new Color(0.10f, 0.35f, 0.15f, 1f));

            _bg = new GUIStyle(GUI.skin.box);
            _bg.normal.background = _texDark;

            _cardBg = new GUIStyle(GUI.skin.box);
            _cardBg.normal.background = _texCard;

            _cardBgWorking = new GUIStyle(GUI.skin.box);
            _cardBgWorking.normal.background = _texCardWorking;

            _cardBgPaused = new GUIStyle(GUI.skin.box);
            _cardBgPaused.normal.background = _texCardPaused;

            _cardBgIdle = new GUIStyle(GUI.skin.box);
            _cardBgIdle.normal.background = _texCardIdle;

            _header = new GUIStyle(GUI.skin.label)
                { fontSize = 22, fontStyle = FontStyle.Bold };
            _header.normal.textColor = new Color(0.5f, 1f, 0.55f);

            _workerName = new GUIStyle(GUI.skin.label)
                { fontSize = 16, fontStyle = FontStyle.Bold };
            _workerName.normal.textColor = Color.white;

            _statusBadge = new GUIStyle(GUI.skin.label)
                { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _statusBadge.normal.textColor = Color.white;

            _small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _small.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

            _label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _label.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            _btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };

            _btnDanger = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            _btnDanger.normal.background = MakeTex(new Color(0.45f, 0.1f, 0.1f));
            _btnDanger.hover.background  = MakeTex(new Color(0.65f, 0.15f, 0.15f));
            _btnDanger.normal.textColor  = Color.white;
            _btnDanger.hover.textColor   = Color.white;

            _btnAssign = new GUIStyle(GUI.skin.button)
                { fontSize = 13, fontStyle = FontStyle.Bold };
            _btnAssign.normal.background = MakeTex(new Color(0.12f, 0.28f, 0.45f));
            _btnAssign.hover.background  = MakeTex(new Color(0.18f, 0.40f, 0.62f));
            _btnAssign.normal.textColor  = Color.white;
            _btnAssign.hover.textColor   = Color.white;

            _zoneBtn = new GUIStyle(GUI.skin.button) { fontSize = 13 };

            _zoneBtnActive = new GUIStyle(GUI.skin.button)
                { fontSize = 13, fontStyle = FontStyle.Bold };
            _zoneBtnActive.normal.background = _texZoneActive;
            _zoneBtnActive.hover.background  = _texWorking;
            _zoneBtnActive.normal.textColor  = new Color(0.5f, 1f, 0.6f);
            _zoneBtnActive.hover.textColor   = Color.white;

            _stylesBuilt = true;
        }

        private Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(2, 2);
            t.SetPixels(new[] { c, c, c, c });
            t.Apply();
            return t;
        }
    }
}
