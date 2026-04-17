using UnityEngine;
using UnityEngine.UIElements;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// UI display for the day/night cycle.
    ///
    /// Supports two display modes:
    ///   1. UI Toolkit — assign a UIDocument and name your UXML elements:
    ///        day-night-icon   (VisualElement, uses background-image)
    ///        day-night-timer  (Label)
    ///   2. Legacy TMP — assign TextMeshProUGUI references directly.
    ///
    /// OPTIMISED:
    ///   - Text/icon only updated when the displayed second or phase changes.
    ///   - OnGUI GUIStyle objects are cached on first use.
    /// </summary>
    public class DayNightUI : MonoBehaviour
    {
        [Header("UI Toolkit (UI Builder)")]
        [Tooltip("UIDocument that contains your UXML HUD. Leave null to use TMP/OnGUI instead.")]
        public UIDocument uiDocument;
        [Tooltip("Sprite shown during Day phase (assigned to 'day-night-icon' element)")]
        public Sprite sunSprite;
        [Tooltip("Sprite shown during Night phase (assigned to 'day-night-icon' element)")]
        public Sprite moonSprite;

        // Cached UI Toolkit element references
        private Label _uitkTimerLabel;
        private VisualElement _uitkIconElement;

        [Header("UI References (Optional — will use OnGUI if null)")]
        public TextMeshProUGUI timeOfDayText;
        public TextMeshProUGUI countdownText;
        public GameObject      dayNightPanel;

        [Header("Display Settings")]
        public bool showCountdown   = true;
        public bool showTimeOfDay   = true;

        [Header("Warning Settings")]
        public Color normalColor    = Color.white;
        public Color warningColor   = Color.yellow;
        public float warningThreshold = 10f;

        private DayNightCycleManager _cycle;
        private bool _isWarning;

        // ── Change-detection cache ────────────────────────────────────────
        private int  _lastDisplayedSecond = -1;
        private DayNightCycleManager.TimeOfDay _lastPhase = (DayNightCycleManager.TimeOfDay)(-1);
        private bool _lastWarning = false;

        // ── OnGUI cached styles (allocated once, not every frame) ─────────
        private GUIStyle _guiTimeStyle;
        private GUIStyle _guiCountdownStyle;
        private bool     _guiStylesBuilt;

        private void Start()
        {
            _cycle = DayNightCycleManager.Instance;
            if (_cycle != null)
            {
                _cycle.OnDayStart        += OnDayStart;
                _cycle.OnNightStart      += OnNightStart;
                _cycle.OnDaylightWarning += OnDaylightWarning;
            }

            // Hook up UI Toolkit elements if a UIDocument is assigned
            if (uiDocument != null)
            {
                var root = uiDocument.rootVisualElement;
                _uitkTimerLabel  = root.Q<Label>("day-night-timer");
                _uitkIconElement = root.Q<VisualElement>("day-night-icon");

                if (_uitkTimerLabel == null)
                    Debug.LogWarning("[DayNightUI] No Label named 'day-night-timer' found in UXML.");
                if (_uitkIconElement == null)
                    Debug.LogWarning("[DayNightUI] No VisualElement named 'day-night-icon' found in UXML.");
            }
        }

        private void OnDestroy()
        {
            if (_cycle != null)
            {
                _cycle.OnDayStart        -= OnDayStart;
                _cycle.OnNightStart      -= OnNightStart;
                _cycle.OnDaylightWarning -= OnDaylightWarning;
            }
        }

        private void Update()
        {
            if (_cycle == null) return;

            float timeRemaining = _cycle.GetTimeRemaining();
            int   displaySecond = Mathf.CeilToInt(timeRemaining);
            var   phase         = _cycle.currentTime;
            bool  warning       = phase == DayNightCycleManager.TimeOfDay.Night
                                  && timeRemaining <= warningThreshold;

            // Only touch TMP when something the player can see has actually changed
            bool phaseChanged   = phase != _lastPhase;
            bool secondChanged  = displaySecond != _lastDisplayedSecond;
            bool warningChanged = warning != _lastWarning;

            if (phaseChanged)
            {
                _lastPhase = phase;
                bool isDay = phase == DayNightCycleManager.TimeOfDay.Day;

                // UI Toolkit icon swap
                if (_uitkIconElement != null)
                {
                    Sprite icon = isDay ? sunSprite : moonSprite;
                    if (icon != null)
                        _uitkIconElement.style.backgroundImage = new StyleBackground(icon);
                }

                // Legacy TMP
                if (timeOfDayText != null && showTimeOfDay)
                {
                    // ASCII alternatives avoid TMP glyph-fallback allocations
                    timeOfDayText.text  = isDay ? "* DAY" : "~ NIGHT";
                    timeOfDayText.color = isDay
                        ? new Color(1f, 0.9f, 0.4f)
                        : new Color(0.4f, 0.6f, 1f);
                }
            }

            if (secondChanged)
            {
                _lastDisplayedSecond = displaySecond;
                string formatted = _cycle.GetFormattedTimeRemaining();

                // UI Toolkit timer label
                if (_uitkTimerLabel != null)
                    _uitkTimerLabel.text = formatted;

                // Legacy TMP
                if (countdownText != null && showCountdown)
                    countdownText.text = formatted;
            }

            if (warningChanged)
            {
                _lastWarning = warning;
                _isWarning   = warning;

                // UI Toolkit warning tint on timer
                if (_uitkTimerLabel != null)
                    _uitkTimerLabel.style.color = warning
                        ? new StyleColor(warningColor)
                        : new StyleColor(normalColor);

                // Legacy TMP
                if (countdownText != null)
                    countdownText.color = warning ? warningColor : normalColor;
            }
        }

        // ── Event callbacks ───────────────────────────────────────────────
        private void OnDayStart()        { _lastPhase = (DayNightCycleManager.TimeOfDay)(-1); } // force refresh
        private void OnNightStart()      { _lastPhase = (DayNightCycleManager.TimeOfDay)(-1); }
        private void OnDaylightWarning() { }

        // ── OnGUI fallback (only active when no TMP refs assigned) ────────
        private void OnGUI()
        {
            if (timeOfDayText != null || countdownText != null) return;
            if (_cycle == null) return;

            // Build styles once — new GUIStyle() every frame was the allocation source
            if (!_guiStylesBuilt)
            {
                _guiTimeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft
                };
                _guiCountdownStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 20,
                    alignment = TextAnchor.UpperLeft
                };
                _guiStylesBuilt = true;
            }

            if (showTimeOfDay)
            {
                bool isDay = _cycle.currentTime == DayNightCycleManager.TimeOfDay.Day;
                _guiTimeStyle.normal.textColor = isDay
                    ? new Color(1f, 0.9f, 0.4f)
                    : new Color(0.4f, 0.6f, 1f);
                GUI.Label(new Rect(10, 10, 200, 40),
                    isDay ? "* DAY" : "~ NIGHT", _guiTimeStyle);
            }

            if (showCountdown)
            {
                _guiCountdownStyle.normal.textColor = _isWarning ? warningColor : normalColor;
                GUI.Label(new Rect(10, 40, 200, 30),
                    _cycle.GetFormattedTimeRemaining(), _guiCountdownStyle);
            }
        }
    }
}
