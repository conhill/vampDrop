using UnityEngine;
using System;
using System.Collections.Generic;

namespace Vampire
{
    /// <summary>
    /// Animation types for comic panels
    /// </summary>
    public enum PanelAnimation
    {
        None,
        FadeIn,
        PanDown,
        PanUp,
        PanLeft,
        PanRight,
        ZoomIn,
        ZoomOut,
        SlideInLeft,
        SlideInRight,
        SlideInTop,
        SlideInBottom,
        CurtainOpen  // Black bars slide apart horizontally to reveal image
    }

    /// <summary>
    /// Layer types for positioning elements
    /// </summary>
    public enum LayerType
    {
        Background,
        Midground,
        Foreground,
        UI
    }

    /// <summary>
    /// How an element should size itself
    /// </summary>
    public enum ElementSizeMode
    {
        FillPanel,      // Stretch to fill entire panel (default for backgrounds)
        FitToSprite,    // Use sprite's native size (scaled down to fit panel)
        Custom          // Use custom width/height in pixels
    }

    /// <summary>
    /// Single element in a comic panel (image, text, character)
    /// </summary>
    [Serializable]
    public class ComicElement
    {
        [Header("Element Settings")]
        public string elementName = "Element";
        public Sprite sprite;
        public LayerType layer = LayerType.Midground;
        
        [Header("Size Mode")]
        [Tooltip("How this element should be sized")]
        public ElementSizeMode sizeMode = ElementSizeMode.FitToSprite;
        
        [Tooltip("Custom size in pixels (only used if sizeMode = Custom)")]
        public Vector2 customSize = new Vector2(500, 500);
        
        [Header("Position & Size")]
        [Tooltip("Normalized position (0-1), where 0.5,0.5 is center")]
        public Vector2 position = new Vector2(0.5f, 0.5f);
        
        [Tooltip("Scale multiplier (1 = original size)")]
        public float scale = 1f;
        
        [Header("Animation")]
        public PanelAnimation animation = PanelAnimation.None;
        
        [Tooltip("Delay before this element appears (seconds)")]
        public float appearDelay = 0f;
        
        [Tooltip("Duration of animation (seconds)")]
        public float animationDuration = 1f;
        
        [Header("Animation Settings")]
        [Tooltip("For pan animations: how far to pan (screen heights/widths)")]
        public float panDistance = 0.3f;
        
        [Tooltip("For zoom animations: start/end scale")]
        public float zoomScale = 0.5f;
        
        // Runtime data
        [NonSerialized] public GameObject gameObject;
        [NonSerialized] public RectTransform rectTransform;
        [NonSerialized] public UnityEngine.UI.Image imageComponent;
        [NonSerialized] public float animationStartTime;
        [NonSerialized] public bool isAnimating;
        [NonSerialized] public Vector2 animationStartPos;
        [NonSerialized] public Vector2 animationEndPos;
        [NonSerialized] public float animationStartScale;
        [NonSerialized] public float animationEndScale;
        [NonSerialized] public Color animationStartColor;
        [NonSerialized] public Color animationEndColor;
        [NonSerialized] public GameObject curtainLeft;  // For CurtainOpen animation
        [NonSerialized] public GameObject curtainRight; // For CurtainOpen animation
    }

    /// <summary>
    /// Width modes for comic panels
    /// </summary>
    public enum PanelWidthMode
    {
        FullScreen,     // Takes full screen width
        Custom,         // Custom pixel width (for bordered/narrow panels)
        HalfScreen      // Half screen width
    }

    /// <summary>
    /// Represents a single panel/scene in the comic intro
    /// </summary>
    [Serializable]
    public class ComicPanel
    {
        [Header("Panel Settings")]
        public string panelName = "Panel";
        
        [Header("Panel Width")]
        [Tooltip("How wide this panel should be")]
        public PanelWidthMode widthMode = PanelWidthMode.FullScreen;
        
        [Tooltip("Custom width in pixels (only used if widthMode = Custom)")]
        public float customWidth = 500f;
        
        [Tooltip("Duration before auto-advancing (0 = wait for input)")]
        public float autoDuration = 0f;
        
        [Header("Elements")]
        [Tooltip("All visual elements in this panel (backgrounds, characters, text)")]
        public List<ComicElement> elements = new List<ComicElement>();
        
        [Header("Audio (Optional)")]
        public AudioClip soundEffect;
        public AudioClip music;
        
        [Tooltip("Volume for music (0-1)")]
        [Range(0f, 1f)]
        public float musicVolume = 0.5f;
        
        // Runtime data
        [NonSerialized] public float panelStartTime;
        [NonSerialized] public bool isComplete;
        [NonSerialized] public GameObject panelContainer;
        [NonSerialized] public RectTransform panelRect;
        [NonSerialized] public float panelWorldX; // X position in the scrolling container
    }
}
