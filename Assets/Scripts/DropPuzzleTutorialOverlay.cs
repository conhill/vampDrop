using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    // In-scene tutorial overlay for the DropPuzzle scene.
    // The puzzle stays FULLY VISIBLE. A character portrait + speech bubble sits at the
    // bottom of the screen. An arrow + pulsing ring points at a real scene object
    // (Knox, a gate, the goal area) defined per-step by dragging a Transform in the Inspector.
    //
    // Knox can still be moved with A/D during tutorial steps.
    // SPACE / Enter advances to the next step.
    //
    // Add to any scene GameObject. Wire each Step.worldTarget by dragging from the Hierarchy.
    public class DropPuzzleTutorialOverlay : MonoBehaviour
    {
        // ── Step data ─────────────────────────────────────────────────────────

        [System.Serializable]
        public struct TutorialStep
        {
            [Tooltip("Character sprite shown on the left (Knox portrait, etc.)")]
            public Sprite characterSprite;

            [TextArea(2, 5)]
            public string speechText;

            [Tooltip("World-space object to point at. Drag Knox, a gate, the goal zone, etc.")]
            public Transform worldTarget;

            [Tooltip("Optional name lookup, resolved when the step FIRES. Use this for gates " +
                     "and anything else PuzzlePrefabLoader / GateRollController spawn at " +
                     "runtime — an Inspector reference can't point at an object that doesn't " +
                     "exist yet. Takes over whenever worldTarget is missing or unusable.")]
            public string targetObjectName;

            [Tooltip("Fallback when no world target resolves: 0-1 normalised FULL-SCREEN " +
                     "position. (0,0)=bottom-left  (1,1)=top-right")]
            public Vector2 screenFallback;

            [Tooltip("Seconds before auto-advancing. 0 = wait for Space / Enter")]
            public float holdSeconds;
        }

        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Steps")]
        public TutorialStep[] Steps;

        [Header("Options")]
        [Tooltip("After TutorialCompleted is true this overlay is skipped entirely")]
        public bool PlayOnlyDuringTutorial = true;

        [Header("Colours")]
        public Color panelColor = new Color(0.03f, 0.03f, 0.10f, 0.90f);
        public Color arrowColor = new Color(1.00f, 0.85f, 0.15f, 0.92f);
        public Color ringColor  = new Color(1.00f, 0.85f, 0.15f, 0.80f);

        // ── Events ────────────────────────────────────────────────────────────

        public event System.Action OnComplete;

        // ── Runtime refs ──────────────────────────────────────────────────────

        private RectTransform   _canvasRT;
        private GameObject      _overlayRoot; // hidden until PlayTutorial()
        private Image           _portrait;
        private TextMeshProUGUI _speechTMP;
        private RectTransform   _shaftRT;    // arrow body
        private RectTransform   _ringRT;     // pulsing circle at target
        private Image           _ringImg;
        private TextMeshProUGUI _arrowTip;   // "▲" rotated to point at target
        private bool            _skipStep;
        private int             _stepIndex = -1;

        // Camera used for world→screen projection. Resolved lazily and re-resolved if it
        // dies, so a scene reload or a late-spawning gameplay camera can't strand us.
        private Camera _cam;

        // Target resolved for the CURRENT step (Inspector ref or name lookup). Cached so the
        // name search runs once per step, not once per frame.
        private Transform _stepTarget;

        // cache of the top-center of the speech panel in canvas local space
        private Vector2 _panelTopLocal;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            BuildUI();
        }

        private void Start()
        {
            // Stays hidden; FlowManager calls PlayTutorial() after puzzle loads
        }

        public bool ShouldPlay()
        {
            if (Steps == null || Steps.Length == 0) return false;
            if (!PlayOnlyDuringTutorial) return true;
            var pdm = PlayerDataManager.Instance;
            return pdm == null || !pdm.TutorialCompleted;
        }

        public void PlayTutorial()
        {
            _overlayRoot.SetActive(true);
            StartCoroutine(RunSteps());
        }

        private void OnDestroy()
        {
            // nothing to unsubscribe — arrow update is in Update()
        }

        // ── Step runner ───────────────────────────────────────────────────────

        private IEnumerator RunSteps()
        {
            for (_stepIndex = 0; _stepIndex < Steps.Length; _stepIndex++)
            {
                _skipStep = false;
                ApplyStep(Steps[_stepIndex]);

                float elapsed = 0f;
                float limit   = Steps[_stepIndex].holdSeconds > 0f
                    ? Steps[_stepIndex].holdSeconds
                    : float.MaxValue;

                while (elapsed < limit && !_skipStep)
                {
                    if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                        _skipStep = true;
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            _stepIndex  = -1;
            _stepTarget = null;
            _overlayRoot.SetActive(false);
            OnComplete?.Invoke();
        }

        private void ApplyStep(TutorialStep step)
        {
            _portrait.sprite  = step.characterSprite;
            _portrait.enabled = step.characterSprite != null;
            _speechTMP.text   = step.speechText ?? "";

            // Resolve the pointer target ONCE, as the step fires. Gates are instantiated and
            // then zero-scaled/re-popped by GateRollController, so an Inspector reference is
            // frequently stale, null or pointing at something that isn't on screen yet.
            _stepTarget = null;
            if (IsUsableTarget(step.worldTarget))
                _stepTarget = step.worldTarget;
            else if (!string.IsNullOrEmpty(step.targetObjectName))
                _stepTarget = FindTargetByName(step.targetObjectName);
        }

        /// <summary>
        /// A target is only worth pointing at if it exists, is in an active hierarchy, and
        /// has real scale — GateRollController parks un-revealed gates at localScale 0, and a
        /// ring pinned to a zero-scaled object reads as "pointing at nothing".
        /// </summary>
        private static bool IsUsableTarget(Transform t)
        {
            if (t == null) return false;
            if (!t.gameObject.activeInHierarchy) return false;
            return t.lossyScale.sqrMagnitude > 1e-6f;
        }

        /// <summary>
        /// Deep name lookup across the active scene, INCLUDING inactive objects
        /// (GameObject.Find skips those, and un-revealed gates are frequently inactive).
        /// </summary>
        private static Transform FindTargetByName(string targetName)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name == targetName)
                        return all[i];
            }
            return null;
        }

        // ── Per-frame arrow + ring update ─────────────────────────────────────

        private void Update()
        {
            if (_stepIndex < 0 || _stepIndex >= Steps.Length) return;

            TutorialStep step         = Steps[_stepIndex];
            Vector2      targetScreen = ResolveTarget(step, out bool pinpoint);
            Vector2      targetLocal  = ToCanvas(targetScreen);

            // Recalc the "from" point each frame (top-centre of the speech panel)
            // Speech panel goes 0→22% screen height; top of panel is at 22%
            _panelTopLocal = ToCanvas(new Vector2(Screen.width * 0.50f, Screen.height * 0.22f));

            UpdateArrow(_panelTopLocal, targetLocal);
            UpdateRing(targetLocal, pinpoint);
        }

        private void UpdateArrow(Vector2 from, Vector2 to)
        {
            Vector2 dir   = to - from;
            float   dist  = dir.magnitude;
            // angle: atan2 - 90° because the rect's natural "up" is +Y which is 90° in atan2
            float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            // Shaft — pivot(0.5,0) sits at 'from', extends toward 'to'
            // Leave ~35 canvas-units gap at tip so the ring is visible
            _shaftRT.anchoredPosition = from;
            _shaftRT.sizeDelta        = new Vector2(5f, Mathf.Max(0f, dist - 42f));
            _shaftRT.localRotation    = Quaternion.Euler(0f, 0f, angle);

            // "▲" arrow-tip label: sits at 'to', rotated so it points BACK toward 'from'
            // (i.e. tip of ▲ faces the target, tail faces 'from')
            _arrowTip.rectTransform.anchoredPosition = to;
            float tipAngle = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg - 90f;
            _arrowTip.rectTransform.localRotation    = Quaternion.Euler(0f, 0f, tipAngle);
        }

        // pinpoint == false means "we only know roughly which way the target is" (behind the
        // camera / clamped to the viewport edge). The ring claims an exact spot, so it's hidden
        // in that case and only the arrow keeps pointing.
        private void UpdateRing(Vector2 targetLocal, bool pinpoint)
        {
            if (_ringImg.enabled != pinpoint) _ringImg.enabled = pinpoint;
            if (!pinpoint) return;

            _ringRT.anchoredPosition = targetLocal;
            float pulse = 1f + Mathf.Sin(Time.time * 3.8f) * 0.20f;
            _ringRT.localScale = Vector3.one * pulse;
        }

        // ── Camera resolution ─────────────────────────────────────────────────
        //
        // Camera.main returns whichever enabled MainCamera-tagged camera Unity finds first,
        // and returns null while that camera is disabled. The gameplay camera is the one
        // carrying the 0.2/0.6/0.2 viewport rect, so ask the controller for it by name and
        // keep Camera.main only as a last resort. Re-resolved whenever the cached one dies.

        private Camera ResolveCamera()
        {
            if (_cam != null && _cam.isActiveAndEnabled) return _cam;

            var ctrl = DropPuzzleCameraController.Instance;
            _cam = ctrl != null ? ctrl.GameplayCamera : null;
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
            return _cam;
        }

        // ── Target resolution ─────────────────────────────────────────────────
        //
        // Viewport vs screen, since getting this backwards is exactly what makes the ring
        // land nowhere near the object:
        //   • WorldToViewportPoint returns 0-1 RELATIVE TO THE CAMERA'S OWN RECT. It does not
        //     know or care where that rect sits on screen — dead centre of the gameplay
        //     column is (0.5, 0.5), not (0.5 * 0.6 + 0.2, …).
        //   • WorldToScreenPoint ALREADY maps through camera.pixelRect, so it returns true
        //     full-screen pixels with the rect offset baked in.
        // So the rect remap below is the correct partner for WorldToViewportPoint; applying it
        // on top of WorldToScreenPoint instead would double-count the rect and squash every
        // target 60% toward screen centre. Kept on the viewport path deliberately, because
        // that path also hands us vp.z for the behind-camera test.

        /// <summary>
        /// Full-screen pixel position to point at. <paramref name="pinpoint"/> is false when
        /// the result is an edge clamp rather than the target's true projected position.
        /// </summary>
        private Vector2 ResolveTarget(TutorialStep step, out bool pinpoint)
        {
            pinpoint = true;

            // Re-check usability every frame: a gate can pop in (or back out) mid-step.
            Transform target = IsUsableTarget(_stepTarget) ? _stepTarget
                             : IsUsableTarget(step.worldTarget) ? step.worldTarget
                             : null;

            var cam = ResolveCamera();
            if (target != null && cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(target.position);

                // vp.z <= 0 → the target is BEHIND the camera and vp.x/vp.y are mirrored
                // garbage. Easy to hit here: the camera is Y-rotated 180° and dollies in Z,
                // so anything in front of the board plane can cross the near plane. Flip the
                // point back through centre so the direction is right, then clamp to the edge.
                bool behind = vp.z <= 0f;
                if (behind)
                {
                    vp.x = 1f - vp.x;
                    vp.y = 1f - vp.y;
                }

                bool offscreen = behind || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f;
                if (offscreen)
                {
                    // 4% inset so the arrow tip stays fully on the gameplay column
                    vp.x     = Mathf.Clamp(vp.x, 0.04f, 0.96f);
                    vp.y     = Mathf.Clamp(vp.y, 0.04f, 0.96f);
                    pinpoint = false;
                }

                return ViewportToFullScreen(cam, vp);
            }

            // No usable world target — fall back to the authored normalised screen position.
            return new Vector2(
                step.screenFallback.x * Screen.width,
                step.screenFallback.y * Screen.height);
        }

        /// <summary>
        /// Camera-relative viewport point → full-screen pixels, accounting for the camera's
        /// letterboxed sub-rect (HUD columns take 20% on each side).
        /// </summary>
        private static Vector2 ViewportToFullScreen(Camera cam, Vector3 vp)
        {
            Rect r = cam.rect;
            return new Vector2(
                (r.x + vp.x * r.width)  * Screen.width,
                (r.y + vp.y * r.height) * Screen.height);
        }

        // Screen pixels → canvas local units. The canvas is ScreenSpaceOverlay, so the camera
        // argument MUST be null (passing one would re-project an already-screen-space point).
        // The result is in REFERENCE units (1920x1080 divided out by the CanvasScaler), and it
        // is measured from the canvas rect's pivot — i.e. screen centre is (0,0). The arrow,
        // tip and ring therefore anchor at (0.5,0.5) so their anchoredPosition shares that
        // origin; anchoring them at (0,0) would offset everything by half a screen down-left.
        private Vector2 ToCanvas(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRT, screenPos, null, out Vector2 local);
            return local;
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildUI()
        {
            var root = new GameObject("TutorialOverlay_Root");
            root.transform.SetParent(transform, false);

            _overlayRoot = root;
            root.SetActive(false); // hidden until PlayTutorial() is called

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight   = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            _canvasRT = root.GetComponent<RectTransform>();

            BuildArrow();
            BuildSpeechPanel();
        }

        private void BuildArrow()
        {
            // Every pointer part anchors at the canvas CENTRE (0.5, 0.5). ToCanvas() returns
            // points measured from the canvas pivot, which is centre — anchoring at (0,0)
            // would measure anchoredPosition from the bottom-left corner instead and shift
            // the whole pointer down-left by half a screen (960, 540 reference units).
            var centre = new Vector2(0.5f, 0.5f);

            // ── Shaft ──────────────────────────────────────────────────────────
            var shaftGO  = new GameObject("ArrowShaft");
            shaftGO.transform.SetParent(_canvasRT, false);
            var shaftImg = shaftGO.AddComponent<Image>();
            shaftImg.color = arrowColor;
            _shaftRT = shaftGO.GetComponent<RectTransform>();
            _shaftRT.anchorMin = _shaftRT.anchorMax = centre;
            _shaftRT.pivot     = new Vector2(0.5f, 0f); // bottom-centre → starts at 'from'
            _shaftRT.sizeDelta = new Vector2(5f, 0f);

            // ── Arrow-tip ("▲" TMP label, rotated each frame) ─────────────────
            var tipGO = new GameObject("ArrowTip");
            tipGO.transform.SetParent(_canvasRT, false);
            _arrowTip = tipGO.AddComponent<TextMeshProUGUI>();
            _arrowTip.text      = "▲";
            _arrowTip.fontSize  = 28;
            _arrowTip.color     = arrowColor;
            _arrowTip.alignment = TextAlignmentOptions.Center;
            var tipRT = _arrowTip.rectTransform;
            tipRT.anchorMin = tipRT.anchorMax = centre;
            tipRT.pivot     = new Vector2(0.5f, 0.5f);
            tipRT.sizeDelta = new Vector2(40f, 40f);

            // ── Pulsing ring at target ─────────────────────────────────────────
            var ringGO  = new GameObject("TargetRing");
            ringGO.transform.SetParent(_canvasRT, false);
            _ringImg = ringGO.AddComponent<Image>();
            _ringImg.color = ringColor;
            _ringRT = ringGO.GetComponent<RectTransform>();
            _ringRT.anchorMin = _ringRT.anchorMax = centre;
            _ringRT.pivot     = new Vector2(0.5f, 0.5f);
            _ringRT.sizeDelta = new Vector2(64f, 64f);
        }

        private void BuildSpeechPanel()
        {
            // ── Dark strip at bottom 22% of screen ────────────────────────────
            var panel = new GameObject("SpeechPanel");
            panel.transform.SetParent(_canvasRT, false);
            var panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0f, 0f);
            panelRT.anchorMax = new Vector2(1f, 0.22f);
            panelRT.offsetMin = panelRT.offsetMax = Vector2.zero;
            panel.AddComponent<Image>().color = panelColor;

            // ── Character portrait (left ~12%) ─────────────────────────────────
            var portGO = new GameObject("Portrait");
            portGO.transform.SetParent(panelRT, false);
            var portRT = portGO.AddComponent<RectTransform>();
            portRT.anchorMin = new Vector2(0.01f, 0.05f);
            portRT.anchorMax = new Vector2(0.12f, 0.95f);
            portRT.offsetMin = portRT.offsetMax = Vector2.zero;
            _portrait = portGO.AddComponent<Image>();
            _portrait.preserveAspect = true;

            // ── Speech text (12% → 85%) ────────────────────────────────────────
            var textGO = new GameObject("SpeechText");
            textGO.transform.SetParent(panelRT, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0.13f, 0.08f);
            textRT.anchorMax = new Vector2(0.85f, 0.95f);
            textRT.offsetMin = new Vector2(10f, 0f);
            textRT.offsetMax = new Vector2(-10f, 0f);
            _speechTMP = textGO.AddComponent<TextMeshProUGUI>();
            _speechTMP.fontSize           = 26;
            _speechTMP.color              = Color.white;
            _speechTMP.alignment          = TextAlignmentOptions.TopLeft;
            _speechTMP.enableWordWrapping = true;

            // ── "SPACE to continue" hint (bottom-right) ────────────────────────
            var hintGO = new GameObject("Hint");
            hintGO.transform.SetParent(panelRT, false);
            var hintRT = hintGO.AddComponent<RectTransform>();
            hintRT.anchorMin = new Vector2(0.86f, 0.05f);
            hintRT.anchorMax = new Vector2(0.99f, 0.60f);
            hintRT.offsetMin = hintRT.offsetMax = Vector2.zero;
            var hint = hintGO.AddComponent<TextMeshProUGUI>();
            hint.text      = "[SPACE]\nto continue";
            hint.fontSize  = 18;
            hint.color     = new Color(0.65f, 0.65f, 0.65f);
            hint.fontStyle = FontStyles.Italic;
            hint.alignment = TextAlignmentOptions.BottomRight;
        }
    }
}
