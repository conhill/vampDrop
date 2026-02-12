using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire
{
    /// <summary>
    /// UI for FPS stage showing transition prompt
    /// </summary>
    public class FPSStageUI : MonoBehaviour
    {
        [Header("UI References")]
        public TextMeshProUGUI TransitionPromptText;
        public Text TransitionPromptTextLegacy;
        
        private void Start()
        {
            string prompt = "Press F3 to go to Drop Puzzle stage";
            
            if (TransitionPromptText != null)
            {
                TransitionPromptText.text = prompt;
            }
            else if (TransitionPromptTextLegacy != null)
            {
                TransitionPromptTextLegacy.text = prompt;
            }
        }
    }
}
