using System.Collections;
using UnityEngine;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Preparation-phase weather roll. Runs right after the gate roll: rolls one
    /// WeatherAnomaly from the player's chances, stashes it in <see cref="CurrentAnomaly"/>
    /// for the drop (and the later "how the town changes" stage), and fires
    /// <see cref="OnWeatherRollComplete"/> so the flow can continue.
    ///
    /// Deliberately lean on visuals — it optionally drives one assigned banner label.
    /// The board/town reaction to the anomaly is intentionally NOT handled here yet;
    /// subscribe to <see cref="OnWeatherRolled"/> to add that later.
    ///
    /// Add to any scene GameObject in DropPuzzle. If absent, DropPuzzleFlowManager
    /// simply skips straight from the gate roll to player control.
    /// </summary>
    public class WeatherRollController : MonoBehaviour
    {
        /// <summary>Fired with the rolled anomaly the instant it is decided.</summary>
        public static event System.Action<WeatherAnomaly> OnWeatherRolled;

        /// <summary>Fired when the reveal finishes and the flow may continue.</summary>
        public static event System.Action OnWeatherRollComplete;

        /// <summary>The anomaly in effect for the current drop. Clear until rolled.</summary>
        public static WeatherAnomaly CurrentAnomaly { get; private set; } = WeatherAnomaly.Clear;

        [Header("Timing")]
        [Tooltip("How long the 'rolling…' shuffle plays before the result locks in.")]
        public float rollDuration = 1.2f;
        [Tooltip("How long the result banner holds before the flow continues.")]
        public float holdDuration = 1.0f;
        [Tooltip("Seconds between flickered options during the roll.")]
        public float shuffleInterval = 0.08f;

        [Header("Reveal UI")]
        [Tooltip("Prize-border weather wheel. If present, it plays the reveal. Leave null to " +
                 "fall back to the banner label / silent roll.")]
        public WeatherWheelUI Wheel;
        [Tooltip("Fallback: if assigned (and no Wheel), shows 'rolling…' then the rolled weather.")]
        public TextMeshProUGUI BannerLabel;

        [Header("Debug")]
        [Tooltip("Force a specific outcome instead of rolling. Clear = roll normally.")]
        public WeatherAnomaly ForceAnomaly = WeatherAnomaly.Clear;

        private static readonly WeatherAnomaly[] _shuffleFaces =
        {
            WeatherAnomaly.Clear, WeatherAnomaly.Radiation,
            WeatherAnomaly.Fallout, WeatherAnomaly.Thunderstorm
        };

        /// <summary>Called by DropPuzzleFlowManager after the gate roll completes.</summary>
        public void StartRoll()
        {
            var dp = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.DropPuzzle : null;
            WeatherAnomaly rolled = ForceAnomaly != WeatherAnomaly.Clear
                ? ForceAnomaly
                : WeatherAnomalyRoller.Roll(dp);

            // Prefer the prize-border wheel; it plays its own reveal and calls back when done.
            var wheel = Wheel != null ? Wheel : FindObjectOfType<WeatherWheelUI>();
            if (wheel != null)
            {
                wheel.Spin(rolled, () => { SetResult(rolled); OnWeatherRollComplete?.Invoke(); });
                return;
            }

            StartCoroutine(RunRoll(rolled)); // banner / silent fallback
        }

        private IEnumerator RunRoll(WeatherAnomaly rolled)
        {
            // Shuffle animation (only if we have a label to show it on).
            if (BannerLabel != null)
            {
                float elapsed = 0f;
                BannerLabel.gameObject.SetActive(true);
                while (elapsed < rollDuration)
                {
                    var face = _shuffleFaces[Random.Range(0, _shuffleFaces.Length)];
                    BannerLabel.text  = WeatherAnomalyRoller.DisplayName(face);
                    BannerLabel.color = WeatherAnomalyRoller.ThemeColor(face);
                    yield return new WaitForSeconds(shuffleInterval);
                    elapsed += shuffleInterval;
                }
                BannerLabel.text  = WeatherAnomalyRoller.DisplayName(rolled);
                BannerLabel.color = WeatherAnomalyRoller.ThemeColor(rolled);
            }

            SetResult(rolled);
            yield return new WaitForSeconds(BannerLabel != null ? holdDuration : 0.1f);
            OnWeatherRollComplete?.Invoke();
        }

        // Lock in the result and broadcast that it was rolled.
        private void SetResult(WeatherAnomaly rolled)
        {
            CurrentAnomaly = rolled;
            Debug.Log($"[WeatherRoll] Tonight's weather: {WeatherAnomalyRoller.DisplayName(rolled)}");
            OnWeatherRolled?.Invoke(rolled);
        }

        /// <summary>Reset to Clear — call between runs if needed.</summary>
        public static void ResetWeather() => CurrentAnomaly = WeatherAnomaly.Clear;
    }
}
