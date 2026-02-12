using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Controls lighting and visual effects for the doorway to "outside"
    /// Shows daylight during day, darkness at night
    /// Attach to the doorway/exit zone object
    /// </summary>
    public class DoorwayLighting : MonoBehaviour
    {
        [Header("Lighting Components")]
        [Tooltip("Light component to control (auto-creates Point Light if null)")]
        public Light doorwayLight;
        
        [Tooltip("IMPORTANT: Assign a RENDERER (MeshRenderer/QuadRenderer) with emissive material. Should be a Quad/Plane positioned in the doorway opening")]
        public Renderer doorwayRenderer;
        
        [Tooltip("Leave null - will auto-use material from doorwayRenderer")]
        public Material doorwayMaterial;
        
        [Header("Day Settings")]
        [Tooltip("Light intensity during day")]
        public float dayIntensity = 2.0f;
        
        [Tooltip("Light color during day (warm sunlight)")]
        public Color dayColor = new Color(1f, 0.95f, 0.8f);
        
        [Tooltip("Material emission color during day")]
        public Color dayEmissionColor = new Color(1f, 0.9f, 0.7f);
        
        [Header("Night Settings")]
        [Tooltip("Light intensity during night")]
        public float nightIntensity = 0.2f;
        
        [Tooltip("Light color during night (cool moonlight)")]
        public Color nightColor = new Color(0.5f, 0.6f, 0.8f);
        
        [Tooltip("Material emission color during night")]
        public Color nightEmissionColor = new Color(0.1f, 0.15f, 0.25f);
        
        [Header("Transition Settings")]
        [Tooltip("Smooth transition duration in seconds")]
        public float transitionDuration = 2.0f;
        
        private DayNightCycleManager cycleManager;
        private float currentIntensity;
        private Color currentColor;
        private Color currentEmission;
        
        private float targetIntensity;
        private Color targetColor;
        private Color targetEmission;
        
        private float transitionProgress = 1f; // Start fully transitioned
        
        private void Start()
        {
            cycleManager = DayNightCycleManager.Instance;
            
            // AUTO-SETUP: Try to find renderer on this GameObject first
            if (doorwayRenderer == null)
            {
                doorwayRenderer = GetComponent<Renderer>();
                if (doorwayRenderer != null)
                {
                    Debug.Log("[DoorwayLighting] ✅ Auto-found Renderer on this GameObject!");
                }
            }
            
            // Get material from renderer
            if (doorwayRenderer != null && doorwayMaterial == null)
            {
                doorwayMaterial = doorwayRenderer.material; // Creates instance automatically
                Debug.Log($"[DoorwayLighting] Using material: {doorwayMaterial.name}");
                
                // Fix the material shader if it's pink (missing shader)
                if (doorwayMaterial.shader.name.Contains("Hidden") || doorwayMaterial.shader.name.Contains("Error"))
                {
                    Debug.LogWarning("[DoorwayLighting] ⚠️ Material has broken shader! Fixing...");
                    FixMaterialShader();
                }
                
                // Set initial color
                SetMaterialColor(dayEmissionColor);
            }
            
            // Auto-create light if not assigned
            if (doorwayLight == null)
            {
                // Check if light already exists as child
                doorwayLight = GetComponentInChildren<Light>();
                
                if (doorwayLight == null)
                {
                    GameObject lightObj = new GameObject("DoorwayLight");
                    lightObj.transform.SetParent(transform);
                    lightObj.transform.localPosition = Vector3.zero;
                    doorwayLight = lightObj.AddComponent<Light>();
                    doorwayLight.type = LightType.Point;
                    doorwayLight.range = 10f;
                    doorwayLight.shadows = LightShadows.Soft;
                    
                    Debug.Log("[DoorwayLighting] ✅ Auto-created Point Light");
                }
                else
                {
                    Debug.Log("[DoorwayLighting] ✅ Found existing Light");
                }
            }
            
            if (cycleManager != null)
            {
                // Subscribe to day/night events
                cycleManager.OnDayStart += OnDayStart;
                cycleManager.OnNightStart += OnNightStart;
                
                // Set initial state immediately (no transition)
                SetImmediateState(cycleManager.currentTime);
            }
            else
            {
                Debug.LogWarning("[DoorwayLighting] No DayNightCycleManager found!");
                SetImmediateState(DayNightCycleManager.TimeOfDay.Day);
            }
        }
        
        private void OnDestroy()
        {
            if (cycleManager != null)
            {
                cycleManager.OnDayStart -= OnDayStart;
                cycleManager.OnNightStart -= OnNightStart;
            }
        }
        
        private void Update()
        {
            // Smooth transition
            if (transitionProgress < 1f)
            {
                transitionProgress += Time.deltaTime / transitionDuration;
                transitionProgress = Mathf.Clamp01(transitionProgress);
                
                // Lerp to target values
                currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, transitionProgress);
                currentColor = Color.Lerp(currentColor, targetColor, transitionProgress);
                currentEmission = Color.Lerp(currentEmission, targetEmission, transitionProgress);
                
                ApplyLighting();
            }
        }
        
        private void SetImmediateState(DayNightCycleManager.TimeOfDay timeOfDay)
        {
            if (timeOfDay == DayNightCycleManager.TimeOfDay.Day)
            {
                currentIntensity = dayIntensity;
                currentColor = dayColor;
                currentEmission = dayEmissionColor;
            }
            else
            {
                currentIntensity = nightIntensity;
                currentColor = nightColor;
                currentEmission = nightEmissionColor;
            }
            
            targetIntensity = currentIntensity;
            targetColor = currentColor;
            targetEmission = currentEmission;
            transitionProgress = 1f;
            
            ApplyLighting();
        }
        
        private void StartTransition(DayNightCycleManager.TimeOfDay targetTimeOfDay)
        {
            if (targetTimeOfDay == DayNightCycleManager.TimeOfDay.Day)
            {
                targetIntensity = dayIntensity;
                targetColor = dayColor;
                targetEmission = dayEmissionColor;
            }
            else
            {
                targetIntensity = nightIntensity;
                targetColor = nightColor;
                targetEmission = nightEmissionColor;
            }
            
            transitionProgress = 0f;
        }
        
        private void ApplyLighting()
        {
            if (doorwayLight != null)
            {
                doorwayLight.intensity = currentIntensity;
                doorwayLight.color = currentColor;
            }
            
            SetMaterialColor(currentEmission);
        }
        
        private void SetMaterialColor(Color color)
        {
            if (doorwayMaterial == null) return;
            
            Color col = color;
            col.a = 0.8f; // Semi-transparent
            
            // Try all possible color property names (Standard, URP, HDRP, Unlit)
            if (doorwayMaterial.HasProperty("_Color"))
            {
                doorwayMaterial.SetColor("_Color", col);
            }
            
            if (doorwayMaterial.HasProperty("_BaseColor"))
            {
                doorwayMaterial.SetColor("_BaseColor", col);
            }
            
            if (doorwayMaterial.HasProperty("_TintColor"))
            {
                doorwayMaterial.SetColor("_TintColor", col);
            }
            
            // Set emission if material supports it
            if (doorwayMaterial.HasProperty("_EmissionColor"))
            {
                doorwayMaterial.SetColor("_EmissionColor", color * 3f);
                doorwayMaterial.EnableKeyword("_EMISSION");
            }
        }
        
        private void FixMaterialShader()
        {
            if (doorwayMaterial == null) return;
            
            // Try shaders in order of preference
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            
            if (shader != null)
            {
                doorwayMaterial.shader = shader;
                Debug.Log($"[DoorwayLighting] ✅ Fixed shader to: {shader.name}");
            }
            else
            {
                Debug.LogError("[DoorwayLighting] ❌ Could not find any working shader!");
            }
        }
        
        private void OnDayStart()
        {
            Debug.Log("[DoorwayLighting] ☀️ Transitioning to DAY lighting");
            StartTransition(DayNightCycleManager.TimeOfDay.Day);
        }
        
        private void OnNightStart()
        {
            Debug.Log("[DoorwayLighting] 🌙 Transitioning to NIGHT lighting");
            StartTransition(DayNightCycleManager.TimeOfDay.Night);
        }
        
        /// <summary>
        /// Manual control for testing
        /// </summary>
        [ContextMenu("Test Day Lighting")]
        public void TestDayLighting()
        {
            StartTransition(DayNightCycleManager.TimeOfDay.Day);
        }
        
        /// <summary>
        /// Manual control for testing
        /// </summary>
        [ContextMenu("Test Night Lighting")]
        public void TestNightLighting()
        {
            StartTransition(DayNightCycleManager.TimeOfDay.Night);
        }
        
        /// <summary>
        /// Fix pink/broken material (call this if material is pink)
        /// </summary>
        [ContextMenu("Fix: Repair Broken Material")]
        public void RepairMaterial()
        {
            if (doorwayRenderer == null)
            {
                doorwayRenderer = GetComponent<Renderer>();
            }
            
            if (doorwayRenderer != null)
            {
                doorwayMaterial = doorwayRenderer.material;
                FixMaterialShader();
                SetMaterialColor(dayEmissionColor);
                Debug.Log("[DoorwayLighting] ✅ Material repaired! Try Test Day/Night Lighting now.");
            }
            else
            {
                Debug.LogError("[DoorwayLighting] ❌ No Renderer found! Attach this script to a GameObject with a MeshRenderer/Quad.");
            }
        }
    }
}
