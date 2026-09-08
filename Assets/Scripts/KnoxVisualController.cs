using UnityEngine;

namespace Vampire.DropPuzzle
{
    // Attach to the Knox (dropper) GameObject.
    // Adds a coloured point light + pulsing disc indicator above Knox.
    // Colour codes: yellow = gate roll in progress, green = ready, orange = balls in flight.
    public class KnoxVisualController : MonoBehaviour
    {
        [Header("Light")]
        public Color readyColor    = new Color(0.25f, 1f, 0.35f);
        public Color rollColor     = new Color(1f, 0.9f, 0.15f);
        public Color droppingColor = new Color(1f, 0.45f, 0.1f);
        public float lightIntensity = 3f;
        public float lightRange     = 5f;

        [Header("Indicator disc")]
        public float discHeight     = 1.5f;
        public float pulseSpeed     = 2.8f;
        public float pulseAmplitude = 0.1f;

        private Light     _light;
        private Transform _disc;
        private Material  _discMat;
        private Vector3   _discBaseScale;
        private float     _t;
        private bool      _dropping;

        private void Start()
        {
            CreateLight();
            CreateDisc();
            DropperControllerECS.OnAnyDropStarted += OnDropStarted;
        }

        private void OnDestroy()
        {
            DropperControllerECS.OnAnyDropStarted -= OnDropStarted;
        }

        private void OnDropStarted() => _dropping = true;

        private void CreateLight()
        {
            var go = new GameObject("KnoxGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _light           = go.AddComponent<Light>();
            _light.type      = LightType.Point;
            _light.intensity = lightIntensity;
            _light.range     = lightRange;
            _light.color     = rollColor;
        }

        private void CreateDisc()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "KnoxDisc";
            go.transform.SetParent(transform, false);
            _discBaseScale = new Vector3(0.35f, 0.12f, 0.35f);
            go.transform.localPosition = new Vector3(0f, discHeight, 0f);
            go.transform.localScale    = _discBaseScale;
            Destroy(go.GetComponent<Collider>());

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            _discMat = new Material(shader);
            _discMat.EnableKeyword("_EMISSION");
            go.GetComponent<Renderer>().material = _discMat;
            _disc = go.transform;
        }

        private void Update()
        {
            _t += Time.deltaTime;

            Color c = !DropPuzzleFlowManager.AllowPlayerInput ? rollColor
                    : _dropping                               ? droppingColor
                    :                                           readyColor;

            if (_light != null) _light.color = c;

            if (_disc != null)
            {
                _discMat.color = c;
                _discMat.SetColor("_EmissionColor", c * 2.2f);
                float pulse = 1f + Mathf.Sin(_t * pulseSpeed) * pulseAmplitude;
                _disc.localScale = _discBaseScale * pulse;
            }
        }
    }
}
