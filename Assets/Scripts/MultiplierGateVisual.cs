using UnityEngine;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Muted, non-neon accent palette for multiplier gates, keyed by multiplier value.
    /// These are rich/warm rather than glowing — a low emission term gives them a little
    /// life without turning into a nightclub. Tweak here to restyle every gate at once.
    /// </summary>
    public static class GatePalette
    {
        public static Color Base(int multiplier)
        {
            switch (multiplier)
            {
                case 2:  return new Color(0.72f, 0.52f, 0.22f); // tarnished bronze/gold
                case 3:  return new Color(0.62f, 0.18f, 0.20f); // oxblood crimson
                case 4:  return new Color(0.30f, 0.50f, 0.44f); // verdigris
                default: return new Color(0.85f, 0.70f, 0.34f); // pale gold (5x+)
            }
        }
    }

    /// <summary>
    /// Drop-in visual polish for a multiplier gate. Attach to the gate object (the one
    /// with the trigger collider). It:
    ///   • ensures the trigger collider is enabled (a common setup gotcha),
    ///   • builds a clean URP/Lit material tinted by multiplier with a SUBTLE emission
    ///     (deliberately not neon),
    ///   • spawns a bold "x2" label that faces the camera,
    ///   • adds gentle life — a small vertical bob + a soft emission "breath" — but
    ///     NOT the scale-growing pulse the old zones used,
    ///   • flashes bright for a moment when a ball passes through.
    ///
    /// The multiplier is read from a MultiplierGate / MultiplierDropZone on the same
    /// object if present, otherwise from the Multiplier field below. If a MultiplierGate
    /// is present, this component subscribes to its Hit event for the flash.
    /// </summary>
    [DisallowMultipleComponent]
    public class MultiplierGateVisual : MonoBehaviour
    {
        [Header("Multiplier (auto-read from gate component if present)")]
        public int Multiplier = 2;

        [Header("Label")]
        public bool ShowLabel = true;
        public float LabelSize = 6f;
        [Tooltip("How far the label is pushed off the gate face, in the gate's LOCAL units. " +
                 "The sign is chosen at build time from where the camera actually is, so this " +
                 "is always a clearance, never a direction.")]
        public float LabelDepthOffset = 0.6f;

        [Header("Motion (kept subtle on purpose)")]
        [Tooltip("Vertical bob height in world units")]
        public float BobHeight = 0.08f;
        [Tooltip("Bob cycles per second")]
        public float BobSpeed = 1.1f;
        [Tooltip("How much the emission gently breathes (0 = steady)")]
        [Range(0f, 1f)] public float EmissionBreath = 0.25f;

        [Header("Emission")]
        [Tooltip("Base emission strength. Low by design — raise for more glow, but this is NOT meant to be neon.")]
        [Range(0f, 1.5f)] public float EmissionStrength = 0.22f;

        private Renderer _renderer;
        private Material _mat;
        private Color _baseColor;
        private Vector3 _restPos;
        private float _flash;                 // 0..1 decaying flash amount
        private TextMeshPro _label;
        private Camera _cam;

        private MultiplierGate _gate;

        private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorID     = Shader.PropertyToID("_Color");
        private static readonly int EmissionID  = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            // Read the real multiplier from whichever gate script is present
            _gate = GetComponent<MultiplierGate>();
            if (_gate != null) Multiplier = _gate.Multiplier;
            else
            {
                var zone = GetComponent<MultiplierDropZone>();
                if (zone != null) Multiplier = zone.Multiplier;
            }

            // Make sure the trigger is actually usable (the authored prefabs had it disabled)
            var col = GetComponent<Collider>();
            if (col != null) { col.enabled = true; col.isTrigger = true; }

            _cam = Camera.main;
            // _restPos is captured in Start, NOT here. PuzzleEnhancer re-centres the gate on
            // its marker immediately after Instantiate (i.e. after Awake), so latching the
            // rest position this early makes Update snap the gate straight back to the
            // pre-centred spot on the first frame.
        }

        private void OnEnable()
        {
            if (_gate != null) _gate.Hit += Flash;
        }

        private void OnDisable()
        {
            if (_gate != null) _gate.Hit -= Flash;
        }

        private void Start()
        {
            _restPos   = transform.localPosition;   // see note in Awake
            _baseColor = GatePalette.Base(Multiplier);
            BuildMaterial();
            if (ShowLabel) BuildLabel();
        }

        /// <summary>
        /// Retarget the visual when the pre-drop roll rewrites the gate's multiplier. Without
        /// this the gate keeps the prefab's tint and label while scoring the rolled value.
        /// Called by <see cref="MultiplierGate.SetMultiplier"/>.
        /// </summary>
        public void SetMultiplier(int value)
        {
            if (Multiplier == value) return;
            Multiplier = value;
            _baseColor = GatePalette.Base(Multiplier);

            if (_mat != null)
            {
                _mat.SetColor(BaseColorID, _baseColor);
                _mat.SetColor(ColorID, _baseColor);
                _mat.SetColor(EmissionID, _baseColor * EmissionStrength);
            }
            if (_label != null) _label.text = $"x{Multiplier}";
        }

        private void BuildMaterial()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null) return;

            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            _mat = new Material(s);

            _mat.SetColor(BaseColorID, _baseColor);
            _mat.SetColor(ColorID, _baseColor);
            _mat.SetFloat("_Smoothness", 0.55f);
            _mat.SetFloat("_Metallic", 0.2f);

            _mat.EnableKeyword("_EMISSION");
            _mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            _mat.SetColor(EmissionID, _baseColor * EmissionStrength);

            _renderer.material = _mat;
        }

        private void BuildLabel()
        {
            var go = new GameObject("GateLabel");
            go.transform.SetParent(transform, false);

            // Push the label off the face the camera is ON. Hard-coding -Z assumed a camera
            // parked on the gate's -Z side; the DropPuzzle camera sits at +Z (z ≈ 28.5,
            // yaw 180) so a fixed -Z offset buries the label inside the gate mesh, which is
            // why gate numbers read as missing on that board.
            if (_cam == null) _cam = Camera.main;
            float side = -1f;
            if (_cam != null)
            {
                Vector3 toCamLocal = transform.InverseTransformDirection(
                    _cam.transform.position - transform.position);
                if (!Mathf.Approximately(toCamLocal.z, 0f)) side = Mathf.Sign(toCamLocal.z);
            }
            go.transform.localPosition = new Vector3(0f, 0f, side * Mathf.Abs(LabelDepthOffset));

            _label = go.AddComponent<TextMeshPro>();
            var font = TMP_Settings.defaultFontAsset;
            if (font != null) _label.font = font;
            _label.text = $"x{Multiplier}";
            _label.fontSize = LabelSize;
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontStyle = FontStyles.Bold;
            _label.enableWordWrapping = false;
            _label.color = Color.white;
            _label.outlineWidth = 0.25f;
            _label.outlineColor = new Color32(25, 20, 15, 255);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 4000;

            // Counter the parent's (often mirrored/large) scale so text stays crisp & upright.
            var ls = transform.lossyScale;
            go.transform.localScale = new Vector3(
                ls.x != 0 ? 1f / ls.x : 1f,
                ls.y != 0 ? 1f / ls.y : 1f,
                ls.z != 0 ? 1f / ls.z : 1f);
        }

        /// <summary>Punchy brighten when a ball scores the gate. Called via MultiplierGate.Hit.</summary>
        public void Flash() => _flash = 1f;

        private void Update()
        {
            float t = Time.time;

            // Gentle vertical bob (position only — no scale growing)
            if (BobHeight > 0f)
                transform.localPosition = _restPos + Vector3.up * (Mathf.Sin(t * BobSpeed * Mathf.PI * 2f) * BobHeight);

            // Decay the hit flash
            if (_flash > 0f) _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 3f);

            // Emission = base + soft breath + flash spike
            if (_mat != null)
            {
                float breath = 1f + Mathf.Sin(t * 1.6f) * EmissionBreath;
                float strength = EmissionStrength * breath + _flash * 1.6f;
                _mat.SetColor(EmissionID, _baseColor * strength);

                // On flash, briefly lift the base colour toward white so it "pops"
                Color c = Color.Lerp(_baseColor, Color.white, _flash * 0.6f);
                _mat.SetColor(BaseColorID, c);
                _mat.SetColor(ColorID, c);
            }

            // Keep the label facing the camera and readable
            if (_label != null && _cam != null)
                _label.transform.rotation = Quaternion.LookRotation(_cam.transform.forward, _cam.transform.up);
        }
    }
}
