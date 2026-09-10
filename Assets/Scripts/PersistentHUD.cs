using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Vampire
{
    /// <summary>
    /// Keeps the overworld HUD (UIDocument + UIController) alive across scene loads so it shows in
    /// the FPS/collection scene AND the house (Base). It hides itself (without rebuilding the visual
    /// tree, so UIController's cached element refs stay valid) in scenes that have their own HUD
    /// (DropPuzzle) or no HUD (Comic).
    ///
    /// Put this on the same GameObject as the HUD's UIDocument in the FPS scene.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PersistentHUD : MonoBehaviour
    {
        private static PersistentHUD _instance;
        private UIDocument _doc;

        [Tooltip("Scene names where the overworld HUD should be visible.")]
        public string[] visibleInScenes = { "FPS_Collect", "Base" };

        private void Awake()
        {
            // Singleton — a reloaded FPS scene brings a fresh copy; keep the original, drop the dupe.
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _doc = GetComponent<UIDocument>();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void Start() => Apply(SceneManager.GetActiveScene().name);

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply(scene.name);

        private void Apply(string sceneName)
        {
            if (_doc == null || _doc.rootVisualElement == null) return;
            bool show = System.Array.IndexOf(visibleInScenes, sceneName) >= 0;
            // Toggle display (not the UIDocument component) so the visual tree isn't rebuilt and
            // UIController's cached element references remain valid.
            _doc.rootVisualElement.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
