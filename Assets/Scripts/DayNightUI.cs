using UnityEngine;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// UI display for day/night cycle
    /// Shows current time of day and countdown
    /// </summary>
    public class DayNightUI : MonoBehaviour
    {
        [Header("UI References (Optional - will use OnGUI if null)")]
        public TextMeshProUGUI timeOfDayText;
        public TextMeshProUGUI countdownText;
        public GameObject dayNightPanel;
        
        [Header("Display Settings")]
        public bool showCountdown = true;
        public bool showTimeOfDay = true;
        
        [Header("Warning Settings")]
        public Color normalColor = Color.white;
        public Color warningColor = Color.yellow;
        public float warningThreshold = 10f; // Start warning at 10 seconds
        
        private DayNightCycleManager cycleManager;
        private bool isWarning = false;
        
        private void Start()
        {
            cycleManager = DayNightCycleManager.Instance;
            
            if (cycleManager != null)
            {
                // Subscribe to events for better reactivity
                cycleManager.OnDayStart += OnDayStart;
                cycleManager.OnNightStart += OnNightStart;
                cycleManager.OnDaylightWarning += OnDaylightWarning;
            }
            else
            {
                Debug.LogWarning("[DayNightUI] No DayNightCycleManager found!");
            }
        }
        
        private void OnDestroy()
        {
            if (cycleManager != null)
            {
                cycleManager.OnDayStart -= OnDayStart;
                cycleManager.OnNightStart -= OnNightStart;
                cycleManager.OnDaylightWarning -= OnDaylightWarning;
            }
        }
        
        private void Update()
        {
            if (cycleManager == null) return;
            
            // Check for warning state (only during night)
            float timeRemaining = cycleManager.GetTimeRemaining();
            if (cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Night)
            {
                isWarning = timeRemaining <= warningThreshold;
            }
            else
            {
                isWarning = false;
            }
            
            // Update TextMeshPro UI if available
            if (timeOfDayText != null && showTimeOfDay)
            {
                UpdateTimeOfDayDisplay();
            }
            
            if (countdownText != null && showCountdown)
            {
                UpdateCountdownDisplay();
            }
        }
        
        private void UpdateTimeOfDayDisplay()
        {
            string icon = cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day ? "☀️" : "🌙";
            string label = cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day ? "DAY" : "NIGHT";
            
            timeOfDayText.text = $"{icon} {label}";
            
            // Color based on time of day
            if (cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day)
            {
                timeOfDayText.color = new Color(1f, 0.9f, 0.4f); // Warm yellow
            }
            else
            {
                timeOfDayText.color = new Color(0.4f, 0.6f, 1f); // Cool blue
            }
        }
        
        private void UpdateCountdownDisplay()
        {
            string timeStr = cycleManager.GetFormattedTimeRemaining();
            countdownText.text = timeStr;
            
            // Change color when warning
            countdownText.color = isWarning ? warningColor : normalColor;
        }
        
        private void OnDayStart()
        {
            Debug.Log("[DayNightUI] Day started");
        }
        
        private void OnNightStart()
        {
            Debug.Log("[DayNightUI] Night started");
        }
        
        private void OnDaylightWarning()
        {
            Debug.Log("[DayNightUI] Daylight warning!");
        }
        
        /// <summary>
        /// Fallback OnGUI display if no TextMeshPro components assigned
        /// </summary>
        private void OnGUI()
        {
            // Only show OnGUI if no proper UI set up
            if (timeOfDayText != null || countdownText != null) return;
            if (cycleManager == null) return;
            
            GUIStyle timeOfDayStyle = new GUIStyle(GUI.skin.label);
            timeOfDayStyle.fontSize = 24;
            timeOfDayStyle.fontStyle = FontStyle.Bold;
            timeOfDayStyle.alignment = TextAnchor.UpperLeft;
            
            GUIStyle countdownStyle = new GUIStyle(GUI.skin.label);
            countdownStyle.fontSize = 20;
            countdownStyle.alignment = TextAnchor.UpperLeft;
            
            // Time of Day
            if (showTimeOfDay)
            {
                string icon = cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day ? "☀️" : "🌙";
                string label = cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day ? "DAY" : "NIGHT";
                
                if (cycleManager.currentTime == DayNightCycleManager.TimeOfDay.Day)
                {
                    timeOfDayStyle.normal.textColor = new Color(1f, 0.9f, 0.4f); // Warm yellow
                }
                else
                {
                    timeOfDayStyle.normal.textColor = new Color(0.4f, 0.6f, 1f); // Cool blue
                }
                
                GUI.Label(new Rect(10, 10, 200, 40), $"{icon} {label}", timeOfDayStyle);
            }
            
            // Countdown
            if (showCountdown)
            {
                string timeStr = cycleManager.GetFormattedTimeRemaining();
                countdownStyle.normal.textColor = isWarning ? warningColor : normalColor;
                
                GUI.Label(new Rect(10, 40, 200, 30), timeStr, countdownStyle);
            }
        }
    }
}
