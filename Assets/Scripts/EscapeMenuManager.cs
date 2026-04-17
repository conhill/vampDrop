using UnityEngine;
using UnityEngine.SceneManagement;

namespace Vampire
{
    /// <summary>
    /// Persistent ESC pause menu — Settings (Music / SFX volume) and Quit.
    ///
    /// Key detection uses OnGUI / Event.current instead of Input.GetKeyDown so that
    /// Unity's UI Toolkit (UIDocument) cannot steal the ESC or Tab events via focus.
    ///
    /// SETUP: Add this component to the GameSystems GameObject alongside
    /// GameSceneManager, PlayerDataManager, etc.
    /// </summary>
    public class EscapeMenuManager : MonoBehaviour
    {
        public static EscapeMenuManager Instance { get; private set; }

        private const string PrefMusicVol = "vamp_music_vol";
        private const string PrefSFXVol   = "vamp_sfx_vol";

        private float _musicVolume = 1f;
        private float _sfxVolume   = 1f;
        private bool  _isOpen      = false;

        public bool IsOpen => _isOpen;

        // OnGUI styles
        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _btnDanger;
        private GUIStyle _btnSecondary;
        private bool     _stylesBuilt;

        private const string FPSScene   = "FPS_Collect";
        private const string DropScene  = "DropPuzzle";
        private const string ComicScene = "Comic";

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Runs BEFORE any scene Awake, so Instance is set before GameSystems or any
        // scene object can race us. Any duplicate component (e.g. a disabled one left
        // on GameSystems) will hit the Instance != null check in Awake and remove itself.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("[EscapeMenu]");
            DontDestroyOnLoad(go);
            go.AddComponent<EscapeMenuManager>(); // triggers Awake synchronously
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Duplicate — e.g. a component left on GameSystems. Remove only the
                // component; never destroy the whole GameSystems object.
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[ESCMenu] Ready on '{gameObject.name}'");

            _musicVolume = PlayerPrefs.GetFloat(PrefMusicVol, 1f);
            _sfxVolume   = PlayerPrefs.GetFloat(PrefSFXVol,   1f);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _isOpen        = false;
            Time.timeScale = 1f;
            ApplyVolumesToScene();
        }

        // ── OnGUI — all rendering AND key handling ─────────────────────────────

        private void OnGUI()
        {
            // ESC detection via Event.current — bypasses UI Toolkit focus capture.
            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape
                && SceneManager.GetActiveScene().name != ComicScene)
            {
                bool workerOpen = Helpers.HelperManagerUI.Instance != null
                               && Helpers.HelperManagerUI.Instance.InUIMode;
                if (!workerOpen)
                {
                    if (_isOpen) CloseMenu();
                    else         OpenMenu();
                    Event.current.Use();
                }
            }

            if (!_isOpen) return;

            BuildStyles();

            bool inDrop  = SceneManager.GetActiveScene().name == DropScene;
            float panelH = inDrop ? 365f : 315f;
            float panelW = 380f;
            float panelX = (Screen.width  - panelW) * 0.5f;
            float panelY = (Screen.height - panelH) * 0.5f;

            // Dark screen dim
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Panel background
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), GUIContent.none, _panelStyle);

            float y  = panelY + 18f;
            float cx = panelX + panelW * 0.5f;
            float bW = 240f;
            float bH = 42f;

            // Title
            GUI.Label(new Rect(panelX, y, panelW, 38f), "PAUSED", _titleStyle);
            y += 46f;

            // Music slider
            GUI.Label(new Rect(panelX + 16f, y, 86f, 26f), "Music", _labelStyle);
            float newMusic = GUI.HorizontalSlider(
                new Rect(panelX + 106f, y + 9f, panelW - 122f, 16f),
                _musicVolume, 0f, 1f);
            if (!Mathf.Approximately(newMusic, _musicVolume))
            {
                _musicVolume = newMusic;
                ApplyVolumesToScene();
            }
            y += 34f;

            // SFX slider
            GUI.Label(new Rect(panelX + 16f, y, 86f, 26f), "SFX", _labelStyle);
            float newSFX = GUI.HorizontalSlider(
                new Rect(panelX + 106f, y + 9f, panelW - 122f, 16f),
                _sfxVolume, 0f, 1f);
            if (!Mathf.Approximately(newSFX, _sfxVolume))
            {
                _sfxVolume = newSFX;
                ApplyVolumesToScene();
            }
            y += 46f;

            // Resume
            if (GUI.Button(new Rect(cx - bW * 0.5f, y, bW, bH), "Resume", _btnStyle))
                CloseMenu();
            y += bH + 10f;

            // Go Back to FPS (drop scene only)
            if (inDrop)
            {
                if (GUI.Button(new Rect(cx - bW * 0.5f, y, bW, bH), "Go Back to FPS", _btnSecondary))
                    GoBackToFPS();
                y += bH + 10f;
            }

            // Quit
            if (GUI.Button(new Rect(cx - bW * 0.5f, y, bW, bH), "Quit to Desktop", _btnDanger))
                Application.Quit();
        }

        // ── Actions ────────────────────────────────────────────────────────────

        public void OpenMenu()
        {
            _isOpen          = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            Time.timeScale   = 0f;
        }

        public void CloseMenu()
        {
            _isOpen        = false;
            Time.timeScale = 1f;
            SavePrefs();
            RestoreCursor();
        }

        private void RestoreCursor()
        {
            if (SceneManager.GetActiveScene().name == FPSScene)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
            }
        }

        private void GoBackToFPS()
        {
            CloseMenu();
            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.ReturnToFPS();
            else
                SceneManager.LoadScene(FPSScene);
        }

        private void ApplyVolumesToScene()
        {
            var fps = FindFirstObjectByType<Player.FPSAudioManager>();
            if (fps  != null) { fps.SetMusicVolume(_musicVolume);  fps.SetSFXVolume(_sfxVolume);  }
            var drp = FindFirstObjectByType<DropPuzzle.BallDropAudioManager>();
            if (drp != null) { drp.SetMusicVolume(_musicVolume); drp.SetSFXVolume(_sfxVolume); }
        }

        private void SavePrefs()
        {
            PlayerPrefs.SetFloat(PrefMusicVol, _musicVolume);
            PlayerPrefs.SetFloat(PrefSFXVol,   _sfxVolume);
            PlayerPrefs.Save();
        }

        // ── Style builder ──────────────────────────────────────────────────────

        private void BuildStyles()
        {
            if (_stylesBuilt) return;

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = MakeTex(new Color(0.07f, 0.07f, 0.13f, 0.97f));

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _titleStyle.normal.textColor = Color.white;

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            _labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };

            _btnDanger = new GUIStyle(_btnStyle);
            _btnDanger.normal.background = MakeTex(new Color(0.5f, 0.1f, 0.1f));
            _btnDanger.hover.background  = MakeTex(new Color(0.7f, 0.15f, 0.15f));
            _btnDanger.normal.textColor  = Color.white;
            _btnDanger.hover.textColor   = Color.white;

            _btnSecondary = new GUIStyle(_btnStyle);
            _btnSecondary.normal.background = MakeTex(new Color(0.3f, 0.26f, 0.06f));
            _btnSecondary.hover.background  = MakeTex(new Color(0.45f, 0.38f, 0.08f));
            _btnSecondary.normal.textColor  = Color.white;
            _btnSecondary.hover.textColor   = Color.white;

            _stylesBuilt = true;
        }

        private Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(2, 2);
            t.SetPixels(new[] { c, c, c, c });
            t.Apply();
            return t;
        }
    }
}
