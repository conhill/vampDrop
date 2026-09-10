using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Three-reel match-3 slot machine for the pre-drop GateRoll phase.
    ///
    /// Replaces the single-column <see cref="SlotSpinner"/> (which rendered slot.fbx through
    /// a camera into a RenderTexture in the side HUD). This is flat UI in a centred modal
    /// sized to match <see cref="WeatherWheelUI"/>, so the two pre-drop steps read as one
    /// system. All three reels must land on the SAME symbol to win; anything else pays out
    /// nothing.
    ///
    /// The outcome is decided BEFORE the animation and the reels are then driven to it —
    /// standard slot practice, and the only way to control the win rate. Rolling each reel
    /// independently would lock the win rate to 1/symbols², which at five symbols is 4%.
    ///
    /// Because a win-or-nothing rule means most spins lose, <see cref="NearMissChance"/>
    /// controls how often a LOSS still lands two matching reels before the third misses.
    /// That is what carries the tension on losing spins — turn it down for a flatter feel.
    /// </summary>
    public class SlotMachine3Reel : MonoBehaviour
    {
        public static SlotMachine3Reel Instance { get; private set; }

        /// <summary>Fired when the spin finishes: (won, multiplier). Multiplier is 0 on a loss.</summary>
        public event Action<bool, int> OnSpinComplete;

        [Header("Panel")]
        [Tooltip("Square panel size in px. Matches WeatherWheelUI: GridSize*(CellSize+Spacing)+40 = 510.")]
        public float PanelSize = 510f;
        public Color PanelColor = new Color(0.08f, 0.06f, 0.10f, 0.95f);
        [Tooltip("Sort order of the modal canvas. Weather wheel uses 25.")]
        public int SortingOrder = 24;

        [Header("Blackout")]
        [Tooltip("Full-screen backdrop drawn behind the panel so the drop board isn't visible " +
                 "through the modal. Without it the puzzle reads straight through and the two " +
                 "layers fight each other.")]
        public bool DimBackground = true;
        [Range(0f, 1f)] public float DimAlpha = 0.92f;
        public Color DimColor = Color.black;
        [Tooltip("Seconds to fade the blackout in and out.")]
        public float DimFadeTime = 0.2f;

        [Header("Reels")]
        [Tooltip("Symbols on every reel strip, in order. Repeat a value to weight how often it " +
                 "APPEARS — this is cosmetic only; the actual odds come from Tiers. Keep this " +
                 "set to the same values the tiers can award, or the reels will tease a " +
                 "near miss on a multiplier the player can never actually win.")]
        public int[] Strip = { 2, 3, 2, 5, 2, 3, 2, 5, 3, 2 };
        [Tooltip("Height of one symbol cell in px.")]
        public float CellHeight = 110f;
        [Tooltip("Width of one reel column in px.")]
        public float ReelWidth = 130f;
        [Tooltip("Gap between reel columns in px.")]
        public float ReelSpacing = 14f;

        /// <summary>
        /// One row of the odds table. Weights are relative, so they can be written as the
        /// percentages they represent — {96, 3, 0.9, 0.1} sums to 100 and reads directly.
        /// </summary>
        [Serializable]
        public class OddsTier
        {
            public string Name = "Level 1";
            [Tooltip("Weight for NO MATCH. Dominant at low levels — upgrades move weight off " +
                     "this row and onto the multipliers.")]
            public float NoMatchWeight = 96f;
            [Tooltip("Multipliers that can be matched, parallel to WinWeights.")]
            public int[] WinValues = { 2, 3, 5 };
            [Tooltip("Weight per multiplier, parallel to WinValues.")]
            public float[] WinWeights = { 3f, 0.9f, 0.1f };
        }

        [Header("Odds (ordered worst → best)")]
        [Tooltip("Odds tables by upgrade level. Level 0 is what a new player sees.")]
        public OddsTier[] Tiers =
        {
            new OddsTier { Name = "Level 1 (default)", NoMatchWeight = 96f, WinValues = new[]{2,3,5}, WinWeights = new[]{3f,   0.9f, 0.1f} },
            new OddsTier { Name = "Level 2",           NoMatchWeight = 85f, WinValues = new[]{2,3,5}, WinWeights = new[]{11f,  3f,   1f}   },
            new OddsTier { Name = "Level 3 (target)",  NoMatchWeight = 70f, WinValues = new[]{2,3,5}, WinWeights = new[]{25f,  3f,   2f}   },
        };

        public enum OddsSource { PlayerUpgrade, Manual }

        [Tooltip("PlayerUpgrade reads PlayerDataManager.DropPuzzle.slotOddsLevel so the shop " +
                 "can raise it. Manual pins the level for testing.")]
        public OddsSource Source = OddsSource.PlayerUpgrade;
        [Tooltip("Used when Source is Manual, or when there's no PlayerDataManager.")]
        public int ManualTier = 0;

        // ── Forced outcome ───────────────────────────────────────────────────
        // A scripted spin (the tutorial's promised x2) has to bypass the odds table
        // entirely. At tier 0 NoMatchWeight is 96 against WinWeights {3, 0.9, 0.1}, so a
        // "guaranteed" gate that still goes through RollOutcome loses ~96% of the time —
        // which is exactly how the tutorial's guaranteed 2x went missing.
        private bool _forcePending;
        private int  _forcedPrize;

        /// <summary>
        /// Pins the NEXT spin's result and skips the weighted roll for it. Pass the
        /// multiplier to award, or 0 to force a loss. The reels still spin and still land
        /// on the symbol for real, so a forced win reads as a won spin rather than a
        /// skipped animation. Consumed by that one spin; later spins roll normally.
        /// </summary>
        public void ForceNextSpin(int multiplier)
        {
            _forcePending = true;
            _forcedPrize  = Mathf.Max(0, multiplier);
        }

        /// <summary>Drops a pending <see cref="ForceNextSpin"/> without spinning.</summary>
        public void ClearForcedSpin()
        {
            _forcePending = false;
            _forcedPrize  = 0;
        }

        /// <summary>True while a forced result is queued for the next spin.</summary>
        public bool HasForcedSpin => _forcePending;

        [Range(0f, 1f)]
        [Tooltip("On a LOSS, chance the first two reels still match so the third is a near miss. " +
                 "This is what keeps losing spins tense; 0 makes losses read as flat noise. " +
                 "It matters most at low levels, where almost every spin is a loss.")]
        public float NearMissChance = 0.55f;

        [Header("Feel")]
        public float SpinUpTime = 0.35f;
        [Tooltip("How long reel 1 spins before stopping. Each later reel adds ReelStagger.")]
        public float BaseSpinTime = 1.1f;
        [Tooltip("Extra spin time per reel, left to right.")]
        public float ReelStagger = 0.55f;
        [Tooltip("Extra time the LAST reel spins when the first two matched — the near-miss crawl.")]
        public float SuspenseTime = 0.9f;
        [Tooltip("Target speed in px/sec at the peak of the spin. The actual peak is solved " +
                 "per reel (see SpinReel) so the landing is exact; it stays within ~10% of this.")]
        public float ScrollSpeed = 2600f;
        [Tooltip("Length of the deceleration tail — the reel coasts to a dead stop on its " +
                 "symbol over this long. The near-miss reel adds SuspenseTime on top.")]
        public float SettleTime = 0.55f;
        [Tooltip("Fewest whole strip lengths a reel travels, so even a short spin reads as a spin.")]
        public int MinLaps = 1;
        public float ResultHold = 1.0f;

        [Header("Input")]
        [Tooltip("Show the machine and wait for a key press before spinning, instead of " +
                 "spinning the moment the phase starts.")]
        public bool RequireKeyPress = true;
        public KeyCode StartKey = KeyCode.Space;
        [Tooltip("Prompt shown while waiting for the key.")]
        public string StartPrompt = "PRESS SPACE TO ROLL";
        [Tooltip("Ignore the key for this long after the modal appears, so a press meant for " +
                 "the previous screen doesn't roll the slot instantly.")]
        public float InputGraceTime = 0.25f;

        [Header("Debug")]
        public bool TestOnStart = false;

        // ── Runtime ──────────────────────────────────────────────────────────
        private Canvas _canvas;
        private RectTransform _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _result;
        private Image _flash;
        private Image _dim;
        private readonly List<RectTransform> _content = new();   // scrolling strip per reel
        private readonly float[] _scroll = new float[3];
        private int  _reelsRunning;
        private bool _built;
        private bool _spinning;
        private bool _crawlAnnounced;

        /// <summary>Copies of the strip stacked in each reel's content rect (see Build).</summary>
        private const int StripCopies = 3;

        public bool IsSpinning => _spinning;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            Build();
            SetVisible(false);
            if (TestOnStart) StartSpin((won, v) => Debug.Log($"[Slot3] test → won={won} x{v}"));
        }

        // ── Build ────────────────────────────────────────────────────────────

        /// <summary>Builds the modal. Idempotent.</summary>
        public void Build()
        {
            if (_built) return;
            if (Strip == null || Strip.Length < 3)
            {
                Debug.LogError("[SlotMachine3Reel] Strip needs at least 3 symbols (the viewport " +
                               "shows 3 rows, so a shorter strip can't fill it).");
                return;
            }

            var canvasGO = new GameObject("SlotMachine3ReelCanvas");
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            // Blackout first so it renders BEHIND the panel (UI draws in sibling order).
            // Also swallows clicks, so nothing behind the modal stays interactive.
            if (DimBackground)
            {
                var dimRt = MakeRect(canvasGO.transform as RectTransform, "Blackout");
                Anchor(dimRt, Vector2.zero, Vector2.one);
                _dim = dimRt.gameObject.AddComponent<Image>();
                _dim.color = new Color(DimColor.r, DimColor.g, DimColor.b, 0f);
                _dim.raycastTarget = true;
            }

            _root = MakeRect(canvasGO.transform as RectTransform, "SlotPanel");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(PanelSize, PanelSize);
            _root.gameObject.AddComponent<Image>().color = PanelColor;

            _title = MakeText(_root, "Title", 26, FontStyles.Bold);
            _title.text  = "GATE ROLL";
            _title.color = new Color(1f, 0.85f, 0.3f);
            Anchor(_title.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 0.98f));

            // Reel bay — three masked viewports side by side.
            float bayW = 3f * ReelWidth + 2f * ReelSpacing;
            var bay = MakeRect(_root, "ReelBay");
            bay.anchorMin = bay.anchorMax = new Vector2(0.5f, 0.5f);
            bay.sizeDelta = new Vector2(bayW, CellHeight * 3f);
            bay.anchoredPosition = new Vector2(0f, 20f);

            for (int r = 0; r < 3; r++)
            {
                float x = -bayW * 0.5f + ReelWidth * 0.5f + r * (ReelWidth + ReelSpacing);

                var view = MakeRect(bay, $"Reel_{r}");
                view.anchorMin = view.anchorMax = new Vector2(0.5f, 0.5f);
                view.sizeDelta = new Vector2(ReelWidth, CellHeight * 3f);
                view.anchoredPosition = new Vector2(x, 0f);
                view.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.04f, 1f);
                view.gameObject.AddComponent<RectMask2D>();

                var content = MakeRect(view, "Content");
                content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
                content.sizeDelta = new Vector2(ReelWidth, CellHeight * Strip.Length * StripCopies);

                // THREE stacked copies, with the middle one centred on the viewport. The scroll
                // only ever walks across that middle copy, so the copies above and below supply
                // the rows entering and leaving the window. Laying the cells out from the centre
                // downward instead (one copy's worth) leaves the whole upper half of the content
                // empty, which shows as blank rows above the win line.
                float stripHeight = Strip.Length * CellHeight;
                for (int i = 0; i < Strip.Length * StripCopies; i++)
                {
                    int val = Strip[i % Strip.Length];
                    var cell = MakeText(content, $"Cell_{i}_x{val}", 54, FontStyles.Bold);
                    cell.text  = $"x{val}";
                    cell.color = MultiplierColor(val);
                    cell.alignment = TextAlignmentOptions.Center;
                    cell.rectTransform.anchorMin = cell.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    cell.rectTransform.sizeDelta = new Vector2(ReelWidth, CellHeight);
                    cell.rectTransform.anchoredPosition = new Vector2(0f, stripHeight - i * CellHeight);
                }

                _content.Add(content);
            }

            SetIdlePose();

            // Win line across the middle cell, so the player knows which row counts.
            var line = MakeRect(bay, "WinLine");
            line.anchorMin = new Vector2(0f, 0.5f);
            line.anchorMax = new Vector2(1f, 0.5f);
            line.sizeDelta = new Vector2(24f, 3f);
            line.gameObject.AddComponent<Image>().color = new Color(1f, 0.85f, 0.3f, 0.5f);

            _result = MakeText(_root, "Result", 30, FontStyles.Bold);
            _result.text = "";
            Anchor(_result.rectTransform, new Vector2(0f, 0.03f), new Vector2(1f, 0.17f));

            // Full-panel flash used on a win.
            var flashRt = MakeRect(_root, "Flash");
            Anchor(flashRt, Vector2.zero, Vector2.one);
            _flash = flashRt.gameObject.AddComponent<Image>();
            _flash.color = new Color(1f, 0.85f, 0.3f, 0f);
            _flash.raycastTarget = false;

            _built = true;
        }

        public void SetVisible(bool on)
        {
            if (_canvas == null) return;
            _canvas.gameObject.SetActive(on);

            // Snap the blackout to its end state; StartSpin fades it in properly.
            if (_dim != null)
                _dim.color = new Color(DimColor.r, DimColor.g, DimColor.b, on ? DimAlpha : 0f);
        }

        private IEnumerator FadeDim(float from, float to)
        {
            if (_dim == null || DimFadeTime <= 0f)
            {
                if (_dim != null) _dim.color = new Color(DimColor.r, DimColor.g, DimColor.b, to);
                yield break;
            }

            float e = 0f;
            while (e < DimFadeTime)
            {
                float a = Mathf.Lerp(from, to, e / DimFadeTime);
                _dim.color = new Color(DimColor.r, DimColor.g, DimColor.b, a);
                e += Time.deltaTime;
                yield return null;
            }
            _dim.color = new Color(DimColor.r, DimColor.g, DimColor.b, to);
        }

        // ── Spin ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Decides the outcome, animates the reels to it, then reports (won, multiplier).
        /// </summary>
        public void StartSpin(Action<bool, int> onComplete)
        {
            if (!_built) Build();
            if (!_built) { onComplete?.Invoke(false, 0); return; }

            // Never swallow the callback. GateRollController drives the whole pre-drop phase
            // off this completing — dropping it on a re-entrant call leaves the flow stuck in
            // GateRoll forever with the modal still up and no way to advance.
            if (_spinning)
            {
                Debug.LogWarning("[SlotMachine3Reel] StartSpin called while already spinning — " +
                                 "reporting a loss so the caller isn't left waiting.");
                onComplete?.Invoke(false, 0);
                return;
            }

            SetVisible(true);
            StartCoroutine(SpinRoutine(onComplete));
        }

        private IEnumerator SpinRoutine(Action<bool, int> onComplete)
        {
            _spinning = true;
            _result.text = "";
            _flash.color = new Color(1f, 0.85f, 0.3f, 0f);

            if (_dim != null) yield return FadeDim(0f, DimAlpha);

            // Wait for the player to commit. The grace window stops a press aimed at the
            // cinematic that precedes this from rolling the slot the instant it appears.
            if (RequireKeyPress)
            {
                _result.text  = StartPrompt;
                _result.color = new Color(1f, 0.85f, 0.3f);

                float grace = 0f;
                while (grace < InputGraceTime) { grace += Time.deltaTime; yield return null; }

                while (!Input.GetKeyDown(StartKey)) yield return null;

                _result.text = "";
            }

            int prizeRoll = TakeOutcome();
            bool won = prizeRoll > 0;
            int[] symbols = won ? new[] { prizeRoll, prizeRoll, prizeRoll } : LosingSymbols();

            // Suspense whenever the first two reels agree — true on every win, and on a loss
            // when NearMissChance put two matching symbols up front.
            bool suspense = symbols[0] == symbols[1];
            bool nearMiss = !won && suspense;
            _crawlAnnounced = false;

            // Each reel drives itself to its symbol on a plan solved up front, so its speed
            // ramps up once and then decays to zero on the landing — no mid-spin snap.
            float stripPx = Strip.Length * CellHeight;
            _reelsRunning = 3;
            for (int r = 0; r < 3; r++)
            {
                float spinTime = BaseSpinTime + r * ReelStagger;
                float tail     = SettleTime;
                bool  crawl    = r == 2 && suspense;
                if (crawl) { spinTime += SuspenseTime; tail += SuspenseTime; }

                StartCoroutine(SpinReel(r, IndexOf(symbols[r]), stripPx, spinTime, tail, crawl));
            }
            while (_reelsRunning > 0) yield return null;

            // A beat to read the landed row before the verdict lands on top of it.
            yield return new WaitForSeconds(0.15f);

            int prize = won ? symbols[0] : 0;
            if (won)
            {
                _result.text  = $"MATCH!  x{prize}";
                _result.color = MultiplierColor(prize);
                yield return Flash(MultiplierColor(prize));
            }
            else
            {
                _result.text  = nearMiss ? "SO CLOSE" : "NO MATCH";
                _result.color = new Color(0.75f, 0.75f, 0.78f);
            }

            yield return new WaitForSeconds(ResultHold);

            _spinning = false;
            OnSpinComplete?.Invoke(won, prize);
            onComplete?.Invoke(won, prize);
        }

        /// <summary>
        /// Runs one reel from where it stands to <paramref name="targetIndex"/>.
        ///
        /// The reel's ENTIRE travel is solved before it moves. Travel has to be the forward gap
        /// to the target cell plus a whole number of laps, so it only comes in 1-strip steps;
        /// the leftover is split between spinning a little longer and spinning a little faster,
        /// which keeps both within a few percent of the authored feel. Position is then read
        /// straight off the profile each frame, so the reel accelerates once, decays smoothly to
        /// a dead stop, and is sitting on its symbol the instant it stops.
        ///
        /// The old approach — spin blind for a fixed time, then ease onto whichever cell was
        /// nearest — could have up to a full strip left to cover in a fixed SettleTime, which
        /// read as the reel lurching back up to speed exactly when it should be settling.
        /// </summary>
        private IEnumerator SpinReel(int reel, int targetIndex, float stripPx,
                                     float wantTime, float tailTime, bool crawl)
        {
            wantTime = Mathf.Max(0.2f, wantTime);
            float up   = Mathf.Clamp(SpinUpTime, 0.01f, wantTime * 0.35f);
            float tail = Mathf.Clamp(tailTime, 0.05f, wantTime - up);

            // Tail shape: velocity ~ (1-x)^decay. A steeper exponent spends longer near zero,
            // which is what makes the near-miss reel visibly tick over its last few symbols.
            float decay = crawl ? 3f : 2f;

            // Distance covered per unit of peak speed: ramp (average ½) + cruise (1) + tail
            // (∫(1-x)^decay dx = 1/(decay+1)). "Span" is that time-equivalent.
            float fixedSpan = up * 0.5f + tail / (decay + 1f);
            float wantSpan  = Mathf.Max(fixedSpan, wantTime - up - tail + fixedSpan);

            float start = _scroll[reel];
            float gap   = Mathf.Repeat(targetIndex * CellHeight - start, stripPx);

            // Enough laps to hit the intended distance, and never so few that the reel would
            // have to crawl the whole way just to avoid overshooting its cell.
            int minLaps = Mathf.Max(0, Mathf.CeilToInt((ScrollSpeed * fixedSpan - gap) / stripPx));
            int laps = Mathf.Max(Mathf.Max(minLaps, MinLaps),
                                 Mathf.RoundToInt((ScrollSpeed * wantSpan - gap) / stripPx));
            float distance = gap + laps * stripPx;

            // Absorb what we can of the rounding into the duration — bounded well inside the
            // stagger so reels still stop strictly left to right — and let peak speed take the
            // rest. Loading it all onto speed instead makes reels visibly race each other.
            float slip = Mathf.Min(0.15f, ReelStagger * 0.4f);
            float span = Mathf.Clamp(distance / ScrollSpeed, wantSpan - slip, wantSpan + slip);
            span = Mathf.Max(span, fixedSpan);

            float cruise    = span - fixedSpan;
            float totalTime = up + cruise + tail;
            float peak      = distance / span;

            float t = 0f;
            while (t < totalTime)
            {
                t += Time.deltaTime;
                float travelled = Travelled(Mathf.Min(t, totalTime), up, cruise, tail, decay, peak);
                _scroll[reel] = Mathf.Repeat(start + travelled, stripPx);
                ApplyScroll(reel);

                if (crawl && !_crawlAnnounced && t >= up + cruise)
                {
                    _crawlAnnounced = true;
                    _result.text  = "...";
                    _result.color = new Color(1f, 0.85f, 0.3f);
                }
                yield return null;
            }

            _scroll[reel] = Mathf.Repeat(targetIndex * CellHeight, stripPx);
            ApplyScroll(reel);
            _reelsRunning--;
        }

        /// <summary>
        /// Distance covered by time <paramref name="t"/> under the ramp → cruise → decay
        /// velocity profile. Integrated in closed form rather than accumulated per frame, so
        /// the landing is frame-rate independent and lands exactly on the cell.
        /// </summary>
        private static float Travelled(float t, float up, float cruise, float tail,
                                       float decay, float peak)
        {
            if (t <= 0f) return 0f;

            // Ramp: v = peak * smoothstep(x)  →  ∫ = peak * up * (x³ - x⁴/2).
            if (t < up)
            {
                float x = t / up;
                return peak * up * (x * x * x - 0.5f * x * x * x * x);
            }
            float d = peak * up * 0.5f;

            if (t < up + cruise) return d + peak * (t - up);
            d += peak * cruise;

            // Tail: v = peak * (1-x)^decay  →  ∫ = peak * tail * (1-(1-x)^(decay+1))/(decay+1).
            float y = tail > 0f ? Mathf.Clamp01((t - up - cruise) / tail) : 1f;
            return d + peak * tail * (1f - Mathf.Pow(1f - y, decay + 1f)) / (decay + 1f);
        }

        /// <summary>
        /// Resting pose between spins: each reel on its own random symbol, never three of a
        /// kind. Starting every reel at strip index 0 lines all three up on the same value,
        /// which reads as a win the player hasn't rolled for.
        /// </summary>
        private void SetIdlePose()
        {
            var idx = new int[3];
            for (int r = 0; r < 3; r++) idx[r] = UnityEngine.Random.Range(0, Strip.Length);

            if (Strip[idx[0]] == Strip[idx[1]] && DistinctSymbols().Count > 1)
            {
                int guard = 0;
                do { idx[2] = UnityEngine.Random.Range(0, Strip.Length); }
                while (Strip[idx[2]] == Strip[idx[0]] && ++guard < 64);
            }

            for (int r = 0; r < 3; r++)
            {
                _scroll[r] = idx[r] * CellHeight;
                ApplyScroll(r);
            }
        }

        private void ApplyScroll(int reel)
        {
            if (reel < 0 || reel >= _content.Count) return;
            // Content moves DOWN as scroll increases, so symbols travel downward like a real reel.
            _content[reel].anchoredPosition = new Vector2(0f, _scroll[reel]);
        }

        // ── Outcome selection ────────────────────────────────────────────────

        /// <summary>Active odds row, from the player's upgrade level unless pinned.</summary>
        private OddsTier ActiveTier()
        {
            if (Tiers == null || Tiers.Length == 0) return new OddsTier();

            int idx = ManualTier;
            if (Source == OddsSource.PlayerUpgrade)
            {
                var dp = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.DropPuzzle : null;
                if (dp != null) idx = dp.slotOddsLevel;
            }
            return Tiers[Mathf.Clamp(idx, 0, Tiers.Length - 1)];
        }

        /// <summary>
        /// The multiplier this spin pays, 0 for a loss.
        ///
        /// A pending <see cref="ForceNextSpin"/> wins over the tier table and is consumed
        /// here, so exactly one spin is scripted. The forced symbol has to exist on
        /// <see cref="Strip"/> — there is no cell for the reels to stop on otherwise — so an
        /// unreachable value falls back to the normal roll rather than landing the reels on
        /// something that contradicts the announced result.
        /// </summary>
        private int TakeOutcome()
        {
            if (_forcePending)
            {
                int forced = _forcedPrize;
                _forcePending = false;
                _forcedPrize  = 0;

                if (forced <= 0) return 0;
                if (System.Array.IndexOf(Strip, forced) >= 0)
                {
                    Debug.Log($"[SlotMachine3Reel] Forced result: x{forced} (odds table bypassed).");
                    return forced;
                }

                Debug.LogWarning($"[SlotMachine3Reel] Forced result x{forced} is not on Strip — " +
                                 "no reel cell to land on. Falling back to the odds table.");
            }

            return RollOutcome(ActiveTier());
        }

        /// <summary>
        /// Rolls the whole outcome in one pass across No-Match plus every multiplier, so the
        /// tier's weights ARE the published odds — no separate win/lose coin flip that would
        /// let the two drift apart. Returns 0 for a loss.
        /// </summary>
        private int RollOutcome(OddsTier tier)
        {
            float total = Mathf.Max(0f, tier.NoMatchWeight);
            if (tier.WinValues != null)
                for (int i = 0; i < tier.WinValues.Length; i++)
                    total += WeightAt(tier, i);

            if (total <= 0f) return 0;

            float roll = UnityEngine.Random.Range(0f, total);
            roll -= Mathf.Max(0f, tier.NoMatchWeight);
            if (roll <= 0f) return 0;

            if (tier.WinValues != null)
                for (int i = 0; i < tier.WinValues.Length; i++)
                {
                    roll -= WeightAt(tier, i);
                    if (roll <= 0f) return tier.WinValues[i];
                }

            return 0;
        }

        private static float WeightAt(OddsTier tier, int i)
            => (tier.WinWeights != null && i < tier.WinWeights.Length)
                ? Mathf.Max(0f, tier.WinWeights[i])
                : 0f;

        /// <summary>
        /// Three symbols that are NOT all equal. Honours <see cref="NearMissChance"/> so a
        /// configurable share of losses still show two matching reels.
        /// </summary>
        private int[] LosingSymbols()
        {
            var distinct = DistinctSymbols();
            if (distinct.Count < 2) return new[] { Strip[0], Strip[0], Strip[0] }; // degenerate strip

            if (UnityEngine.Random.value < NearMissChance)
            {
                // The teased symbol is drawn from the TIER's weights, not uniformly. Picking
                // uniformly makes a near miss on the rarest prize as common as one on the
                // commonest — "5x 5x 2x" would show up as often as "2x 2x 5x" and tell the
                // player a jackpot is nearly landing when its real odds are 0.1%.
                int a = WeightedTeaseSymbol(distinct);
                int b;
                do { b = distinct[UnityEngine.Random.Range(0, distinct.Count)]; } while (b == a);
                return new[] { a, a, b };
            }

            // Plain loss — make sure it isn't accidentally a match.
            int x, y, z;
            do
            {
                x = distinct[UnityEngine.Random.Range(0, distinct.Count)];
                y = distinct[UnityEngine.Random.Range(0, distinct.Count)];
                z = distinct[UnityEngine.Random.Range(0, distinct.Count)];
            } while (x == y && y == z);
            return new[] { x, y, z };
        }

        /// <summary>
        /// Picks which multiplier a near miss teases, weighted by the active tier so the
        /// frequency of "almost won x5" tracks how often x5 can actually be won.
        /// </summary>
        private int WeightedTeaseSymbol(List<int> fallback)
        {
            var tier = ActiveTier();
            if (tier.WinValues == null || tier.WinValues.Length == 0)
                return fallback[UnityEngine.Random.Range(0, fallback.Count)];

            float total = 0f;
            for (int i = 0; i < tier.WinValues.Length; i++) total += WeightAt(tier, i);
            if (total <= 0f) return fallback[UnityEngine.Random.Range(0, fallback.Count)];

            float roll = UnityEngine.Random.Range(0f, total);
            for (int i = 0; i < tier.WinValues.Length; i++)
            {
                roll -= WeightAt(tier, i);
                if (roll <= 0f) return tier.WinValues[i];
            }
            return tier.WinValues[0];
        }

        private List<int> DistinctSymbols()
        {
            var set = new List<int>();
            foreach (int s in Strip) if (!set.Contains(s)) set.Add(s);
            return set;
        }

        /// <summary>A strip index carrying this symbol (random among matches, for variety).</summary>
        private int IndexOf(int symbol)
        {
            var hits = new List<int>();
            for (int i = 0; i < Strip.Length; i++) if (Strip[i] == symbol) hits.Add(i);
            if (hits.Count == 0) return 0;
            return hits[UnityEngine.Random.Range(0, hits.Count)];
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private IEnumerator Flash(Color tint)
        {
            const float up = 0.1f, down = 0.55f;
            Color a = new Color(tint.r, tint.g, tint.b, 0f);
            Color b = new Color(tint.r, tint.g, tint.b, 0.55f);

            float e = 0f;
            while (e < up)   { _flash.color = Color.Lerp(a, b, e / up);   e += Time.deltaTime; yield return null; }
            e = 0f;
            while (e < down) { _flash.color = Color.Lerp(b, a, e / down); e += Time.deltaTime; yield return null; }
            _flash.color = a;
        }

        private static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float size, FontStyles style)
        {
            var rt = MakeRect(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize  = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color     = Color.white;
            t.raycastTarget = false;
            return t;
        }

        public static Color MultiplierColor(int mult) => mult switch
        {
            2 => new Color(0.4f, 1f, 0.53f),
            3 => new Color(0.4f, 0.67f, 1f),
            4 => new Color(1f, 0.72f, 0.2f),
            5 => new Color(1f, 0.4f, 1f),
            _ => new Color(0.7f, 0.7f, 0.7f),
        };
    }
}
