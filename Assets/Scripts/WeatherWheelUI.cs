using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// "Prize border" weather spinner. Weather tiles line the perimeter of a grid; a
    /// highlight travels clockwise around the border, decelerates, and lands on the
    /// rolled outcome, which is revealed big in the center panel ("Normal Day" for Clear).
    ///
    /// Purely 2D UI — no 3D/Blender needed. Greyboxes with colored tiles + short labels;
    /// assign per-weather <see cref="Styles"/> (Icon/Color/Label) to drop in real 2D art.
    ///
    /// The landed tile is cosmetic: the actual result is decided by WeatherAnomalyRoller
    /// and passed to <see cref="Spin"/> — the wheel just animates to a tile carrying it.
    /// Add to the DropPuzzle scene; WeatherRollController finds and drives it.
    /// </summary>
    public class WeatherWheelUI : MonoBehaviour
    {
        [Serializable]
        public class WeatherStyle
        {
            public WeatherAnomaly Anomaly;
            public Sprite Icon;
            public Color Color = Color.gray;
            public string Label = "";
        }

        [Header("Layout")]
        [Tooltip("Grid size N (odd looks best). Border tiles = 4*(N-1).")]
        public int GridSize = 5;
        public float CellSize = 88f;
        public float CellSpacing = 6f;

        [Header("Spin feel")]
        public int SpinLoops = 2;
        public float MinStepInterval = 0.03f;
        public float MaxStepInterval = 0.14f;
        [Tooltip("How long the outcome holds in the center before the Accept prompt appears.")]
        public float RevealHold = 0.4f;
        [Tooltip("Hide the wheel after the reveal so it doesn't linger into the drop.")]
        public bool HideWhenDone = true;

        [Header("Styling (optional overrides)")]
        public WeatherStyle[] Styles;
        [Tooltip("Ring outcomes clockwise from top-left. Empty = auto-generate (mostly Clear).")]
        public WeatherAnomaly[] RingLayout;

        private class Tile
        {
            public RectTransform rt;
            public Image bg;
            public Color baseColor;
            public WeatherAnomaly weather;
        }

        private GameObject _root;
        private readonly List<Tile> _ring = new();
        private Image _centerIcon;
        private TextMeshProUGUI _centerName;
        private Button _acceptButton;
        private int _selected = -1;
        private bool _built;
        private bool _spinning;
        private bool _accepted;

        // ── Public entry ──────────────────────────────────────────────────────

        /// <summary>Animate to a tile carrying <paramref name="target"/>, reveal it, then call onComplete.</summary>
        public void Spin(WeatherAnomaly target, Action onComplete)
        {
            EnsureBuilt();
            if (_spinning) { onComplete?.Invoke(); return; }
            StartCoroutine(SpinRoutine(target, onComplete));
        }

        private IEnumerator SpinRoutine(WeatherAnomaly target, Action onComplete)
        {
            _spinning = true;
            _root.SetActive(true);

            int len = _ring.Count;
            int start = _selected < 0 ? 0 : _selected;
            int targetIdx = PickIndexFor(target);
            int forward = ((targetIdx - start) % len + len) % len;
            if (forward == 0) forward = len;               // always travel at least one lap's tail
            int total = SpinLoops * len + forward;

            for (int s = 1; s <= total; s++)
            {
                int idx = (start + s) % len;
                Select(idx);
                SetCenter(_ring[idx].weather, revealed: false);

                float p = (float)s / total;
                yield return new WaitForSeconds(Mathf.Lerp(MinStepInterval, MaxStepInterval, p * p));
            }

            Select(targetIdx);
            SetCenter(target, revealed: true);

            // Auto-ran to here with no input. The only interaction is Accept.
            yield return new WaitForSeconds(Mathf.Clamp(RevealHold, 0f, 1f));

            // The DropPuzzle scene locks the cursor (FPS carryover), which would make the
            // button un-clickable. Free the cursor while waiting, and also accept on
            // Space/Enter/click so it can never get stuck. Restore the cursor afterward.
            var prevLock = Cursor.lockState;
            bool prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _accepted = false;
            ShowAccept(true);
            Debug.Log("[WeatherWheel] Result ready — click ACCEPT or press Space.");
            while (!_accepted)
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetMouseButtonDown(0))
                    _accepted = true;
                yield return null;
            }
            ShowAccept(false);

            Cursor.lockState = prevLock;
            Cursor.visible = prevVisible;

            if (HideWhenDone) _root.SetActive(false);
            _spinning = false;
            onComplete?.Invoke();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void EnsureBuilt()
        {
            if (_built) return;
            GridSize = Mathf.Max(3, GridSize);

            var canvasGO = new GameObject("WeatherWheelCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 25;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Wheel");
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.SetParent(canvas.transform, false);
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            float step = CellSize + CellSpacing;
            float panel = GridSize * step;
            rootRt.sizeDelta = new Vector2(panel + 40f, panel + 40f);

            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.06f, 0.10f, 0.95f);

            var ringOutcomes = ResolveRingLayout();
            int n = GridSize;
            int ptr = 0;

            // Clockwise ring: top row → right col → bottom row → left col.
            var coords = new List<(int r, int c)>();
            for (int c = 0; c < n; c++)         coords.Add((0, c));
            for (int r = 1; r < n; r++)         coords.Add((r, n - 1));
            for (int c = n - 2; c >= 0; c--)    coords.Add((n - 1, c));
            for (int r = n - 2; r >= 1; r--)    coords.Add((r, 0));

            foreach (var (r, c) in coords)
            {
                var w = ringOutcomes[ptr % ringOutcomes.Length];
                ptr++;
                MakeTile(rootRt, r, c, step, w);
            }

            BuildCenter(rootRt, step);
            BuildAcceptButton(rootRt, step);
            _built = true;
            _root.SetActive(false); // hidden until Spin
        }

        private void MakeTile(RectTransform parent, int r, int c, float step, WeatherAnomaly w)
        {
            var go = new GameObject($"Tile_{r}_{c}");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(CellSize, CellSize);
            rt.anchoredPosition = new Vector2(
                (c - (GridSize - 1) / 2f) * step,
                ((GridSize - 1) / 2f - r) * step);

            var img = go.AddComponent<Image>();
            Color col = StyleColor(w);
            img.color = col;
            img.sprite = StyleIcon(w);
            if (img.sprite != null) img.preserveAspect = true;

            var lbl = MakeText(rt, "Label", 20, TextAlignmentOptions.Center);
            lbl.text = StyleLabel(w);
            lbl.color = new Color(1f, 1f, 1f, 0.9f);

            _ring.Add(new Tile { rt = rt, bg = img, baseColor = col, weather = w });
        }

        private void BuildCenter(RectTransform parent, float step)
        {
            int inner = GridSize - 2;
            var center = new GameObject("Center");
            var rt = center.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(inner * step + CellSpacing, inner * step + CellSpacing);
            var panelImg = center.AddComponent<Image>();
            panelImg.color = new Color(0.04f, 0.03f, 0.06f, 0.98f);

            var iconGO = new GameObject("Icon");
            var iconRt = iconGO.AddComponent<RectTransform>();
            iconRt.SetParent(rt, false);
            iconRt.anchorMin = new Vector2(0.2f, 0.35f); iconRt.anchorMax = new Vector2(0.8f, 0.9f);
            iconRt.offsetMin = iconRt.offsetMax = Vector2.zero;
            _centerIcon = iconGO.AddComponent<Image>();
            _centerIcon.preserveAspect = true;
            _centerIcon.color = new Color(1f, 1f, 1f, 0f);

            _centerName = MakeText(rt, "Name", 30, TextAlignmentOptions.Center);
            var nameRt = _centerName.rectTransform;
            nameRt.anchorMin = new Vector2(0.05f, 0.05f); nameRt.anchorMax = new Vector2(0.95f, 0.34f);
            nameRt.offsetMin = nameRt.offsetMax = Vector2.zero;
            _centerName.fontStyle = FontStyles.Bold;
            _centerName.enableAutoSizing = true;
            _centerName.fontSizeMin = 10f; _centerName.fontSizeMax = 40f;
            _centerName.text = "";
        }

        private void BuildAcceptButton(RectTransform parent, float step)
        {
            float panel = GridSize * step;
            var go = new GameObject("AcceptButton");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -(panel / 2f + 48f));
            rt.sizeDelta = new Vector2(240f, 74f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.30f, 0.75f, 0.42f, 1f);

            _acceptButton = go.AddComponent<Button>();
            _acceptButton.targetGraphic = img;
            _acceptButton.onClick.AddListener(() => _accepted = true);

            var label = MakeText(rt, "Label", 26, TextAlignmentOptions.Center);
            label.text = "ACCEPT  (Space)";
            label.fontStyle = FontStyles.Bold;

            go.SetActive(false); // shown only after the reveal
        }

        private void ShowAccept(bool on)
        {
            if (_acceptButton != null) _acceptButton.gameObject.SetActive(on);
        }

        // ── Selection / center ────────────────────────────────────────────────

        private void Select(int idx)
        {
            if (_selected >= 0 && _selected < _ring.Count)
            {
                var prev = _ring[_selected];
                prev.rt.localScale = Vector3.one;
                prev.bg.color = prev.baseColor;
            }
            if (idx >= 0 && idx < _ring.Count)
            {
                var t = _ring[idx];
                t.rt.localScale = Vector3.one * 1.15f;
                t.bg.color = Color.Lerp(t.baseColor, Color.white, 0.5f);
            }
            _selected = idx;
        }

        private void SetCenter(WeatherAnomaly w, bool revealed)
        {
            if (_centerName == null) return;
            _centerName.text  = OutcomeName(w);
            _centerName.color = revealed ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.7f);
            if (_centerIcon != null)
            {
                var sprite = StyleIcon(w);
                _centerIcon.sprite = sprite;
                _centerIcon.color = sprite != null
                    ? new Color(1f, 1f, 1f, revealed ? 1f : 0.4f)
                    : new Color(StyleColor(w).r, StyleColor(w).g, StyleColor(w).b, revealed ? 1f : 0.3f);
            }
        }

        // ── Data helpers ──────────────────────────────────────────────────────

        private WeatherAnomaly[] ResolveRingLayout()
        {
            if (RingLayout != null && RingLayout.Length >= 4) return RingLayout;

            int count = 4 * (GridSize - 1);
            var ring = new WeatherAnomaly[count];
            for (int i = 0; i < count; i++) ring[i] = WeatherAnomaly.Clear;

            // Sprinkle anomalies around the ring, spaced out, so each appears.
            var anomalies = new[] { WeatherAnomaly.Radiation, WeatherAnomaly.Fallout, WeatherAnomaly.Thunderstorm };
            int a = 0;
            for (int i = 1; i < count; i += 3)
            {
                ring[i] = anomalies[a % anomalies.Length];
                a++;
            }
            return ring;
        }

        private int PickIndexFor(WeatherAnomaly target)
        {
            var matches = new List<int>();
            for (int i = 0; i < _ring.Count; i++)
                if (_ring[i].weather == target) matches.Add(i);
            if (matches.Count > 0) return matches[UnityEngine.Random.Range(0, matches.Count)];
            return 0; // fallback (shouldn't happen — Clear is always present)
        }

        private WeatherStyle FindStyle(WeatherAnomaly w)
        {
            if (Styles != null)
                foreach (var s in Styles)
                    if (s != null && s.Anomaly == w) return s;
            return null;
        }

        private Color StyleColor(WeatherAnomaly w)
        {
            var s = FindStyle(w);
            return s != null ? s.Color : WeatherAnomalyRoller.ThemeColor(w) * new Color(1f, 1f, 1f, 1f);
        }

        private Sprite StyleIcon(WeatherAnomaly w) => FindStyle(w)?.Icon;

        private string StyleLabel(WeatherAnomaly w)
        {
            var s = FindStyle(w);
            if (s != null && !string.IsNullOrEmpty(s.Label)) return s.Label;
            return w switch
            {
                WeatherAnomaly.Radiation    => "RAD",
                WeatherAnomaly.Fallout      => "FALL",
                WeatherAnomaly.Thunderstorm => "STORM",
                _                           => ""
            };
        }

        private static string OutcomeName(WeatherAnomaly w)
            => w == WeatherAnomaly.Clear ? "Normal Day" : WeatherAnomalyRoller.DisplayName(w);

        private static TextMeshProUGUI MakeText(RectTransform parent, string name,
            float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size; t.alignment = align; t.raycastTarget = false;
            t.color = Color.white;
            return t;
        }
    }
}
