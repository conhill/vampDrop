using UnityEngine;
using TMPro;
using Vampire.DropPuzzle;

namespace Vampire.UI
{
    /// <summary>
    /// Displays current skrilla (currency) and total riceballs crafted.
    /// Text is only rebuilt when values change to avoid per-frame string allocations.
    /// </summary>
    public class MoneyUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI skrillaText;
        [SerializeField] private TextMeshProUGUI riceBallsCraftedText;

        private int _lastSkrilla = -1;
        private int _lastRiceBallsCrafted = -1;

        private void Update()
        {
            if (PlayerDataManager.Instance == null) return;

            int skrilla = PlayerDataManager.Instance.TotalCurrency;
            if (skrilla != _lastSkrilla)
            {
                _lastSkrilla = skrilla;
                if (skrillaText != null)
                    skrillaText.text = $"Skrilla: ${_lastSkrilla}";
            }

            int crafted = PlayerDataManager.Instance.TotalRiceBallsCrafted;
            if (crafted != _lastRiceBallsCrafted)
            {
                _lastRiceBallsCrafted = crafted;
                if (riceBallsCraftedText != null)
                    riceBallsCraftedText.text = $"Rice Balls: {_lastRiceBallsCrafted}";
            }
        }
    }
}
