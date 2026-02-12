using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro;

namespace Vampire
{
    /// <summary>
    /// Manages comic-style story sequences
    /// Can be used for intro, mid-game story moments, endings, etc.
    /// Set ComicSceneManager.NextSceneToLoad before loading the comic scene
    /// </summary>
    public class ComicSceneManager : MonoBehaviour
    {
        public static ComicSceneManager Instance { get; private set; }
        
        /// <summary>
        /// Set this before loading the comic scene to specify which comic config to play
        /// </summary>
        public static ComicSequenceConfig CurrentSequence { get; set; }
        
        [Header("Comic Sequence Configuration")]
        [Tooltip("Available comic sequence configs - add your comic configs here")]
        public List<ComicSequenceConfig> availableSequences = new List<ComicSequenceConfig>();
        
        [Header("Fallback (if no CurrentSequence set)")]
        [Tooltip("Default sequence to play if CurrentSequence is null")]
        public ComicSequenceConfig defaultSequence;
        
        [Header("UI References")]
        [Tooltip("Canvas for displaying comic panels")]
        public Canvas comicCanvas;
        
        [Tooltip("Scrolling container that holds all panels side-by-side")]
        public RectTransform scrollingContainer;
        
        [Tooltip("Text showing '[Space] to continue' prompt")]
        public TextMeshProUGUI continuePrompt;
        
        [Header("Settings")]
        [Tooltip("Show continue prompt")]
        public bool showContinuePrompt = true;
        
        [Tooltip("Allow skipping the entire comic sequence with ESC")]
        public bool allowSkip = true;
        
        [Header("Audio (Optional)")]
        public AudioSource musicSource;
        public AudioSource sfxSource;
        
        // Current state
        private int currentPanelIndex = 0;
        private bool waitingForInput = false;
        private bool comicComplete = false;
        private List<ComicElement> activeElements = new List<ComicElement>();
        private ComicSequenceConfig activeSequence;
        private List<ComicPanel> panels;
        private bool isScrolling = false;
        private float scrollStartTime;
        private float scrollDuration = 0.8f; // Time to slide to next panel
        private Vector2 scrollStartPos;
        private Vector2 scrollTargetPos;
        private float currentScrollOffset = 0f; // Current accumulated scroll distance
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // Create default canvas if none assigned
            if (comicCanvas == null)
            {
                SetupDefaultCanvas();
            }
            
            // Hide continue prompt initially
            if (continuePrompt != null)
            {
                continuePrompt.gameObject.SetActive(false);
            }
        }
        
        private void Start()
        {
            // Enable cursor for potential UI interaction
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            
            // Load the comic sequence configuration
            LoadSequenceConfig();
            
            // Start the comic
            if (panels != null && panels.Count > 0)
            {
                ShowPanel(0);
            }
            else
            {
                Debug.LogWarning($"[ComicScene] No panels configured! Skipping comic.");
                CompleteComic();
            }
        }
        
        /// <summary>
        /// Load the comic sequence configuration
        /// </summary>
        private void LoadSequenceConfig()
        {
            // Use static override if set
            if (CurrentSequence != null)
            {
                activeSequence = CurrentSequence;
                Debug.Log($"[ComicScene] Loading sequence from static: '{activeSequence.sequenceName}' ({activeSequence.sequenceId})");
            }
            // Otherwise use default
            else if (defaultSequence != null)
            {
                activeSequence = defaultSequence;
                Debug.Log($"[ComicScene] Loading default sequence: '{activeSequence.sequenceName}' ({activeSequence.sequenceId})");
            }
            else
            {
                Debug.LogError("[ComicScene] No comic sequence configured! Assign CurrentSequence or defaultSequence.");
                CompleteComic();
                return;
            }
            
            // Load panels from config
            panels = activeSequence.panels;
            
            // Play sequence music if specified
            if (activeSequence.sequenceMusic != null && musicSource != null)
            {
                musicSource.clip = activeSequence.sequenceMusic;
                musicSource.volume = activeSequence.musicVolume;
                musicSource.loop = true;
                musicSource.Play();
                Debug.Log($"[ComicScene] Playing sequence music: {activeSequence.sequenceMusic.name}");
            }
            
            Debug.Log($"[ComicScene] Initialized '{activeSequence.sequenceName}' with {panels.Count} panels");
            
            // Create scrolling container if it doesn't exist
            if (scrollingContainer == null)
            {
                CreateScrollingContainer();
            }
            
            // Create all panels side-by-side
            CreateAllPanels();
        }
        
