using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Three-phase camera for the drop puzzle:
    ///   Assessment  — zoomed out, shows the entire puzzle, "Ready?" prompt
    ///   ZoomIn      — smooth lerp toward the dropper after player confirms
    ///   Drop        — zooms back out to watch all the action
    ///
    /// Attach to Main Camera in the DropPuzzle scene.
    /// Works with orthographic cameras.  Perspective cameras are supported via FOV.
    /// </summary>
    public class DropPuzzleCameraController : MonoBehaviour
    {
        // ── Camera controller singleton ───────────────────────────────────────
        public static DropPuzzleCameraController Instance { get; private set; }

        /// <summary>
        /// The camera that actually renders the puzzle (with the 0.2/0.6/0.2 viewport rect
        /// applied). UI code that needs world→screen projection MUST use this rather than
        /// Camera.main: Camera.main picks an arbitrary enabled MainCamera-tagged camera, and
        /// a camera carried over from the FPS scene via DontDestroyOnLoad can win that race.
        /// </summary>
        public Camera GameplayCamera => _cam;

        /// <summary>
        /// Returns false during the Assessment phase so the dropper won't fire
        /// when the player presses Space to confirm they're ready.
        /// </summary>
        public static bool AllowDrop =>
            (Instance == null || Instance._phase != Phase.Assessment) &&
            DropPuzzleFlowManager.AllowPlayerInput;

        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Assessment (overview)")]
        [Tooltip("Orthographic half-height of the puzzle view. Set this once to match your scene scale.")]
        public float OverviewOrthoSize = 13f;

        // Z=0 is not an accident and not a placeholder — it is the plane the board is
        // DEFINED to sit on. The puzzle prefabs are authored ~11.6 units forward in Z, but
        // PuzzlePrefabLoader.AlignPuzzleToWalls() re-centres the puzzle's bounds onto Z=0 on
        // every run (PuzzlePrefabLoader.cs:357-364), and the side walls, the spawn anchor and
        // Knox are all hard-forced to Z=0 alongside it (:453, :461, :478-482, :490).
        // So do NOT "correct" this to the authored depth by reading prefab bounds: those
        // bounds are only ~11.6 before alignment runs, and CalculatePositions() executes
        // BEFORE AlignPuzzleToWalls() in SnapToOverview() — sampling there would dolly the
        // camera to ~Z 40 for a board that then moves to Z 0, shrinking the whole puzzle.
        [Tooltip("World Z of the board plane — the plane the perspective framing is solved " +
                 "against. Puzzle, side walls, spawn point, Knox and balls are all forced to " +
                 "Z=0 at runtime by PuzzlePrefabLoader, so leave this at 0 unless that " +
                 "invariant changes. Only used when the camera is perspective.")]
        public float BoardPlaneZ = 0f;

        [Tooltip("How far above Knox the camera top edge sits (world units)")]
        public float TopClearance = 0.5f;

        [Header("Ready-to-drop (zoomed in)")]
        [Tooltip("Fraction of the assessment ortho size to zoom to (0.5 = half as wide)")]
        [Range(0.2f, 0.9f)]
        public float ReadyZoomFraction = 0.55f;
        [Tooltip("World-space offset from the dropper position when zoomed in")]
        public Vector2 ReadyOffset = new Vector2(0f, 1.5f);

        [Header("Transitions")]
        [Tooltip("Lerp speed for camera position and ortho size")]
        public float TransitionSpeed = 2.5f;

        [Header("Prompt UI")]
        public string AssessmentPrompt = "Press [SPACE] when ready to drop";

        // ── Private state ─────────────────────────────────────────────────────

        private enum Phase { Assessment, ZoomIn, ReadyToDrop, Drop }
        private Phase _phase = Phase.Assessment;

        private Camera _cam;

        // Cached positions/sizes for each phase
        private Vector3 _overviewPos;
        private float   _overviewOrtho;
        private Vector3 _readyPos;
        private float   _readyOrtho;

        // Current lerp targets
        private Vector3 _targetPos;
        private float   _targetOrtho;

        // ── Camera punch/shake ────────────────────────────────────────────────
        // _basePos is the "real" lerped position; the shake offset is added on top
        // each frame so it never feeds back into the lerp (which would cause drift).
        private Vector3 _basePos;
        private float   _shakeMag;
        [Header("Camera Punch")]
        [Tooltip("How quickly a camera punch settles back to zero (higher = snappier)")]
        public float ShakeDecay = 1.4f;
        [Tooltip("Hard cap on shake magnitude so a mass-score can't fling the camera")]
        public float MaxShake = 0.6f;

        /// <summary>Kick the camera. Called by DropPuzzleJuice on scores / multiplier hits.</summary>
        public void Punch(float strength)
        {
            _shakeMag = Mathf.Min(MaxShake, Mathf.Max(_shakeMag, strength));
        }

        // GUI
        private GUIStyle _promptStyle;
        private bool     _stylesBuilt;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;

            // Restrict rendering to the center column so the camera's aspect ratio
            // matches the actual visible puzzle area (HUD panels take 20% each side)
            if (_cam != null)
                _cam.rect = new Rect(0.2f, 0f, 0.6f, 1f);

            // Prevent camera from lerping toward Vector3.zero before InitAfterLoad runs
            _targetPos   = transform.position;
            _basePos     = transform.position;
            _targetOrtho = _cam != null && _cam.orthographic ? _cam.orthographicSize : 5f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DropperControllerECS.OnAnyDropStarted -= OnDropStarted;
            PuzzlePrefabLoader.OnPuzzleLoaded     -= OnPuzzleLoaded;
        }

        private void Start()
        {
            DropperControllerECS.OnAnyDropStarted += OnDropStarted;
            PuzzlePrefabLoader.OnPuzzleLoaded     += OnPuzzleLoaded;

            // Puzzle may have already loaded before Start() if execution order placed
            // PuzzlePrefabLoader first — snap immediately in that case.
            if (PuzzlePrefabLoader.IsPuzzleReady)
                SnapToOverview();
        }

        private void OnPuzzleLoaded()
        {
            SnapToOverview();
        }

        private void SnapToOverview()
        {
            CalculatePositions();
            transform.position = _overviewPos;
            SetOrtho(_overviewOrtho);
            _basePos     = _overviewPos;
            _targetPos   = _overviewPos;
            _targetOrtho = _overviewOrtho;
            _phase       = Phase.Assessment;

            var loader = FindObjectOfType<PuzzlePrefabLoader>();
            if (loader != null)
            {
                // 1. Place side walls at exact viewport edges.
                loader.AlignSideWalls(_overviewPos.x, _overviewPos.y, _overviewOrtho, _cam.aspect);
                // 2. Scale puzzle vertically to fill camera height, bottom-anchor to camera bottom.
                float camBottom = _overviewPos.y - _overviewOrtho;
                loader.AlignPuzzleToWalls(_overviewPos.x, camBottom, _overviewOrtho, _cam.aspect);
            }
        }

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            // Smooth lerp the BASE position toward the current target
            _basePos = Vector3.Lerp(
                _basePos, _targetPos,
                TransitionSpeed * Time.deltaTime);

            // Decay the punch and derive a random offset from what's left
            _shakeMag = Mathf.MoveTowards(_shakeMag, 0f, ShakeDecay * Time.deltaTime);
            Vector3 shake = Vector3.zero;
            if (_shakeMag > 0.0001f)
            {
                Vector2 r = Random.insideUnitCircle * _shakeMag;
                shake = new Vector3(r.x, r.y, 0f);
            }

            transform.position = _basePos + shake;

            if (_cam.orthographic)
                _cam.orthographicSize = Mathf.Lerp(
                    _cam.orthographicSize, _targetOrtho,
                    TransitionSpeed * Time.deltaTime);

            switch (_phase)
            {
                case Phase.Assessment:
                    // Space captured here → go to zoom-in; gated until gate roll finishes
                    if (Input.GetKeyDown(KeyCode.Space) && DropPuzzleFlowManager.AllowPlayerInput)
                        BeginZoomIn();
                    break;

                case Phase.ZoomIn:
                    // Finish zoom-in when close enough (compare the un-shaken base pos)
                    if (Vector3.Distance(_basePos, _targetPos) < 0.15f)
                        _phase = Phase.ReadyToDrop;
                    break;

                case Phase.ReadyToDrop:
                    // AllowDrop is now true — DropperControllerECS will fire on Space
                    break;

                case Phase.Drop:
                    break;
            }
        }

        // ── Phase transitions ─────────────────────────────────────────────────

        private void BeginZoomIn()
        {
            _phase       = Phase.ZoomIn;
            _targetPos   = _readyPos;
            _targetOrtho = _readyOrtho;
        }

        private void OnDropStarted()
        {
            // Zoom back out to watch the action
            _phase       = Phase.Drop;
            _targetPos   = _overviewPos;
            _targetOrtho = _overviewOrtho;
        }

        // ── Position calculation ──────────────────────────────────────────────

        private void CalculatePositions()
        {
            float camZ  = transform.position.z;
            var dropper = FindObjectOfType<DropperControllerECS>();

            float knoxX = dropper != null ? dropper.transform.position.x : 0f;
            float knoxY = dropper != null ? dropper.transform.position.y : 0f;

            // Camera is anchored to Knox with a fixed framing half-height set in the
            // Inspector. Knox sits TopClearance units below the camera's top edge.
            // Under perspective, camZ is derived so the board plane frames identically.
            _overviewOrtho = OverviewOrthoSize;
            float camCenterY = knoxY - OverviewOrthoSize + TopClearance;
            _overviewPos     = new Vector3(knoxX, camCenterY, CamZFor(_overviewOrtho));

            _readyOrtho = _overviewOrtho * ReadyZoomFraction;
            _readyPos   = new Vector3(
                knoxX + ReadyOffset.x,
                knoxY + ReadyOffset.y,
                CamZFor(_readyOrtho));

            Debug.Log($"[CameraController] knox=({knoxX:F1},{knoxY:F1}) ortho={_overviewOrtho:F1} " +
                      $"camCenter=({_overviewPos.x:F1},{_overviewPos.y:F1}) " +
                      $"camBottom={camCenterY - _overviewOrtho:F1}");
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // Prompts now live in the left HUD column — nothing to draw here
        }

        private void DrawPrompt(string msg)
        {
            // Kept for reference; no longer called
        }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _promptStyle = new GUIStyle(GUI.skin.box);
            _promptStyle.fontSize  = 26;
            _promptStyle.fontStyle = FontStyle.Bold;
            _promptStyle.alignment = TextAnchor.MiddleCenter;
            _promptStyle.normal.textColor = Color.white;
            _promptStyle.normal.background = MakeTex(new Color(0f, 0f, 0f, 0.78f));
        }

        private void SetOrtho(float size)
        {
            if (_cam.orthographic) _cam.orthographicSize = size;
        }

        // ── Perspective support ───────────────────────────────────────────────
        //
        // The whole board layout (AlignSideWalls / AlignPuzzleToWalls) is expressed as a
        // half-height at the board plane. Orthographic cameras hand that over directly as
        // orthographicSize; a perspective camera has to derive it from FOV and distance.
        //
        // Framing is kept authoritative on OverviewOrthoSize, and the camera DOLLIES in Z to
        // whatever distance frames that half-height. Zooming by changing FOV instead would
        // warp the perspective mid-shot, which reads as a lens breathing rather than a move.

        /// <summary>Camera distance from the board plane that frames the given half-height.</summary>
        private float DistanceForHalfHeight(float halfHeight)
        {
            float t = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return t < 0.0001f
                ? Mathf.Abs(transform.position.z - BoardPlaneZ)
                : halfHeight / t;
        }

        /// <summary>
        /// World Z the camera must sit at to frame <paramref name="halfHeight"/> at the board
        /// plane. Orthographic cameras don't move — their framing comes from orthographicSize.
        /// </summary>
        private float CamZFor(float halfHeight)
            => _cam.orthographic
                ? transform.position.z
                : BoardPlaneZ + DistanceForHalfHeight(halfHeight);

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(2, 2);
            t.SetPixels(new[] { c, c, c, c });
            t.Apply();
            return t;
        }

        // ── Context menu helpers ──────────────────────────────────────────────

        [ContextMenu("Recalculate Camera Positions")]
        public void EditorRecalculate() => CalculatePositions();
    }
}
