using UnityEngine;

namespace Vampire.DropPuzzle
{
    // Orchestrates the DropPuzzle scene flow. The "Preparation phase" runs all the
    // pre-drop rolls AND their result animations before the player can drop:
    //
    //   [Cinematic] → GateRoll → WeatherRoll → PrepEffects → PlayerControl → Dropping → Complete
    //   \____________________ Preparation phase ____________________/
    //
    // Input stays locked through the entire Preparation phase; the drop unlocks only
    // after every roll AND every result animation has finished. Each sub-step degrades
    // gracefully — a missing controller is simply skipped.
    //
    // AllowPlayerInput defaults to true so the scene works without this component.
    // Add to any scene GameObject in DropPuzzle.
    public class DropPuzzleFlowManager : MonoBehaviour
    {
        public static DropPuzzleFlowManager Instance { get; private set; }

        // Checked by DropPuzzleCameraController.AllowDrop and its Assessment Space handler.
        public static bool AllowPlayerInput { get; private set; } = true;

        /// <summary>Fired once the Preparation phase finishes and the player may drop.</summary>
        public static event System.Action OnPreparationComplete;

        public enum Phase { Cinematic, GateRoll, WeatherRoll, PrepEffects, PlayerControl, Dropping, Complete }
        public Phase CurrentPhase { get; private set; }

        /// <summary>True while any Preparation sub-step is still running.</summary>
        public bool IsPreparing =>
            CurrentPhase == Phase.Cinematic || CurrentPhase == Phase.GateRoll ||
            CurrentPhase == Phase.WeatherRoll || CurrentPhase == Phase.PrepEffects;

        [Header("Debug")]
        [Tooltip("Adds this many Fine riceballs to inventory at scene start. Set 0 to disable.")]
        public int DebugStartingBalls = 0;

        private GateRollController          _roll;
        private WeatherRollController        _weather;
        private PrepEffectsController        _effects;
        private DropPuzzleTutorialOverlay   _tutorial;
        private DropPuzzleCinematicOverlay  _cinematic; // fallback if no tutorial overlay

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance         = this;
            AllowPlayerInput = false;
            CurrentPhase     = Phase.Cinematic;
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; AllowPlayerInput = true; }
        }

        private void OnEnable()
        {
            GateRollController.OnRollComplete                 += OnRollComplete;
            WeatherRollController.OnWeatherRollComplete        += OnWeatherComplete;
            DropperControllerECS.OnAnyDropStarted             += OnDropStarted;
            BallDropCompletionManager_Hook.OnDropCompleteHook += OnDropComplete;
        }

        private void OnDisable()
        {
            GateRollController.OnRollComplete                 -= OnRollComplete;
            WeatherRollController.OnWeatherRollComplete        -= OnWeatherComplete;
            DropperControllerECS.OnAnyDropStarted             -= OnDropStarted;
            BallDropCompletionManager_Hook.OnDropCompleteHook -= OnDropComplete;
        }

        private void Start()
        {
            if (DebugStartingBalls > 0 && PlayerDataManager.Instance != null)
                PlayerDataManager.Instance.Inventory.FineBalls += DebugStartingBalls;

            var completionMgr = FindObjectOfType<BallDropCompletionManager>();
            BallDropCompletionManager_Hook.Register(completionMgr);

            _roll      = FindObjectOfType<GateRollController>();
            _weather   = FindObjectOfType<WeatherRollController>();
            _effects   = FindObjectOfType<PrepEffectsController>();
            _tutorial  = FindObjectOfType<DropPuzzleTutorialOverlay>();
            _cinematic = FindObjectOfType<DropPuzzleCinematicOverlay>();

            // FlowManager owns roll timing — always disable AutoStart to prevent
            // GateRollController.Start() from subscribing to OnPuzzleLoaded itself
            if (_roll != null) _roll.AutoStart = false;

            bool hasTutorial  = _tutorial  != null && _tutorial.ShouldPlay();
            bool hasCinematic = !hasTutorial && _cinematic != null && _cinematic.ShouldPlay();

            if (hasTutorial)
            {
                _tutorial.OnComplete += OnCinematicComplete;
                _tutorial.PlayTutorial();
            }
            else if (hasCinematic)
            {
                _cinematic.PlayOnStart = false;
                _cinematic.OnComplete += OnCinematicComplete;
                _cinematic.PlayCinematic();
            }
            else
            {
                BeginGateRoll();
            }
        }

        // ── Phase transitions ─────────────────────────────────────────────────

        private void OnCinematicComplete()
        {
            if (_tutorial  != null) _tutorial.OnComplete  -= OnCinematicComplete;
            if (_cinematic != null) _cinematic.OnComplete -= OnCinematicComplete;
            BeginGateRoll();
        }

        private void BeginGateRoll()
        {
            CurrentPhase = Phase.GateRoll;

            if (_roll != null)
            {
                // Puzzle may already be loaded (PuzzlePrefabLoader ran in Start before us)
                if (PuzzlePrefabLoader.IsPuzzleReady)
                    _roll.StartRoll();
                else
                    PuzzlePrefabLoader.OnPuzzleLoaded += OnPuzzleReadyForRoll;
            }
            else
            {
                UnlockInput(); // No roll controller — unlock immediately
            }
        }

        private void OnPuzzleReadyForRoll()
        {
            PuzzlePrefabLoader.OnPuzzleLoaded -= OnPuzzleReadyForRoll;
            if (_roll != null) _roll.StartRoll();
        }

        private void OnRollComplete()
        {
            if (CurrentPhase != Phase.GateRoll) return;
            BeginWeatherRoll();
        }

        private void BeginWeatherRoll()
        {
            CurrentPhase = Phase.WeatherRoll;

            if (_weather != null)
                _weather.StartRoll(); // OnWeatherComplete unlocks when the reveal ends
            else
                UnlockInput();        // No weather controller — skip straight to play
        }

        private void OnWeatherComplete()
        {
            if (CurrentPhase != Phase.WeatherRoll) return;
            BeginPrepEffects();
        }

        // Final Preparation step: play whatever animations the rolled results trigger
        // (weather effects, board changes) and only unlock the drop once they finish.
        private void BeginPrepEffects()
        {
            CurrentPhase = Phase.PrepEffects;

            if (_effects != null)
                _effects.Play(WeatherRollController.CurrentAnomaly, OnPrepEffectsComplete);
            else
                OnPrepEffectsComplete(); // No effects controller — nothing to animate
        }

        private void OnPrepEffectsComplete()
        {
            if (CurrentPhase != Phase.PrepEffects) return;
            UnlockInput();
        }

        private void OnDropStarted() => CurrentPhase = Phase.Dropping;
        private void OnDropComplete() => CurrentPhase = Phase.Complete;

        private void UnlockInput()
        {
            AllowPlayerInput = true;
            CurrentPhase     = Phase.PlayerControl;
            OnPreparationComplete?.Invoke();
            Debug.Log("[FlowManager] Preparation complete — player may drop.");
        }
    }

    // Thin bridge so DropPuzzleFlowManager can subscribe to BallDropCompletionManager
    // without requiring it as a direct dependency (BallDropCompletionManager uses
    // its own event; this shim re-broadcasts it as a static event).
    public static class BallDropCompletionManager_Hook
    {
        public static event System.Action OnDropCompleteHook;

        public static void Register(BallDropCompletionManager mgr)
        {
            if (mgr != null) mgr.OnDropComplete += () => OnDropCompleteHook?.Invoke();
        }
    }
}
