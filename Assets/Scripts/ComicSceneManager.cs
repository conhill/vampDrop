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
        /// Set this before loading the comic scene to specify which comic config to play.
        /// </summary>
        public static ComicSequenceConfig CurrentSequence { get; set; }

        /// <summary>
        /// Optional: override the next scene that loads after the comic completes.
        /// Takes priority over <c>ComicSequenceConfig.nextSceneName</c>.
        /// Cleared automatically after use.
        /// </summary>
        public static string NextSceneOverride { get; set; }
        
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
                // Debug.LogWarning($"[ComicScene] No panels configured! Skipping comic.");
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
                // Debug.Log($"[ComicScene] Loading sequence from static: '{activeSequence.sequenceName}' ({activeSequence.sequenceId})");
            }
            // Otherwise use default
            else if (defaultSequence != null)
            {
                activeSequence = defaultSequence;
                // Debug.Log($"[ComicScene] Loading default sequence: '{activeSequence.sequenceName}' ({activeSequence.sequenceId})");
            }
            else
            {
                // Debug.LogError("[ComicScene] No comic sequence configured! Assign CurrentSequence or defaultSequence.");
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
                // Debug.Log($"[ComicScene] Playing sequence music: {activeSequence.sequenceMusic.name}");
            }
            
            // Debug.Log($"[ComicScene] Initialized '{activeSequence.sequenceName}' with {panels.Count} panels");
            
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
            
            // Debug.Log($"[ComicScene] Created {panels.Count} panels, total width: {currentX}px");
        }
        
        /// <summary>
        /// Create all elements for a specific panel
        /// </summary>
        private void CreatePanelElements(ComicPanel panel, RectTransform panelRT, float panelWidth, float screenHeight)
        {
            foreach (var element in panel.elements)
            {
                // Allow elements that have at least a sprite OR dialogue text
                if (element.sprite == null && string.IsNullOrEmpty(element.dialogueText))
                {
                    // Debug.LogWarning($"[ComicScene] [{activeSequence?.sequenceId}] Element '{element.elementName}' has no sprite or text - skipping.");
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
                // Debug.Log($"[ComicScene] '{activeSequence?.sequenceName}' skipped by user");
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
            
            // Debug.Log($"[ComicScene] [{activeSequence?.sequenceId}] Scrolling to panel {index}: {panel.panelName}");
            
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
            
            // Debug.Log($"[ComicScene] Scrolling by {panelWidth}px (total offset: {currentScrollOffset}px)");
        }
        
        /// <summary>
        /// Start animations for all elements in a panel
        /// </summary>
        private void StartPanelAnimations(ComicPanel panel)
        {
            // Clear previous elements
            activeElements.Clear();
            
            // Add all elements from this panel to active elements
            foreach (var element in panel.elements)
            {
                activeElements.Add(element);
                
                if (element.animation != PanelAnimation.None)
                {
                    SetupElementAnimation(element);
                }
                else
                {
                    // Elements with no animation are immediately ready
                    element.isAnimating = false;
                    // Debug.Log($"[ComicScene] Element '{element.elementName}' has no animation - immediately ready");
                }
            }
            
            // Debug.Log($"[ComicScene] Started animations for {panel.elements.Count} elements in panel '{panel.panelName}' ({activeElements.Count} active)");
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
                    if (element.isAnimating) // Check again after update
                    {
                        anyAnimating = true;
                    }
                }
            }
            
            // Debug: Log animation status occasionally
            if (Time.frameCount % 60 == 0) // Every second
            {
                // Debug.Log($"[ComicScene] Panel {currentPanelIndex}: anyAnimating={anyAnimating}, waitingForInput={waitingForInput}, panelTime={panelTime:F1}s, autoDuration={panel.autoDuration}");
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
                        // Debug.Log($"[ComicScene] [{activeSequence?.sequenceId}] Panel {currentPanelIndex} '{panel.panelName}' waiting for input - CONTINUE PROMPT SHOWN");
                    }
                    else
                    {
                        // Debug.LogWarning($"[ComicScene] Continue prompt not shown! continuePrompt={continuePrompt != null}, showContinuePrompt={showContinuePrompt}");
                    }
                }
            }
            
            // Fallback: Force show continue prompt after reasonable time if autoDuration = 0
            if (panel.autoDuration == 0f && panelTime > 2f && !waitingForInput)
            {
                int stuckAnimationCount = 0;
                foreach (var element in activeElements)
                {
                    if (element.isAnimating) stuckAnimationCount++;
                }
                
                // Debug.LogWarning($"[ComicScene] Forcing continue prompt after 2s - animations might be stuck. Completing {stuckAnimationCount} stuck animations");
                
                // Force complete all stuck animations properly
                foreach (var element in activeElements)
                {
                    if (element.isAnimating)
                    {
                        // Debug.LogWarning($"[ComicScene] Force completing stuck animation: {element.elementName} ({element.animation})");
                        element.isAnimating = false;
                        
                        // Set elements to their final animation state
                        if (element.imageComponent != null && element.animation == PanelAnimation.FadeIn)
                        {
                            var color = element.imageComponent.color;
                            color.a = 1f; // Full opacity
                            element.imageComponent.color = color;
                        }
                        
                        if (element.rectTransform != null)
                        {
                            element.rectTransform.anchoredPosition = element.animationEndPos;
                            element.rectTransform.localScale = Vector3.one * element.animationEndScale;
                        }
                        
                        // Clean up curtain animation objects
                        if (element.animation == PanelAnimation.CurtainOpen)
                        {
                            if (element.curtainLeft != null) Destroy(element.curtainLeft);
                            if (element.curtainRight != null) Destroy(element.curtainRight);
                            element.curtainLeft = null;
                            element.curtainRight = null;
                        }
                    }
                }
                
                waitingForInput = true;
                if (continuePrompt != null && showContinuePrompt)
                {
                    continuePrompt.gameObject.SetActive(true);
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

            // Add Image component (transparent placeholder when sprite is null)
            Image img = go.AddComponent<Image>();
            img.sprite = element.sprite;
            img.preserveAspect = true;
            if (element.sprite == null)
                img.color = Color.clear; // invisible backing rect for text-only elements
            
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
            
            // Overlay dialogue text if set (e.g. blank_text sprite + text string)
            if (!string.IsNullOrEmpty(element.dialogueText))
            {
                GameObject textGO = new GameObject("DialogueText");
                textGO.transform.SetParent(go.transform, false);

                RectTransform textRT = textGO.AddComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.pivot     = new Vector2(0.5f, 0.5f);
                // Inset by padding: offsetMin = (left, bottom), offsetMax = (-right, -top)
                textRT.offsetMin = new Vector2(element.textPadding.x, element.textPadding.w);
                textRT.offsetMax = new Vector2(-element.textPadding.y, -element.textPadding.z);

                // Sorting above the sprite
                Canvas textCanvas = textGO.AddComponent<Canvas>();
                textCanvas.overrideSorting = true;
                textCanvas.sortingOrder    = GetSortingOrder(element.layer) + 1;

                var tmp = textGO.AddComponent<TMPro.TextMeshProUGUI>();
                tmp.text             = element.dialogueText;
                tmp.color            = element.textColor;
                tmp.fontSize         = element.fontSize;
                tmp.alignment        = element.textAlignment;
                tmp.enableWordWrapping = true;
                tmp.overflowMode     = TMPro.TextOverflowModes.Truncate;

                element.textComponent = tmp;
            }

            // Store references
            element.gameObject    = go;
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
                    if (element.imageComponent != null && element.sprite != null)
                        element.imageComponent.color = element.animationStartColor;
                    if (element.textComponent != null)
                        element.textComponent.color = new Color(
                            element.textColor.r, element.textColor.g, element.textColor.b, 0);
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
        /// Create black curtain bars for vertical reveal effect (top and bottom)
        /// </summary>
        private void CreateCurtainBars(ComicElement element, RectTransform canvasRT)
        {
            // Top curtain bar (starts covering top half)
            element.curtainLeft = new GameObject("CurtainTop");
            element.curtainLeft.transform.SetParent(element.gameObject.transform.parent, false);
            RectTransform topRT = element.curtainLeft.AddComponent<RectTransform>();
            topRT.anchorMin = new Vector2(0f, 0.5f);  // Covers top half initially
            topRT.anchorMax = new Vector2(1f, 1f);
            topRT.sizeDelta = Vector2.zero;
            topRT.anchoredPosition = Vector2.zero;
            
            UnityEngine.UI.Image topImg = element.curtainLeft.AddComponent<UnityEngine.UI.Image>();
            topImg.color = Color.black;
            
            Canvas topCanvas = element.curtainLeft.AddComponent<Canvas>();
            topCanvas.overrideSorting = true;
            topCanvas.sortingOrder = GetSortingOrder(element.layer) + 1; // Above the image
            
            // Bottom curtain bar (starts covering bottom half)
            element.curtainRight = new GameObject("CurtainBottom");
            element.curtainRight.transform.SetParent(element.gameObject.transform.parent, false);
            RectTransform bottomRT = element.curtainRight.AddComponent<RectTransform>();
            bottomRT.anchorMin = new Vector2(0f, 0f);
            bottomRT.anchorMax = new Vector2(1f, 0.5f);  // Covers bottom half initially
            bottomRT.sizeDelta = Vector2.zero;
            bottomRT.anchoredPosition = Vector2.zero;
            
            UnityEngine.UI.Image bottomImg = element.curtainRight.AddComponent<UnityEngine.UI.Image>();
            bottomImg.color = Color.black;
            
            Canvas bottomCanvas = element.curtainRight.AddComponent<Canvas>();
            bottomCanvas.overrideSorting = true;
            bottomCanvas.sortingOrder = GetSortingOrder(element.layer) + 1; // Above the image
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
            
            // Handle zero-duration animations (instant completion)
            if (element.animationDuration <= 0f)
            {
                // Instantly set to final values
                switch (element.animation)
                {
                    case PanelAnimation.FadeIn:
                        element.imageComponent.color = element.animationEndColor;
                        break;
                    case PanelAnimation.PanDown:
                    case PanelAnimation.PanUp:
                    case PanelAnimation.PanLeft:
                    case PanelAnimation.PanRight:
                    case PanelAnimation.SlideInLeft:
                    case PanelAnimation.SlideInRight:
                    case PanelAnimation.SlideInTop:
                    case PanelAnimation.SlideInBottom:
                        element.rectTransform.anchoredPosition = element.animationEndPos;
                        break;
                    case PanelAnimation.ZoomIn:
                    case PanelAnimation.ZoomOut:
                        element.rectTransform.localScale = Vector3.one * element.animationEndScale;
                        break;
                    case PanelAnimation.CurtainOpen:
                        UpdateCurtainAnimation(element, 1f);
                        break;
                }
                // Mark as complete
                element.isAnimating = false;
                // Debug.Log($"[ComicScene] Element '{element.elementName}' instant animation completed (duration was {element.animationDuration})");
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
                    if (element.imageComponent != null && element.sprite != null)
                        element.imageComponent.color = Color.Lerp(element.animationStartColor, element.animationEndColor, t);
                    if (element.textComponent != null)
                    {
                        Color tc = element.textColor;
                        tc.a = t;
                        element.textComponent.color = tc;
                    }
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
                // Debug.Log($"[ComicScene] Element '{element.elementName}' animation completed after {(Time.time - element.animationStartTime):F1}s");
                
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
        /// Update vertical curtain reveal animation (top and bottom)
        /// </summary>
        private void UpdateCurtainAnimation(ComicElement element, float t)
        {
            if (element.curtainLeft != null && element.curtainRight != null)
            {
                RectTransform topRT = element.curtainLeft.GetComponent<RectTransform>();
                RectTransform bottomRT = element.curtainRight.GetComponent<RectTransform>();
                
                // Top curtain slides from center to top edge (covers top half, then slides away)
                // Start: covers top 50%, End: off screen to the top
                float topAnchor = Mathf.Lerp(0.5f, 1.5f, t); // Slides from center to off-screen top
                topRT.anchorMin = new Vector2(0f, topAnchor);
                topRT.anchorMax = new Vector2(1f, 1f);
                
                // Bottom curtain slides from center to bottom edge (covers bottom half, then slides away)
                // Start: covers bottom 50%, End: off screen to the bottom
                float bottomAnchor = Mathf.Lerp(0.5f, -0.5f, t); // Slides from center to off-screen bottom
                bottomRT.anchorMin = new Vector2(0f, 0f);
                bottomRT.anchorMax = new Vector2(1f, bottomAnchor);
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
            
            // NextSceneOverride lets callers (e.g. BallDropSceneEntry) redirect to DropPuzzle
            // without having to modify the ScriptableObject asset.
            string nextScene = !string.IsNullOrEmpty(NextSceneOverride)
                ? NextSceneOverride
                : (activeSequence != null ? activeSequence.nextSceneName : "FPS_Collect");
            NextSceneOverride = null; // consume the override
            // Debug.Log($"[ComicScene] '{activeSequence?.sequenceName}' complete! Loading scene: {nextScene}");
            
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
            
            // Debug.Log($"[ComicScene] Created default canvas");
        }
    }
}