        /// <summary>
        /// Create scrolling container for panels
        /// </summary>
        private void CreateScrollingContainer()
        {
            GameObject containerGO = new GameObject("ScrollingContainer");
            containerGO.transform.SetParent(comicCanvas.transform, false);
            scrollingContainer = containerGO.AddComponent<RectTransform>();
            scrollingContainer.anchorMin = new Vector2(0, 0);
            scrollingContainer.anchorMax = new Vector2(0, 1);
            scrollingContainer.pivot = new Vector2(0, 0.5f);
            scrollingContainer.anchoredPosition = Vector2.zero;
            scrollingContainer.sizeDelta = new Vector2(0, 0); // Will expand as we add panels
        }
        
        /// <summary>
        /// Create all panels side-by-side in the scrolling container
        /// </summary>
        private void CreateAllPanels()
        {
            RectTransform canvasRT = comicCanvas.GetComponent<RectTransform>();
            float screenWidth = canvasRT.rect.width;
            float screenHeight = canvasRT.rect.height;
            float currentX = 0f;
            
            for (int i = 0; i < panels.Count; i++)
            {
                ComicPanel panel = panels[i];
                
                // Determine panel width
                float panelWidth = screenWidth; // Default: full screen
                switch (panel.widthMode)
                {
                    case PanelWidthMode.FullScreen:
                        panelWidth = screenWidth;
                        break;
                    case PanelWidthMode.HalfScreen:
                        panelWidth = screenWidth * 0.5f;
                        break;
                    case PanelWidthMode.Custom:
                        panelWidth = panel.customWidth;
                        break;
                }
                
                // Create panel container - ALWAYS full height
                GameObject panelGO = new GameObject($"Panel_{i}_{panel.panelName}");
                panelGO.transform.SetParent(scrollingContainer, false);
                
                RectTransform panelRT = panelGO.AddComponent<RectTransform>();
                panelRT.anchorMin = Vector2.zero;
                panelRT.anchorMax = new Vector2(0, 1);  // Anchor left edge, stretch height
                panelRT.pivot = Vector2.zero;  // Bottom-left pivot
                panelRT.anchoredPosition = new Vector2(currentX, 0);
                panelRT.sizeDelta = new Vector2(panelWidth, 0);  // Width set, height from stretch
                
                panel.panelContainer = panelGO;
                panel.panelRect = panelRT;
                panel.panelWorldX = currentX;
                
                // Create elements within this panel
                CreatePanelElements(panel, panelRT, panelWidth, screenHeight);
                
                currentX += panelWidth;
            }
            
            // Set scrolling container total width
            scrollingContainer.sizeDelta = new Vector2(currentX, 0);
            
            Debug.Log($"[ComicScene] Created {panels.Count} panels, total width: {currentX}px");
        }
        
        /// <summary>
        /// Create all elements for a specific panel
        /// </summary>
        private void CreatePanelElements(ComicPanel panel, RectTransform panelRT, float panelWidth, float screenHeight)
        {
            foreach (var element in panel.elements)
            {
                if (element.sprite == null)
                {
                    Debug.LogWarning($"[ComicScene] [{activeSequence?.sequenceId}] Element '{element.elementName}' has no sprite!");
                    continue;
                }
                
                CreateElementInPanel(element, panelRT, panelWidth, screenHeight);
            }
        }
        
