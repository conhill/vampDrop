using UnityEngine;
using TMPro;

namespace Vampire
{
    /// <summary>
    /// Pulses the continue prompt to draw attention
    /// </summary>
    public class ContinuePromptPulse : MonoBehaviour
    {
        [Header("Pulse Settings")]
        public float pulseSpeed = 2f;
        public float minAlpha = 0.5f;
        public float maxAlpha = 1f;
        
        private TextMeshProUGUI text;
        private float timer = 0f;
        
        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
        }
        
        private void Update()
        {
            if (text == null) return;
            
            // Pulse alpha using sine wave
            timer += Time.deltaTime * pulseSpeed;
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, (Mathf.Sin(timer) + 1f) * 0.5f);
            
            Color color = text.color;
            color.a = alpha;
            text.color = color;
        }
    }
}
