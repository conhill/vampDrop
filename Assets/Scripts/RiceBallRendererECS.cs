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
        private bool ballQueryCreated = false;

        private Matrix4x4[] matrixCache;
        
        private void Start()
        {
            matrixCache = new Matrix4x4[MaxBallsPerBatch];
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            
            // Create query for all rice balls
            ballQuery = entityManager.CreateEntityQuery(
                typeof(LocalTransform),
                typeof(RiceBallTag)
            );
            ballQueryCreated = true;
            
            if (BallMesh == null)
            {
                // Debug.LogError("[RiceBallRendererECS] No BallMesh assigned!");
            }
            
            if (BallMaterial == null)
            {
                // Debug.LogError("[RiceBallRendererECS] No BallMaterial assigned!");
            }
            else
            {
                // Ensure material supports GPU instancing
                BallMaterial.enableInstancing = true;
            }
            
            // Debug.Log("[RiceBallRendererECS] GPU Instanced renderer ready");
        }
        
        private void Update()
        {
            if (BallMesh == null || BallMaterial == null) return;

            // 1. GATHER: This is the "Bridge". 
            // We use 'using' so the memory is disposed of the millisecond the brackets close.
            using (NativeArray<LocalTransform> transforms = ballQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob))
            {
                if (transforms.Length == 0) return;

                int totalBalls = transforms.Length;
                int batches = Mathf.CeilToInt((float)totalBalls / MaxBallsPerBatch);

                for (int batch = 0; batch < batches; batch++)
                {
                    int startIndex = batch * MaxBallsPerBatch;
                    int count = Mathf.Min(MaxBallsPerBatch, totalBalls - startIndex);

                    // 2. FILL: Use the pre-allocated matrixCache to avoid GC spikes
                    for (int i = 0; i < count; i++)
                    {
                        LocalTransform transform = transforms[startIndex + i];
                        matrixCache[i] = Matrix4x4.TRS(
                            transform.Position,
                            transform.Rotation,
                            new Vector3(transform.Scale, transform.Scale, transform.Scale * 0.1f)
                        );
                    }

                    // 3. DRAW: Send to the GPU
                    Graphics.DrawMeshInstanced(BallMesh, 0, BallMaterial, matrixCache, count);
                }
            }
        }
        
        private void OnDestroy()
        {
            if (ballQueryCreated)
                ballQuery.Dispose();
        }
    }
}
