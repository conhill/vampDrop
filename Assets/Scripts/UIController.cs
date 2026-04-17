using UnityEngine;
using UnityEngine.UIElements;
using Vampire.DropPuzzle;
using Vampire.Helpers;

/// <summary>
/// Central UI Toolkit controller for the in-game HUD.
///
/// Attach this to the same GameObject as your UIDocument.
/// All UXML element names are listed in the regions below — match them exactly
/// in UI Builder's Name field.
///
/// Sections driven:
///   - Day/Night cycle  (timer, phase label, icon swap)
///   - Active quest     (title, description, progress)
///   - Rice             (grain count)
///   - Rice balls       (inventory by quality)
///   - Currency         (skrilla)
///   - Helpers          (deployed / max, per-worker status)
/// </summary>
public class UIController : MonoBehaviour
{
    [SerializeField] UIDocument uiDocument;

    [Header("Day/Night Icons")]
    [SerializeField] Sprite sunSprite;
    [SerializeField] Sprite moonSprite;

    [Header("Debug")]
    [Tooltip("Enable to print binding status and quest state changes to the console")]
    [SerializeField] bool debugLogs = true;

    // ── cached element references ─────────────────────────────────────────────

    // Day/Night
    private VisualElement _dayNightIcon;
    private Label _dayNightPhase;
    private Label _dayNightTimer;
    private VisualElement _timeBarFill;  // width driven 0–100%

    // Quest
    private Label _questTitle;
    private Label _questDescription;
    private Label _questProgressText;
    private VisualElement _questProgressFill;   // inner fill bar; set width %
    private Label _questNone;                   // shown when no active quest

    // Rice
    private Label _riceCount;

    // Rice balls
    private Label _riceBallTotal;
    private Label _riceBallFine;
    private Label _riceBallGood;
    private Label _riceBallGreat;
    private Label _riceBallExcellent;

    // Currency
    private Label _currencyAmount;

    // Helpers
    private Label _helperDeployed;
    private Label _helperMax;
    private VisualElement _helperListContainer; // optional — rows injected at runtime

    // ── change-detection cache ────────────────────────────────────────────────

    private DayNightCycleManager.TimeOfDay _lastPhase = (DayNightCycleManager.TimeOfDay)(-1);
    private int  _lastTimerSecond   = -1;
    private int  _lastRice          = -1;
    private int  _lastFine          = -1;
    private int  _lastGood          = -1;
    private int  _lastGreat         = -1;
    private int  _lastExcellent     = -1;
    private int  _lastCurrency      = -1;
    private int  _lastDeployed      = -1;
    private int  _lastMaxHelpers    = -1;
    private string _lastQuestId     = null;
    private int  _lastQuestValue    = -1;

    // tracks whether we've subscribed to QuestManager events yet
    private bool _questManagerSubscribed = false;

    // tracks whether we've subscribed to DayNightCycleManager events
    private bool _dayNightSubscribed = false;

    // ── lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (uiDocument == null)
        {
            Debug.LogWarning("[UIController] UIDocument not assigned — attach a UIDocument to this GameObject.");
            return;
        }

        var root = uiDocument.rootVisualElement;

        // Day/Night
        _dayNightIcon  = Bind<VisualElement>(root, "day-night-icon");
        _dayNightPhase = Bind<Label>(root, "day-night-phase");
        _dayNightTimer = Bind<Label>(root, "day-night-timer");
        _timeBarFill   = Bind<VisualElement>(root, "time-bar-fill");

        // Quest
        _questTitle        = Bind<Label>(root, "quest-title");
        _questDescription  = Bind<Label>(root, "quest-description");
        _questProgressText = Bind<Label>(root, "quest-progress-text");
        _questProgressFill = Bind<VisualElement>(root, "quest-progress-fill");
        _questNone         = Bind<Label>(root, "quest-none");

        // Rice
        _riceCount = Bind<Label>(root, "rice-count");

        // Rice balls
        _riceBallTotal     = Bind<Label>(root, "rice-ball-total");
        _riceBallFine      = Bind<Label>(root, "rice-ball-fine");
        _riceBallGood      = Bind<Label>(root, "rice-ball-good");
        _riceBallGreat     = Bind<Label>(root, "rice-ball-great");
        _riceBallExcellent = Bind<Label>(root, "rice-ball-excellent");

        // Currency
        _currencyAmount = Bind<Label>(root, "currency-amount");

        // Helpers
        _helperDeployed      = Bind<Label>(root, "helper-deployed");
        _helperMax           = Bind<Label>(root, "helper-max");
        _helperListContainer = Bind<VisualElement>(root, "helper-list");

        Log("Element binding complete. QuestManager ready: " + (QuestManager.Instance != null));

        // QuestManager may not be alive yet — TrySubscribeQuestManager is called
        // each Update until it succeeds, so we never miss events even if the
        // manager initialises after this GameObject.
        TrySubscribeQuestManager();

