# Comic System - Developer Guide

## Overview
Horizontal scrolling comic system for cutscenes, story intros, and narrative sequences. ScriptableObject-based configuration allows creating unlimited comic sequences without code changes. Supports complex layouts, animations, and flexible sizing.

---

## Architecture

### Core Components

**ComicSequenceConfig** (ScriptableObject):
- Reusable comic configuration asset
- Contains list of panels, next scene, music
- Created via: Right-click → Create → Vampire → Comic Sequence Config

**ComicSceneManager** (MonoBehaviour):
- Main controller in ComicScene
- Manages horizontal scrolling container
- Handles input and scene transitions
- Single instance per scene

**ComicPanel** (Data Class):
- Defines one panel in the comic strip
- Width mode (FullScreen, HalfScreen, Custom)
- Contains list of elements

**ComicElement** (Data Class):
- Individual visual element (background, character, text, etc.)
- Size mode (FillPanel, FitToSprite, Custom)
- Position, animation, layer

**ComicSceneLoader** (Static Helper):
- Simplified loading API: `LoadComic(config)`
- Sets static CurrentSequence and loads scene

### Data Flow
```
Game Scene
   └─> ComicSceneLoader.LoadComic(config)
       └─> ComicSceneManager.CurrentSequence = config
           └─> Load "ComicScene"
               └─> ComicSceneManager.OnEnable()
                   └─> CreateAllPanels()
                       └─> CreatePanelElements()
                           └─> StartScrollToPanel()
```

---

## Creating Comic Sequences

### 1. Create ScriptableObject Asset

**In Unity Editor:**
1. Right-click in Project window
2. Create → Vampire → Comic Sequence Config
3. Name it (e.g., "IntroComic", "AfterBossComic")

### 2. Configure Sequence Properties

**Inspector Fields:**
```csharp
[Header("Sequence Info")]
public string sequenceId = "intro";          // Unique ID
public string sequenceName = "Introduction"; // Display name

[Header("Content")]
public List<ComicPanel> panels;              // The comic panels

[Header("Navigation")]
public string nextSceneName = "FPS_Collect"; // Where to go after

[Header("Audio")]
public AudioClip sequenceMusic;              // Background music (optional)
```

### 3. Add Panels

**Click "Add Panel" in Inspector:**

**Panel Configuration:**
```
Panel Name: "Opening Shot"
Width Mode: FullScreen
Custom Width: 1920 (ignored if FullScreen)
Auto Duration: 0 (wait for input) or 3.0 (auto-advance after 3s)
Elements: [List of visual elements]
```

### 4. Add Elements to Panel

**Element Configuration:**
```
Sprite: DragSprite from Assets
Size Mode: FillPanel (for backgrounds) or Custom (for positioned elements)
Custom Size: (500, 800) for exact pixel dimensions
Position: (0.5, 0.5) for center, (0, 1) for top-left corner
Scale: 1.0 (normal size)
Rotation: 0 (no rotation)
Layer: Background (0), Midground (10), Foreground (20), or UI (30)
Animation: FadeIn, Pan, Zoom, etc.
```

---

## Panel Width Modes

### FullScreen
**Use Case:** Main story panels, full backgrounds
```csharp
panel.widthMode = PanelWidthMode.FullScreen;
// Width automatically matches screen width
```
**Effect:** Panel fills entire screen width (e.g., 1920px at 1080p)

### HalfScreen
**Use Case:** Split-screen effects, side-by-side comparisons
```csharp
panel.widthMode = PanelWidthMode.HalfScreen;
// Width = Screen.width * 0.5f
```
**Effect:** Panel is 50% of screen width (e.g., 960px at 1080p)

### Custom
**Use Case:** Specific dimensions, dialogue boxes, inset panels
```csharp
panel.widthMode = PanelWidthMode.Custom;
panel.customWidth = 500f;
```
**Effect:** Panel has exact pixel width (500px in example)

### Example Sequence
```
[FullScreen] Opening shot (1920px)
  → Scroll 1920px left
[Custom 500px] Dialogue panel
  → Scroll 500px left (partial reveal)
[FullScreen] Action scene (1920px)
  → Scroll 1920px left
[HalfScreen] Split view (960px)
  → Scroll 960px left
```

