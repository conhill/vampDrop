using UnityEngine;
using System;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Manages day/night cycle for the game
    /// - Rice collection: Day or Night
    /// - Ball dropping: Night only
    /// - Switches every 1 minute (upgradeable)
    /// - Warns 10 seconds before daylight
    /// </summary>
    public class DayNightCycleManager : MonoBehaviour
    {
        public static DayNightCycleManager Instance { get; private set; }
        
        public enum TimeOfDay
        {
            Day,
            Night
        }
        
        [Header("Cycle Settings")]
        [Tooltip("Duration of each phase in seconds (will be upgradeable)")]
        public float phaseDuration = 60f; // 1 minute default
        
        [Tooltip("Warning time before daylight in seconds")]
        public float daylightWarningTime = 10f;
        
        [Header("Current State")]
        public TimeOfDay currentTime = TimeOfDay.Day;
        public float timeRemaining;
        public bool isWarningActive = false;
        public bool isPaused = false;
        
        // Events
        public event Action OnDayStart;
        public event Action OnNightStart;
        public event Action OnDaylightWarning; // 10 seconds before day
        public event Action OnNightEnd; // Immediate night end
        
        private void Awake()
        {
            // Singleton pattern with DontDestroyOnLoad
            if (Instance != null && Instance != this)
            {
                Debug.Log("[DayNightCycle] Destroying duplicate DayNightCycleManager");
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            Debug.Log("[DayNightCycle] Initialized and marked DontDestroyOnLoad");
        }
        
        private void Start()
        {
            // Start with day
            timeRemaining = phaseDuration;
            currentTime = TimeOfDay.Day;
            Debug.Log($"[DayNightCycle] Starting as {currentTime}, duration: {phaseDuration}s, this.enabled={enabled}, gameObject.activeSelf={gameObject.activeSelf}");
        }
        
        private void Update()
        {
            // Skip updates if paused (shop open, completion screen, etc.)
            if (isPaused) return;
            
            // Debug logging to diagnose why timer might not be counting
            if (Time.frameCount % 300 == 0) // Log every ~5 seconds
            {
                Debug.Log($"[DayNightCycle] Update - Time: {currentTime}, Remaining: {timeRemaining:F1}s, DeltaTime: {Time.deltaTime:F3}");
            }
            
            timeRemaining -= Time.deltaTime;
            
            // Check for warning (only during night)
            if (currentTime == TimeOfDay.Night && !isWarningActive)
            {
                if (timeRemaining <= daylightWarningTime)
                {
                    isWarningActive = true;
                    Debug.LogWarning($"[DayNightCycle] ⚠️ DAYLIGHT WARNING! {timeRemaining:F0}s remaining");
                    OnDaylightWarning?.Invoke();
                }
            }
            
            // Phase transition
            if (timeRemaining <= 0f)
            {
                SwitchPhase();
            }
        }
        
        private void SwitchPhase()
        {
            if (currentTime == TimeOfDay.Day)
            {
                // Day -> Night
                currentTime = TimeOfDay.Night;
                timeRemaining = phaseDuration;
                isWarningActive = false;
                
                Debug.Log($"[DayNightCycle] 🌙 NIGHT TIME! Duration: {phaseDuration}s");
                OnNightStart?.Invoke();
            }
            else
            {
                // Night -> Day
                currentTime = TimeOfDay.Day;
                timeRemaining = phaseDuration;
                isWarningActive = false;
                
                Debug.Log($"[DayNightCycle] ☀️ DAY TIME! Duration: {phaseDuration}s");
                OnNightEnd?.Invoke(); // Kick player out of ball drop if active
                OnDayStart?.Invoke();
            }
        }
        
        /// <summary>
        /// Check if player can enter ball drop puzzle (night only)
        /// </summary>
        public bool CanEnterBallDrop()
        {
            return currentTime == TimeOfDay.Night;
        }
        
        /// <summary>
        /// Check if player can collect rice (day or night)
        /// </summary>
        public bool CanCollectRice()
        {
            return true; // Always allowed
        }
        
        /// <summary>
        /// Get time remaining in current phase
        /// </summary>
        public float GetTimeRemaining()
        {
            return timeRemaining;
        }
        
        /// <summary>
        /// Get formatted time string (MM:SS)
        /// </summary>
        public string GetFormattedTimeRemaining()
        {
            int minutes = Mathf.FloorToInt(timeRemaining / 60f);
            int seconds = Mathf.FloorToInt(timeRemaining % 60f);
            return $"{minutes:00}:{seconds:00}";
        }
        
        /// <summary>
        /// Force a phase change (for testing or upgrades)
        /// </summary>
        public void ForcePhase(TimeOfDay targetTime)
        {
            if (currentTime == targetTime) return;
            
            currentTime = targetTime;
            timeRemaining = phaseDuration;
            isWarningActive = false;
            
            Debug.Log($"[DayNightCycle] Forced phase to {targetTime}");
            
            if (targetTime == TimeOfDay.Night)
            {
                OnNightStart?.Invoke();
            }
            else
            {
                OnNightEnd?.Invoke();
                OnDayStart?.Invoke();
            }
        }
        
        /// <summary>
        /// Extend current phase duration (from upgrades)
        /// </summary>
        public void ExtendPhaseDuration(float additionalSeconds)
        {
            phaseDuration += additionalSeconds;
            Debug.Log($"[DayNightCycle] Phase duration extended to {phaseDuration}s");
        }
        
        /// <summary>
        /// Add time to current phase (temporary boost)
        /// </summary>
        public void AddTimeToCurrentPhase(float additionalSeconds)
        {
            timeRemaining += additionalSeconds;
            Debug.Log($"[DayNightCycle] Added {additionalSeconds}s to current phase");
        }
        
        /// <summary>
        /// Pause the day/night cycle (for shop UI, completion screens, etc.)
        /// </summary>
        public void Pause()
        {
            if (!isPaused)
            {
                isPaused = true;
                Debug.Log("[DayNightCycle] ⏸️ PAUSED");
            }
        }
        
        /// <summary>
        /// Resume the day/night cycle
        /// </summary>
        public void Resume()
        {
            if (isPaused)
            {
                isPaused = false;
                Debug.Log("[DayNightCycle] ▶️ RESUMED");
            }
        }
    }
}
