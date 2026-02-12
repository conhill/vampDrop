using UnityEngine;

namespace Vampire.Rice
{
    /// <summary>
    /// Configuration for rice rendering - attach to a GameObject in scene
    /// Drag your rice mesh model here in the Inspector
    /// </summary>
    public class RiceRenderingConfig : MonoBehaviour
    {
        [Header("Rice Visual Settings")]
        [Tooltip("The mesh to use for rice (e.g., rice_grain.stl.obj)")]
        public Mesh RiceMesh;
        
        [Tooltip("Material for rice rendering (gets instanced)")]
        public Material RiceMaterial;
        
        [Tooltip("Scale multiplier for rice grains")]
        public float Scale = 0.1f;
        
        [Header("Performance")]
        [Tooltip("Use GPU instancing (faster for 100k+ entities)")]
        public bool UseGPUInstancing = true;
        
        private static RiceRenderingConfig instance;
        
        public static RiceRenderingConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<RiceRenderingConfig>();
                }
                return instance;
            }
        }
        
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                Debug.LogWarning("[RiceRenderingConfig] Multiple instances found, using first one");
            }
            
            ValidateSettings();
        }
        
        private void ValidateSettings()
        {
            if (RiceMesh == null)
            {
                Debug.LogError("[RiceRenderingConfig] ❌ Rice Mesh is not assigned! Drag rice_grain.stl.obj to the RiceMesh field");
            }
            
            if (RiceMaterial == null)
            {
                Debug.LogWarning("[RiceRenderingConfig] No material assigned, creating default...");
                RiceMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                RiceMaterial.color = new Color(0.95f, 0.9f, 0.7f); // Rice white color
                RiceMaterial.enableInstancing = true;
            }
            
            if (!RiceMaterial.enableInstancing && UseGPUInstancing)
            {
                Debug.LogWarning("[RiceRenderingConfig] Enabling GPU instancing on material for performance");
                RiceMaterial.enableInstancing = true;
            }
        }
    }
}
