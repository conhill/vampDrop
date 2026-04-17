using UnityEngine;
using UnityEngine.SceneManagement;
using Vampire.Player;
using Unity.Entities;

namespace Vampire
{
    /// <summary>
    /// Manages scene transitions between FPS collection and Drop Puzzle stages
    /// </summary>
    public class GameSceneManager : MonoBehaviour
    {
        [Header("Scene Names")]
        public string FPSSceneName = "FPS_Collect";
        public string DropPuzzleSceneName = "DropPuzzle";
        
        [Header("Transition Settings")]
        [Tooltip("Key to transition to Drop Puzzle from FPS")]
        public KeyCode TransitionKey = KeyCode.F3;
        
        private static GameSceneManager instance;
        public static GameSceneManager Instance => instance;
        
        private int collectedRiceCount = 0;
        
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                // Always hide/show rice regardless of which code path loads the scene
                SceneManager.sceneLoaded += OnAnySceneLoaded;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnAnySceneLoaded;
        }

        private void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == DropPuzzleSceneName)
                SetRiceEntitiesEnabled(false);
            else if (scene.name == FPSSceneName)
                SetRiceEntitiesEnabled(true);
        }

        private void SetRiceEntitiesEnabled(bool enabled)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em = world.EntityManager;
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All     = new[] { ComponentType.ReadOnly<Vampire.Rice.RiceEntity>() },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
            if (query.IsEmpty) { query.Dispose(); return; }
            if (enabled)
                em.RemoveComponent<Disabled>(query);
            else
                em.AddComponent<Disabled>(query);
            query.Dispose();
        }
        
        private float lastLogTime = 0f;
        
        private void Update()
        {
            // Get current scene once
            string currentScene = SceneManager.GetActiveScene().name;
            
            if (currentScene == FPSSceneName)
            {
                if (Input.GetKeyDown(TransitionKey))
                {
                    // Debug.Log($"[GameSceneManager] Transitioning from {FPSSceneName} to {DropPuzzleSceneName}...");
                    TransitionToDropPuzzle();
                }
            }
            else
            {
                if (Input.GetKeyDown(TransitionKey))
                {
                    // Debug.Log($"[GameSceneManager] Transition key pressed but not in FPS scene. Current: {currentScene}, Expected: {FPSSceneName}");
                }
            }
        }
        
        /// <summary>
        /// Transition from FPS stage to Drop Puzzle
        /// </summary>
        public void TransitionToDropPuzzle()
        {
            // Rice hiding is handled by OnAnySceneLoaded — no manual entity work needed here
            SceneManager.sceneLoaded += OnDropPuzzleSceneLoaded;
            SceneManager.LoadScene(DropPuzzleSceneName);
        }
        
        private void OnDropPuzzleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == DropPuzzleSceneName)
            {
                SceneManager.sceneLoaded -= OnDropPuzzleSceneLoaded;
                
                // Pass collected rice to drop puzzle manager
                var dropManager = FindFirstObjectByType<DropPuzzle.DropPuzzleManager>();
                if (dropManager != null)
                {
                    dropManager.LoadFromFPSStage(collectedRiceCount);
                }
                
                // Debug.Log($"[GameSceneManager] ✅ Drop Puzzle loaded with {collectedRiceCount} rice balls");
            }
        }
        
        /// <summary>
        /// Return to FPS stage
        /// </summary>
        public void ReturnToFPS()
        {
            // Rice showing is handled by OnAnySceneLoaded
            SceneManager.LoadScene(FPSSceneName);
        }
    }
}
