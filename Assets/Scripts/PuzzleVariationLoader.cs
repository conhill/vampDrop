using UnityEngine;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Loads and builds puzzle variations dynamically
    /// No prebaked scenes - everything spawned at runtime
    /// </summary>
    public class PuzzleVariationLoader : MonoBehaviour
    {
        [Header("Puzzle Configuration")]
        [Tooltip("The variation to load (create via right-click > Create > Vampire > Puzzle Variation)")]
        public PuzzleVariation CurrentVariation;
        
        [Header("Prefabs")]
        public GameObject WallPrefab;
        public GameObject MultiplierGatePrefab;
        public GameObject GoalGatePrefab;
        
        [Header("Materials")]
        public Material WallMaterial;
        public Material[] MultiplierMaterials; // Different colors for different multipliers
        
        private List<GameObject> spawnedObjects = new List<GameObject>();
        
        private void Start()
        {
            if (CurrentVariation == null)
            {
                Debug.LogError("[PuzzleVariationLoader] No variation assigned! Create one via Assets > Create > Vampire > Puzzle Variation");
                return;
            }
            
            Debug.Log($"[PuzzleVariationLoader] Loading variation: {CurrentVariation.VariationName} ({CurrentVariation.Layout})");
            LoadVariation();
        }
        
        public void LoadVariation()
        {
            ClearPuzzle();
            
            // Build walls
            BuildWalls();
            
            // Build multipliers
            BuildMultipliers();
            
            // Build goal gate
            BuildGoalGate();
            
            Debug.Log($"[PuzzleVariationLoader] ✅ Loaded {CurrentVariation.VariationName} - {spawnedObjects.Count} objects spawned");
        }
        
        private void BuildWalls()
        {
            if (CurrentVariation.Walls == null || CurrentVariation.Walls.Length == 0)
            {
                Debug.LogWarning("[PuzzleVariationLoader] No walls configured");
                return;
            }
            
            foreach (var wallConfig in CurrentVariation.Walls)
            {
                GameObject wall;
                
                if (WallPrefab != null)
                {
                    wall = Instantiate(WallPrefab, wallConfig.Position, Quaternion.Euler(wallConfig.Rotation));
                }
                else
                {
                    // Create simple wall cube
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = wallConfig.Position;
                    wall.transform.rotation = Quaternion.Euler(wallConfig.Rotation);
                    
                    if (WallMaterial != null)
                    {
                        wall.GetComponent<Renderer>().material = WallMaterial;
                    }
                }
                
                wall.transform.localScale = wallConfig.Scale;
                wall.name = $"Wall_{wallConfig.Type}";
                wall.transform.SetParent(transform);
                spawnedObjects.Add(wall);
            }
            
            Debug.Log($"[PuzzleVariationLoader] Built {CurrentVariation.Walls.Length} walls");
        }
        
        private void BuildMultipliers()
        {
            if (CurrentVariation.Multipliers == null || CurrentVariation.Multipliers.Length == 0)
            {
                Debug.LogWarning("[PuzzleVariationLoader] No multipliers configured");
                return;
            }
            
            foreach (var multConfig in CurrentVariation.Multipliers)
            {
                GameObject multGate;
                
                if (MultiplierGatePrefab != null)
                {
                    multGate = Instantiate(MultiplierGatePrefab, multConfig.Position, Quaternion.identity);
                }
                else
                {
                    // Create simple trigger zone
                    multGate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    multGate.transform.position = multConfig.Position;
                    multGate.transform.localScale = multConfig.Size;
                    
                    // Make it a trigger
                    Collider col = multGate.GetComponent<Collider>();
                    col.isTrigger = true;
                    
                    // Color code based on multiplier
                    Renderer renderer = multGate.GetComponent<Renderer>();
                    if (MultiplierMaterials != null && MultiplierMaterials.Length > 0)
                    {
                        int matIndex = Mathf.Min(multConfig.Multiplier - 2, MultiplierMaterials.Length - 1);
                        if (matIndex >= 0)
                        {
                            renderer.material = MultiplierMaterials[matIndex];
                        }
                    }
                    else
                    {
                        // Default color coding
                        Material mat = new Material(Shader.Find("Standard"));
                        mat.color = GetMultiplierColor(multConfig.Multiplier);
                        renderer.material = mat;
                    }
                }
                
                // Add/configure multiplier component
                MultiplierGate multComponent = multGate.GetComponent<MultiplierGate>();
                if (multComponent == null)
                {
                    multComponent = multGate.AddComponent<MultiplierGate>();
                }
                multComponent.Multiplier = multConfig.Multiplier;
                
                multGate.name = $"Multiplier_x{multConfig.Multiplier}";
                multGate.transform.SetParent(transform);
                spawnedObjects.Add(multGate);
            }
            
            Debug.Log($"[PuzzleVariationLoader] Built {CurrentVariation.Multipliers.Length} multiplier gates");
        }
        
        private void BuildGoalGate()
        {
            GameObject goal;
            
            if (GoalGatePrefab != null)
            {
                goal = Instantiate(GoalGatePrefab, CurrentVariation.GoalPosition, Quaternion.identity);
            }
            else
            {
                goal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                goal.transform.position = CurrentVariation.GoalPosition;
                goal.transform.localScale = CurrentVariation.GoalSize;
                
                Collider col = goal.GetComponent<Collider>();
                col.isTrigger = true;
                
                Renderer renderer = goal.GetComponent<Renderer>();
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = Color.green;
                renderer.material = mat;
            }
            
            // Add/configure goal gate component
            GoalGate goalComponent = goal.GetComponent<GoalGate>();
            if (goalComponent == null)
            {
                goalComponent = goal.AddComponent<GoalGate>();
            }
            
            goal.name = "GoalGate";
            goal.transform.SetParent(transform);
            spawnedObjects.Add(goal);
            
            Debug.Log($"[PuzzleVariationLoader] Built goal gate at {CurrentVariation.GoalPosition}");
        }
        
        private Color GetMultiplierColor(int multiplier)
        {
            // Color coding like Cup Heroes
            switch (multiplier)
            {
                case 2: return new Color(1f, 0.65f, 0f); // Orange
                case 3: return Color.yellow;
                case 4: return Color.green;
                case 5: return Color.cyan;
                case 6: return Color.blue;
                case 7: return new Color(0.5f, 0f, 1f); // Purple
                default: return Color.magenta;
            }
        }
        
        public void ClearPuzzle()
        {
            foreach (var obj in spawnedObjects)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            spawnedObjects.Clear();
        }
        
        private void OnDestroy()
        {
            ClearPuzzle();
        }
    }
}
