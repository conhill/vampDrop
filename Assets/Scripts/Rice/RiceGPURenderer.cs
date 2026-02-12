using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Vampire.Rice
{
    /// <summary>
    /// High-performance GPU instanced rendering for rice entities using RenderMeshArray
    /// Optimized for 100k+ entities with actual rice mesh
    /// 
    /// SETUP:
    /// 1. Add RiceRenderingConfig component to a GameObject in your scene
    /// 2. Drag rice_grain.stl.obj to the RiceMesh field
    /// 3. Assign a material (or it will create one automatically)
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RiceGPURenderer : SystemBase
    {
        private Mesh riceMesh;
        private Material riceMaterial;
        private float riceScale;
        private bool hasWarnedAboutConfig = false;

        protected override void OnCreate()
        {
            // Debug.Log("[RiceGPURenderer] Creating optimized GPU renderer with RenderMeshArray...");
        }

        protected override void OnUpdate()
        {
            // Get rendering config
            var config = RiceRenderingConfig.Instance;
            if (config == null)
            {
                if (!hasWarnedAboutConfig)
                {
                    Debug.LogError("[RiceGPURenderer] ❌ No RiceRenderingConfig found in scene! Add it to a GameObject and assign the rice mesh.");
                    hasWarnedAboutConfig = true;
                }
                return;
            }

            // Load mesh and material from config (only once or when changed)
            if (riceMesh != config.RiceMesh || riceMaterial != config.RiceMaterial)
            {
                riceMesh = config.RiceMesh;
                riceMaterial = config.RiceMaterial;
                riceScale = config.Scale;
                
                if (riceMesh != null)
                {
                    Debug.Log($"[RiceGPURenderer] ✅ Using mesh: {riceMesh.name} ({riceMesh.vertexCount} vertices)");
                }
            }

            if (riceMesh == null || riceMaterial == null)
            {
                return;
            }

            // Collect all rice transforms
            var riceQuery = GetEntityQuery(typeof(RiceEntity), typeof(LocalTransform));
            int riceCount = riceQuery.CalculateEntityCount();
            
            if (riceCount == 0) return;

            // Get all transforms as arrays
            var transforms = riceQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            
            // Batch rendering (max 1023 instances per draw call)
            int batchSize = 1023;
            int numBatches = (riceCount + batchSize - 1) / batchSize;
            
            Matrix4x4[] matrices = new Matrix4x4[Mathf.Min(batchSize, riceCount)];
            
            for (int batch = 0; batch < numBatches; batch++)
            {
                int startIdx = batch * batchSize;
                int count = Mathf.Min(batchSize, riceCount - startIdx);
                
                // Fill matrices for this batch
                for (int i = 0; i < count; i++)
                {
                    var transform = transforms[startIdx + i];
                    matrices[i] = Matrix4x4.TRS(
                        transform.Position,
                        transform.Rotation,
                        new Vector3(riceScale, riceScale, riceScale)
                    );
                }
                
                // Draw this batch using the configured mesh
                Graphics.DrawMeshInstanced(
                    riceMesh,
                    0,
                    riceMaterial,
                    matrices,
                    count,
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows: false,
                    layer: 0,
                    null,
                    UnityEngine.Rendering.LightProbeUsage.Off
                );
            }
            
            transforms.Dispose();
        }

        protected override void OnDestroy()
        {
            // Config owns the mesh and material, don't destroy them
            riceMesh = null;
            riceMaterial = null;
        }
    }
}
