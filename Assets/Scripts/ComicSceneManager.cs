using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

namespace Vampire
{
    public class ComicSceneManager : MonoBehaviour
    {
        public static ComicSequenceConfig CurrentSequence { get; set; }
        public static string NextSceneOverride { get; set; }

        [Header("Panel Prefab")]
        public GameObject panelPrefab;

        [Header("Container")]
        public Transform panelContainer;

        [Header("Fallback")]
        public ComicSequenceConfig defaultSequence;

        [Header("Settings")]
        public bool allowSkip = true;

        [Header("Audio")]
        public AudioSource musicSource;
        public AudioSource sfxSource;

        [Header("UI")]
        public TextMeshProUGUI continuePrompt;

        private ComicSequenceConfig activeSequence;
        private int currentIndex;
        private ComicPanelController currentPanel;
        private bool waitingForInput;
        private bool transitioning;
        private float panelStartTime;
        private Sprite lastBackground;
        private float _inputCooldownUntil;   // debounce so mashing space can't chain actions
        private GUIStyle _promptStyle;        // fallback "press space" prompt (if none wired)

        private void Start()
        {
            activeSequence = CurrentSequence ?? defaultSequence;
            CurrentSequence = null;

            if (activeSequence == null || activeSequence.panels.Count == 0)
            {
                CompleteComic();
                return;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (activeSequence.sequenceMusic != null && musicSource != null)
            {
                musicSource.clip = activeSequence.sequenceMusic;
                musicSource.volume = activeSequence.musicVolume;
                musicSource.loop = true;
                musicSource.Play();
            }

            lastBackground = null;
            ShowPanel(0);
        }

        private void Update()
        {
            if (transitioning) return;

            if (allowSkip && Input.GetKeyDown(KeyCode.Escape))
            {
                CompleteComic();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                // Debounce: ignore presses that arrive too fast after the last action so a
                // mash can't both skip the entrance AND advance in the same burst.
                if (Time.unscaledTime < _inputCooldownUntil) return;

                // First press during entrance skips the animation
                if (currentPanel != null && !currentPanel.entranceComplete)
                {
                    currentPanel.SkipEntrance();
                    _inputCooldownUntil = Time.unscaledTime + 0.2f;
                    return;
                }
                // Second press (after entrance) advances to next panel
                if (waitingForInput)
                {
                    _inputCooldownUntil = Time.unscaledTime + 0.2f;
                    StartCoroutine(AdvancePanel());
                    return;
                }
            }

            // Auto-advance (only after entrance finishes)
            if (currentPanel != null && currentPanel.entranceComplete && !waitingForInput)
            {
                float auto = activeSequence.panels[currentIndex].autoDuration;
                if (auto > 0f && Time.time - panelStartTime >= auto)
                    StartCoroutine(AdvancePanel());
            }
        }

        private void ShowPanel(int index)
        {
            currentIndex = index;
            ComicPanel data = activeSequence.panels[index];

            if (panelContainer == null) panelContainer = transform;
            GameObject go = Instantiate(panelPrefab, panelContainer);
            currentPanel = go.GetComponent<ComicPanelController>();
            if (currentPanel.canvasGroup != null) currentPanel.canvasGroup.alpha = 1f;
            currentPanel.Populate(data);

            PlayPanelAudio(data);
            bool bgChanged = data.background != lastBackground;
            lastBackground = data.background;

            // Snap background visible immediately for same-bg panels to avoid a 1-frame gap.
            if (!bgChanged && currentPanel.background != null && currentPanel.background.gameObject.activeSelf)
                currentPanel.background.color = Color.white;

            StartCoroutine(currentPanel.PlayEntrance(bgChanged));
            StartCoroutine(WaitForEntrance(data));
        }

        private IEnumerator WaitForEntrance(ComicPanel data)
        {
            yield return new WaitUntil(() => currentPanel == null || currentPanel.entranceComplete);
            if (currentPanel == null || transitioning) yield break;

            panelStartTime = Time.time;

            if (data.autoDuration <= 0f)
            {
                waitingForInput = true;
                SetContinuePrompt(true);
            }
        }

        private IEnumerator AdvancePanel()
        {
            transitioning = true;
            waitingForInput = false;
            SetContinuePrompt(false);

            // Only fade the full panel to black when the next panel has a different background.
            // Same-background transitions fade only the content (character + dialogue).
            int nextIndex = currentIndex + 1;
            bool bgWillChange = nextIndex >= activeSequence.panels.Count
                || activeSequence.panels[nextIndex].background != lastBackground;

            if (bgWillChange)
                yield return StartCoroutine(currentPanel.FadeOut(0.35f));
            else
                yield return StartCoroutine(currentPanel.FadeOutContent(0.25f));

            Destroy(currentPanel.gameObject);
            currentPanel = null;
            currentIndex++;

            if (currentIndex >= activeSequence.panels.Count)
            {
                CompleteComic();
                yield break;
            }

            if (bgWillChange)
                yield return new WaitForSeconds(0.15f);

            transitioning = false;
            ShowPanel(currentIndex);
        }

        private void CompleteComic()
        {
            string nextScene = !string.IsNullOrEmpty(NextSceneOverride)
                ? NextSceneOverride
                : (activeSequence != null ? activeSequence.nextSceneName : "FPS_Collect");
            NextSceneOverride = null;

            if (musicSource != null) musicSource.Stop();
            SceneManager.LoadScene(nextScene);
        }

        private void PlayPanelAudio(ComicPanel data)
        {
            if (data.sfx != null && sfxSource != null)
                sfxSource.PlayOneShot(data.sfx);

            if (data.music != null && musicSource != null)
            {
                musicSource.clip = data.music;
                musicSource.volume = data.musicVolume;
                musicSource.Play();
            }
        }

        private void SetContinuePrompt(bool visible)
        {
            if (continuePrompt != null)
                continuePrompt.gameObject.SetActive(visible);
        }

        // Fallback on-screen prompt when no continuePrompt TMP is wired in the scene.
        private void OnGUI()
        {
            if (continuePrompt != null || !waitingForInput || transitioning) return;

            if (_promptStyle == null)
            {
                _promptStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                _promptStyle.normal.textColor = Color.white;
            }

            float a = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 1.5f, 1f);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f * a);
            GUI.Label(new Rect(2f, Screen.height - 62f, Screen.width, 40f), "Press [SPACE] to continue", _promptStyle);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(0f, Screen.height - 64f, Screen.width, 40f), "Press [SPACE] to continue", _promptStyle);
            GUI.color = prev;
        }
    }
}
