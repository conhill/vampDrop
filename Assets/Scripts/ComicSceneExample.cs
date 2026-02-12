using UnityEngine;

namespace Vampire
{
    /// <summary>
    /// Example showing how to use comic sequence configs
    /// 
    /// CREATING COMIC CONFIGS:
    /// 1. Right-click in Project → Create → Vampire → Comic Sequence Config
    /// 2. Name it (e.g., "IntroComic", "AfterGoingOutsideComic")
    /// 3. Configure in Inspector:
    ///    - Sequence ID: Unique identifier (e.g., "intro", "after_going_outside")
    ///    - Sequence Name: Display name for debugging
    ///    - Panels: Add panels (each panel = 1 screen/page)
    ///      - For each panel, add elements (backgrounds, text images, characters)
    ///      - Set positions (0-1 normalized), animations, timing
    ///    - Next Scene Name: Where to go after comic completes
    /// 
    /// LOADING COMICS:
    /// ComicSceneLoader.LoadComic(myComicConfig);
    /// </summary>
    public class ComicSceneExample : MonoBehaviour
    {
        [Header("Example Configs (Assign in Inspector)")]
        [Tooltip("The intro comic config")]
        public ComicSequenceConfig introComic;
        
        [Tooltip("After going outside comic config")]
        public ComicSequenceConfig afterGoingOutsideComic;
        
        [Tooltip("After coming inside comic config")]
        public ComicSequenceConfig afterComingInsideComic;
        
        /// <summary>
        /// Example: Load intro comic from main menu
        /// </summary>
        public void StartGameWithIntro()
        {
            if (introComic != null)
            {
                ComicSceneLoader.LoadComic(introComic);
            }
            else
            {
                Debug.LogWarning("Intro comic config not assigned!");
            }
        }
        
        /// <summary>
        /// Example: Load "after going outside" comic
        /// Call this from BallDropUI.Start() on first visit
        /// </summary>
        public void ShowAfterGoingOutsideComic()
        {
            if (afterGoingOutsideComic != null)
            {
                ComicSceneLoader.LoadComic(afterGoingOutsideComic);
            }
        }
        
        /// <summary>
        /// Example: Load "after coming inside" comic
        /// Call this from BallDropUI.GoBackToFPS() after first drop
        /// </summary>
        public void ShowAfterComingInsideComic()
        {
            if (afterComingInsideComic != null)
            {
                ComicSceneLoader.LoadComic(afterComingInsideComic);
            }
        }
    }
}

/*
 * EXAMPLE CONFIG SETUP (you'll do this in the Unity Inspector, not code):
 * 
 * ===== INTRO COMIC CONFIG =====
 * Create: Right-click → Create → Vampire → Comic Sequence Config → Name: "IntroComic"
 * 
 * Settings:
 *   Sequence ID: "intro"
 *   Sequence Name: "Game Intro"
 *   Next Scene Name: "FPS_Collect"
 * 
 * Panels:
 *   Panel 1:
 *     Panel Name: "Opening Scene"
 *     Auto Duration: 0 (wait for input)
 *     Elements:
 *       Element 1 (Background):
 *         Element Name: "Background"
 *         Sprite: [Your intro background image]
 *         Normalized Position: X=0.5, Y=0.5 (center)
 *         Element Scale: X=1, Y=1, Z=1
 *         Layer: Background (0)
 *         Animation: PanDown
 *         Animation Duration: 2
 *         Appear Delay: 0
 *         
 *       Element 2 (Text):
 *         Element Name: "Title Text"
 *         Sprite: [Your text/dialogue image]
 *         Normalized Position: X=0.8, Y=0.2 (top-right)
 *         Element Scale: X=0.5, Y=0.5, Z=1
 *         Layer: UI (30)
 *         Animation: FadeIn
 *         Animation Duration: 1
 *         Appear Delay: 2 (wait for background first)
 *   
 *   Panel 2:
 *     Panel Name: "Story Setup"
 *     Auto Duration: 5 (auto-advance)
 *     Elements:
 *       Element 1 (Background):
 *         Sprite: [Background image]
 *         Position: X=0.5, Y=0.5
 *         Animation: ZoomIn, Duration: 3
 *       
 *       Element 2 (Character):
 *         Sprite: [Character sprite]
 *         Position: X=0.3, Y=0.3
 *         Scale: X=0.5, Y=0.5, Z=1
 *         Layer: Foreground (20)
 *         Animation: SlideInLeft, Duration: 1
 *       
 *       Element 3 (Dialogue):
 *         Sprite: [Dialogue text image]
 *         Position: X=0.5, Y=0.8
 *         Layer: UI (30)
 *         Animation: FadeIn, Duration: 0.5, Appear Delay: 1
 * 
 * 
 * ===== AFTER GOING OUTSIDE COMIC =====
 * Create: "AfterGoingOutsideComic"
 * 
 * Settings:
 *   Sequence ID: "after_going_outside"
 *   Sequence Name: "First Time Outside"
 *   Next Scene Name: "DropPuzzle"
 * 
 * Panels:
 *   Panel 1:
 *     Auto Duration: 0
 *     Elements:
 *       - Background: [Your "outside" background]
 *       - Text: [Image with "Time to drop rice balls..." text]
 * 
 * 
 * ===== AFTER COMING INSIDE COMIC =====
 * Create: "AfterComingInsideComic"
 * 
 * Settings:
 *   Sequence ID: "after_coming_inside"
 *   Sequence Name: "Returning Home"
 *   Next Scene Name: "FPS_Collect"
 * 
 * Panels:
 *   Panel 1:
 *     Auto Duration: 0
 *     Elements:
 *       - Background: [Your "inside" background]
 *       - Text: [Image with "Back to collecting rice..." text]
 */
