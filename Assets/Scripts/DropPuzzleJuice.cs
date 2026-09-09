using UnityEngine;
using TMPro;
using System.Collections.Generic;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Shared colour palette for rice balls, keyed by RiceBallType.TypeID.
    /// Used by BOTH the ball renderer (RiceBallRendererECS) and the score popups
    /// (DropPuzzleJuice) so a gold "Excellent" ball and its gold "+5" popup match.
    ///
    /// TypeID map (see DropperControllerECS.GetBallTypeFromQuality):
    ///   0 = Fine       (1x)  → warm rice-white
    ///   1 = Good       (2x)  → bright blue
    ///   2 = Great      (+1)  → vivid purple
    ///   4 = Excellent  (5x)  → gold
    /// </summary>
    public static class RiceBallPalette
    {
        public static readonly Color Fine      = new Color(1.00f, 0.96f, 0.85f); // warm white
        public static readonly Color Good      = new Color(0.25f, 0.66f, 1.00f); // blue
        public static readonly Color Great     = new Color(0.69f, 0.36f, 1.00f); // purple
        public static readonly Color Excellent = new Color(1.00f, 0.82f, 0.25f); // gold

        /// <summary>
        /// Balls minted by a multiplier gate, rather than dropped from the dropper.
        ///
        /// Quality TypeIDs are 0/1/2/4, so 5 is free. A gate clone inherits its parent's
        /// RiceBallType wholesale, which made a x2 gate's output visually identical to its
        /// input — there was no way to see, in motion, whether the gate was multiplying at
        /// all. Stamping this id on the clones (and only the id; PointsMultiplier and the
        /// rest of the struct are untouched, so payouts are unchanged) makes them read as a
        /// separate colour everywhere the palette is used: the balls themselves, their score
        /// popups, and their impact sparks.
        /// </summary>
        public const  int   MultipliedTypeId = 5;
        public static readonly Color Multiplied = new Color(0.20f, 1.00f, 0.45f); // hot green

        /// <summary>Colour for a given RiceBallType.TypeID.</summary>
        public static Color ForType(int typeId)
        {
            switch (typeId)
            {
                case 1:                return Good;
                case 2:                return Great;
                case 4:                return Excellent;
                case MultipliedTypeId: return Multiplied;
                default:               return Fine; // 0 and any unmapped id
            }
        }
    }

    /// <summary>
    /// Central "juice" hub for the drop puzzle — score popups, camera punch, and SFX.
    ///
    /// Design goals: Balatro-style payoff. When a ball scores you SEE a coloured number
    /// pop and float; big multiplier hits punch the camera. Everything is fired through
    /// static helpers so ECS/MonoBehaviour callers don't need a reference:
    ///
    ///     DropPuzzleJuice.ScorePop(worldPos, amount, ballTypeId);
    ///     DropPuzzleJuice.MultiplierPop(worldPos, multiplier);
    ///
    /// The instance auto-bootstraps the first time it's needed, so no scene wiring is
    /// required. Assign the optional AudioClips in the inspector for sound.
    /// </summary>
    public class DropPuzzleJuice : MonoBehaviour
    {
        // ── Singleton with lazy auto-bootstrap ────────────────────────────────
        private static DropPuzzleJuice _instance;
        private static bool _appQuitting;

        public static DropPuzzleJuice Instance
        {
            get
            {
                if (_appQuitting) return null;
                if (_instance == null)
                {
                    _instance = FindObjectOfType<DropPuzzleJuice>();
                    if (_instance == null)
                    {
                        var go = new GameObject("DropPuzzleJuice (auto)");
                        _instance = go.AddComponent<DropPuzzleJuice>();
                    }
                }
                return _instance;
            }
        }

        [Header("Popup Feel")]
        [Tooltip("How long a score popup lives (seconds)")]
        public float PopupLifetime = 0.9f;
        [Tooltip("How fast popups float upward (world units/sec)")]
        public float PopupRiseSpeed = 1.6f;
        [Tooltip("Base font size for a normal score popup")]
        public float PopupFontSize = 5f;
        [Tooltip("Max popups spawned per frame (prevents clutter when hundreds of balls score at once)")]
        public int MaxPopupsPerFrame = 8;

        [Header("Popup readability")]
        [Tooltip("Thickness of the dark border drawn around popup text. The popup is tinted to " +
                 "match the ball that scored, and a Fine ball is warm white — which is invisible " +
                 "against a screen full of warm-white balls. The border is what separates the " +
                 "number from the mass behind it, so it matters more here than the face colour.")]
        [Range(0f, 1f)] public float PopupOutlineWidth = 0.4f;

        [Tooltip("Colour of that border. Near-black reads against every ball tier.")]
        public Color PopupOutlineColor = new Color(0.06f, 0.05f, 0.08f, 1f);

        [Tooltip("Random world-space offset applied to each popup. Several balls scoring in the " +
                 "same gate on the same frame would otherwise stack their numbers on the exact " +
                 "same point and smear into an unreadable blob.")]
        public float PopupJitter = 0.35f;

        [Tooltip("Floor on how bright a popup's face colour may be. Stops a dark ball tint " +
                 "disappearing into the board; 0 keeps the palette colour untouched.")]
        [Range(0f, 1f)] public float PopupMinBrightness = 0.75f;

        [Header("Camera Punch")]
        [Tooltip("Camera shake strength for a single ball scoring")]
        public float ScorePunch = 0.06f;
        [Tooltip("Camera shake strength for a multiplier gate hit")]
        public float MultiplierPunch = 0.25f;

        [Header("SFX (optional — leave empty to use synthesized sounds)")]
        [Tooltip("Score tick. If null, a synthesized blip is generated at runtime.")]
        public AudioClip ScoreSound;
        [Tooltip("Multiplier hit. If null, a synthesized 'chunk' is generated at runtime.")]
        public AudioClip MultiplierSound;
        [Range(0f, 1f)] public float SfxVolume = 0.5f;

        [Header("Combo (crescendo)")]
        [Tooltip("A chain breaks if no ball scores within this many seconds.")]
        public float ComboWindow = 1.1f;
        [Tooltip("Each chained score raises the tick pitch by this much, up to the cap.")]
        public float ComboPitchStep = 0.045f;
        [Tooltip("Highest pitch multiplier the crescendo can reach.")]
        public float ComboPitchCap = 2.4f;
        [Tooltip("Every N chained scores fires a bonus flash + camera kick.")]
        public int ComboMilestone = 8;

        private AudioSource _sfx;
        private TMP_FontAsset _font;

        // Synthesized fallbacks (generated once if the inspector clips are empty)
        private AudioClip _genTick;
        private AudioClip _genChunk;

        // Combo state — a chain of scores landing inside ComboWindow builds a crescendo
        private int   _combo;
        private float _comboExpiry;
        public int CurrentCombo => Time.time <= _comboExpiry ? _combo : 0;

        // Per-frame popup budget
        private int _popupsThisFrame;

        // Pool of "ScorePopup" GameObjects, reused instead of Instantiate/Destroy per pop —
        // matters here because a hot streak can fire several of these every frame.
        private readonly Stack<GameObject> _popupPool = new Stack<GameObject>();
        private int _lastPopupFrame = -1;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
            _sfx.volume = SfxVolume;

            // Synthesize fallback SFX so the drop always has sound, even with no
            // audio assets imported. A bright plucked blip for scores, a fatter
            // body for multiplier hits.
            _genTick  = MakeBlip("JuiceTick",  1046f, 0.12f, 0.9f);   // C6-ish pluck
            _genChunk = MakeBlip("JuiceChunk", 320f,  0.28f, 0.55f);  // low thud

            _font = TMP_Settings.defaultFontAsset;
        }

        private void OnApplicationQuit() => _appQuitting = true;
        private void OnDestroy() { if (_instance == this) _instance = null; }

        // ── Static API ────────────────────────────────────────────────────────

        /// <summary>Show a "+N" popup for a ball scoring at a goal, coloured by its tier.</summary>
        public static void ScorePop(Vector3 worldPos, long amount, int ballTypeId)
        {
            var inst = Instance;
            if (inst != null) inst.DoScorePop(worldPos, amount, ballTypeId);
        }

        /// <summary>Show a big "x2" popup and punch the camera when a ball hits a multiplier gate.</summary>
        public static void MultiplierPop(Vector3 worldPos, int multiplier)
        {
            var inst = Instance;
            if (inst != null) inst.DoMultiplierPop(worldPos, multiplier);
        }

        // ── Implementation ────────────────────────────────────────────────────

        private bool ConsumeBudget()
        {
            if (Time.frameCount != _lastPopupFrame)
            {
                _lastPopupFrame = Time.frameCount;
                _popupsThisFrame = 0;
            }
            if (_popupsThisFrame >= MaxPopupsPerFrame) return false;
            _popupsThisFrame++;
            return true;
        }

        private void DoScorePop(Vector3 worldPos, long amount, int ballTypeId)
        {
            Color c = RiceBallPalette.ForType(ballTypeId);

            // ── Combo: chain scores that land inside the window into a crescendo ──
            _combo = Time.time <= _comboExpiry ? _combo + 1 : 1;
            _comboExpiry = Time.time + ComboWindow;

            // Pitch climbs with the chain — the audible "roar builds" as balls stack up.
            float pitch = Mathf.Min(ComboPitchCap, 1f + (_combo - 1) * ComboPitchStep);

            // Bigger, higher-tier pops feel weightier; a long chain also enlarges the pop.
            float tierMul  = ballTypeId == 4 ? 1.6f : ballTypeId == 1 ? 1.2f : 1f;
            float comboMul = 1f + Mathf.Min(0.6f, _combo * 0.02f);

            if (ConsumeBudget())
            {
                string msg = _combo >= 3 ? $"+{amount}  <size=60%>x{_combo}</size>" : $"+{amount}";
                SpawnText(worldPos, msg, c, PopupFontSize * tierMul * comboMul);
            }

            // Camera kick scales gently with the chain so a hot streak feels alive.
            PunchCamera(ScorePunch * (1f + Mathf.Min(1.5f, _combo * 0.05f)));
            PlaySfx(ScoreSound != null ? ScoreSound : _genTick, pitch);

            // Milestone payoff — a bigger flash + kick every N in the chain.
            if (ComboMilestone > 0 && _combo % ComboMilestone == 0)
            {
                PunchCamera(MultiplierPunch);
                PlaySfx(MultiplierSound != null ? MultiplierSound : _genChunk, Mathf.Min(ComboPitchCap, pitch + 0.3f));
            }
        }

        private void DoMultiplierPop(Vector3 worldPos, int multiplier)
        {
            // Gold, chunky, always shown (multiplier hits are rare enough not to need budgeting)
            SpawnText(worldPos, $"x{multiplier}!", RiceBallPalette.Excellent, PopupFontSize * 2.2f);
            PunchCamera(MultiplierPunch);
            // Pitch rises with the multiplier value so an x5 gate reads bigger than an x2.
            PlaySfx(MultiplierSound != null ? MultiplierSound : _genChunk, 0.85f + multiplier * 0.12f);
        }

        private void SpawnText(Vector3 worldPos, string msg, Color color, float fontSize)
        {
            GameObject go = _popupPool.Count > 0 ? _popupPool.Pop() : CreatePopupObject();

            // Scatter simultaneous pops so they don't land on the identical world point and
            // overprint each other into an unreadable smear.
            Vector3 jitter = PopupJitter > 0f
                ? new Vector3(Random.Range(-PopupJitter, PopupJitter),
                              Random.Range(-PopupJitter * 0.5f, PopupJitter * 0.5f), 0f)
                : Vector3.zero;
            go.transform.position = worldPos + jitter;
            go.SetActive(true);

            var tmp = go.GetComponent<TextMeshPro>();
            if (_font != null) tmp.font = _font;
            tmp.text = msg;
            tmp.fontSize = fontSize;
            tmp.color = Brighten(color, PopupMinBrightness);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = false;
            tmp.outlineWidth = PopupOutlineWidth;   // dark border is what carries readability
            tmp.outlineColor = PopupOutlineColor;

            // Render on top of the balls
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 5000;

            go.GetComponent<FloatingText>().Init(PopupLifetime, PopupRiseSpeed, ReleasePopup);
        }

        /// <summary>
        /// Raise a colour to at least <paramref name="minValue"/> brightness, keeping its hue.
        /// A popup is tinted to match the ball that scored, and the darker tiers can sink into
        /// the board behind them; this lifts those without recolouring the palette.
        /// </summary>
        private static Color Brighten(Color c, float minValue)
        {
            if (minValue <= 0f) return c;
            float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (v >= minValue || v <= 0.0001f) return c;
            float k = minValue / v;
            return new Color(Mathf.Min(1f, c.r * k), Mathf.Min(1f, c.g * k), Mathf.Min(1f, c.b * k), c.a);
        }

        /// <summary>Creates a fresh, pool-backed popup GameObject (only happens until the pool warms up).</summary>
        private GameObject CreatePopupObject()
        {
            var go = new GameObject("ScorePopup (pooled)");
            go.AddComponent<TextMeshPro>();
            go.AddComponent<FloatingText>();
            return go;
        }

        /// <summary>Returned by FloatingText when a popup's lifetime ends, instead of Destroy().</summary>
        private void ReleasePopup(FloatingText popup)
        {
            popup.gameObject.SetActive(false);
            _popupPool.Push(popup.gameObject);
        }

        private void PunchCamera(float strength)
        {
            if (DropPuzzleCameraController.Instance != null)
                DropPuzzleCameraController.Instance.Punch(strength);
        }

        /// <summary>
        /// Master SFX level from the settings slider, 0..1, multiplied over SfxVolume.
        /// The escape-menu SFX slider used to reach only BallDropAudioManager (whose SFX
        /// source carries nothing but the transition sting) and FPSAudioManager — so it never
        /// touched the score/multiplier sounds at all.
        /// </summary>
        public void SetMasterVolume(float volume) => _master = Mathf.Clamp01(volume);
        private float _master = 1f;

        private void PlaySfx(AudioClip clip, float pitch = 1f)
        {
            if (clip == null || _sfx == null) return;
            _sfx.pitch = pitch;                 // applies to the PlayOneShot voice
            _sfx.PlayOneShot(clip, SfxVolume * _master);
        }

        /// <summary>
        /// Builds a short percussive blip: a sine tone with a couple of harmonics and a
        /// fast exponential decay. Cheap, asset-free, and pleasant — used when no SFX
        /// clips are assigned so the drop is never silent.
        /// </summary>
        private static AudioClip MakeBlip(string name, float freq, float duration, float decay)
        {
            const int sampleRate = 44100;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t   = (float)i / sampleRate;
                float env = Mathf.Exp(-t / (duration * decay));         // percussive tail
                float w   = 2f * Mathf.PI * freq * t;
                float s   = Mathf.Sin(w)
                          + 0.35f * Mathf.Sin(2f * w)                    // 2nd harmonic = brightness
                          + 0.15f * Mathf.Sin(3f * w);                   // 3rd = pluck bite
                data[i] = s * env * 0.5f;
            }
            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>
    /// Self-animating floating score text. Balatro-style pop: scales up with an
    /// overshoot, drifts upward with a little sideways wander, then fades out.
    /// Attaches at runtime — no prefab needed. Pooled by DropPuzzleJuice: on
    /// expiry it hands itself back via the onExpire callback instead of
    /// Destroy()ing, so re-Init() must reset all per-run state (see _age below).
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        private TextMeshPro _tmp;
        private float _life;
        private float _age;
        private float _rise;
        private Vector3 _drift;
        private Color _startColor;
        private Camera _cam;
        private System.Action<FloatingText> _onExpire;

        public void Init(float lifetime, float riseSpeed, System.Action<FloatingText> onExpire = null)
        {
            _life = Mathf.Max(0.1f, lifetime);
            _rise = riseSpeed;
            _onExpire = onExpire;
            _age = 0f;
            _tmp = GetComponent<TextMeshPro>();
            _startColor = _tmp != null ? _tmp.color : Color.white;
            _drift = new Vector3(Random.Range(-0.35f, 0.35f), 0f, 0f);
            _cam = Camera.main;
            transform.localScale = Vector3.one * 0.2f;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);

            // Rise, slowing over time; gentle sideways wander
            float riseFactor = 1f - t;             // decelerate as it ages
            transform.position += (Vector3.up * _rise * riseFactor + _drift) * Time.deltaTime;

            // Scale: 0.2 → 1.15 overshoot → settle → shrink slightly at the end
            float scale;
            if (t < 0.18f)          scale = Mathf.Lerp(0.2f, 1.15f, t / 0.18f);
            else if (t < 0.30f)     scale = Mathf.Lerp(1.15f, 1.0f, (t - 0.18f) / 0.12f);
            else                    scale = Mathf.Lerp(1.0f, 0.85f, (t - 0.30f) / 0.70f);
            transform.localScale = Vector3.one * scale;

            // Fade out over the last 40%
            if (_tmp != null)
            {
                float a = t < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);
                var c = _startColor; c.a = a;
                _tmp.color = c;
            }

            // Billboard toward the camera so it reads flat regardless of camera facing
            if (_cam != null)
                transform.rotation = Quaternion.LookRotation(_cam.transform.forward, _cam.transform.up);

            if (_age >= _life)
            {
                if (_onExpire != null) _onExpire(this);
                else Destroy(gameObject);
            }
        }
    }
}
