using UnityEngine;
using UnityEngine.SceneManagement;
using Vampire.Player;

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
                Debug.Log("[GameSceneManager] ✅ Created and persisting across scenes");
            }
            else
            {
                Debug.Log("[GameSceneManager] Duplicate instance found, destroying");
                Destroy(gameObject);
            }
        }
        
        private float lastLogTime = 0f;
        
        private void Update()
        {
            // Get current scene once
            string currentScene = SceneManager.GetActiveScene().name;
            
            // Debug heartbeat every 5 seconds
            if (Time.time - lastLogTime > 5f)
            {
                lastLogTime = Time.time;
                Debug.Log($"[GameSceneManager] Update running. Current scene: {currentScene}, Transition key: {TransitionKey}");
            }
            
            // Check for ANY key press
            if (Input.anyKeyDown)
            {
                Debug.Log($"[GameSceneManager] Key pressed detected!");
            }
            
            // Specifically test F3
            if (Input.GetKeyDown(KeyCode.F3))
            {
                Debug.Log($"[GameSceneManager] F3 DETECTED!");
            }
            
            if (Input.GetKeyDown(TransitionKey))
            {
                Debug.Log($"[GameSceneManager] Transition key ({TransitionKey}) pressed! Current scene: {currentScene}");
            }
            
            if (currentScene == FPSSceneName)
            {
                if (Input.GetKeyDown(TransitionKey))
                {
                    Debug.Log($"[GameSceneManager] Transitioning from {FPSSceneName} to {DropPuzzleSceneName}...");
                    TransitionToDropPuzzle();
                }
            }
            else
            {
                if (Input.GetKeyDown(TransitionKey))
                {
                    Debug.Log($"[GameSceneManager] Transition key pressed but not in FPS scene. Current: {currentScene}, Expected: {FPSSceneName}");
                }
            }
        }
        
        /// <summary>
        /// Transition from FPS stage to Drop Puzzle
        /// </summary>
        public void TransitionToDropPuzzle()
        {
            Debug.Log("[GameSceneManager] === TRANSITION STARTED ===");
            
            // Get collected rice count from FPS stage
            var playerEntity = UnityEngine.Object.FindFirstObjectByType<PlayerAuthoring>();
            Debug.Log($"[GameSceneManager] PlayerAuthoring found: {playerEntity != null}");
            
            if (playerEntity != null)
            {
                // Try to get rice count from ECS
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                Debug.Log($"[GameSceneManager] ECS World found: {world != null}");
                
                if (world != null)
                {
                    var entityManager = world.EntityManager;
                    var query = entityManager.CreateEntityQuery(typeof(Player.PlayerData));
                    int playerCount = query.CalculateEntityCount();
                    Debug.Log($"[GameSceneManager] Player entities found: {playerCount}");
                    
                    if (playerCount > 0)
                    {
                        var playerData = query.GetSingleton<Player.PlayerData>();
                        collectedRiceCount = playerData.RiceCollected;
                        Debug.Log($"[GameSceneManager] ✅ Collected rice count: {collectedRiceCount}");
                    }
                    else
                    {
                        Debug.LogWarning("[GameSceneManager] No player entity found, using 0 rice");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[GameSceneManager] No PlayerAuthoring found, using 0 rice");
            }
            
            // Load drop puzzle scene
            Debug.Log($"[GameSceneManager] Loading scene: {DropPuzzleSceneName}...");
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
                
                Debug.Log($"[GameSceneManager] ✅ Drop Puzzle loaded with {collectedRiceCount} rice balls");
            }
        }
        
        /// <summary>
        /// Return to FPS stage
        /// </summary>
        public void ReturnToFPS()
        {
            SceneManager.LoadScene(FPSSceneName);
            Debug.Log("[GameSceneManager] Returning to FPS stage");
        }
    }
}
