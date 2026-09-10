using UnityEngine;
using Unity.Entities;
using Unity.Collections;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// The kinds of obstacle a rice ball can hit. Stored in WallData.SurfaceType as a plain
    /// int so it can ride into the Burst collision job, and used on the main thread as an
    /// index into RiceBallImpactAudio.Surfaces.
    ///
    /// Add new entries at the END only — existing scenes store the int value.
    /// </summary>
    public enum BallImpactSurface
    {
        Wall   = 0, // generic board / playfield wall (default)
        Peg    = 1, // small hard pin — bright tick
        Bumper = 2, // rubbery, springy boing
        Metal  = 3, // ringing, long tail
        Wood   = 4, // dull knock
        Glass  = 5, // bright, glassy ping
        Boost  = 6, // boost block — energetic zap
        Soft   = 7  // padded / dampened thud
    }

    /// <summary>
    /// Optional marker you drop on any "Wall"-tagged obstacle to tell the impact system
    /// what it should sound like. Purely descriptive — it has no Update and no cost at
    /// runtime; RiceBallWallCollisionSystem.CacheWalls() reads it once when it builds the
    /// wall cache and then never touches the GameObject again.
    ///
    /// If an obstacle has no marker, Resolve() falls back to component sniffing and then
    /// to name keywords, so an unwired scene still gets varied impact sounds.
    /// </summary>
    [DisallowMultipleComponent]
    public class ImpactSurfaceTag : MonoBehaviour
    {
        [Tooltip("Which impact sound / spark profile this obstacle uses.")]
        public BallImpactSurface Surface = BallImpactSurface.Wall;

        /// <summary>
        /// Main-thread-only surface resolution for one obstacle. Called from CacheWalls()
        /// (once at scene start and once per puzzle reload), never per frame — so the
        /// GetComponent + string work here is free in the steady state.
        /// </summary>
        public static BallImpactSurface Resolve(GameObject obj)
        {
            if (obj == null) return BallImpactSurface.Wall;

            // 1. Explicit authoring always wins.
            var tag = obj.GetComponent<ImpactSurfaceTag>();
            if (tag != null) return tag.Surface;

            // 2. Component sniffing — a boost block is unmistakable.
            if (obj.GetComponent<BoostBlock>() != null) return BallImpactSurface.Boost;

            // 3. Name keywords, so a scene authored before this feature existed still
            //    sounds varied without anyone touching a single Inspector field.
            string n = obj.name.ToLowerInvariant();
            if (n.Contains("peg") || n.Contains("pin"))                  return BallImpactSurface.Peg;
            if (n.Contains("bumper") || n.Contains("bounce"))            return BallImpactSurface.Bumper;
            if (n.Contains("metal") || n.Contains("steel") || n.Contains("rail")
                || n.Contains("tin") || n.Contains("corrugat")) return BallImpactSurface.Metal;
            if (n.Contains("wood") || n.Contains("plank"))               return BallImpactSurface.Wood;
            if (n.Contains("glass") || n.Contains("crystal"))            return BallImpactSurface.Glass;
            if (n.Contains("soft") || n.Contains("pad") || n.Contains("cushion")) return BallImpactSurface.Soft;

            return BallImpactSurface.Wall;
        }
    }

    /// <summary>
    /// Per-obstacle-type impact sound + spark settings. One of these per BallImpactSurface
    /// entry, exposed as an array on RiceBallImpactAudio so a designer can drop a clip in
    /// and dial the pitch range without touching code.
    /// </summary>
    [System.Serializable]
    public class ImpactSurfaceProfile
    {
        [Tooltip("Display name only — which BallImpactSurface this is comes from its array index.")]
        public string Label = "Surface";

        [Header("Sound")]
        [Tooltip("Impact clip. Leave empty to use the procedural blip generated at runtime.")]
        public AudioClip Clip;

        [Tooltip("Pitch is randomised uniformly inside this range on every single hit, which " +
                 "is what stops repeated impacts sounding like a machine gun.")]
        public Vector2 PitchRange = new Vector2(0.88f, 1.18f);

        [Range(0f, 1f)]
        [Tooltip("Per-surface gain, before the impact-speed and master-SFX scaling.")]
        public float Volume = 0.45f;

        [Tooltip("Minimum seconds between two sounds from THIS surface type. Coalescing: a " +
                 "hundred simultaneous peg hits should read as one texture, not a hundred ticks.")]
        public float MinInterval = 0.035f;

        [Header("Procedural fallback (used when Clip is empty)")]
        [Tooltip("Base tone of the generated blip, Hz. Higher = smaller/harder-sounding object.")]
        public float FallbackFrequency = 780f;
        [Tooltip("Length of the generated blip in seconds. Keep short — these are impacts.")]
        public float FallbackDuration = 0.075f;
        [Range(0f, 1f)]
        [Tooltip("How much of the generated blip is noise vs tone. 0 = pure pitched ping, " +
                 "1 = pure dry click. Noise is what makes wood/soft read as non-musical.")]
        public float FallbackNoise = 0.35f;

        [Header("Spark")]
        [Tooltip("Tint for the DropPuzzleSparks burst this impact fires.")]
        public Color SparkColor = new Color(1f, 0.92f, 0.7f);

        // Runtime-generated stand-in for Clip; never serialized.
        [System.NonSerialized] public AudioClip GeneratedClip;

        public AudioClip ResolvedClip => Clip != null ? Clip : GeneratedClip;
    }

    /// <summary>
    /// Ball-vs-obstacle impact SFX + sparks for the drop puzzle.
    ///
    /// ── How it stays cheap with thousands of balls ────────────────────────────────
    /// The actual collision runs in a Burst parallel job (WallCollisionJob) which cannot
    /// call into Unity audio. So the job writes tiny blittable BallImpactEvent structs
    /// into a NativeQueue.ParallelWriter, and this MonoBehaviour drains them on the main
    /// thread in LateUpdate. The queue is double-buffered inside
    /// RiceBallWallCollisionECSSystem, so the drain reads a buffer whose producing job
    /// finished a whole frame ago — no JobHandle.Complete(), no sync point.
    ///
    /// Three independent throttles keep the cost flat regardless of ball count:
    ///   1. In-job:   impact-speed threshold + stride subsampling + one event per ball
    ///                per frame, so the queue never receives more than ~TargetEvents.
    ///   2. On drain: MaxEventsScannedPerFrame caps main-thread work; the remainder is
    ///                Clear()ed, never queued for later (same philosophy as
    ///                DropPuzzleSparks' MaxBurstsPerFrame).
    ///   3. On play:  MaxSoundsPerFrame plus per-surface and per-grid-cell cooldowns.
    ///
    /// ── Voices ───────────────────────────────────────────────────────────────────
    /// AudioSource.pitch is a property of the SOURCE, not of the voice: setting .pitch
    /// then calling PlayOneShot re-pitches every voice already playing on that source. A
    /// single shared source therefore cannot give each impact its own random pitch — the
    /// whole point of the feature. So we round-robin a small pool of AudioSources, one
    /// voice each, which is the standard fix and costs ~nothing (8 idle AudioSources).
    ///
    /// Fires itself up automatically — no scene wiring required, and it works with zero
    /// AudioClips assigned thanks to the procedural blips (see MakeImpactBlip).
    /// </summary>
    [DisallowMultipleComponent]
    public class RiceBallImpactAudio : MonoBehaviour
    {
        // ── Singleton with lazy auto-bootstrap (matches DropPuzzleJuice/Sparks) ───
        private static RiceBallImpactAudio _instance;
        private static bool _appQuitting;

        public static RiceBallImpactAudio Instance
        {
            get
            {
                if (_appQuitting) return null;
                if (_instance == null)
                {
                    _instance = FindObjectOfType<RiceBallImpactAudio>();
                    if (_instance == null)
                        _instance = new GameObject("RiceBallImpactAudio (auto)")
                            .AddComponent<RiceBallImpactAudio>();
                }
                return _instance;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Master switch")]
        [Tooltip("The one toggle. Off = the Burst job never even builds an impact event, " +
                 "the drain returns immediately, and nothing is allocated or played.")]
        public bool EnableImpactFeedback = true;

        [Header("Budgets (drop-don't-queue, like DropPuzzleSparks)")]
        [Tooltip("Hard cap on impact sounds started per frame. Extra events are DISCARDED, " +
                 "never queued. 8 = one voice per pooled AudioSource.")]
        public int MaxSoundsPerFrame = 8;

        [Tooltip("Hard cap on how many queued events the main thread even looks at per " +
                 "frame. Anything past this is dropped wholesale via Clear().")]
        public int MaxEventsScannedPerFrame = 128;

        [Tooltip("Roughly how many events the Burst job is allowed to produce per frame. " +
                 "The job strides over the ball set to hit this number no matter how many " +
                 "balls are in flight, so queue traffic is O(budget), not O(balls).")]
        public int TargetEventsPerFrame = 96;

        [Tooltip("Number of pooled AudioSources. Must be >= MaxSoundsPerFrame for every " +
                 "sound in a frame to keep its own independent pitch.")]
        public int VoiceCount = 8;

        [Header("Impact response")]
        [Tooltip("Closing speed (m/s) below which an impact is silent. Filters out the " +
                 "endless micro-contacts of balls settling in a pile.")]
        public float MinImpactSpeed = 1.6f;

        [Tooltip("Closing speed (m/s) treated as a full-volume slam. Balls are speed-capped " +
                 "at 15 m/s by RiceBallPhysicsSystem.")]
        public float LoudImpactSpeed = 8f;

        [Range(0f, 1f)]
        [Tooltip("Volume of the quietest audible impact, as a fraction of the loudest.")]
        public float SoftImpactVolume = 0.45f;

        [Range(0f, 0.5f)]
        [Tooltip("Extra pitch added at full impact speed, on top of the random range. " +
                 "Makes hard hits read as harder rather than just louder.")]
        public float SpeedPitchBias = 0.12f;

        [Range(0f, 1f)]
        [Tooltip("Stereo spread from the impact's screen X position. 0 = dead centre.")]
        public float StereoSpread = 0.55f;

        [Header("Coalescing")]
        [Tooltip("World-space size of the grid cell used to merge nearby simultaneous hits. " +
                 "Two impacts in the same cell within CellCooldown produce one sound.")]
        public float CoalesceCellSize = 0.7f;

        [Tooltip("Seconds a grid cell stays muted after it produces a sound.")]
        public float CellCooldown = 0.08f;

        [Header("Sparks (routed through the existing DropPuzzleSparks budget)")]
        public bool EnableImpactSparks = true;

        [Tooltip("Sub-budget so impact sparks cannot starve the gate-scoring sparks that " +
                 "share DropPuzzleSparks' MaxBurstsPerFrame.")]
        public int MaxSparksPerFrame = 4;

        [Tooltip("Only impacts at least this fast throw sparks.")]
        public float SparkSpeedThreshold = 4f;

        [Tooltip("Particles per impact burst — deliberately smaller than a score burst.")]
        public int SparkParticles = 4;

        [Tooltip("When a surface has no Clip of its own, play the Wall clip instead of that " +
                 "surface's synthesized fallback. Keeps a project that only authored Wall " +
                 "sounding consistent everywhere, while any surface you DO give a clip to " +
                 "takes over automatically. Turn off to hear the per-surface procedural " +
                 "blips while tuning them.")]
        public bool FallBackToWallClip = true;

        [Header("Per-obstacle-type profiles (index = BallImpactSurface)")]
        public ImpactSurfaceProfile[] Surfaces;

        // ─────────────────────────────────────────────────────────────────────────
        // Runtime state — all fixed-size, nothing allocates after Awake
        // ─────────────────────────────────────────────────────────────────────────

        private const int SurfaceCount = 8;   // keep in sync with BallImpactSurface
        private const int CellSlots    = 128; // power of two, direct-mapped cooldown table

        private AudioSource[] _voices;
        private int           _nextVoice;

        private readonly float[] _surfaceLastPlay = new float[SurfaceCount];
        private readonly int[]   _cellKey  = new int[CellSlots];
        private readonly float[] _cellTime = new float[CellSlots];

        private BallDropAudioManager _audioMgr;
        private int    _mgrRetries = 20; // hard cap on re-acquisition attempts
        private Camera _cam;

        private World        _world;
        private SystemHandle _sysHandle;
        private bool         _sysResolved;

        // Cached config so we only poke the ECS system when something actually changed.
        private bool  _pushedEnabled;
        private float _pushedMinSpeed;
        private int   _pushedTarget;
        private bool  _pushedOnce;

        // ─────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;

            EnsureProfiles();
            BuildVoicePool();
            GenerateFallbackClips();
        }

        private void Start()
        {
            _audioMgr = FindObjectOfType<BallDropAudioManager>();
            _cam      = Camera.main;
        }

        private void OnApplicationQuit() => _appQuitting = true;

        private void OnDisable()
        {
            // Disarm the job so it stops producing events while we are not around to drain.
            PushConfig(true);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Per-frame drain
        // ─────────────────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            PushConfig(false);

            // Single early-out when the feature is off: no queue read, no allocation,
            // no AudioSource touched. The job side is already inert (see PushConfig).
            if (!EnableImpactFeedback) return;
            if (!ResolveSystem()) return;

            TryAcquireAudioManager();

            ref var sys = ref _world.Unmanaged
                .GetUnsafeSystemRef<RiceBallWallCollisionECSSystem>(_sysHandle);

            // This is LAST frame's buffer. Its producing job was completed at the top of
            // this frame's OnUpdate, so no Complete() / sync point is needed here.
            NativeQueue<BallImpactEvent> queue = sys.GetReadableImpacts();
            if (!queue.IsCreated) return;

            int scanned = 0;
            int played  = 0;
            int sparked = 0;
            float now   = Time.time;

            int scanCap  = Mathf.Max(1, MaxEventsScannedPerFrame);
            int soundCap = Mathf.Max(1, MaxSoundsPerFrame);

            while (scanned < scanCap && queue.TryDequeue(out BallImpactEvent e))
            {
                scanned++;

                // Speed gate is enforced in-job too; re-checked here because the Inspector
                // value can change between the job scheduling and this drain.
                if (e.Speed < MinImpactSpeed) continue;

                int surface = e.SurfaceType;
                if (surface < 0 || surface >= Surfaces.Length) surface = 0;
                ImpactSurfaceProfile profile = Surfaces[surface];
                if (profile == null) continue;

                // Most projects only ever author the Wall clip. Once PuzzlePropDresser started
                // tagging deflectors by the prop they wear, those obstacles resolved to Metal
                // and Wood — surfaces with no clip — so the board suddenly played synthesized
                // blips instead of the one real impact sound that had been dropped in. An
                // unauthored surface should borrow the Wall clip rather than diverge from it:
                // add a Metal clip and Metal takes over on its own, no code change.
                if (FallBackToWallClip && surface != 0 && profile.Clip == null &&
                    Surfaces[0] != null && Surfaces[0].Clip != null)
                {
                    profile = Surfaces[0];
                    surface = 0;   // share Wall's interval + cell cooldown: it is the same sound
                }

                // ── Coalescing pass 1: this surface type just spoke, stay quiet ──
                if (now - _surfaceLastPlay[surface] < profile.MinInterval) continue;

                // ── Coalescing pass 2: something already hit this patch of the board ──
                // Direct-mapped table: a hash collision just costs one extra skipped
                // sound, which is inaudible and far cheaper than a real hash map.
                if (!ClaimCell(e.Position, surface, now)) continue;

                Vector3 pos = new Vector3(e.Position.x, e.Position.y, e.Position.z);

                if (played < soundCap)
                {
                    PlayImpact(profile, e.Speed, pos, played);
                    _surfaceLastPlay[surface] = now;
                    played++;
                }

                if (EnableImpactSparks && sparked < MaxSparksPerFrame &&
                    e.Speed >= SparkSpeedThreshold)
                {
                    // Blend the surface tint toward the ball's tier colour so a gold
                    // "Excellent" ball still visibly reads as gold when it smacks a peg.
                    Color c = Color.Lerp(profile.SparkColor,
                                         RiceBallPalette.ForType(e.BallTypeId), 0.35f);
                    DropPuzzleSparks.Burst(pos, c, SparkParticles);
                    sparked++;
                }

                // Stop scanning entirely once both budgets are spent — the rest of the
                // queue can add nothing this frame.
                if (played >= soundCap && (!EnableImpactSparks || sparked >= MaxSparksPerFrame))
                    break;
            }

            // Whatever is left is DISCARDED. Never carried into next frame: a backlog would
            // turn one mega-drop into seconds of stale machine-gun audio.
            queue.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Playback
        // ─────────────────────────────────────────────────────────────────────────

        private void PlayImpact(ImpactSurfaceProfile profile, float speed, Vector3 pos, int playedThisFrame)
        {
            AudioClip clip = profile.ResolvedClip;
            if (clip == null || _voices == null || _voices.Length == 0) return;

            // 0 at MinImpactSpeed → 1 at LoudImpactSpeed.
            float t = Mathf.InverseLerp(MinImpactSpeed, Mathf.Max(MinImpactSpeed + 0.01f, LoudImpactSpeed), speed);

            // Volume: physical mapping (harder hit = louder) times the user's SFX setting.
            float vol = profile.Volume * Mathf.Lerp(SoftImpactVolume, 1f, t) * MasterSfxVolume();

            // Crowd ducking: the Nth simultaneous impact in a frame is quieter than the
            // first, so a mega-drop swells instead of clipping into a wall of noise.
            vol *= 1f / (1f + 0.18f * playedThisFrame);
            if (vol <= 0.001f) return;

            // Randomised pitch inside this surface's range — the core variation — plus a
            // small upward bias for fast hits so speed is audible as well as visible.
            float pitch = Random.Range(profile.PitchRange.x, profile.PitchRange.y)
                        + t * SpeedPitchBias;
            pitch = Mathf.Clamp(pitch, 0.35f, 3f);

            AudioSource src = TakeVoice();
            if (src == null) return;

            src.pitch      = pitch;                 // per-voice: safe because this source
            src.panStereo  = StereoPan(pos);        // plays at most one impact at a time
            src.PlayOneShot(clip, Mathf.Clamp01(vol));
        }

        /// <summary>
        /// Round-robin voice allocation. Prefers an idle source so we do not cut a sound
        /// short; if every voice is busy we steal the oldest, which is correct — with 8
        /// voices the stolen one is already ~8 impacts old and inaudible under the new hit.
        /// </summary>
        private AudioSource TakeVoice()
        {
            int n = _voices.Length;
            for (int i = 0; i < n; i++)
            {
                AudioSource s = _voices[(_nextVoice + i) % n];
                if (s != null && !s.isPlaying)
                {
                    _nextVoice = (_nextVoice + i + 1) % n;
                    return s;
                }
            }
            AudioSource steal = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % n;
            return steal;
        }

        /// <summary>
        /// The user's SFX volume, taken from BallDropAudioManager so the settings slider
        /// governs impacts too instead of them bypassing it. Falls back to 1 when the
        /// manager is absent (e.g. an isolated test scene).
        /// </summary>
        private float MasterSfxVolume()
        {
            // Pure lookup — no scene searching happens here. Re-acquisition is handled once
            // per drain by TryAcquireAudioManager(), and is bounded (see _mgrRetries).
            // Read the player's master SFX setting only. This used to read
            // sfxSource.volume / sfxVolume, i.e. the mix level of BallDropAudioManager's own
            // one-shot source — which carries nothing but the transition sting. Folding that
            // in scaled every ball impact by an unrelated clip's balance.
            if (_audioMgr == null) return 1f;
            return _audioMgr.SfxMasterLevel;
        }

        /// <summary>
        /// Re-find BallDropAudioManager if it appeared after us (it is created by scene
        /// setup, we may bootstrap first). Bounded and throttled so this can never become a
        /// per-frame FindObjectOfType: at most _mgrRetries attempts, one every 64 frames.
        /// </summary>
        private void TryAcquireAudioManager()
        {
            if (_audioMgr != null || _mgrRetries <= 0) return;
            if ((Time.frameCount & 63) != 0) return;
            _mgrRetries--;
            _audioMgr = FindObjectOfType<BallDropAudioManager>();
        }

        private float StereoPan(Vector3 worldPos)
        {
            if (StereoSpread <= 0f) return 0f;
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return 0f; }
            float vx = _cam.WorldToViewportPoint(worldPos).x;
            return Mathf.Clamp(vx * 2f - 1f, -1f, 1f) * StereoSpread;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Coalescing table
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Direct-mapped (position-cell, surface) cooldown table. Returns false if this
        /// patch of the board already produced a sound inside CellCooldown. Fixed-size and
        /// allocation-free — no dictionary, no per-frame clearing.
        /// </summary>
        private bool ClaimCell(Unity.Mathematics.float3 pos, int surface, float now)
        {
            if (CellCooldown <= 0f) return true;

            float cell = Mathf.Max(0.01f, CoalesceCellSize);
            int cx = Mathf.FloorToInt(pos.x / cell);
            int cy = Mathf.FloorToInt(pos.y / cell);

            // Cheap integer mix; quality only affects how often two distinct cells share a
            // slot, and a shared slot merely skips a sound.
            int key  = (cx * 73856093) ^ (cy * 19349663) ^ (surface * 83492791);
            int slot = (key & 0x7FFFFFFF) & (CellSlots - 1);

            if (_cellKey[slot] == key && now - _cellTime[slot] < CellCooldown) return false;

            _cellKey[slot]  = key;
            _cellTime[slot] = now;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Setup
        // ─────────────────────────────────────────────────────────────────────────

        private bool ResolveSystem()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) { _sysResolved = false; return false; }

            if (!_sysResolved || _world != w)
            {
                _world     = w;
                _sysHandle = w.GetOrCreateSystem<RiceBallWallCollisionECSSystem>();
                _sysResolved = _sysHandle != default;
            }
            return _sysResolved;
        }

        /// <summary>
        /// Push the enable flag and job-side thresholds into the ECS system. Only writes
        /// when a value actually changed, so the common case is three float compares.
        /// </summary>
        private void PushConfig(bool forceOff)
        {
            bool  wantEnabled = !forceOff && EnableImpactFeedback && isActiveAndEnabled;
            float wantSpeed   = MinImpactSpeed;
            int   wantTarget  = Mathf.Max(1, TargetEventsPerFrame);

            if (_pushedOnce && wantEnabled == _pushedEnabled &&
                Mathf.Approximately(wantSpeed, _pushedMinSpeed) && wantTarget == _pushedTarget)
                return;

            if (!ResolveSystem()) return;

            ref var sys = ref _world.Unmanaged
                .GetUnsafeSystemRef<RiceBallWallCollisionECSSystem>(_sysHandle);
            sys.ConfigureImpactCapture(wantEnabled, wantSpeed, wantTarget);

            _pushedEnabled  = wantEnabled;
            _pushedMinSpeed = wantSpeed;
            _pushedTarget   = wantTarget;
            _pushedOnce     = true;
        }

        private void BuildVoicePool()
        {
            int count = Mathf.Clamp(VoiceCount, 1, 32);
            _voices = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"ImpactVoice_{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake   = false;
                src.loop          = false;
                src.spatialBlend  = 0f;  // 2D: the drop puzzle is a flat board, panning is enough
                src.volume        = 1f;  // per-shot volume comes from PlayOneShot
                src.dopplerLevel  = 0f;
                _voices[i] = src;
            }
        }

        /// <summary>Fill in any missing profiles with sane per-surface defaults.</summary>
        private void EnsureProfiles()
        {
            if (Surfaces == null || Surfaces.Length < SurfaceCount)
            {
                var grown = new ImpactSurfaceProfile[SurfaceCount];
                if (Surfaces != null)
                    for (int i = 0; i < Surfaces.Length && i < SurfaceCount; i++) grown[i] = Surfaces[i];
                Surfaces = grown;
            }
            for (int i = 0; i < SurfaceCount; i++)
                if (Surfaces[i] == null) Surfaces[i] = DefaultProfile((BallImpactSurface)i);
        }

        private static ImpactSurfaceProfile DefaultProfile(BallImpactSurface s)
        {
            var p = new ImpactSurfaceProfile { Label = s.ToString() };
            switch (s)
            {
                case BallImpactSurface.Peg:
                    p.FallbackFrequency = 1480f; p.FallbackDuration = 0.045f; p.FallbackNoise = 0.20f;
                    p.PitchRange = new Vector2(0.90f, 1.30f); p.Volume = 0.38f; p.MinInterval = 0.030f;
                    p.SparkColor = new Color(1.00f, 0.95f, 0.70f);
                    break;
                case BallImpactSurface.Bumper:
                    p.FallbackFrequency = 320f;  p.FallbackDuration = 0.140f; p.FallbackNoise = 0.10f;
                    p.PitchRange = new Vector2(0.82f, 1.10f); p.Volume = 0.55f; p.MinInterval = 0.055f;
                    p.SparkColor = new Color(1.00f, 0.45f, 0.35f);
                    break;
                case BallImpactSurface.Metal:
                    p.FallbackFrequency = 1180f; p.FallbackDuration = 0.220f; p.FallbackNoise = 0.12f;
                    p.PitchRange = new Vector2(0.92f, 1.22f); p.Volume = 0.42f; p.MinInterval = 0.045f;
                    p.SparkColor = new Color(0.85f, 0.92f, 1.00f);
                    break;
                case BallImpactSurface.Wood:
                    p.FallbackFrequency = 430f;  p.FallbackDuration = 0.070f; p.FallbackNoise = 0.55f;
                    p.PitchRange = new Vector2(0.85f, 1.15f); p.Volume = 0.46f; p.MinInterval = 0.038f;
                    p.SparkColor = new Color(0.80f, 0.62f, 0.38f);
                    break;
                case BallImpactSurface.Glass:
                    p.FallbackFrequency = 1860f; p.FallbackDuration = 0.130f; p.FallbackNoise = 0.08f;
                    p.PitchRange = new Vector2(0.95f, 1.35f); p.Volume = 0.34f; p.MinInterval = 0.040f;
                    p.SparkColor = new Color(0.70f, 0.95f, 1.00f);
                    break;
                case BallImpactSurface.Boost:
                    p.FallbackFrequency = 900f;  p.FallbackDuration = 0.110f; p.FallbackNoise = 0.45f;
                    p.PitchRange = new Vector2(1.05f, 1.55f); p.Volume = 0.50f; p.MinInterval = 0.060f;
                    p.SparkColor = new Color(1.00f, 0.55f, 0.10f);
                    break;
                case BallImpactSurface.Soft:
                    p.FallbackFrequency = 210f;  p.FallbackDuration = 0.090f; p.FallbackNoise = 0.75f;
                    p.PitchRange = new Vector2(0.80f, 1.05f); p.Volume = 0.34f; p.MinInterval = 0.050f;
                    p.SparkColor = new Color(0.55f, 0.55f, 0.62f);
                    break;
                default: // Wall
                    p.FallbackFrequency = 640f;  p.FallbackDuration = 0.075f; p.FallbackNoise = 0.40f;
                    p.PitchRange = new Vector2(0.86f, 1.18f); p.Volume = 0.42f; p.MinInterval = 0.035f;
                    p.SparkColor = new Color(0.95f, 0.90f, 0.78f);
                    break;
            }
            return p;
        }

        /// <summary>
        /// Build the procedural stand-in clip for every surface that has no AudioClip
        /// assigned, so the feature is audible the instant it is dropped into a scene with
        /// nothing wired up. Same idea as DropPuzzleJuice.MakeBlip, one clip per obstacle
        /// type instead of one global blip.
        /// </summary>
        private void GenerateFallbackClips()
        {
            for (int i = 0; i < Surfaces.Length; i++)
            {
                var p = Surfaces[i];
                if (p == null || p.Clip != null) continue;
                p.GeneratedClip = MakeImpactBlip(
                    $"Impact_{p.Label}", p.FallbackFrequency, p.FallbackDuration,
                    p.FallbackNoise, (uint)(i + 1) * 9781u);
            }
        }

        /// <summary>
        /// Percussive impact blip: a decaying tone (with two harmonics for body) crossfaded
        /// against filtered noise, wrapped in a fast exponential envelope and a 1 ms attack
        /// ramp so it starts without a DC pop.
        ///
        /// The noise mix is what separates the surfaces perceptually — a near-pure tone
        /// reads as glass/metal, a noise-dominant one reads as wood/padding. Deterministic
        /// RNG so the generated set is identical every run.
        /// </summary>
        private static AudioClip MakeImpactBlip(string name, float freq, float duration,
                                                float noiseMix, uint seed)
        {
            const int sampleRate = 44100;
            duration = Mathf.Clamp(duration, 0.02f, 0.6f);
            int samples = Mathf.Max(8, Mathf.RoundToInt(sampleRate * duration));
            var data = new float[samples];

            var rng = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            float lowpass = 0f;                        // one-pole filter state for the noise
            float alpha   = Mathf.Clamp01(0.12f + freq / 12000f); // brighter surfaces, brighter noise
            int   attack  = Mathf.Max(1, sampleRate / 1000);      // ~1 ms

            for (int i = 0; i < samples; i++)
            {
                float t   = (float)i / sampleRate;
                float env = Mathf.Exp(-t / (duration * 0.30f));   // sharp percussive tail
                if (i < attack) env *= (float)i / attack;

                float w    = 2f * Mathf.PI * freq * t;
                float tone = Mathf.Sin(w)
                           + 0.40f * Mathf.Sin(2f * w)
                           + 0.18f * Mathf.Sin(3.01f * w);        // slightly detuned = less "beep"

                float white = rng.NextFloat(-1f, 1f);
                lowpass += alpha * (white - lowpass);             // tames the hiss into a "thock"

                data[i] = Mathf.Lerp(tone * 0.45f, lowpass * 1.1f, noiseMix) * env * 0.8f;
            }

            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void Reset()
        {
            Surfaces = null;
            EnsureProfiles();
        }
    }
}
