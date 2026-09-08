using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Drives the per-map "skrilla goal" loop. Each map (puzzle index) has a goal from
    /// PuzzlePrefabLoader.LevelSkrillaGoals. Skrilla earned on the map accumulates across
    /// drops (persisted in PlayerDataManager.LevelSkrillaProgress); when it crosses the
    /// goal, the next map unlocks, it becomes the selected map, and the player is sent home
    /// so they can head back out into the freshly-unlocked level.
    ///
    /// Progress accumulates per map and carries between drops on the SAME map, but each map
    /// keeps its own tally — moving on starts the next map's goal from zero.
    ///
    /// Add to a GameObject in the DropPuzzle scene. Reads the level from
    /// PuzzlePrefabLoader.OnBoardAligned and earnings from PlayerDataManager.OnCurrencyChanged.
    /// </summary>
    public class LevelGoalManager : MonoBehaviour
    {
        public static LevelGoalManager Instance { get; private set; }

        [Header("Home")]
        [Tooltip("Scene loaded when the goal is reached ('sent home').")]
        public string HomeSceneName = "Base";
        [Tooltip("Send the player home automatically when the goal is hit.")]
        public bool SendHomeOnGoal = true;
        [Tooltip("Seconds the 'goal reached' banner shows before heading home.")]
        public float HomeDelay = 2.5f;

        [Header("UI")]
        [Tooltip("Build the built-in compact progress readout. Turn off to drive your own UI via events.")]
        public bool BuildUI = true;

        /// <summary>Fired on every progress change. (level, progress, goal).</summary>
        public static event System.Action<int, int, int> OnProgress;
        /// <summary>Fired once when a map's goal is crossed. (completedLevel).</summary>
        public static event System.Action<int> OnGoalReached;

        private int  _level;
        private int  _goal;
        private bool _completed;
        private int  _puzzleCount;

        // UI
        private Canvas _canvas;
        private TextMeshProUGUI _label;
        private Image _fill;
        private TextMeshProUGUI _banner;

        public int Progress => PlayerDataManager.Instance != null ? PlayerDataManager.Instance.GetLevelProgress(_level) : 0;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void OnEnable()
        {
            PlayerDataManager.OnCurrencyChanged += OnCurrency;
            PuzzlePrefabLoader.OnBoardAligned   += OnBoard;
        }

        private void OnDisable()
        {
            PlayerDataManager.OnCurrencyChanged -= OnCurrency;
            PuzzlePrefabLoader.OnBoardAligned   -= OnBoard;
        }

        // ── Level setup ───────────────────────────────────────────────────────

        private void OnBoard(PuzzlePrefabLoader.BoardFrame frame)
        {
            var loader = FindObjectOfType<PuzzlePrefabLoader>();
            _level       = PuzzlePrefabLoader.CurrentLevel;
            _goal        = loader != null ? loader.CurrentLevelGoal : 0;
            _puzzleCount = loader != null && loader.PuzzlePrefabs != null ? loader.PuzzlePrefabs.Length : 0;

            // If they walk back into a map they've already beaten, show it complete but DON'T
            // force them home again — only a fresh crossing during play does that.
            _completed = _goal > 0 && Progress >= _goal;

            if (BuildUI) EnsureUI();
            Refresh();

            Debug.Log($"[LevelGoal] Level {_level}: {Progress}/{_goal} skrilla " +
                      $"({(_completed ? "already complete" : "in progress")}).");
        }

        // ── Earnings ──────────────────────────────────────────────────────────

        private void OnCurrency(int total, int delta, string source)
        {
            if (_completed || delta <= 0) return;

            PlayerDataManager.Instance?.AddLevelSkrilla(_level, delta);
            Refresh();

            if (_goal > 0 && Progress >= _goal)
                Complete();
        }

        private void Complete()
        {
            _completed = true;
            var pdm = PlayerDataManager.Instance;

            int next = _level + 1;
            bool hasNext = next < _puzzleCount;
            if (hasNext && pdm != null)
            {
                pdm.UnlockLevel(next);
                pdm.SelectedLevel = next; // queue it up for next time out
            }

            Debug.Log($"[LevelGoal] GOAL on level {_level}! " +
                      (hasNext ? $"Unlocked level {next}." : "That was the final map."));

            OnGoalReached?.Invoke(_level);

            if (BuildUI && _banner != null)
            {
                _banner.gameObject.SetActive(true);
                _banner.text = hasNext ? "GOAL REACHED\nNext map unlocked" : "FINAL MAP CLEARED";
            }

            if (SendHomeOnGoal) StartCoroutine(GoHome());
        }

        private IEnumerator GoHome()
        {
            yield return new WaitForSeconds(HomeDelay);
            if (!string.IsNullOrEmpty(HomeSceneName))
                SceneManager.LoadScene(HomeSceneName);
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private void Refresh()
        {
            float n = _goal > 0 ? Mathf.Clamp01((float)Progress / _goal) : 0f;
            if (_label != null) _label.text = $"SKRILLA GOAL   {Progress} / {_goal}";
            if (_fill  != null) _fill.fillAmount = n;
            OnProgress?.Invoke(_level, Progress, _goal);
        }

        private void EnsureUI()
        {
            if (_canvas != null) return;

            var canvasGO = new GameObject("LevelGoalCanvas");
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            // Compact bar pinned to the top-center — intentionally understated.
            var panel = MakeRect(_canvas.transform, "GoalPanel",
                new Vector2(0.32f, 0.94f), new Vector2(0.68f, 0.99f));
            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = new Color(0.05f, 0.06f, 0.08f, 0.82f);

            var fillRect = MakeRect(panel, "Fill", new Vector2(0f, 0f), new Vector2(1f, 1f));
            _fill = fillRect.gameObject.AddComponent<Image>();
            _fill.color = new Color(0.35f, 0.75f, 0.4f, 0.55f);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillOrigin = 0;
            _fill.fillAmount = 0f;

            _label = MakeText(panel, "Label", new Vector2(0f, 0f), new Vector2(1f, 1f),
                "", 18, TextAlignmentOptions.Center);
            _label.color = new Color(0.92f, 0.94f, 0.9f);

            // Goal banner (hidden until reached) just below the bar.
            var bannerRect = MakeRect(_canvas.transform, "GoalBanner",
                new Vector2(0.3f, 0.82f), new Vector2(0.7f, 0.92f));
            _banner = bannerRect.gameObject.AddComponent<TextMeshProUGUI>();
            _banner.text = "";
            _banner.fontSize = 34;
            _banner.fontStyle = FontStyles.Bold;
            _banner.alignment = TextAlignmentOptions.Center;
            _banner.color = new Color(1f, 0.9f, 0.4f);
            bannerRect.gameObject.SetActive(false);
        }

        private static RectTransform MakeRect(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, string text, float size, TextAlignmentOptions align)
        {
            var rt = MakeRect(parent, name, aMin, aMax);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = align;
            t.raycastTarget = false;
            return t;
        }
    }
}
