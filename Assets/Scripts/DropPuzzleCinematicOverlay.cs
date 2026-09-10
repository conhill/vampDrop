using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    // Lightweight in-scene cinematic slide system for the DropPuzzle scene.
    // Populate Slides in the Inspector (Sprite image + title + body text per slide).
    // Plays on Start (or call PlayCinematic() manually).
    // Dismissed with Space / Enter / left-click, or auto-advances after each slide's duration.
    // Fires OnComplete when all slides are done.
    public class DropPuzzleCinematicOverlay : MonoBehaviour
    {
        [System.Serializable]
        public struct Slide
        {
            public Sprite image;
            [TextArea(2, 4)]
            public string title;
            [TextArea(3, 6)]
            public string body;
            [Tooltip("Auto-advance after this many seconds (0 = wait for input only)")]
            public float duration;
        }

        public event System.Action OnComplete;

        [Header("Content")]
        public Slide[] Slides;

        [Header("Options")]
        [Tooltip("Play automatically on Start")]
        public bool PlayOnStart = true;
        [Tooltip("Skip playback entirely after the tutorial is completed")]
        public bool PlayOnlyDuringTutorial = true;
        [Tooltip("Fade duration between slides (seconds)")]
        public float FadeDuration = 0.35f;

        // Canvas references created at runtime
        private CanvasGroup  _group;
        private Image        _image;
        private TextMeshProUGUI _titleLabel;
        private TextMeshProUGUI _bodyLabel;
        private TextMeshProUGUI _skipLabel;
        private GameObject   _overlayRoot;

        private bool _skipRequested;

        // Returns true if this cinematic should actually play given current game state.
        public bool ShouldPlay()
        {
            if (Slides == null || Slides.Length == 0) return false;
            if (PlayOnlyDuringTutorial)
            {
                var pdm = PlayerDataManager.Instance;
                if (pdm != null && pdm.TutorialCompleted) return false;
            }
            return true;
        }

        private void Start()
        {
            BuildOverlay();
            if (PlayOnStart && ShouldPlay())
                PlayCinematic();
            else
                _overlayRoot.SetActive(false);
        }

        public void PlayCinematic()
        {
            _overlayRoot.SetActive(true);
            StartCoroutine(RunSlides());
        }

        // ── Overlay construction ──────────────────────────────────────────────

        private void BuildOverlay()
        {
            _overlayRoot = new GameObject("CinematicOverlay");
            _overlayRoot.transform.SetParent(transform, false);

            var canvas = _overlayRoot.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // above HUD

            var scaler = _overlayRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;

            _overlayRoot.AddComponent<GraphicRaycaster>();

            _group = _overlayRoot.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // Full-screen black background
            var bg = MakeRect(_overlayRoot.GetComponent<RectTransform>(), "BG",
                Vector2.zero, Vector2.one);
            bg.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

            // Image panel (left 45%)
            var imgPanel = MakeRect(_overlayRoot.GetComponent<RectTransform>(), "ImagePanel",
                new Vector2(0.05f, 0.1f), new Vector2(0.48f, 0.9f));
            _image = imgPanel.gameObject.AddComponent<Image>();
            _image.color           = Color.white;
            _image.preserveAspect  = true;

            // Text area (right side)
            var textPanel = MakeRect(_overlayRoot.GetComponent<RectTransform>(), "TextPanel",
                new Vector2(0.52f, 0.1f), new Vector2(0.95f, 0.9f));

            _titleLabel = AddTMP(textPanel, "Title",
                new Vector2(0f, 0.72f), new Vector2(1f, 1f),
                "", 28, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            _titleLabel.color = new Color(1f, 0.85f, 0.3f);

            _bodyLabel = AddTMP(textPanel, "Body",
                new Vector2(0f, 0f), new Vector2(1f, 0.68f),
                "", 18, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _bodyLabel.color = Color.white;

            // Skip hint at bottom
            _skipLabel = AddTMP(_overlayRoot.GetComponent<RectTransform>(), "Skip",
                new Vector2(0.3f, 0.01f), new Vector2(0.7f, 0.08f),
                "[SPACE] / [Enter] to continue", 14,
                FontStyles.Italic, TextAlignmentOptions.Center);
            _skipLabel.color = new Color(0.7f, 0.7f, 0.7f);
        }

        // ── Slide sequencing ──────────────────────────────────────────────────

        private IEnumerator RunSlides()
        {
            for (int i = 0; i < Slides.Length; i++)
            {
                _skipRequested = false;
                LoadSlide(Slides[i]);
                yield return StartCoroutine(Fade(0f, 1f));

                float elapsed = 0f;
                float wait    = Slides[i].duration > 0f ? Slides[i].duration : float.MaxValue;
                while (elapsed < wait && !_skipRequested)
                {
                    if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                        _skipRequested = true;
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (i < Slides.Length - 1)
                    yield return StartCoroutine(Fade(1f, 0f));
            }

            yield return StartCoroutine(Fade(1f, 0f));
            _overlayRoot.SetActive(false);
            OnComplete?.Invoke();
        }

        private void LoadSlide(Slide slide)
        {
            _image.sprite  = slide.image;
            _image.enabled = slide.image != null;
            _titleLabel.text = slide.title ?? "";
            _bodyLabel.text  = slide.body  ?? "";
        }

        private IEnumerator Fade(float from, float to)
        {
            float t = 0f;
            while (t < FadeDuration)
            {
                _group.alpha = Mathf.Lerp(from, to, t / FadeDuration);
                t += Time.deltaTime;
                yield return null;
            }
            _group.alpha = to;
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        private RectTransform MakeRect(RectTransform parent, string name,
                                       Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private TextMeshProUGUI AddTMP(RectTransform parent, string name,
                                       Vector2 anchorMin, Vector2 anchorMax,
                                       string text, float size,
                                       FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, 0f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text                = text;
            tmp.fontSize            = size;
            tmp.fontStyle           = style;
            tmp.color               = Color.white;
            tmp.alignment           = align;
            tmp.enableWordWrapping  = true;
            tmp.overflowMode        = TextOverflowModes.Overflow;
            return tmp;
        }
    }
}
