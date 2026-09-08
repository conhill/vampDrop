using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    // Slot-machine gate reveal for the right HUD panel.
    // After the puzzle loads, scans for MultiplierGate components and animates
    // a slot reel per gate. Fires OnRollComplete when done.
    //
    // Add to any scene GameObject in DropPuzzle. Works with or without DropPuzzleHUD.
    public class GateRollController : MonoBehaviour
    {
        public static event System.Action OnRollComplete;

        [Header("Animation")]
        public float spinDuration = 1.8f;
        public float spinInterval = 0.07f; // seconds between number flips during spin
        public float slowInterval = 0.25f; // interval at the end of the spin

        [Header("Behaviour")]
        [Tooltip("Start automatically when the puzzle loads. Set false when DropPuzzleFlowManager controls sequencing.")]
        public bool AutoStart = true;

        [Header("Guaranteed Drop")]
        [Tooltip("On a drop the game has promised a bonus gate for (the tutorial drop and the " +
                 "first run after it), force the slot machine to this multiplier instead of " +
                 "rolling the player's real odds. The reels still spin and still land on the " +
                 "symbol, so it reads as a won spin. Set 0 to always use the real odds.")]
        public int GuaranteedDropMultiplier = 2;

        [Tooltip("Fallback used only if PuzzlePrefabLoader did not publish a guarantee but the " +
                 "tutorial is still running. Leave on — it is the safety net for scenes where " +
                 "the puzzle was loaded by something other than PuzzlePrefabLoader.")]
        public bool TreatTutorialAsGuaranteed = true;

        [Header("Layout")]
        public float slotHeight   = 80f;
        public float slotSpacing  = 10f;

        // Parent rect for all gate roll content — injected by DropPuzzleHUD or set in Inspector
        public RectTransform HostPanel;

        private RectTransform _container;
        private TextMeshProUGUI _statusLabel;
        private TextMeshProUGUI _chancesLabel;
        private readonly List<TextMeshProUGUI> _slotLabels = new();
        private int[]        _gateValues;
        private GameObject[] _gateObjects;      // parallel to _gateValues
        private Vector3[]    _gateOriginalScale; // scales to restore on reveal
        private bool[]       _gateReveal;        // which gates survived the roll

        // Remembers each gate's AUTHORED scale the first time it is collected. CollectGates
        // zeroes localScale to hide the gate, so a second roll in the same scene would
        // otherwise cache that zero as the gate's "real" size and reveal it to nothing.
        private static readonly Dictionary<int, Vector3> _authoredScale = new();

        private static readonly int[] _spinOptions = { 1, 2, 3, 4, 5 };

        private void Start()
        {
            // AutoStart is always disabled by FlowManager — nothing to do here
            // except ensure HostPanel is ready if someone calls StartRoll() early.
        }

        private void OnDestroy()
        {
            PuzzlePrefabLoader.OnPuzzleLoaded -= OnPuzzleLoaded;
            _authoredScale.Clear();   // instance ids die with the scene
        }

        private void OnPuzzleLoaded() => StartRoll();

        // ── Public entry ──────────────────────────────────────────────────────

        public void StartRoll()
        {
            EnsureHostPanel();
            CollectGates();

            // Preferred: the 3-reel match-3 machine. Win applies the matched multiplier to
            // every gate; a loss means no bonus gates at all this drop.
            var slot3 = SlotMachine3Reel.Instance != null
                ? SlotMachine3Reel.Instance
                : FindObjectOfType<SlotMachine3Reel>();
            if (slot3 != null)
            {
                // A promised drop must not go through the weighted roll. Tier 0 — what a new
                // player has — is 96% no-match, so the tutorial's "guaranteed" x2 lost its own
                // guarantee 96 times out of 100. Forcing the result here keeps the reels
                // honest: they still spin and still land on x2-x2-x2 for real.
                int forced = GuaranteedMultiplierForThisDrop();
                if (forced > 0)
                {
                    slot3.ForceNextSpin(forced);
                    Debug.Log($"[GateRoll] Guaranteed drop — forcing the slot to x{forced}.");
                }

                slot3.StartSpin(OnSlotResolved);
                return;
            }

            // Legacy: the single-column 3D slot spinner; falls back to the text reel below.
            var spinner = SlotSpinner.Instance != null
                ? SlotSpinner.Instance
                : FindObjectOfType<SlotSpinner>();
            if (spinner != null)
            {
                if (_gateValues.Length == 0)
                {
                    // No multiplier gates this run — nothing to spin for.
                    StartCoroutine(FinishAfter(0.3f));
                }
                else
                {
                    // Legacy spinner has no win/lose concept — every gate it spun is revealed.
                    for (int i = 0; i < _gateReveal.Length; i++) _gateReveal[i] = true;
                    spinner.StartSpin(_gateValues, () => StartCoroutine(RevealGatesThenFinish()));
                }
                return;
            }

            BuildUI();
            StartCoroutine(RunRoll());
        }

        /// <summary>
        /// Match-3 result, applied as a FLOOR rather than a verdict.
        ///
        /// A win raises every gate on the board to the matched multiplier. A loss removes the
        /// gates the player did not earn — but never a gate PuzzleEnhancer stamped as
        /// guaranteed (MultiplierGate.GuaranteedMultiplier), because that gate was promised
        /// before the reels ever turned. Two systems used to own the same objects here: the
        /// enhancer placed a guaranteed x2 and this method then threw it away on the ~96%
        /// no-match roll a new player faces.
        ///
        /// CollectGates() has already zeroed each gate's scale, so a gate that is NOT kept
        /// gets its authored scale back and is then deactivated — a zero-scale collider still
        /// sits in the ball's path and would silently swallow trigger events.
        /// </summary>
        private void OnSlotResolved(bool won, int multiplier)
        {
            // Dismiss the modal so the gate pop-in and the weather roll aren't hidden behind it.
            var slot3 = SlotMachine3Reel.Instance != null
                ? SlotMachine3Reel.Instance
                : FindObjectOfType<SlotMachine3Reel>();
            if (slot3 != null) slot3.SetVisible(false);

            bool anyRevealed = false;

            for (int i = 0; i < _gateObjects.Length; i++)
            {
                var go = _gateObjects[i];
                if (go == null) continue;

                var gate  = go.GetComponent<MultiplierGate>();
                int floor = gate != null ? gate.GuaranteedMultiplier : 0;

                // The spin can only ADD. PuzzleEnhancer already decided this gate is promised
                // to the player, so a losing roll is not allowed to take it back — that clash
                // is what made the tutorial's guaranteed x2 disappear.
                bool keep  = won || floor > 0;
                int  value = Mathf.Max(won ? multiplier : 0, floor);

                if (!keep)
                {
                    // Restore the authored scale BEFORE deactivating. A gate parked at zero
                    // scale is an invisible object with a zero-size collider, and the next
                    // CollectGates would latch that zero as its real size.
                    go.transform.localScale = _gateOriginalScale[i];
                    go.SetActive(false);
                    _gateValues[i] = 0;
                    continue;
                }

                // A previous losing roll may have left this gate off; a keeper has to come back.
                if (!go.activeSelf) go.SetActive(true);

                if (gate != null) gate.SetMultiplier(value);
                _gateValues[i] = gate != null ? gate.Multiplier : value;
                _gateReveal[i] = true;
                anyRevealed = true;
            }

            // Colliders are read by RiceBallGateInteractionSystem through Collider.bounds, and
            // PhysicsOptimizer turns off Physics.autoSyncTransforms — sync the deactivations
            // now rather than leaving stale shapes in the ball's path.
            Physics.SyncTransforms();

            if (anyRevealed) StartCoroutine(RevealGatesThenFinish());
            else             StartCoroutine(FinishAfter(0.35f));
        }

        // Spinner path: pop the (hidden) gates into the puzzle one by one, then finish.
        private IEnumerator RevealGatesThenFinish()
        {
            for (int i = 0; i < _gateValues.Length && i < _gateObjects.Length; i++)
            {
                if (_gateObjects[i] == null) continue;
                if (_gateReveal != null && i < _gateReveal.Length && !_gateReveal[i]) continue;

                yield return StartCoroutine(PopGateIn(_gateObjects[i], _gateOriginalScale[i]));
            }

            // The gates are at their final size now — push it to the physics scene so the
            // ECS gate check sees a full-size collider on the very first ball.
            Physics.SyncTransforms();

            yield return new WaitForSeconds(0.4f);
            FinishRoll();
        }

        private IEnumerator FinishAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            FinishRoll();
        }

        private void EnsureHostPanel()
        {
            if (HostPanel != null) return;
            if (DropPuzzleHUD.Instance != null)
                HostPanel = DropPuzzleHUD.Instance.RightPanel;
            if (HostPanel == null)
                HostPanel = CreateFallbackPanel();
        }

        // ── Gate discovery ────────────────────────────────────────────────────

        private void CollectGates()
        {
            // Include inactive gates: a previous losing roll switches non-guaranteed gates off,
            // and the default overload skips those — a re-roll could then never find them
            // again, let alone bring one back on a win.
            var gates = FindObjectsByType<MultiplierGate>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            _gateValues        = new int[gates.Length];
            _gateObjects       = new GameObject[gates.Length];
            _gateOriginalScale = new Vector3[gates.Length];
            _gateReveal        = new bool[gates.Length];

            for (int i = 0; i < gates.Length; i++)
            {
                var t  = gates[i].transform;
                int id = gates[i].GetInstanceID();

                _gateValues[i]  = gates[i].Multiplier;
                _gateObjects[i] = gates[i].gameObject;

                // Remember the authored scale once, and never re-learn it from a scale a
                // previous roll already zeroed.
                if (!_authoredScale.TryGetValue(id, out Vector3 authored))
                {
                    authored = t.localScale;
                    if (authored.sqrMagnitude < 0.000001f)
                    {
                        Debug.LogWarning($"[GateRoll] {gates[i].name} was already at zero scale " +
                                         "when collected — falling back to unit scale so the " +
                                         "reveal doesn't pop it into nothing.");
                        authored = Vector3.one;
                    }
                    _authoredScale[id] = authored;
                }
                _gateOriginalScale[i] = authored;

                // Hide gate until the roll reveals it
                t.localScale = Vector3.zero;
            }

            Physics.SyncTransforms();
        }

        // ── Guaranteed drops ──────────────────────────────────────────────────

        /// <summary>
        /// The multiplier this drop is promised to show, or 0 when the real odds apply.
        ///
        /// Reads PuzzlePrefabLoader's decision rather than re-deriving it from the tutorial
        /// flags, so the gate that got SPAWNED and the result that gets ROLLED can never
        /// disagree. The tutorial check below is only a safety net for scenes where something
        /// other than PuzzlePrefabLoader built the board.
        /// </summary>
        private int GuaranteedMultiplierForThisDrop()
        {
            if (GuaranteedDropMultiplier <= 0) return 0;

            if (PuzzlePrefabLoader.GuaranteedGateMultiplier > 0)
                return Mathf.Max(GuaranteedDropMultiplier, PuzzlePrefabLoader.GuaranteedGateMultiplier);

            // A gate that was stamped as guaranteed is authoritative even if the static was
            // cleared by a scene reload.
            if (_gateObjects != null)
                foreach (var go in _gateObjects)
                {
                    if (go == null) continue;
                    var gate = go.GetComponent<MultiplierGate>();
                    if (gate != null && gate.IsGuaranteed)
                        return Mathf.Max(GuaranteedDropMultiplier, gate.GuaranteedMultiplier);
                }

            if (!TreatTutorialAsGuaranteed) return 0;

            var pdm = PlayerDataManager.Instance;
            if (pdm != null && !pdm.TutorialCompleted) return GuaranteedDropMultiplier;

            var tm = TutorialManager.Instance;
            if (tm != null && tm.tutorialActive) return GuaranteedDropMultiplier;

            return 0;
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildUI()
        {
            // Clear any previous content
            if (_container != null) Destroy(_container.gameObject);
            _slotLabels.Clear();

            _container = MakeRect(HostPanel, "GateRollContainer",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Header
            var header = MakeLabel(_container, "RollHeader",
                new Vector2(0f, 0.88f), new Vector2(1f, 1f),
                "── GATE ROLL ──", 22, FontStyles.Bold, TextAlignmentOptions.Center);
            header.color = new Color(1f, 0.85f, 0.3f);

            // Status line
            _statusLabel = MakeLabel(_container, "Status",
                new Vector2(0f, 0.78f), new Vector2(1f, 0.88f),
                "Rolling…", 18, FontStyles.Italic, TextAlignmentOptions.Center);
            _statusLabel.color = new Color(0.8f, 0.8f, 0.8f);

            // One slot per gate (or a single "no gates" slot)
            int count = Mathf.Max(1, _gateValues.Length);
            float startY = 0.74f;
            float stepY  = 0.12f;

            for (int i = 0; i < count; i++)
            {
                float yMax = startY - i * stepY;
                float yMin = yMax - stepY + 0.01f;

                var slotBg = MakeRect(_container, $"Slot_{i}",
                    new Vector2(0.1f, yMin), new Vector2(0.9f, yMax),
                    Vector2.zero, Vector2.zero);
                var bg = slotBg.gameObject.AddComponent<Image>();
                bg.color = new Color(0.08f, 0.08f, 0.15f, 0.9f);

                string initText = _gateValues.Length == 0 ? "x1" : "??";
                var lbl = MakeLabel(slotBg, $"SlotLabel_{i}",
                    Vector2.zero, Vector2.one, "??", 32, FontStyles.Bold,
                    TextAlignmentOptions.Center);
                lbl.color = Color.white;
                _slotLabels.Add(lbl);
            }

            // Modifier placeholder (shifted up to make room for chances)
            var modBtn = MakeRect(_container, "ModBtn",
                new Vector2(0.05f, 0.22f), new Vector2(0.95f, 0.32f),
                Vector2.zero, Vector2.zero);
            var modBg = modBtn.gameObject.AddComponent<Image>();
            modBg.color = new Color(0.2f, 0.2f, 0.35f, 0.9f);
            var modLbl = MakeLabel(modBtn, "ModLabel",
                Vector2.zero, Vector2.one, "[+] Add Modifier\n(coming soon)", 15,
                FontStyles.Normal, TextAlignmentOptions.Center);
            modLbl.color = new Color(0.6f, 0.6f, 0.7f);

            // Gate chances section
            var chHdr = MakeLabel(_container, "ChancesHeader",
                new Vector2(0f, 0.13f), new Vector2(1f, 0.21f),
                "── YOUR CHANCES ──", 17, FontStyles.Bold, TextAlignmentOptions.Center);
            chHdr.color = new Color(0.7f, 0.7f, 0.7f);

            _chancesLabel = MakeLabel(_container, "ChancesDetail",
                new Vector2(0f, 0.01f), new Vector2(1f, 0.13f),
                BuildChancesText(), 16, FontStyles.Normal, TextAlignmentOptions.Left);
            _chancesLabel.color = Color.white;
        }

        // ── Slot animation ────────────────────────────────────────────────────

        private IEnumerator RunRoll()
        {
            if (_statusLabel) _statusLabel.text = "Rolling…";

            if (_gateValues.Length == 0)
            {
                // No multiplier gates this run — show a brief spin then confirm
                if (_slotLabels.Count > 0)
                {
                    yield return SpinSlot(_slotLabels[0], 1, spinDuration);
                    _slotLabels[0].text  = "x1";
                    _slotLabels[0].color = Color.gray;
                }
                if (_statusLabel) _statusLabel.text = "No bonus gates";
                yield return new WaitForSeconds(0.5f);
                FinishRoll();
                yield break;
            }

            // 1. Spin all slots simultaneously
            for (int i = 0; i < _gateValues.Length; i++)
            {
                if (i < _slotLabels.Count)
                    StartCoroutine(SpinSlot(_slotLabels[i], _gateValues[i], spinDuration));
            }
            yield return new WaitForSeconds(spinDuration);

            // 2. Reveal each value and pop the gate into the puzzle, one at a time
            for (int i = 0; i < _gateValues.Length && i < _slotLabels.Count; i++)
            {
                _slotLabels[i].text  = $"x{_gateValues[i]}";
                _slotLabels[i].color = MultiplierColor(_gateValues[i]);

                if (i < _gateObjects.Length && _gateObjects[i] != null)
                {
                    if (_gateReveal != null && i < _gateReveal.Length) _gateReveal[i] = true;
                    yield return StartCoroutine(PopGateIn(_gateObjects[i], _gateOriginalScale[i]));
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }

            string summary = _gateValues.Length == 1
                ? $"1 gate: x{_gateValues[0]}"
                : $"{_gateValues.Length} gates ready!";
            if (_statusLabel) _statusLabel.text = summary;

            yield return new WaitForSeconds(0.8f);
            FinishRoll();
        }

        private IEnumerator PopGateIn(GameObject gate, Vector3 targetScale)
        {
            // A zero target would "reveal" the gate into nothing and leave a zero-size
            // collider sitting in the ball's path.
            if (targetScale.sqrMagnitude < 0.000001f) targetScale = Vector3.one;

            const float popDuration = 0.45f;
            float elapsed = 0f;
            // Overshoot then settle — feels snappier than linear
            while (elapsed < popDuration)
            {
                float t = elapsed / popDuration;
                // Punch curve: overshoot at ~70% then settle
                float s = t < 0.7f
                    ? Mathf.Lerp(0f, 1.15f, t / 0.7f)
                    : Mathf.Lerp(1.15f, 1f, (t - 0.7f) / 0.3f);
                gate.transform.localScale = targetScale * s;
                elapsed += Time.deltaTime;
                yield return null;
            }
            gate.transform.localScale = targetScale;
        }

        private IEnumerator SpinSlot(TextMeshProUGUI label, int finalValue, float duration)
        {
            float elapsed  = 0f;
            float interval = spinInterval;

            while (elapsed < duration)
            {
                int fake = _spinOptions[Random.Range(0, _spinOptions.Length)];
                label.text  = $"x{fake}";
                label.color = MultiplierColor(fake);

                float remaining = duration - elapsed;
                interval = remaining < 0.4f
                    ? Mathf.Lerp(slowInterval, spinInterval, remaining / 0.4f)
                    : spinInterval;

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            label.text  = $"x{finalValue}";
            label.color = MultiplierColor(finalValue);
        }

        private void FinishRoll()
        {
            OnRollComplete?.Invoke();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildChancesText()
        {
            var dp = PlayerDataManager.Instance?.DropPuzzle;
            if (dp == null) return "x2: —\nx3: —\nx4: —\nx5: —";
            return $"<color=#88FF88>x2</color>  {dp.x2GateChance * 100f:F0}%\n" +
                   $"<color=#88AAFF>x3</color>  {dp.x3GateChance * 100f:F0}%\n" +
                   $"<color=#FFB833>x4</color>  {dp.x4GateChance * 100f:F0}%\n" +
                   $"<color=#FF66FF>x5</color>  {dp.x5GateChance * 100f:F0}%";
        }

        private static Color MultiplierColor(int mult) => mult switch
        {
            2 => new Color(0.5f, 1f, 0.5f),
            3 => new Color(0.5f, 0.7f, 1f),
            4 => new Color(1f, 0.7f, 0.2f),
            5 => new Color(1f, 0.4f, 1f),
            _ => Color.gray
        };

        private RectTransform MakeRect(RectTransform parent, string name,
                                       Vector2 anchorMin, Vector2 anchorMax,
                                       Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private TextMeshProUGUI MakeLabel(RectTransform parent, string name,
                                          Vector2 anchorMin, Vector2 anchorMax,
                                          string text, float size,
                                          FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(4f, 2f);
            rt.offsetMax = new Vector2(-4f, -2f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = size;
            tmp.fontStyle = style;
            tmp.color     = Color.white;
            tmp.alignment = align;
            return tmp;
        }

        private RectTransform CreateFallbackPanel()
        {
            var go = new GameObject("GateRollFallbackCanvas");
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode   = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 11;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(go.transform, false);
            var rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.8f, 0f);
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            panel.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.72f);
            return rt;
        }
    }
}
