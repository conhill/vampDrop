using UnityEngine;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// UI display for the day/night cycle.
    ///
    /// OPTIMISED:
    ///   - TMP text is only rebuilt when the displayed second or phase changes,
    ///     not every frame. This eliminates ~2 string allocations per frame.
    ///   - OnGUI GUIStyle objects are cached on first use instead of being
    ///     re-allocated every frame.
    ///   - Emoji characters (☀️ 🌙) replaced with ASCII equivalents to avoid
    ///     the TMP glyph-fallback warning that caused extra allocations.
    /// </summary>
    public class DayNightUI : MonoBehaviour
    {
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
                if (timeOfDayText != null && showTimeOfDay)
                {
                    // ASCII alternatives avoid TMP glyph-fallback allocations
                    bool isDay = phase == DayNightCycleManager.TimeOfDay.Day;
                    timeOfDayText.text  = isDay ? "* DAY" : "~ NIGHT";
                    timeOfDayText.color = isDay
                        ? new Color(1f, 0.9f, 0.4f)
                        : new Color(0.4f, 0.6f, 1f);
                }
            }

            if (secondChanged)
            {
                _lastDisplayedSecond = displaySecond;
                if (countdownText != null && showCountdown)
                {
                    // GetFormattedTimeRemaining builds a string — only call when second changes
                    countdownText.text = _cycle.GetFormattedTimeRemaining();
                }
            }

            if (warningChanged)
            {
                _lastWarning = warning;
                _isWarning   = warning;
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
