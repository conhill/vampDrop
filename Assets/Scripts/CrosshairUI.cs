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
        
        private void Update()
        {
            if (!EnableHoverDetection)
            {
                return;
            }
            
            // Check the static flag from RiceHoverHighlightSystem
            // (No need for raycast - the ECS system handles detection)
        }
        
        private void OnGUI()
        {
            // Get screen center
            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;
            
            // Choose color based on hover state (check ECS hover system)
            bool isHoveringRice = EnableHoverDetection && Rice.RiceHoverHighlightSystem.IsHoveringRice;
            Color crosshairColor = isHoveringRice ? HoverColor : NormalColor;
            
            // Create texture for drawing
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, crosshairColor);
            texture.Apply();
            
            // Draw horizontal line (left)
            GUI.DrawTexture(
                new Rect(centerX - CrosshairSize - CenterGap, centerY - LineThickness / 2f, CrosshairSize, LineThickness),
                texture
            );
            
            // Draw horizontal line (right)
            GUI.DrawTexture(
                new Rect(centerX + CenterGap, centerY - LineThickness / 2f, CrosshairSize, LineThickness),
                texture
            );
            
            // Draw vertical line (top)
            GUI.DrawTexture(
                new Rect(centerX - LineThickness / 2f, centerY - CrosshairSize - CenterGap, LineThickness, CrosshairSize),
                texture
            );
            
            // Draw vertical line (bottom)
            GUI.DrawTexture(
                new Rect(centerX - LineThickness / 2f, centerY + CenterGap, LineThickness, CrosshairSize),
                texture
            );
            
            // Optional: Draw center dot
            GUI.DrawTexture(
                new Rect(centerX - 1, centerY - 1, 2, 2),
                texture
            );
            
            Destroy(texture);
        }
    }
}
