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
            // Persist across scenes
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                // Debug.Log("[GameSceneManager] ✅ Created and persisting across scenes");
            }
            else
            {
                // Debug.Log("[GameSceneManager] Duplicate instance found, destroying");
                Destroy(gameObject);
            }
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
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                var entityManager = world.EntityManager;

                // Use the full path to the TAG struct defined in RiceSpawnComponents.cs
                // This avoids the "Vampire.Rice is a namespace" conflict
                var riceQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new [] { ComponentType.ReadOnly<Vampire.Rice.RiceSpawned>() }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities 
                });

                if (!riceQuery.IsEmpty)
                {
                    // Adding the Disabled component hides them from rendering and physics systems
                    entityManager.AddComponent<Unity.Entities.Disabled>(riceQuery);
                    // Debug.Log($"[GameSceneManager] 🧊 {riceQuery.CalculateEntityCount()} rice entities hibernated.");
                }
            }

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
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                var entityManager = world.EntityManager;
                
                var riceQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new [] { ComponentType.ReadOnly<Vampire.Rice.RiceSpawned>() }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities 
                });
                // SHOW: Remove the Disabled tag
                 if (!riceQuery.IsEmpty)
                {
                    // Adding the Disabled component hides them from rendering and physics systems
                     entityManager.RemoveComponent<Unity.Entities.Disabled>(riceQuery);
                }
                
                // Debug.Log("[GameSceneManager] ✨ Rice entities restored.");
            }
            SceneManager.LoadScene(FPSSceneName);
            // Debug.Log("[GameSceneManager] Returning to FPS stage");
        }
    }
}