---

## Element Size Modes

### FillPanel
**Use Case:** Background images, full panel overlays
```csharp
element.sizeMode = ElementSizeMode.FillPanel;
```
**Effect:**
- Anchors to (0,0) → (1,1)
- Stretches to fill entire panel
- `preserveAspect = false` (stretches, doesn't maintain ratio)

**Example:**
```
Panel: 1920x1080 (FullScreen)
Element: FillPanel mode
Result: Element becomes 1920x1080, stretched to fit
```

### FitToSprite
**Use Case:** Characters, objects, preserving aspect ratio
```csharp
element.sizeMode = ElementSizeMode.FitToSprite;
```
**Effect:**
- Auto-scales sprite to fit panel reasonably (50% max dimension)
- Maintains aspect ratio
- Positioned using normalized coordinates

**Example:**
```
Panel: 1920x1080
Sprite: 500x800 portrait
Result: Scaled to fit, max 960px height (50% of 1080)
```

### Custom
**Use Case:** Precise layouts, UI elements, positioned graphics
```csharp
element.sizeMode = ElementSizeMode.Custom;
element.customSize = new Vector2(400, 300);
```
**Effect:**
- Exact pixel dimensions (400x300 in example)
- Positioned using normalized coordinates
- No automatic scaling

**Example:**
```
Panel: 1920x1080
Custom Size: 400x300
Position: (0.25, 0.75) → Placed 480px from left, 810px from bottom
```

---

## Positioning System

### Normalized Coordinates (0-1)
```csharp
element.position = new Vector2(0.5f, 0.5f); // Center
```

**Common Positions:**
```
(0, 1)      (0.5, 1)      (1, 1)
  ┌───────────┬───────────┐
  │ Top-Left  │ Top-Right │
  │           │           │
(0, 0.5) ────┼─── (0.5, 0.5) Center
  │           │           │
  │ Bot-Left  │ Bot-Right │
  └───────────┴───────────┘
(0, 0)      (0.5, 0)      (1, 0)
```

### Layout Examples

**Background + Centered Character:**
```
Element 1: Background
  - Size Mode: FillPanel
  - Layer: 0 (Background)

Element 2: Character
  - Size Mode: Custom (400, 600)
  - Position: (0.5, 0.5) - Center
  - Layer: 10 (Midground)
```

**Quadrant Layout (4 Images):**
```
Element 1: Top-Left Image
  - Size Mode: Custom (500, 500)
  - Position: (0.25, 0.75)
  - Layer: 10

Element 2: Top-Right Image
  - Size Mode: Custom (500, 500)
  - Position: (0.75, 0.75)
  - Layer: 10

Element 3: Bottom-Left Image
  - Size Mode: Custom (500, 500)
  - Position: (0.25, 0.25)
  - Layer: 10

Element 4: Bottom-Right Image
  - Size Mode: Custom (500, 500)
  - Position: (0.75, 0.25)
  - Layer: 10
```

---

## Animation System

### Available Animations

**FadeIn:**
```csharp
animation.type = PanelAnimationType.FadeIn;
animation.duration = 1.0f;
```
Effect: Alpha 0 → 1 over duration

**Pan (Left/Right/Up/Down):**
```csharp
animation.type = PanelAnimationType.PanLeft;
animation.duration = 2.0f;
animation.distance = 500f;  // Pixels to move
```
Effect: Slides element from off-screen

**Zoom (In/Out):**
```csharp
animation.type = PanelAnimationType.ZoomIn;
animation.duration = 1.5f;
animation.startScale = 0.5f;  // Start at 50% size
animation.endScale = 1.0f;    // End at 100% size
```

**SlideIn (Left/Right/Up/Down):**
```csharp
animation.type = PanelAnimationType.SlideInLeft;
animation.duration = 0.8f;
animation.distance = 300f;
```
Effect: Element slides in from edge

**CurtainOpen:**
```csharp
animation.type = PanelAnimationType.CurtainOpen;
animation.duration = 2.0f;
```
Effect: Cinema-style curtain reveal from center

**None:**
```csharp
animation.type = PanelAnimationType.None;
```
Effect: Element appears instantly

### Animation Timing
```csharp
animation.delay = 0.5f;  // Wait 0.5s before starting
animation.duration = 1.0f; // Animation lasts 1s
// Total: Element animates from 0.5s to 1.5s
```

### Layered Animation Example
```
Panel: "Character Introduction"

Element 1: Background
  - Animation: FadeIn, duration 0.5s, delay 0s
  
Element 2: Character
  - Animation: SlideInLeft, duration 1.0s, delay 0.3s
  
Element 3: Name Text
  - Animation: FadeIn, duration 0.8s, delay 1.0s
  
Timeline:
0.0s: Background starts fading in
0.3s: Character starts sliding in
0.5s: Background fully visible
1.0s: Name text starts fading in
1.3s: Character fully visible
1.8s: Name text fully visible
```

---

## Loading Comics

### From Code

**Simple Load:**
```csharp
public ComicSequenceConfig introComic;

public void PlayIntro()
{
    ComicSceneLoader.LoadComic(introComic);
}
```

**With Scene Return:**
```csharp
public void ShowEndingComic()
{
    // Comic config has nextSceneName = "MainMenu"
    ComicSceneLoader.LoadComic(endingComic);
    // After comic, user goes to main menu
}
```

### From Main Menu

**Button Click:**
```csharp
public class MainMenu : MonoBehaviour
{
    public ComicSequenceConfig startComic;
    
    public void OnNewGameClicked()
    {
        // Show intro comic, then go to FPS_Collect
        ComicSceneLoader.LoadComic(startComic);
    }
}
```

### After Gameplay Event

**Boss Defeated:**
```csharp
public class BossController : MonoBehaviour
{
    public ComicSequenceConfig victoryComic;
    
    private void OnBossDefeated()
    {
        // Save progress
        PlayerDataManager.Instance.SetBossDefeated(true);
        
        // Show victory comic
        ComicSceneLoader.LoadComic(victoryComic);
    }
}
```

### Mid-Game Cutscene

**Trigger-Based:**
```csharp
public class CutsceneTrigger : MonoBehaviour
{
    public ComicSequenceConfig cutsceneComic;
    public bool hasTriggered = false;
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !hasTriggered)
        {
            hasTriggered = true;
            ComicSceneLoader.LoadComic(cutsceneComic);
        }
    }
}
```

---

## Extending the System

### Adding New Animations

**1. Add to PanelAnimationType enum:**
```csharp
public enum PanelAnimationType
{
    None,
    FadeIn,
    PanLeft,
    // ... existing ...
    Typewriter,  // NEW
    Glitch       // NEW
}
```

**2. Implement in CreateElementInPanel:**
```csharp
case PanelAnimationType.Typewriter:
    TypewriterComponent typewriter = img.gameObject.AddComponent<TypewriterComponent>();
    typewriter.text = element.text;
    typewriter.duration = element.animation.duration;
    typewriter.delay = element.animation.delay;
    break;

case PanelAnimationType.Glitch:
    GlitchComponent glitch = img.gameObject.AddComponent<GlitchComponent>();
    glitch.intensity = element.animation.intensity;
    glitch.duration = element.animation.duration;
    break;
```

**3. Create Animation Component:**
```csharp
public class TypewriterComponent : MonoBehaviour
{
    public string text;
    public float duration;
    public float delay;
    
    private TextMeshProUGUI textComponent;
    private float elapsed = 0f;
    
    private void Start()
    {
        textComponent = GetComponent<TextMeshProUGUI>();
        textComponent.text = "";
    }
    
    private void Update()
    {
        if (elapsed < delay)
        {
            elapsed += Time.deltaTime;
            return;
        }
        
        float progress = (elapsed - delay) / duration;
        int charCount = Mathf.RoundToInt(text.Length * progress);
        textComponent.text = text.Substring(0, Mathf.Min(charCount, text.Length));
        
        elapsed += Time.deltaTime;
    }
}
```

### Adding New Element Types

**Current:** Only Image/Sprite elements

**Add Text Elements:**

**1. Add to ComicElement:**
```csharp
public class ComicElement
{
    public enum ElementType { Sprite, Text }
    
    public ElementType elementType = ElementType.Sprite;
    
    // Sprite fields
    public Sprite sprite;
    
    // Text fields
    public string text;
    public Font font;
    public int fontSize = 24;
    public Color textColor = Color.white;
    
    // ... existing fields ...
}
```

**2. Update CreateElementInPanel:**
```csharp
private GameObject CreateElementInPanel(ComicElement element, RectTransform panelRect)
{
    GameObject elementObj = new GameObject(element.elementName);
    elementObj.transform.SetParent(panelRect, false);
    RectTransform rt = elementObj.AddComponent<RectTransform>();
    
    if (element.elementType == ComicElement.ElementType.Sprite)
    {
        Image img = elementObj.AddComponent<Image>();
        img.sprite = element.sprite;
        // ... existing sprite setup ...
    }
    else if (element.elementType == ComicElement.ElementType.Text)
    {
        TextMeshProUGUI text = elementObj.AddComponent<TextMeshProUGUI>();
        text.text = element.text;
        text.font = element.font;
        text.fontSize = element.fontSize;
        text.color = element.textColor;
        // ... text sizing ...
    }
    
    // ... existing animation/positioning ...
}
```

### Adding Video Support

**1. Add VideoClip field:**
```csharp
public class ComicElement
{
    public enum ElementType { Sprite, Text, Video }
    
    public VideoClip videoClip;
    public bool loopVideo = true;
}
```

**2. Create Video Player:**
```csharp
if (element.elementType == ComicElement.ElementType.Video)
{
    RawImage rawImg = elementObj.AddComponent<RawImage>();
    VideoPlayer videoPlayer = elementObj.AddComponent<VideoPlayer>();
    
    videoPlayer.clip = element.videoClip;
    videoPlayer.isLooping = element.loopVideo;
    videoPlayer.renderMode = VideoRenderMode.RenderTexture;
    
    RenderTexture rt = new RenderTexture(1920, 1080, 0);
    videoPlayer.targetTexture = rt;
    rawImg.texture = rt;
    
    videoPlayer.Play();
}
```

### Dynamic Content

**Load sprite at runtime:**
```csharp
public class ComicElement
{
    public string spriteResourcePath;  // "Comics/Character1"
    
    public Sprite GetSprite()
    {
        if (sprite != null)
            return sprite;
        
        return Resources.Load<Sprite>(spriteResourcePath);
    }
}
```

**Player name in text:**
```csharp
public string text = "Hello, {PlayerName}!";

// In CreateElementInPanel:
string displayText = text.Replace("{PlayerName}", PlayerDataManager.Instance.PlayerName);
textComponent.text = displayText;
```

**Conditional panels:**
```csharp
public class ComicPanel
{
    public string conditionId;  // "has_defeated_boss"
    
    public bool ShouldShow()
    {
        if (string.IsNullOrEmpty(conditionId))
            return true;
        
        return PlayerDataManager.Instance.MeetsCondition(conditionId);
    }
}

// In CreateAllPanels:
foreach (var panel in config.panels)
{
    if (!panel.ShouldShow())
        continue;  // Skip this panel
    
    CreatePanel(panel);
}
```

---

## Performance Considerations

### Element Count
**Recommended Limits:**
```
Per Panel: 10-15 elements (comfortable)
           20-30 elements (acceptable)
           50+ elements (performance issues)

Total Comic: 100-200 elements across all panels
```

### Texture Memory
**Optimization:**
- Use TextureAtlas for many small sprites
- Compress textures (ASTC/ETC2 on mobile)
- Unload comics after viewing: `Resources.UnloadUnusedAssets()`

### Scrolling Performance
```csharp
// Efficient: Update only scrolling container
scrollingContainer.anchoredPosition = Vector2.Lerp(
    scrollStartPos,
    scrollTargetPos,
    scrollProgress
);

// Inefficient: Don't update every element individually
```

### Async Asset Loading
```csharp
public IEnumerator LoadComicAsync(ComicSequenceConfig config)
{
    // Load sprites asynchronously
    foreach (var panel in config.panels)
    {
        foreach (var element in panel.elements)
        {
            ResourceRequest request = Resources.LoadAsync<Sprite>(element.spriteResourcePath);
            yield return request;
            element.sprite = request.asset as Sprite;
        }
    }
    
    ComicSceneLoader.LoadComic(config);
}
```

---

## Integration Examples

### Quest System
```csharp
public class QuestManager : MonoBehaviour
{
    public ComicSequenceConfig questStartComic;
    public ComicSequenceConfig questCompleteComic;
    
    public void StartQuest(int questId)
    {
        // Show quest introduction comic
        ComicSceneLoader.LoadComic(questStartComic);
    }
    
    public void CompleteQuest(int questId)
    {
        // Show quest completion comic
        ComicSceneLoader.LoadComic(questCompleteComic);
    }
}
```

### Dialogue System
```csharp
public class DialogueManager : MonoBehaviour
{
    public ComicSequenceConfig CreateDialogueComic(DialogueData dialogue)
    {
        ComicSequenceConfig comic = ScriptableObject.CreateInstance<ComicSequenceConfig>();
        comic.sequenceId = $"dialogue_{dialogue.id}";
        comic.panels = new List<ComicPanel>();
        
        foreach (var line in dialogue.lines)
        {
            ComicPanel panel = new ComicPanel();
            panel.widthMode = PanelWidthMode.HalfScreen;
            
            // Background
            panel.elements.Add(new ComicElement
            {
                sprite = line.speakerPortrait,
                sizeMode = ElementSizeMode.FillPanel,
                layer = 0
            });
            
            // Text
            panel.elements.Add(new ComicElement
            {
                text = line.text,
                elementType = ComicElement.ElementType.Text,
                position = new Vector2(0.5f, 0.2f),
                layer = 30
            });
            
            comic.panels.Add(panel);
        }
        
        return comic;
    }
}
```

### Achievement Unlocked
```csharp
public class AchievementManager : MonoBehaviour
{
    public ComicSequenceConfig achievementTemplate;
    
    public void ShowAchievement(string achievementName, Sprite icon)
    {
        ComicSequenceConfig comic = Instantiate(achievementTemplate);
        
        // Modify first panel to show achievement
        ComicPanel panel = comic.panels[0];
        panel.elements[0].sprite = icon;  // Icon
        panel.elements[1].text = $"Achievement Unlocked: {achievementName}";
        panel.autoDuration = 3f;  // Auto-close after 3s
        
        ComicSceneLoader.LoadComic(comic);
    }
}
```

---

## Troubleshooting

**Panels not scrolling:**
- Verify scrollingContainer is assigned in Inspector
- Check panel widths are > 0
- Ensure currentScrollOffset is updating

**Elements not visible:**
- Check sprite is assigned
- Verify layer order (Background=0, UI=30)
- Check position is within (0-1, 0-1) range

**Animations not playing:**
- Verify animation duration > 0
- Check animation components are added
- Ensure Time.timeScale ≠ 0

**Memory issues:**
- Reduce texture sizes
- Use compression
- Call Resources.UnloadUnusedAssets() after comic

**Wrong scene loads after comic:**
- Check ComicSequenceConfig.nextSceneName
- Verify scene is in Build Settings
- Check scene name spelling

---

## Best Practices

**Comic Layout:**
- Start with FullScreen panel for establishing shot
- Use Custom panels for dialogue/text
- End with FullScreen panel for dramatic closure

**Element Sizing:**
- FillPanel for backgrounds only
- Custom for precise UI elements
- FitToSprite for characters/objects

**Animation Timing:**
- Stagger animations with delays (0.2s-0.5s apart)
- Keep total panel duration < 10s (user attention)
- Use autoDuration for non-interactive panels

**Performance:**
- Keep element count < 20 per panel
- Preload sprites for smoother experience
- Test on target hardware

**User Experience:**
- Always show [Enter] prompt for interactive panels
- Allow skipping with ESC key
- Auto-advance for simple transitions
