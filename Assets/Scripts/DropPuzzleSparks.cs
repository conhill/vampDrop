using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Pooled impact-spark hub for the drop puzzle.
    ///
    /// ONE shared ParticleSystem renders every spark on the GPU — bursts are added with
    /// ParticleSystem.Emit(), never by instantiating prefabs. A per-frame budget caps how
    /// many bursts we emit so that even a mega-drop (thousands of balls scoring at once)
    /// can only ever spawn a fixed, cheap amount of visual work. This is the same
    /// budgeting idea as DropPuzzleJuice's popup cap — it's what makes sparks safe at scale.
    ///
    /// Fire from anywhere via the static helper, no scene wiring required:
    ///     DropPuzzleSparks.Burst(worldPos, color);
    /// </summary>
    public class DropPuzzleSparks : MonoBehaviour
    {
        private static DropPuzzleSparks _instance;
        private static bool _appQuitting;

        public static DropPuzzleSparks Instance
        {
            get
            {
                if (_appQuitting) return null;
                if (_instance == null)
                {
                    _instance = FindObjectOfType<DropPuzzleSparks>();
                    if (_instance == null)
                        _instance = new GameObject("DropPuzzleSparks (auto)").AddComponent<DropPuzzleSparks>();
                }
                return _instance;
            }
        }

        [Header("Budget (keeps it cheap at any ball count)")]
        [Tooltip("Max spark bursts emitted per frame; extra impacts are skipped, not queued.")]
        public int MaxBurstsPerFrame = 24;
        [Tooltip("Particles per burst.")]
        public int ParticlesPerBurst = 8;

        [Header("Capacity / feel")]
        public int   MaxParticles = 4000;
        public float Lifetime     = 0.45f;
        public float Speed        = 3.5f;
        public float Size         = 0.14f;
        public float Gravity      = 0.6f;

        private ParticleSystem _ps;
        private ParticleSystem.EmitParams _ep;
        private int _burstsThisFrame;
        private int _lastFrame = -1;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            Build();
        }

        private void OnApplicationQuit() => _appQuitting = true;
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Build()
        {
            _ps = gameObject.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.loop             = false;
            main.playOnAwake      = false;
            main.maxParticles     = MaxParticles;
            main.startLifetime    = Lifetime;
            main.startSpeed       = Speed;
            main.startSize        = Size;
            main.gravityModifier  = Gravity;
            main.simulationSpace  = ParticleSystemSimulationSpace.World;
            main.startColor       = Color.white;

            // We emit manually via Emit() — disable the automatic emitter.
            var emission = _ps.emission;
            emission.enabled = false;

            // Small sphere so a burst sprays outward from the impact point.
            var shape = _ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius    = 0.05f;

            // Shrink over life so sparks taper to nothing (reads as a spark, not confetti).
            var sol = _ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size    = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            // Renderer: a simple additive/unlit material so sparks glow. Fall back through a
            // couple of shader names so it works whatever pipeline shaders are present.
            var pr = GetComponent<ParticleSystemRenderer>();
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                       ?? Shader.Find("Sprites/Default")
                       ?? Shader.Find("Unlit/Color");
            if (s != null)
            {
                var mat = new Material(s);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                pr.material = mat;
            }
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sortingOrder = 6000; // over the balls

            _ps.Play();
        }

        /// <summary>Emit a spark burst at a world position, tinted to match the event.</summary>
        public static void Burst(Vector3 worldPos, Color color, int count = -1)
        {
            var inst = Instance;
            if (inst != null) inst.DoBurst(worldPos, color, count);
        }

        private bool ConsumeBudget()
        {
            if (Time.frameCount != _lastFrame) { _lastFrame = Time.frameCount; _burstsThisFrame = 0; }
            if (_burstsThisFrame >= MaxBurstsPerFrame) return false;
            _burstsThisFrame++;
            return true;
        }

        private void DoBurst(Vector3 worldPos, Color color, int count)
        {
            if (_ps == null || !ConsumeBudget()) return;
            _ep.position   = worldPos;
            _ep.startColor = color;
            _ps.Emit(_ep, count < 0 ? ParticlesPerBurst : count);
        }
    }
}
