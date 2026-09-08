using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// 3D slot-reel spinner for the DropPuzzle gate roll.
    ///
    /// Stages the slot machine rig (slot.fbx) far from gameplay, renders it through a
    /// dedicated camera into a RenderTexture shown in the right HUD panel, mounts
    /// placeholder multiplier cards on the reel's 10 sockets, then spins the reel to land
    /// a chosen multiplier at the front window — with a decelerating spin, a settle pass,
    /// and a glow pulse on the hit.
    ///
    /// The rig comes from Blender with a baked 100× node scale; this is absorbed here
    /// (cards are scaled by 1/socket.lossyScale, the reel is spun about its own local X).
    ///
    /// Call <see cref="StartSpin"/> with the multiplier values to reveal; it lands on the
    /// highest for drama and invokes the callback when the glow pulse finishes.
    /// </summary>
    public class SlotSpinner : MonoBehaviour
    {
        public static SlotSpinner Instance { get; private set; }

        [Header("Rig")]
        [Tooltip("The slot machine model (slot.fbx). Must contain Reel + Socket_00..09 + WindowGlow.")]
        public GameObject SlotRigPrefab;
        [Tooltip("Isolated world location to stage the rig, far from gameplay (HUD mode only).")]
        public Vector3 StagingPosition = new Vector3(0f, 1000f, 0f);

        [Header("In-World Display")]
        [Tooltip("Show the actual slot model in the scene and spin it there — no HUD window. " +
                 "OFF = render the model into the right HUD panel (recommended, always on the right).")]
        public bool InWorldMode = false;
        [Tooltip("Where to place the model in the scene (beside/above the drop board).")]
        public Vector3 InWorldPosition = new Vector3(0f, 6f, 0f);
        [Tooltip("Rotation of the model. Face its front toward the camera.")]
        public Vector3 InWorldEuler = Vector3.zero;
        [Tooltip("Manual scale of the model (used only when AutoFitHeight <= 0).")]
        public float InWorldScale = 1f;
        [Tooltip("If > 0, auto-scale the model to this world height so it's visible regardless " +
                 "of the fbx's baked scale. Set 0 to use InWorldScale instead.")]
        public float AutoFitHeight = 3f;
        [Tooltip("Show a small world-space 'x5' label above the model for the landed value.")]
        public bool ShowWorldLabel = true;

        [Header("HUD Mode (Right Panel)")]
        [Tooltip("Extra rotation applied to the model. X=180 flips it upright if the fbx " +
                 "imports upside-down.")]
        public Vector3 RigEuler = new Vector3(180f, 0f, 0f);
        [Tooltip("Orbit the view horizontally to face the machine's front. Try 90 / -90 / 180 " +
                 "if it renders sideways.")]
        public float CameraYaw = 0f;
        [Tooltip("Orbit the view vertically (look down/up on the machine).")]
        public float CameraPitch = 0f;
        [Tooltip("Hide the cabinet body (Housing/Lever/Pointer). OFF = show the full machine.")]
        public bool HideCabinet = false;
        [Tooltip("Frame the whole machine in the panel. OFF = zoom onto just the reel window.")]
        public bool FrameWholeRig = true;
        [Tooltip("Extra zoom on the model in the view (camera stays framed on the base size). " +
                 "1x1 = fit; raise to zoom in.")]
        public Vector2 RigScale = new Vector2(1f, 1f);

        [Header("Display")]
        [Tooltip("Render texture WIDTH in px; height = width / WindowAspect.")]
        public int RenderTextureSize = 768;
        [Tooltip("Window aspect (w/h). >1 = landscape, to fit a wide/short card.")]
        public float WindowAspect = 1.8f;
        [Range(15f, 60f)] public float CameraFOV = 32f;
        [Tooltip("Distance from the rig front the camera sits (auto-tuned to frame height).")]
        public float CameraDistance = 0f; // 0 = auto
        [Tooltip("World height visible at the front card (smaller = zoomed onto one card).")]
        public float WindowViewHeight = 0.95f;
        public Color BackgroundColor = new Color(0.03f, 0.02f, 0.06f, 1f);

        [Header("Spin feel")]
        public float MaxSpinSpeed = 900f;    // deg/sec at the peak of the spin
        public float SpinDuration = 2.2f;
        public float SettleDuration = 0.7f;
        [Tooltip("Speed multiplier over normalized spin time (fast ramp, long decel tail = near-miss feel).")]
        public AnimationCurve SpinSpeedCurve;

        [Header("Symbols (placeholder)")]
        [Tooltip("Multiplier value shown on each of the 10 sockets, in order.")]
        public int[] SocketValues = { 2, 3, 4, 5, 2, 3, 4, 5, 2, 3 };
        [Tooltip("Axial (full) width of the flat card across the drum facet. Drum length ~3.0.")]
        public float CardWidth = 2.8f;
        [Tooltip("Circumferential height of the card (one decagon facet ~0.70).")]
        public float CardHeight = 0.68f;
        [Tooltip("Number-label size in world units.")]
        public float LabelWidth = 0.95f;
        public float LabelHeight = 0.55f;

        [Header("Debug")]
        [Tooltip("Auto-build and run a test spin on Start (editor testing).")]
        public bool TestOnStart = false;
        public int TestLandValue = 5;

        // ── Runtime ──────────────────────────────────────────────────────────
        private GameObject _rig;
        private Transform  _reel;
        private readonly Transform[] _sockets = new Transform[10];
        private Renderer   _glowRenderer;
        private Material    _glowMat;
        private Color       _glowBaseEmission;
        private Camera     _cam;
        private RenderTexture _rt;
        private RawImage   _display;
        private Graphic    _uiGlow;     // UI window frame pulsed on a hit
        private TextMeshProUGUI _windowValue; // big number shown over the window
        private TextMeshPro _worldLabel; // in-world value label (InWorldMode)
        private Vector3    _spinAxis;
        private Vector3    _frontDir;
        private bool       _built;
        private bool       _spinning;

        public bool IsBuilt => _built;
        public RenderTexture Texture => _rt;

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Reset()
        {
            SpinSpeedCurve = DefaultSpeedCurve();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (SpinSpeedCurve == null || SpinSpeedCurve.length == 0)
                SpinSpeedCurve = DefaultSpeedCurve();
        }

        private void Start()
        {
            // Build at scene start so the reel window is always visible (idle) in the HUD;
            // the gate roll then just spins it.
            Build();
            if (TestOnStart)
                StartSpin(new[] { TestLandValue }, () => Debug.Log("[SlotSpinner] Test spin complete"));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }

        private static AnimationCurve DefaultSpeedCurve()
        {
            // Fast ramp up, brief hold near peak, then a long decel tail for tension.
            var c = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.55f, 0.85f),
                new Keyframe(1f, 0.12f));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0.3f);
            return c;
        }

        // ── Build ────────────────────────────────────────────────────────────

        /// <summary>Instantiates the rig, camera, lighting, cards, and HUD display. Idempotent.</summary>
        public void Build()
        {
            if (_built) return;
            if (SlotRigPrefab == null)
            {
                Debug.LogError("[SlotSpinner] SlotRigPrefab not assigned (drag slot.fbx onto it).");
                return;
            }

            if (InWorldMode) { BuildInWorld(); return; }

            // 1. Stage the rig far from gameplay, applying the upright/orientation fix.
            _rig = Instantiate(SlotRigPrefab, StagingPosition, Quaternion.Euler(RigEuler), transform);
            _rig.name = "SlotRig";

            // 2. Resolve key transforms by name.
            foreach (var t in _rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Reel") _reel = t;
                else if (t.name == "WindowGlow") _glowRenderer = t.GetComponent<Renderer>();
                else if (t.name.StartsWith("Socket_") &&
                         int.TryParse(t.name.Substring(7), out int idx) && idx >= 0 && idx < 10)
                    _sockets[idx] = t;
            }
            if (_reel == null) { Debug.LogError("[SlotSpinner] 'Reel' not found in rig."); return; }

            // Reel spins about its own local X; that axis is invariant under the spin.
            _spinAxis = _reel.right;

            // 3. Base bounds for framing, then scale the MODEL up. The camera stays framed on
            //    the base size, so a bigger model reads as a zoom-in (not a smaller camera).
            Bounds baseB = ComputeBounds(_rig);
            _rig.transform.localScale = Vector3.Scale(_rig.transform.localScale,
                new Vector3(Mathf.Max(0.01f, RigScale.x), Mathf.Max(0.01f, RigScale.y), 1f));
            Bounds rb = ComputeBounds(_rig);

            // Match the render texture to the machine's real shape so it fills the window
            // instead of being letterboxed to a fixed aspect.
            float displayAspect = baseB.size.y > 0.001f ? baseB.size.x / baseB.size.y : 1.6f;

            // 4. Camera on the machine's front side. CameraYaw/Pitch orbit the view so you
            //    can face the reel window even if the model's front isn't authored on +Z.
            var camGO = new GameObject("SpinnerCamera");
            camGO.transform.SetParent(transform, false);
            Vector3 front = Quaternion.Euler(CameraPitch, CameraYaw, 0f) * Vector3.forward;

            Vector3 aim;
            float dist;
            if (FrameWholeRig)
            {
                // Frame the BASE size (so RigScale zooms the model in), aimed at the scaled center.
                aim = rb.center;
                float aspect = Mathf.Max(0.1f, displayAspect);
                float viewH  = Mathf.Max(baseB.size.y, baseB.size.x / aspect) * 1.1f;
                float d      = (viewH * 0.5f) / Mathf.Tan(CameraFOV * 0.5f * Mathf.Deg2Rad);
                dist = CameraDistance > 0f ? CameraDistance : d + rb.size.z * 0.5f + 0.2f;
            }
            else
            {
                // Zoom onto just the reel window (original behaviour).
                aim = _reel.position;
                float radius = _sockets[0] != null
                    ? Vector3.ProjectOnPlane(_sockets[0].position - _reel.position, _spinAxis).magnitude
                    : 1.12f;
                float viewH    = WindowViewHeight > 0f ? WindowViewHeight : 1.2f;
                float faceDist = (viewH * 0.5f) / Mathf.Tan(CameraFOV * 0.5f * Mathf.Deg2Rad);
                dist = CameraDistance > 0f ? CameraDistance : radius + faceDist;
            }
            camGO.transform.position = aim + front * dist;
            camGO.transform.rotation = Quaternion.LookRotation(aim - camGO.transform.position, Vector3.up);

            _cam = camGO.AddComponent<Camera>();
            _cam.clearFlags      = CameraClearFlags.SolidColor;
            _cam.backgroundColor = BackgroundColor;
            _cam.fieldOfView     = CameraFOV;
            _cam.nearClipPlane   = 0.1f;
            _cam.farClipPlane    = dist + rb.size.magnitude + 5f;

            int rtW = Mathf.Max(64, RenderTextureSize);
            int rtH = Mathf.Max(64, Mathf.RoundToInt(rtW / Mathf.Max(0.1f, displayAspect)));
            _rt = new RenderTexture(rtW, rtH, 16) { name = "SlotSpinnerRT" };
            _cam.targetTexture = _rt;
            _cam.aspect = (float)rtW / rtH;

            _frontDir = (camGO.transform.position - _reel.position).normalized;

            // 5. Dedicated light so the rig reads regardless of scene lighting (short range).
            var lightGO = new GameObject("SpinnerLight");
            lightGO.transform.SetParent(camGO.transform, false);
            lightGO.transform.localPosition = new Vector3(1.5f, 2f, 0.5f);
            lightGO.transform.LookAt(rb.center);
            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Point;
            light.range     = rb.size.magnitude * 3f;
            light.intensity = 3f;
            light.color     = new Color(1f, 0.96f, 0.9f);

            // 6. Glow material instance (so we can pulse emission without touching the asset).
            if (_glowRenderer != null)
            {
                _glowMat = _glowRenderer.material; // instances it
                _glowMat.EnableKeyword("_EMISSION");
                _glowMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                _glowBaseEmission = _glowMat.HasProperty("_EmissionColor")
                    ? _glowMat.GetColor("_EmissionColor")
                    : Color.black;
            }

            // 7. Multiplier cards with number labels on each reel face.
            MountCards();

            // 8. Optionally hide the cabinet body (off by default — show the whole machine).
            if (HideCabinet) HideCabinetParts();

            // 9. HUD display (UI cabinet frame + render texture) in the right panel.
            BuildDisplay();

            _built = true;
        }

        /// <summary>
        /// In-world build: place the real model in the scene and spin it in place.
        /// Full cabinet visible, no render texture, no HUD window, no neon placeholder cards.
        /// </summary>
        private void BuildInWorld()
        {
            _rig = Instantiate(SlotRigPrefab, transform);
            _rig.name = "SlotRig";
            _rig.transform.rotation   = Quaternion.Euler(InWorldEuler);
            _rig.transform.localScale = Vector3.one * Mathf.Max(0.0001f, InWorldScale);

            // Auto-fit to a target world height so it shows regardless of the fbx's baked scale.
            if (AutoFitHeight > 0f)
            {
                Bounds fit = ComputeBounds(_rig);
                if (fit.size.y > 1e-5f)
                    _rig.transform.localScale *= AutoFitHeight / fit.size.y;
            }
            _rig.transform.position = InWorldPosition;

            foreach (var t in _rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Reel") _reel = t;
                else if (t.name == "WindowGlow") _glowRenderer = t.GetComponent<Renderer>();
                else if (t.name.StartsWith("Socket_") &&
                         int.TryParse(t.name.Substring(7), out int idx) && idx >= 0 && idx < 10)
                    _sockets[idx] = t;
            }
            if (_reel == null) { Debug.LogError("[SlotSpinner] 'Reel' not found in rig."); return; }

            _spinAxis = _reel.right;

            // Front = toward the viewing camera (the face the player reads).
            var cam = Camera.main;
            Vector3 toCam = cam != null ? cam.transform.position - _reel.position : _rig.transform.forward;
            _frontDir = Vector3.ProjectOnPlane(toCam, _spinAxis).normalized;
            if (_frontDir.sqrMagnitude < 1e-6f) _frontDir = _rig.transform.forward;

            // Glow material instance for the hit pulse (emission), if the rig has one.
            if (_glowRenderer != null)
            {
                _glowMat = _glowRenderer.material;
                _glowMat.EnableKeyword("_EMISSION");
                _glowMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                _glowBaseEmission = _glowMat.HasProperty("_EmissionColor")
                    ? _glowMat.GetColor("_EmissionColor") : Color.black;
            }

            if (ShowWorldLabel) BuildWorldLabel(cam);

            _built = true;
            Bounds fitted = ComputeBounds(_rig);
            Debug.Log($"[SlotSpinner] In-world rig built at {InWorldPosition}, " +
                      $"world size {fitted.size.ToString("F2")} (scale {_rig.transform.localScale.x:F4}).");
        }

        /// <summary>Tear down and rebuild — lets you tune position/scale/rotation live in Play.</summary>
        [ContextMenu("Rebuild In-World")]
        private void RebuildInWorld()
        {
            if (_rig != null)        DestroySafe(_rig);
            if (_worldLabel != null) DestroySafe(_worldLabel.gameObject);
            _rig = null; _reel = null; _glowMat = null; _worldLabel = null;
            for (int i = 0; i < _sockets.Length; i++) _sockets[i] = null;
            _built = false;
            _lastShownValue = -1;
            Build();
        }

        private static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private void BuildWorldLabel(Camera cam)
        {
            var labelGO = new GameObject("SpinnerValueLabel");
            // Parent to the (unscaled) spinner root so the label stays readable regardless
            // of the rig's baked scale.
            labelGO.transform.SetParent(transform, false);

            Bounds b = ComputeBounds(_rig);
            labelGO.transform.position = new Vector3(b.center.x, b.max.y + 0.5f, b.center.z);

            _worldLabel = labelGO.AddComponent<TextMeshPro>();
            _worldLabel.text = "";
            _worldLabel.fontSize = 4f;
            _worldLabel.alignment = TextAlignmentOptions.Center;
            _worldLabel.color = Color.white;

            if (cam != null) labelGO.AddComponent<FaceCamera>().Cam = cam;
        }

        private void HideCabinetParts()
        {
            if (_rig == null) return;
            var hide = new HashSet<string> { "Housing", "Lever", "Pointer", "WindowGlow" };
            foreach (var r in _rig.GetComponentsInChildren<Renderer>(true))
                if (hide.Contains(r.gameObject.name)) r.enabled = false;
        }

        private void MountCards()
        {
            for (int i = 0; i < _sockets.Length; i++)
            {
                var socket = _sockets[i];
                if (socket == null) continue;
                int val = SocketValues[i % SocketValues.Length];
                Color c = ColorFor(val);

                // Root neutralizes the socket's baked 100× scale so children are world-sized.
                // Socket lies in its decagon facet plane: local +Z = OUTWARD, X = drum axis
                // (width), Y = circumferential (height). Flat cards therefore tile the facets.
                var card = new GameObject($"Card_{i}_x{val}");
                card.transform.SetParent(socket, false);
                card.transform.localPosition = Vector3.zero;
                card.transform.localRotation = Quaternion.identity;
                float inv = 1f / Mathf.Max(0.0001f, socket.lossyScale.x);
                card.transform.localScale = Vector3.one * inv;

                // Colored facet (border behind, panel front). The value is shown by the
                // screen-space window number (see BuildDisplay/Update) — reliable UI text.
                MakeCardQuad(card.transform, "Frame", -0.01f, CardWidth + 0.07f, CardHeight + 0.07f, c * 0.85f);
                MakeCardQuad(card.transform, "BG",     0f,    CardWidth,         CardHeight,         c * 0.5f);
                MakeCardNumber(card.transform, $"x{val}");
            }
        }

        // A readable multiplier number on the reel face (so the slot has visible labels).
        private void MakeCardNumber(Transform parent, string text)
        {
            var go = new GameObject("Number");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.02f); // just in front of the card
            go.transform.localRotation = Quaternion.identity;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text      = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = Color.white;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 24f;
            tmp.rectTransform.sizeDelta = new Vector2(CardWidth, CardHeight);
        }

        // Value of the facet currently closest to the front window.
        private int FrontValue()
        {
            int best = -1; float bestAbs = float.MaxValue;
            for (int i = 0; i < _sockets.Length; i++)
            {
                if (_sockets[i] == null) continue;
                float a = Mathf.Abs(SignedAngleToFront(_sockets[i]));
                if (a < bestAbs) { bestAbs = a; best = i; }
            }
            return best >= 0 ? SocketValues[best % SocketValues.Length] : 2;
        }

        private int _lastShownValue = -1;

        private void Update()
        {
            if (!_built) return;
            int val = FrontValue();
            if (val == _lastShownValue) return; // only rebuild strings on change (avoids per-frame GC)
            _lastShownValue = val;

            string txt = $"x{val}";
            if (_windowValue != null)
            {
                _windowValue.text  = txt;
                _windowValue.color = Color.white;
            }
            if (_worldLabel != null)
            {
                _worldLabel.text  = txt;
                _worldLabel.color = ColorFor(val);
            }
        }

        private void MakeCardQuad(Transform parent, string name, float z, float w, float h, Color color)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            var col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.transform.SetParent(parent, false);
            q.transform.localPosition = new Vector3(0f, 0f, z);
            q.transform.localScale = new Vector3(w, h, 1f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            SetColor(mat, color);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // double-sided (facing-proof)
            q.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private void BuildDisplay()
        {
            // Use the HUD right panel when it's wired; otherwise make our own right-side
            // canvas. (RightPanel is often null, which would parent the display to nothing.)
            RectTransform host = DropPuzzleHUD.Instance != null && DropPuzzleHUD.Instance.RightPanel != null
                ? DropPuzzleHUD.Instance.RightPanel
                : CreateFallbackHost();

            // Cabinet panel — tall, upper portion of the right column.
            var panel = MakeUI(host, "SlotCabinet", new Vector2(0.04f, 0.30f), new Vector2(0.96f, 0.99f));
            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = new Color(0.06f, 0.05f, 0.10f, 0.97f);

            // Title.
            var title = MakeUIText(panel, "Title", new Vector2(0f, 0.90f), new Vector2(1f, 1f),
                "GATE ROLL", 22, TextAlignmentOptions.Center, FontStyles.Bold);
            title.color = new Color(1f, 0.85f, 0.3f);

            // Glow frame (fills the machine window) — invisible until a hit pulses it.
            var glow = MakeUI(panel, "WindowGlow", new Vector2(0.06f, 0.16f), new Vector2(0.94f, 0.88f));
            var glowImg = glow.gameObject.AddComponent<Image>();
            glowImg.color = new Color(1f, 0.85f, 0.3f, 0f);
            _uiGlow = glowImg;

            // Window: dark inset holding the machine render texture.
            var window = MakeUI(glow, "Window", new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.97f));
            var winBg = window.gameObject.AddComponent<Image>();
            winBg.color = new Color(0.02f, 0.02f, 0.03f, 1f);

            var rawGO = new GameObject("Reel");
            var rrt = rawGO.AddComponent<RectTransform>();
            rrt.SetParent(window, false);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            _display = rawGO.AddComponent<RawImage>();
            _display.texture = _rt;

            // Keep the render texture's aspect so the machine isn't stretched to the window shape.
            var fitter = rawGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = _rt != null && _rt.height > 0 ? (float)_rt.width / _rt.height : 1f;

            // Value readout — small bar under the machine, so it never covers the model.
            _windowValue = MakeUIText(panel, "WindowValue", new Vector2(0.1f, 0.02f), new Vector2(0.9f, 0.15f),
                "", 40, TextAlignmentOptions.Center, FontStyles.Bold);
            _windowValue.color = Color.white;
            _windowValue.raycastTarget = false;
            _windowValue.enableAutoSizing = true;
            _windowValue.fontSizeMin = 12f;
            _windowValue.fontSizeMax = 80f;
            _windowValue.outlineColor = new Color(0f, 0f, 0f, 0.85f);
            _windowValue.outlineWidth = 0.2f;
        }

        private RectTransform CreateFallbackHost()
        {
            var canvasGO = new GameObject("SlotSpinnerCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(canvasGO.transform, false);
            panel.anchorMin = new Vector2(0.8f, 0f);
            panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            return panel;
        }

        private static RectTransform MakeUI(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static TextMeshProUGUI MakeUIText(RectTransform parent, string name,
            Vector2 aMin, Vector2 aMax, string text, float size,
            TextAlignmentOptions align, FontStyles style)
        {
            var rt = MakeUI(parent, name, aMin, aMax);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = align;
            t.fontStyle = style; t.color = Color.white;
            return t;
        }

        // ── Spin ─────────────────────────────────────────────────────────────

        /// <summary>Spin and land on the highest value in <paramref name="resultValues"/>.</summary>
        public void StartSpin(int[] resultValues, Action onComplete)
        {
            if (!_built) Build();
            if (!_built) { onComplete?.Invoke(); return; }
            if (_spinning) return;

            int target = 2;
            if (resultValues != null && resultValues.Length > 0)
            {
                target = resultValues[0];
                foreach (int v in resultValues) if (v > target) target = v;
            }
            StartCoroutine(SpinRoutine(target, onComplete));
        }

        private IEnumerator SpinRoutine(int targetValue, Action onComplete)
        {
            _spinning = true;

            // Phase 1 — free spin with decelerating speed curve.
            float t = 0f;
            while (t < SpinDuration)
            {
                float k  = SpinSpeedCurve.Evaluate(t / SpinDuration);
                float dw = MaxSpinSpeed * k * Time.deltaTime;
                _reel.Rotate(_spinAxis, dw, Space.World);
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 2 — settle: bring a target-valued socket to the exact front,
            // plus one extra slow turn (the near-miss crawl).
            Transform target = PickTargetSocket(targetValue);
            float residual = target != null ? SignedAngleToFront(target) : 0f;
            float total    = residual + 360f;

            float s = 0f, prev = 0f;
            while (s < SettleDuration)
            {
                float eased = EaseOutCubic(s / SettleDuration);
                float cur   = eased * total;
                _reel.Rotate(_spinAxis, cur - prev, Space.World);
                prev = cur;
                s += Time.deltaTime;
                yield return null;
            }
            // Exact snap.
            if (target != null) _reel.Rotate(_spinAxis, SignedAngleToFront(target), Space.World);

            // Phase 3 — glow pulse on the hit.
            yield return PulseGlow(ColorFor(targetValue));

            _spinning = false;
            onComplete?.Invoke();
        }

        /// <summary>Signed angle (about the spin axis) to bring a socket to the front window.</summary>
        private float SignedAngleToFront(Transform socket)
        {
            Vector3 v  = Vector3.ProjectOnPlane(socket.position - _reel.position, _spinAxis);
            Vector3 f  = Vector3.ProjectOnPlane(_frontDir, _spinAxis);
            if (v.sqrMagnitude < 1e-6f || f.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(v, f, _spinAxis);
        }

        /// <summary>Nearest-to-front socket carrying the target value (falls back to nearest of any value).</summary>
        private Transform PickTargetSocket(int value)
        {
            Transform best = null; float bestAbs = float.MaxValue;
            for (int i = 0; i < _sockets.Length; i++)
            {
                if (_sockets[i] == null) continue;
                if (SocketValues[i % SocketValues.Length] != value) continue;
                float a = Mathf.Abs(SignedAngleToFront(_sockets[i]));
                if (a < bestAbs) { bestAbs = a; best = _sockets[i]; }
            }
            if (best != null) return best;
            // Fallback: any socket nearest the front.
            for (int i = 0; i < _sockets.Length; i++)
            {
                if (_sockets[i] == null) continue;
                float a = Mathf.Abs(SignedAngleToFront(_sockets[i]));
                if (a < bestAbs) { bestAbs = a; best = _sockets[i]; }
            }
            return best;
        }

        private IEnumerator PulseGlow(Color tint)
        {
            const float up = 0.12f, down = 0.6f;

            // In-world: pulse the model's WindowGlow emission.
            if (_uiGlow == null)
            {
                if (_glowMat == null || !_glowMat.HasProperty("_EmissionColor")) yield break;
                Color baseE = _glowBaseEmission;
                Color peakE = tint * 3f;
                float e = 0f;
                while (e < up)   { _glowMat.SetColor("_EmissionColor", Color.Lerp(baseE, peakE, e / up));   e += Time.deltaTime; yield return null; }
                e = 0f;
                while (e < down) { _glowMat.SetColor("_EmissionColor", Color.Lerp(peakE, baseE, e / down)); e += Time.deltaTime; yield return null; }
                _glowMat.SetColor("_EmissionColor", baseE);
                yield break;
            }

            // HUD: pulse the UI glow frame.
            Color baseC = new Color(tint.r, tint.g, tint.b, 0f);
            Color peakC = new Color(tint.r, tint.g, tint.b, 0.9f);
            float u = 0f;
            while (u < up)  { _uiGlow.color = Color.Lerp(baseC, peakC, u / up);   u += Time.deltaTime; yield return null; }
            u = 0f;
            while (u < down){ _uiGlow.color = Color.Lerp(peakC, baseC, u / down); u += Time.deltaTime; yield return null; }
            _uiGlow.color = baseC;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Bounds ComputeBounds(GameObject root)
        {
            var rends = root.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
        }

        private static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); float f = 1f - x; return 1f - f * f * f; }

        public static Color ColorFor(int mult) => mult switch
        {
            2 => new Color(0.4f, 1f, 0.53f),
            3 => new Color(0.4f, 0.67f, 1f),
            4 => new Color(1f, 0.72f, 0.2f),
            5 => new Color(1f, 0.4f, 1f),
            _ => new Color(0.7f, 0.7f, 0.7f),
        };
    }

    /// <summary>Keeps a transform oriented so its front face points at the camera.</summary>
    public class FaceCamera : MonoBehaviour
    {
        public Camera Cam;
        private void LateUpdate()
        {
            if (Cam == null) return;
            transform.rotation = Quaternion.LookRotation(
                transform.position - Cam.transform.position, Cam.transform.up);
        }
    }
}