        private void Update()
        {
            if (comicComplete) return;
            
            // Handle scrolling animation
            if (isScrolling)
            {
                UpdateScrolling();
                return;
            }
            
            // Skip entire comic with ESC
            if (allowSkip && Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log($"[ComicScene] '{activeSequence?.sequenceName}' skipped by user");
                CompleteComic();
                return;
            }
            
            // Enter or Space bar to advance (when waiting for input)
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) && waitingForInput)
            {
                AdvancePanel();
                return;
            }
            
            // Update current panel
            if (currentPanelIndex < panels.Count)
            {
                UpdateCurrentPanel();
            }
        }
        
        /// <summary>
        /// Update scrolling animation
        /// </summary>
        private void UpdateScrolling()
        {
            float elapsed = Time.time - scrollStartTime;
            float t = Mathf.Clamp01(elapsed / scrollDuration);
            
            // Ease out cubic
            t = 1f - Mathf.Pow(1f - t, 3f);
            
            scrollingContainer.anchoredPosition = Vector2.Lerp(scrollStartPos, scrollTargetPos, t);
            
            if (t >= 1f)
            {
                isScrolling = false;
                // Start animations for the new panel
                StartPanelAnimations(panels[currentPanelIndex]);
            }
        }
        
        /// <summary>
        /// Show a specific panel by scrolling to it
        /// </summary>
        private void ShowPanel(int index)
        {
            if (index >= panels.Count)
            {
                CompleteComic();
                return;
            }
            
            currentPanelIndex = index;
            ComicPanel panel = panels[index];
            panel.panelStartTime = Time.time;
            panel.isComplete = false;
            waitingForInput = false;
            
            Debug.Log($"[ComicScene] [{activeSequence?.sequenceId}] Scrolling to panel {index}: {panel.panelName}");
            
            // Hide continue prompt
            if (continuePrompt != null)
            {
                continuePrompt.gameObject.SetActive(false);
            }
            
            // If first panel, show immediately (no scroll)
            if (index == 0)
            {
                scrollingContainer.anchoredPosition = Vector2.zero;
                StartPanelAnimations(panel);
            }
            else
            {
                // Scroll to this panel
                StartScrollToPanel(panel);
            }
            
            // Play music if specified
            if (panel.music != null && musicSource != null)
            {
                musicSource.clip = panel.music;
                musicSource.volume = panel.musicVolume;
                musicSource.loop = true;
                musicSource.Play();
            }
            
            // Play sound effect if specified
            if (panel.soundEffect != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(panel.soundEffect);
            }
        }
        
        /// <summary>
        /// Start scrolling animation to show a panel
        /// </summary>
        private void StartScrollToPanel(ComicPanel panel)
        {
            isScrolling = true;
            scrollStartTime = Time.time;
            scrollStartPos = scrollingContainer.anchoredPosition;
            
            // Calculate how far to scroll based on the panel's WIDTH (not its position)
            // This makes each panel slide in by its own width amount
            float panelWidth = panel.panelRect.sizeDelta.x;
            currentScrollOffset -= panelWidth; // Move left by this panel's width
            
            scrollTargetPos = new Vector2(currentScrollOffset, 0);
            
            Debug.Log($"[ComicScene] Scrolling by {panelWidth}px (total offset: {currentScrollOffset}px)");
        }
        
        /// <summary>
        /// Start animations for all elements in a panel
        /// </summary>
        private void StartPanelAnimations(ComicPanel panel)
        {
            foreach (var element in panel.elements)
            {
                if (element.animation != PanelAnimation.None)
                {
                    SetupElementAnimation(element);
                }
            }
        }
        
        /// <summary>
        /// Update animations and check for panel completion
        /// </summary>
        private void UpdateCurrentPanel()
        {
            ComicPanel panel = panels[currentPanelIndex];
            float panelTime = Time.time - panel.panelStartTime;
            
            // Update element animations
            bool anyAnimating = false;
            foreach (var element in activeElements)
            {
                if (element.isAnimating)
                {
                    UpdateElementAnimation(element);
                    anyAnimating = true;
                }
            }
            
            // Check if we should wait for input or auto-advance
            if (!anyAnimating && !waitingForInput)
            {
                if (panel.autoDuration > 0f)
                {
                    // Auto-advance after duration
                    if (panelTime >= panel.autoDuration)
                    {
                        AdvancePanel();
                    }
                }
                else
                {
                    // Wait for user input
                    waitingForInput = true;
                    if (continuePrompt != null && showContinuePrompt)
                    {
                        continuePrompt.gameObject.SetActive(true);
                        Debug.Log($"[ComicScene] [{activeSequence?.sequenceId}] Panel {currentPanelIndex} waiting for input (Enter to continue)");
                    }
                }
            }
        }
        
        /// <summary>
        /// Advance to next panel
        /// </summary>
        private void AdvancePanel()
        {
            waitingForInput = false;
            
            if (continuePrompt != null)
            {
                continuePrompt.gameObject.SetActive(false);
            }
            
            currentPanelIndex++;
            
            if (currentPanelIndex < panels.Count)
            {
                ShowPanel(currentPanelIndex);
            }
            else
            {
                CompleteComic();
            }
        }
        
        /// <summary>
        /// Create a visual element within a panel
        /// </summary>
        private void CreateElementInPanel(ComicElement element, RectTransform panelRT, float panelWidth, float screenHeight)
        {
            // Create GameObject
            GameObject go = new GameObject(element.elementName);
            go.transform.SetParent(panelRT, false);
            
            // Add RectTransform
            RectTransform rt = go.AddComponent<RectTransform>();
            
            // Set layer-based sorting
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = GetSortingOrder(element.layer);
            
            // Add Image component
            Image img = go.AddComponent<Image>();
            img.sprite = element.sprite;
            img.preserveAspect = true;
            
            // Declare variables used across cases
            Vector2 screenPos;
            
            // Size based on sizeMode
            switch (element.sizeMode)
            {
                case ElementSizeMode.FillPanel:
                    // Fill the entire panel - stretch both width and height
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.sizeDelta = Vector2.zero;  // No offset from anchors
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;  // Centered between anchors
                    rt.localScale = Vector3.one * element.scale;
                    img.preserveAspect = false;  // Don't preserve aspect, stretch to fill
                    break;
                    
                case ElementSizeMode.Custom:
                    // Use custom pixel size
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = element.customSize;
                    
                    // Convert normalized position to panel position
                    screenPos = new Vector2(
                        (element.position.x - 0.5f) * panelWidth,
                        (element.position.y - 0.5f) * screenHeight
                    );
                    rt.anchoredPosition = screenPos;
                    rt.localScale = Vector3.one * element.scale;
                    break;
                    
                case ElementSizeMode.FitToSprite:
                default:
                    // Size based on sprite, scaled to fit panel reasonably
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    
                    // Scale sprite to reasonable size (max 50% of panel/screen by default, then apply user scale)
                    float maxDimension = Mathf.Max(element.sprite.rect.width, element.sprite.rect.height);
                    float targetMaxDimension = Mathf.Min(panelWidth, screenHeight) * 0.5f;
                    float autoScale = targetMaxDimension / maxDimension;
                    
                    Vector2 elementSize = new Vector2(element.sprite.rect.width * autoScale, element.sprite.rect.height * autoScale);
                    rt.sizeDelta = elementSize;
                    
                    // Convert normalized position to panel position
                    screenPos = new Vector2(
                        (element.position.x - 0.5f) * panelWidth,
                        (element.position.y - 0.5f) * screenHeight
                    );
                    rt.anchoredPosition = screenPos;
                    rt.localScale = Vector3.one * element.scale;
                    break;
            }
            
            // Store references
            element.gameObject = go;
            element.rectTransform = rt;
            element.imageComponent = img;
            activeElements.Add(element);
        }
        
        /// <summary>
        /// Setup animation for an element
        /// </summary>
        private void SetupElementAnimation(ComicElement element)
        {
            element.isAnimating = true;
            element.animationStartTime = Time.time + element.appearDelay;
            element.animationEndPos = element.rectTransform.anchoredPosition;
            element.animationEndScale = element.scale;
            element.animationEndColor = element.imageComponent.color;
            
            Canvas mainCanvas = comicCanvas != null ? comicCanvas : GetComponent<Canvas>();
            RectTransform canvasRT = mainCanvas.GetComponent<RectTransform>();
            float screenWidth = canvasRT.rect.width;
            float screenHeight = canvasRT.rect.height;
            
            switch (element.animation)
            {
                case PanelAnimation.FadeIn:
                    element.animationStartColor = new Color(1, 1, 1, 0);
                    element.imageComponent.color = element.animationStartColor;
                    break;
                    
                case PanelAnimation.PanDown:
                    element.animationStartPos = element.animationEndPos + Vector2.up * screenHeight * element.panDistance;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.PanUp:
                    element.animationStartPos = element.animationEndPos + Vector2.down * screenHeight * element.panDistance;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.PanLeft:
                    element.animationStartPos = element.animationEndPos + Vector2.right * screenWidth * element.panDistance;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.PanRight:
                    element.animationStartPos = element.animationEndPos + Vector2.left * screenWidth * element.panDistance;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.ZoomIn:
                    element.animationStartScale = element.scale * element.zoomScale;
                    element.rectTransform.localScale = Vector3.one * element.animationStartScale;
                    break;
                    
                case PanelAnimation.ZoomOut:
                    element.animationStartScale = element.scale / element.zoomScale;
                    element.rectTransform.localScale = Vector3.one * element.animationStartScale;
                    break;
                    
                case PanelAnimation.SlideInLeft:
                    element.animationStartPos = element.animationEndPos + Vector2.left * screenWidth;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.SlideInRight:
                    element.animationStartPos = element.animationEndPos + Vector2.right * screenWidth;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.SlideInTop:
                    element.animationStartPos = element.animationEndPos + Vector2.up * screenHeight;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.SlideInBottom:
                    element.animationStartPos = element.animationEndPos + Vector2.down * screenHeight;
                    element.rectTransform.anchoredPosition = element.animationStartPos;
                    break;
                    
                case PanelAnimation.CurtainOpen:
                    // Create two black rectangles that will slide away
                    CreateCurtainBars(element, canvasRT);
                    break;
            }
        }
        
        /// <summary>
        /// Create black curtain bars for reveal effect
        /// </summary>
        private void CreateCurtainBars(ComicElement element, RectTransform canvasRT)
        {
            // Left curtain bar
            element.curtainLeft = new GameObject("CurtainLeft");
            element.curtainLeft.transform.SetParent(element.gameObject.transform.parent, false);
            RectTransform leftRT = element.curtainLeft.AddComponent<RectTransform>();
            leftRT.anchorMin = Vector2.zero;
            leftRT.anchorMax = Vector2.one;
            leftRT.sizeDelta = Vector2.zero;
            leftRT.anchoredPosition = Vector2.zero;
            
            UnityEngine.UI.Image leftImg = element.curtainLeft.AddComponent<UnityEngine.UI.Image>();
            leftImg.color = Color.black;
            
            Canvas leftCanvas = element.curtainLeft.AddComponent<Canvas>();
            leftCanvas.overrideSorting = true;
            leftCanvas.sortingOrder = GetSortingOrder(element.layer) + 1; // Above the image
            
            // Right curtain bar
            element.curtainRight = new GameObject("CurtainRight");
            element.curtainRight.transform.SetParent(element.gameObject.transform.parent, false);
            RectTransform rightRT = element.curtainRight.AddComponent<RectTransform>();
            rightRT.anchorMin = Vector2.zero;
            rightRT.anchorMax = Vector2.one;
            rightRT.sizeDelta = Vector2.zero;
            rightRT.anchoredPosition = Vector2.zero;
            
            UnityEngine.UI.Image rightImg = element.curtainRight.AddComponent<UnityEngine.UI.Image>();
            rightImg.color = Color.black;
            
            Canvas rightCanvas = element.curtainRight.AddComponent<Canvas>();
            rightCanvas.overrideSorting = true;
            rightCanvas.sortingOrder = GetSortingOrder(element.layer) + 1; // Above the image
        }
        
        /// <summary>
        /// Update animation for an element
        /// </summary>
        private void UpdateElementAnimation(ComicElement element)
        {
            float currentTime = Time.time;
            
            // Wait for appear delay
            if (currentTime < element.animationStartTime)
            {
                return;
            }
            
            // Calculate animation progress (0 to 1)
            float elapsed = currentTime - element.animationStartTime;
            float t = Mathf.Clamp01(elapsed / element.animationDuration);
            
            // Ease out cubic for smooth animation
            t = 1f - Mathf.Pow(1f - t, 3f);
            
            // Apply animation based on type
            switch (element.animation)
            {
                case PanelAnimation.FadeIn:
                    element.imageComponent.color = Color.Lerp(element.animationStartColor, element.animationEndColor, t);
                    break;
                    
                case PanelAnimation.PanDown:
                case PanelAnimation.PanUp:
                case PanelAnimation.PanLeft:
                case PanelAnimation.PanRight:
                case PanelAnimation.SlideInLeft:
                case PanelAnimation.SlideInRight:
                case PanelAnimation.SlideInTop:
                case PanelAnimation.SlideInBottom:
                    element.rectTransform.anchoredPosition = Vector2.Lerp(element.animationStartPos, element.animationEndPos, t);
                    break;
                    
                case PanelAnimation.ZoomIn:
                case PanelAnimation.ZoomOut:
                    float scale = Mathf.Lerp(element.animationStartScale, element.animationEndScale, t);
                    element.rectTransform.localScale = Vector3.one * scale;
                    break;
                    
                case PanelAnimation.CurtainOpen:
                    UpdateCurtainAnimation(element, t);
                    break;
            }
            
            // Mark complete when done
            if (t >= 1f)
            {
                element.isAnimating = false;
                
                // Clean up curtain bars
                if (element.curtainLeft != null)
                {
                    Destroy(element.curtainLeft);
                    element.curtainLeft = null;
                }
                if (element.curtainRight != null)
                {
                    Destroy(element.curtainRight);
                    element.curtainRight = null;
                }
            }
        }
        
        /// <summary>
        /// Update curtain reveal animation
        /// </summary>
        private void UpdateCurtainAnimation(ComicElement element, float t)
        {
            if (element.curtainLeft != null && element.curtainRight != null)
            {
                RectTransform leftRT = element.curtainLeft.GetComponent<RectTransform>();
                RectTransform rightRT = element.curtainRight.GetComponent<RectTransform>();
                
                // Left curtain slides from center to left edge (covers left half, then slides away)
                // Start: covers left 50%, End: off screen to the left
                float leftAnchor = Mathf.Lerp(0.5f, -0.5f, t); // Slides from center to off-screen left
                leftRT.anchorMin = new Vector2(0f, 0f);
                leftRT.anchorMax = new Vector2(leftAnchor, 1f);
                
                // Right curtain slides from center to right edge (covers right half, then slides away)
                // Start: covers right 50%, End: off screen to the right
                float rightAnchor = Mathf.Lerp(0.5f, 1.5f, t); // Slides from center to off-screen right
                rightRT.anchorMin = new Vector2(rightAnchor, 0f);
                rightRT.anchorMax = new Vector2(1f, 1f);
            }
        }
        
        /// <summary>
        /// Get sorting order based on layer
        /// </summary>
        private int GetSortingOrder(LayerType layer)
        {
            switch (layer)
            {
                case LayerType.Background: return 0;
                case LayerType.Midground: return 10;
                case LayerType.Foreground: return 20;
                case LayerType.UI: return 30;
                default: return 10;
            }
        }
        
        /// <summary>
        /// Clear all active elements
        /// </summary>
        private void ClearActiveElements()
        {
            foreach (var element in activeElements)
            {
                if (element.gameObject != null)
                {
                    Destroy(element.gameObject);
                }
                
                // Clean up curtain bars if they exist
                if (element.curtainLeft != null)
                {
                    Destroy(element.curtainLeft);
                }
                if (element.curtainRight != null)
                {
                    Destroy(element.curtainRight);
                }
            }
            activeElements.Clear();
        }
        
        /// <summary>
        /// Complete the comic and load next scene
        /// </summary>
        private void CompleteComic()
        {
            if (comicComplete) return;
            
            comicComplete = true;
            
            string nextScene = activeSequence != null ? activeSequence.nextSceneName : "FPS_Collect";
            Debug.Log($"[ComicScene] '{activeSequence?.sequenceName}' complete! Loading scene: {nextScene}");
            
            // Stop music
            if (musicSource != null)
            {
                musicSource.Stop();
            }
            
            // Clear elements
            ClearActiveElements();
            
            // Load next scene
            SceneManager.LoadScene(nextScene);
            
            // Reset static property for next use
            CurrentSequence = null;
        }
        
        /// <summary>
        /// Create default canvas if none exists
        /// </summary>
        private void SetupDefaultCanvas()
        {
            GameObject canvasGO = new GameObject("ComicCanvas");
            
            comicCanvas = canvasGO.AddComponent<Canvas>();
            comicCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            comicCanvas.sortingOrder = 1000;
            
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasGO.AddComponent<GraphicRaycaster>();
            
            // Set scale after Canvas is added (Canvas adds RectTransform)
            RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.localScale = Vector3.one;
            
            // Create continue prompt
            GameObject promptGO = new GameObject("ContinuePrompt");
            promptGO.transform.SetParent(canvasGO.transform, false);
            RectTransform promptRT = promptGO.AddComponent<RectTransform>();
            promptRT.anchorMin = new Vector2(0.5f, 0.1f);
            promptRT.anchorMax = new Vector2(0.5f, 0.1f);
            promptRT.sizeDelta = new Vector2(400, 60);
            
            TextMeshProUGUI text = promptGO.AddComponent<TextMeshProUGUI>();
            text.text = "Press [Enter] to continue";
            text.fontSize = 28;
            text.color = new Color(1f, 1f, 1f, 0.9f);
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            
            // Add subtle outline for visibility
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color(0, 0, 0, 0.8f);
            
            continuePrompt = text;
            
            // Add pulsing animation effect
            var pulseAnim = promptGO.AddComponent<ContinuePromptPulse>();
            
            Debug.Log($"[ComicScene] Created default canvas");
        }
    }
}
