using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// UI Display for Drop Puzzle game state
    /// Shows rice balls remaining and currency earned
    /// </summary>
    public class DropPuzzleUI : MonoBehaviour
    {
        [Header("UI References")]
        public TextMeshProUGUI RiceCountText;
        public TextMeshProUGUI CurrencyText;
        public TextMeshProUGUI InstructionText;
        
        [Header("Fallback (legacy Text)")]
        public Text RiceCountTextLegacy;
        public Text CurrencyTextLegacy;
        public Text InstructionTextLegacy;
        
        private void Start()
        {
            if (InstructionText != null)
            {
                InstructionText.text = "Press SPACE to drop rice balls";
            }
            else if (InstructionTextLegacy != null)
            {
                InstructionTextLegacy.text = "Press SPACE to drop rice balls";
            }
        }
        
        private void Update()
        {
            if (DropPuzzleManager.Instance == null) return;
            
            // Update rice count
            string riceText = $"Rice Balls: {DropPuzzleManager.Instance.RiceBallsAvailable}";
            if (RiceCountText != null)
            {
                RiceCountText.text = riceText;
            }
            else if (RiceCountTextLegacy != null)
            {
                RiceCountTextLegacy.text = riceText;
            }
            
            // Update currency
            string currencyText = $"Currency: {DropPuzzleManager.Instance.Currency}¢";
            if (CurrencyText != null)
            {
                CurrencyText.text = currencyText;
            }
            else if (CurrencyTextLegacy != null)
            {
                CurrencyTextLegacy.text = currencyText;
            }
        }
    }
}
