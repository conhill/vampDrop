using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class StartupInstructionsOverlay : MonoBehaviour
{
    [Header("Overlay text")]
    [SerializeField] private string title = "Instructions";
    [SerializeField, TextArea] private string instructions = "Welcome!\n\nUse the mouse and keyboard to play.\nPress any key to start.";

    [Header("Visual settings")]
    [SerializeField] private Vector2 panelSize = new Vector2(700f, 400f);
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.85f);
    [SerializeField] private Color textColor = Color.white;

    private Canvas _canvas;
    private GameObject _panelObject;
    private bool _closing;
    private readonly List<Behaviour> _disabledUiBehaviours = new List<Behaviour>();
    public static bool IsInstructionsActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (FindFirstObjectByType<StartupInstructionsOverlay>() != null)
            return;

        var go = new GameObject("StartupInstructionsOverlay");
        go.AddComponent<StartupInstructionsOverlay>();
    }

    private void Awake()
    {
        if (FindObjectsByType<StartupInstructionsOverlay>(FindObjectsSortMode.None).Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateOverlay();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        if (_closing)
            return;

        DisableOtherUi();

        if (Input.anyKeyDown)
        {
            CloseOverlay();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DisableOtherUi();
    }

    private void CreateOverlay()
    {
        Time.timeScale = 0f;
        IsInstructionsActive = true;

        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10000;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        gameObject.AddComponent<GraphicRaycaster>();

        _panelObject = new GameObject("InstructionsPanel");
        _panelObject.transform.SetParent(transform, false);

        var panelImage = _panelObject.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = panelColor;

        var panelRect = _panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = panelSize;
        panelRect.anchoredPosition = Vector2.zero;

        var textObject = new GameObject("InstructionsText");
        textObject.transform.SetParent(_panelObject.transform, false);

        var textMesh = textObject.AddComponent<TextMeshProUGUI>();
        textMesh.text = title + "\n\n" + instructions;
        textMesh.fontSize = 32f;
        textMesh.color = textColor;
        textMesh.alignment = TextAlignmentOptions.Center;
        textMesh.textWrappingMode = TextWrappingModes.Normal;

        var textRect = textMesh.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(30f, 30f);
        textRect.offsetMax = new Vector2(-30f, -30f);

        DisableOtherUi();
    }

    private void DisableOtherUi()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas == null || canvas == _canvas || canvas.transform.IsChildOf(transform))
                continue;

            if (canvas.enabled)
            {
                _disabledUiBehaviours.Add(canvas);
                canvas.enabled = false;
            }
        }

        foreach (var uiDocument in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
            if (uiDocument == null || uiDocument.transform.IsChildOf(transform))
                continue;

            if (uiDocument.enabled)
            {
                _disabledUiBehaviours.Add(uiDocument);
                uiDocument.enabled = false;
            }
        }

        foreach (var canvasGroup in FindObjectsByType<CanvasGroup>(FindObjectsSortMode.None))
        {
            if (canvasGroup == null || canvasGroup.transform.IsChildOf(transform))
                continue;

            if (canvasGroup.enabled)
            {
                _disabledUiBehaviours.Add(canvasGroup);
                canvasGroup.enabled = false;
            }
        }
    }

    private void RestoreOtherUi()
    {
        foreach (var behaviour in _disabledUiBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = true;
        }

        _disabledUiBehaviours.Clear();
    }

    private void CloseOverlay()
    {
        if (_closing)
            return;

        _closing = true;
        Time.timeScale = 1f;
        IsInstructionsActive = false;
        RestoreOtherUi();

        if (_panelObject != null)
            Destroy(_panelObject);

        if (_canvas != null)
            Destroy(_canvas);

        Destroy(gameObject);
    }
}
