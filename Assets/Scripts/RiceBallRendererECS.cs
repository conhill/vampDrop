using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// GPU Instanced renderer for ECS rice balls - handles 1000+ balls efficiently
    /// </summary>
    public class RiceBallRendererECS : MonoBehaviour
    {
        [Header("Rendering")]
        public Mesh BallMesh;
        public Material BallMaterial;
        
        [Header("Settings")]
        public int MaxBallsPerBatch = 1023; // GPU instancing limit
        
        private EntityQuery ballQuery;
        private EntityManager entityManager;
        
        private void Start()
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            
            // Create query for all rice balls
            ballQuery = entityManager.CreateEntityQuery(
                typeof(LocalTransform),
                typeof(RiceBallTag)
            );
            
            if (BallMesh == null)
            {
                Debug.LogError("[RiceBallRendererECS] No BallMesh assigned!");
            }
            
            if (BallMaterial == null)
            {
                Debug.LogError("[RiceBallRendererECS] No BallMaterial assigned!");
            }
            else
            {
                // Ensure material supports GPU instancing
                BallMaterial.enableInstancing = true;
            }
            
            Debug.Log("[RiceBallRendererECS] GPU Instanced renderer ready");
        }
        
        private void Update()
        {
            if (BallMesh == null || BallMaterial == null)
            {
                if (Time.frameCount % 120 == 0)
                {
                    Debug.LogWarning($"[BallRenderer] Cannot render - Mesh:{(BallMesh == null ? "NULL" : "OK")} Material:{(BallMaterial == null ? "NULL" : "OK")}");
                }
                return;
            }
            
            // Get all ball transforms
            NativeArray<LocalTransform> transforms = ballQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            
            // Debug: Log entity count periodically
            if (Time.frameCount % 300 == 0)
            {
                if (transforms.Length > 0)
                {
                    // Log first 3 ball positions for debugging
                    string posDebug = "";
                    for (int i = 0; i < Mathf.Min(3, transforms.Length); i++)
                    {
                        posDebug += $" [{i}]:{transforms[i].Position}";
                    }
                    Debug.Log($"[BallRenderer] Rendering {transforms.Length} balls. Positions:{posDebug}");
                }
                else
                {
                    Debug.LogWarning("[BallRenderer] Query returned 0 entities! No balls exist in ECS.");
                }
            }
            
            if (transforms.Length == 0)
            {
                transforms.Dispose();
                return;
            }
            
            // Convert to matrices for GPU instancing
            int totalBalls = transforms.Length;
            int batches = Mathf.CeilToInt((float)totalBalls / MaxBallsPerBatch);
            
            for (int batch = 0; batch < batches; batch++)
            {
                int startIndex = batch * MaxBallsPerBatch;
                int count = Mathf.Min(MaxBallsPerBatch, totalBalls - startIndex);
                
                Matrix4x4[] matrices = new Matrix4x4[count];
                
                for (int i = 0; i < count; i++)
                {
                    LocalTransform transform = transforms[startIndex + i];
                    // Flat disc: normal XY scale, but Z=0.1 for thin cylinder
                    matrices[i] = Matrix4x4.TRS(
                        transform.Position,
                        transform.Rotation,
                        new Vector3(transform.Scale, transform.Scale, transform.Scale * 0.1f) // Flat disc!
                    );
                }
                
                // Draw instanced
                Graphics.DrawMeshInstanced(
                    BallMesh,
                    0,
                    BallMaterial,
                    matrices,
                    count,
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    false,
                    0,
                    null,
                    UnityEngine.Rendering.LightProbeUsage.Off
                );
            }
            
            transforms.Dispose();
        }
        
        private void OnDestroy()
        {
            if (entityManager != null && ballQuery != null)
            {
                ballQuery.Dispose();
            }
        }
    }
}
