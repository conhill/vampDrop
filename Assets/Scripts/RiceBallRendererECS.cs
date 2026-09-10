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
        private MaterialPropertyBlock _mpb;

        private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorID     = Shader.PropertyToID("_Color");
        private static readonly int EmissionID  = Shader.PropertyToID("_EmissionColor");

        // The four visible quality buckets, in RiceBallType.TypeID → bucket order.
        // Colours come from the shared RiceBallPalette so a ball matches its score popup.
        // Bucket 4 is not a quality tier — it is "minted by a multiplier gate". See
        // RiceBallPalette.MultipliedTypeId for why gate clones carry their own id.
        private static readonly Color[] BucketColor =
        {
            RiceBallPalette.Fine, RiceBallPalette.Good, RiceBallPalette.Great, RiceBallPalette.Excellent,
            RiceBallPalette.Multiplied
        };
        // Higher tiers glow so a rare ball is unmistakable mid-fall. Kept restrained:
        // common balls don't glow at all, only the genuinely rare ones lift a little.
        // Gate clones glow hardest of all — the whole point is to spot them in a falling mass.
        private static readonly float[] BucketEmission = { 0f, 0f, 0.3f, 0.8f, 1.0f };

        private void Start()
        {
            matrixCache = new Matrix4x4[MaxBallsPerBatch];
            _mpb = new MaterialPropertyBlock();
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            // Query now also pulls RiceBallType so we can colour balls by quality.
            ballQuery = entityManager.CreateEntityQuery(
                typeof(LocalTransform),
                typeof(RiceBallType),
                typeof(RiceBallTag)
            );
            ballQueryCreated = true;

            if (BallMaterial != null)
            {
                BallMaterial.enableInstancing = true;
                // Enable emission so the per-tier glow (set via the property block) shows.
                BallMaterial.EnableKeyword("_EMISSION");
                BallMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        // TypeID → visible bucket: 1=Good, 2=Great, 4=Excellent,
        // 5=minted by a multiplier gate; everything else = Fine.
        private static int BucketOf(int typeId) => typeId switch
        {
            1 => 1,
            2 => 2,
            4 => 3,
            RiceBallPalette.MultipliedTypeId => 4,
            _ => 0
        };

        private void Update()
        {
            if (BallMesh == null || BallMaterial == null) return;

            // GATHER transforms + types together (same query → indices line up).
            using NativeArray<LocalTransform> transforms =
                ballQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using NativeArray<RiceBallType> types =
                ballQuery.ToComponentDataArray<RiceBallType>(Allocator.TempJob);

            if (transforms.Length == 0) return;

            // Draw one bucket (quality) at a time so each batch carries a single colour —
            // one flat colour per DrawMeshInstanced call, which URP/Lit honours reliably
            // (no fragile per-instance colour arrays). At most 4 extra draw calls total.
            for (int bucket = 0; bucket < BucketColor.Length; bucket++)
                DrawBucket(transforms, types, bucket);
        }

        private void DrawBucket(NativeArray<LocalTransform> transforms, NativeArray<RiceBallType> types, int bucket)
        {
            Color c = BucketColor[bucket];
            _mpb.Clear();
            _mpb.SetColor(BaseColorID, c);
            _mpb.SetColor(ColorID, c);
            _mpb.SetColor(EmissionID, c * BucketEmission[bucket]);

            int filled = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (BucketOf(types[i].TypeID) != bucket) continue;

                LocalTransform t = transforms[i];
                matrixCache[filled++] = Matrix4x4.TRS(
                    t.Position, t.Rotation,
                    new Vector3(t.Scale, t.Scale, t.Scale * 0.1f));

                if (filled == MaxBallsPerBatch)
                {
                    Graphics.DrawMeshInstanced(BallMesh, 0, BallMaterial, matrixCache, filled, _mpb);
                    filled = 0;
                }
            }
            if (filled > 0)
                Graphics.DrawMeshInstanced(BallMesh, 0, BallMaterial, matrixCache, filled, _mpb);
        }

        private void OnDestroy()
        {
            // On play-mode exit the ECS World can be torn down before this runs, which
            // makes disposing the query throw. Only dispose while the world is still alive.
            var world = World.DefaultGameObjectInjectionWorld;
            if (ballQueryCreated && world != null && world.IsCreated)
                ballQuery.Dispose();
        }
    }
}
