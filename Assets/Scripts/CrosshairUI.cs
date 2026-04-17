using UnityEngine;

namespace Vampire
{
    /// <summary>
    /// Simple crosshair reticle in screen center
    /// Shows what the player is looking at
    /// </summary>
    public class CrosshairUI : MonoBehaviour
    {
        [Header("Crosshair Settings")]
        [Tooltip("Size of the crosshair")]
        public float CrosshairSize = 20f;
        
        [Tooltip("Thickness of crosshair lines")]
        public float LineThickness = 2f;
        
        [Tooltip("Gap from center")]
        public float CenterGap = 5f;
        
        [Header("Colors")]
        [Tooltip("Normal crosshair color")]
        public Color NormalColor = new Color(1f, 1f, 1f, 0.7f); // White, semi-transparent
        
        [Tooltip("Color when hovering over collectible")]
        public Color HoverColor = new Color(1f, 1f, 0f, 1f); // Yellow, opaque
        
        [Header("Detection")]
        [Tooltip("Show hover color when rice is under crosshair")]
        public bool EnableHoverDetection = true;
        
        // Cached 1×1 textures — allocated once, reused every frame
        private Texture2D _normalTex;
        private Texture2D _hoverTex;

        private void Awake()
        {
            _normalTex = MakeTex(NormalColor);
            _hoverTex  = MakeTex(HoverColor);
        }

        private void OnDestroy()
        {
            Destroy(_normalTex);
            Destroy(_hoverTex);
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            float centerX = Screen.width  / 2f;
            float centerY = Screen.height / 2f;

            bool isHoveringRice = EnableHoverDetection && Rice.RiceHoverHighlightSystem.IsHoveringRice;
            Texture2D tex = isHoveringRice ? _hoverTex : _normalTex;

            // Horizontal lines
            GUI.DrawTexture(new Rect(centerX - CrosshairSize - CenterGap, centerY - LineThickness / 2f, CrosshairSize, LineThickness), tex);
            GUI.DrawTexture(new Rect(centerX + CenterGap,                 centerY - LineThickness / 2f, CrosshairSize, LineThickness), tex);
            // Vertical lines
            GUI.DrawTexture(new Rect(centerX - LineThickness / 2f, centerY - CrosshairSize - CenterGap, LineThickness, CrosshairSize), tex);
            GUI.DrawTexture(new Rect(centerX - LineThickness / 2f, centerY + CenterGap,                 LineThickness, CrosshairSize), tex);
            // Center dot
            GUI.DrawTexture(new Rect(centerX - 1, centerY - 1, 2, 2), tex);
        }
    }
}