        // Force a first-frame data push
        RefreshAll();
    }

    private void OnDisable()
    {
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.OnQuestStarted   -= OnQuestChanged;
            QuestManager.Instance.OnQuestCompleted -= OnQuestChanged;
            QuestManager.Instance.OnQuestProgress  -= OnQuestProgress;
        }
        _questManagerSubscribed = false;

        var dnm = DayNightCycleManager.Instance;
        if (dnm != null)
        {
            dnm.OnDayStart   -= OnDayNightPhaseChanged;
            dnm.OnNightStart -= OnDayNightPhaseChanged;
        }
        _dayNightSubscribed = false;
    }

    private void Update()
    {
        // Subscribe as soon as managers come online (handles scene load order)
        if (!_questManagerSubscribed)
            TrySubscribeQuestManager();
        if (!_dayNightSubscribed)
            TrySubscribeDayNight();

        UpdateDayNight();
        UpdatePlayerData();
        UpdateHelpers();
    }

    // Tries to subscribe to QuestManager events. Safe to call repeatedly.
    private void TrySubscribeQuestManager()
    {
        if (_questManagerSubscribed || QuestManager.Instance == null) return;

        QuestManager.Instance.OnQuestStarted   += OnQuestChanged;
        QuestManager.Instance.OnQuestCompleted += OnQuestChanged;
        QuestManager.Instance.OnQuestProgress  += OnQuestProgress;
        _questManagerSubscribed = true;

        Log($"Subscribed to QuestManager. Current quest: " +
            (QuestManager.Instance.currentQuest != null
                ? $"'{QuestManager.Instance.currentQuest.title}' active={QuestManager.Instance.currentQuest.isActive} complete={QuestManager.Instance.currentQuest.isComplete}"
                : "none"));

        // Catch any quest that was already active before we subscribed
        RefreshQuest();
    }

    // Subscribes to DayNightCycleManager phase-change events so the icon updates
    // even if the polling check misses the transition frame.
    private void TrySubscribeDayNight()
    {
        if (_dayNightSubscribed) return;
        var dnm = DayNightCycleManager.Instance;
        if (dnm == null) return;
        dnm.OnDayStart   += OnDayNightPhaseChanged;
        dnm.OnNightStart += OnDayNightPhaseChanged;
        _dayNightSubscribed = true;
    }

    // Force-reset the change-detection cache so UpdateDayNight() refreshes icon + label.
    private void OnDayNightPhaseChanged()
    {
        _lastPhase       = (DayNightCycleManager.TimeOfDay)(-1);
        _lastTimerSecond = -1;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Queries an element by name and logs a warning if it is missing.
    private T Bind<T>(VisualElement root, string name) where T : VisualElement
    {
        var el = root.Q<T>(name);
        if (el == null)
            Debug.LogWarning($"[UIController] Element not found in UXML: name='{name}' type={typeof(T).Name}");
        else
            Log($"  bound '{name}' ({typeof(T).Name})");
        return el;
    }

    private void Log(string msg)
    {
        if (debugLogs) Debug.Log("[UIController] " + msg);
    }

    // ── Day / Night ───────────────────────────────────────────────────────────

    private void UpdateDayNight()
    {
        var mgr = DayNightCycleManager.Instance;
        if (mgr == null) return;

        // Phase change
        if (mgr.currentTime != _lastPhase)
        {
            _lastPhase = mgr.currentTime;
            bool isDay = _lastPhase == DayNightCycleManager.TimeOfDay.Day;

            if (_dayNightPhase != null)
                _dayNightPhase.text = isDay ? "DAY" : "NIGHT";

            if (_dayNightIcon != null)
            {
                Sprite icon = isDay ? sunSprite : moonSprite;
                if (icon != null)
                    _dayNightIcon.style.backgroundImage = new StyleBackground(icon);
            }
        }

        // Timer + progress bar — update once per second
        int sec = Mathf.CeilToInt(mgr.GetTimeRemaining());
        if (sec != _lastTimerSecond)
        {
            _lastTimerSecond = sec;
            if (_dayNightTimer != null)
                _dayNightTimer.text = mgr.GetFormattedTimeRemaining();

            // Drive the time-bar-fill width as % of phaseDuration
            if (_timeBarFill != null && mgr.phaseDuration > 0f)
            {
                float pct = Mathf.Clamp01(mgr.GetTimeRemaining() / mgr.phaseDuration);
                _timeBarFill.style.width = new StyleLength(new Length(pct * 100f, LengthUnit.Percent));
            }
        }
    }

    // ── Player data (rice, balls, currency) ──────────────────────────────────

    private void UpdatePlayerData()
    {
        var pd = PlayerDataManager.Instance;
        if (pd == null) return;

        if (pd.RiceGrains != _lastRice)
        {
            _lastRice = pd.RiceGrains;
            if (_riceCount != null) _riceCount.text = _lastRice.ToString();
        }

        var inv = pd.Inventory;

        if (inv.FineBalls != _lastFine)
        {
            _lastFine = inv.FineBalls;
            if (_riceBallFine != null) _riceBallFine.text = _lastFine.ToString();
        }
        if (inv.GoodBalls != _lastGood)
        {
            _lastGood = inv.GoodBalls;
            if (_riceBallGood != null) _riceBallGood.text = _lastGood.ToString();
        }
        if (inv.GreatBalls != _lastGreat)
        {
            _lastGreat = inv.GreatBalls;
            if (_riceBallGreat != null) _riceBallGreat.text = _lastGreat.ToString();
        }
        if (inv.ExcellentBalls != _lastExcellent)
        {
            _lastExcellent = inv.ExcellentBalls;
            if (_riceBallExcellent != null) _riceBallExcellent.text = _lastExcellent.ToString();
        }

        int total = inv.GetTotalBalls();
        // Reuse _lastFine dirty-check isn't quite right for total — track separately via sum
        if (_riceBallTotal != null)
            _riceBallTotal.text = total.ToString();

        if (pd.TotalCurrency != _lastCurrency)
        {
            _lastCurrency = pd.TotalCurrency;
            if (_currencyAmount != null) _currencyAmount.text = $"${_lastCurrency}";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateHelpers()
    {
        var pd = PlayerDataManager.Instance;
        if (pd == null) return;

        int deployed = pd.Helpers.GetDeployedHelperCount();
        int max      = pd.Helpers.GetMaxHelpers();

        if (deployed != _lastDeployed || max != _lastMaxHelpers)
        {
            _lastDeployed    = deployed;
            _lastMaxHelpers  = max;
            if (_helperDeployed != null) _helperDeployed.text = deployed.ToString();
            if (_helperMax != null)      _helperMax.text      = max.ToString();
        }
    }

    // ── Quest (event-driven + polled progress) ────────────────────────────────

    private void OnQuestChanged(QuestManager.Quest q)
    {
        Log($"Quest event received — id='{q?.questId}' title='{q?.title}' active={q?.isActive} complete={q?.isComplete}");
        RefreshQuest();
    }

    private void OnQuestProgress(QuestManager.Quest q)
    {
        Log($"Quest progress — '{q?.title}' {q?.currentValue}/{q?.targetValue}");
        RefreshQuestProgress(q);
    }

    private void RefreshQuest()
    {
        var qm = QuestManager.Instance;
        if (qm == null)
        {
            Log("RefreshQuest: QuestManager.Instance is null — skipping");
            return;
        }

        var q = qm.currentQuest;
        bool hasQuest = q != null && q.isActive && !q.isComplete;

        Log($"RefreshQuest: currentQuest='{q?.questId}' hasQuest={hasQuest}" +
            (q != null ? $" active={q.isActive} complete={q.isComplete}" : ""));

        if (_questNone != null)
            _questNone.style.display = hasQuest ? DisplayStyle.None : DisplayStyle.Flex;
        else
            Log("RefreshQuest: quest-none element not bound — skipping display toggle");

        if (_questTitle != null)
            _questTitle.text = hasQuest ? q.title : "";
        else
            Log("RefreshQuest: quest-title element not bound");

        if (_questDescription != null)
            _questDescription.text = hasQuest ? q.description : "";
        else
            Log("RefreshQuest: quest-description element not bound");

        if (hasQuest)
        {
            _lastQuestId    = q.questId;
            _lastQuestValue = q.currentValue;
            RefreshQuestProgress(q);
        }
        else
        {
            _lastQuestId    = null;
            _lastQuestValue = 0;
            if (_questProgressText != null) _questProgressText.text = "";
            SetProgressFill(0f);
        }
    }

    private void RefreshQuestProgress(QuestManager.Quest q)
    {
        if (q == null) return;
        if (_questProgressText != null)
            _questProgressText.text = q.targetValue > 0
                ? $"{q.currentValue}/{q.targetValue}"
                : "In Progress";

        float pct = q.targetValue > 0
            ? Mathf.Clamp01((float)q.currentValue / q.targetValue)
            : 0f;
        SetProgressFill(pct);
    }

    private void SetProgressFill(float pct)
    {
        if (_questProgressFill == null) return;
        // Assumes quest-progress-fill is inside a fixed-width parent.
        // Set width as a percentage of the parent.
        _questProgressFill.style.width = new StyleLength(new Length(pct * 100f, LengthUnit.Percent));
    }

    // ── Full refresh (called on enable or first frame) ────────────────────────

    private void RefreshAll()
    {
        _lastPhase       = (DayNightCycleManager.TimeOfDay)(-1);
        _lastTimerSecond = -1;
        _lastRice = _lastFine = _lastGood = _lastGreat = _lastExcellent = -1;
        _lastCurrency = _lastDeployed = _lastMaxHelpers = -1;

        UpdateDayNight();
        UpdatePlayerData();
        UpdateHelpers();
        RefreshQuest();
    }
}
