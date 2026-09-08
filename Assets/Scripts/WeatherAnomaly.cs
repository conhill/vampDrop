using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// A weather event rolled during the Preparation phase of a drop. For now this
    /// is just a rolled outcome carried into the drop; how each anomaly reshapes the
    /// town board is a later stage (see WeatherRollController.CurrentAnomaly).
    ///
    /// Ordered least → most dramatic. WeatherAnomalyRoller uses that ordering to pick
    /// the rarest result when multiple independent rolls succeed.
    /// </summary>
    public enum WeatherAnomaly
    {
        Clear        = 0,
        Radiation    = 1,   // lingering glow / passive board effect
        Fallout      = 2,   // nuclear fallout — heavier, board-wide
        Thunderstorm = 3    // storm — most dramatic
    }

    /// <summary>
    /// Rolls a single weather outcome from the player's current anomaly chances.
    /// Each anomaly rolls independently against its own 0-1 chance; if more than one
    /// succeeds, the highest-severity (highest enum value) wins so a lucky run shows
    /// its most dramatic weather. If none succeed the day is Clear.
    ///
    /// Kept as pure logic (no MonoBehaviour) so it can be unit-tested and reused by
    /// a future buy-station preview ("what are my odds tonight?").
    /// </summary>
    public static class WeatherAnomalyRoller
    {
        public static WeatherAnomaly Roll(DropPuzzleUpgrades dp)
        {
            if (dp == null) return WeatherAnomaly.Clear;

            var result = WeatherAnomaly.Clear;
            if (Random.value < dp.radiationChance)    result = Max(result, WeatherAnomaly.Radiation);
            if (Random.value < dp.falloutChance)      result = Max(result, WeatherAnomaly.Fallout);
            if (Random.value < dp.thunderstormChance) result = Max(result, WeatherAnomaly.Thunderstorm);
            return result;
        }

        private static WeatherAnomaly Max(WeatherAnomaly a, WeatherAnomaly b)
            => (int)b > (int)a ? b : a;

        public static string DisplayName(WeatherAnomaly a) => a switch
        {
            WeatherAnomaly.Radiation    => "Radiation",
            WeatherAnomaly.Fallout      => "Nuclear Fallout",
            WeatherAnomaly.Thunderstorm => "Thunderstorm",
            _                           => "Clear Skies"
        };

        /// <summary>Theme color for reveal UI / board tinting.</summary>
        public static Color ThemeColor(WeatherAnomaly a) => a switch
        {
            WeatherAnomaly.Radiation    => new Color(0.55f, 1f,    0.3f),  // sickly green
            WeatherAnomaly.Fallout      => new Color(1f,    0.55f, 0.2f),  // ash orange
            WeatherAnomaly.Thunderstorm => new Color(0.6f,  0.7f,  1f),    // electric blue
            _                           => new Color(0.85f, 0.85f, 0.85f)  // pale sky
        };
    }
}
