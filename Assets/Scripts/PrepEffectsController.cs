using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// The final step of the Preparation phase: after the gate roll and weather roll have
    /// landed their results, this plays whatever animations those results trigger, and only
    /// signals complete once they finish — so the drop stays locked until the board is set.
    ///
    /// Right now it plays a themed full-screen flash for the rolled weather (a visible,
    /// correctly-gated placeholder). This is the hook for real weather board-effects later:
    /// subscribe to <see cref="OnEffectStarted"/> to drive lightning, fallout tint, etc.,
    /// or extend <see cref="PlayRoutine"/>. Whatever you add here, keep the onComplete
    /// contract — DropPuzzleFlowManager waits on it before unlocking the drop.
    ///
    /// Add to a GameObject in the DropPuzzle scene. If absent, the flow skips straight from
    /// the weather roll to player control.
    /// </summary>
    public class PrepEffectsController : MonoBehaviour
    {
        [Tooltip("Full-screen flash duration for the weather result (seconds).")]
        public float FlashDuration = 0.9f;
        [Tooltip("Peak opacity of the weather flash.")]
        [Range(0f, 1f)] public float FlashPeakAlpha = 0.5f;
        [Tooltip("Clear skies has no anomaly — skip the flash and continue instantly.")]
        public bool SkipOnClear = true;

        /// <summary>Fired when result effects begin — hook real board-effects here.</summary>
        public static event Action<WeatherAnomaly> OnEffectStarted;

        private Canvas _canvas;
        private Image  _flash;

        /// <summary>
        /// Play the result animations for the rolled weather, then invoke onComplete.
        /// Always calls onComplete exactly once (even on the Clear/instant path).
        /// </summary>
        public void Play(WeatherAnomaly anomaly, Action onComplete)
        {
            OnEffectStarted?.Invoke(anomaly);
            Debug.Log($"[PrepEffects] Result animation for {WeatherAnomalyRoller.DisplayName(anomaly)}.");

            if (anomaly == WeatherAnomaly.Clear && SkipOnClear)
            {
                onComplete?.Invoke();
                return;
            }

            StartCoroutine(PlayRoutine(anomaly, onComplete));
        }

        private IEnumerator PlayRoutine(WeatherAnomaly anomaly, Action onComplete)
        {
            EnsureUI();

            Color tint = WeatherAnomalyRoller.ThemeColor(anomaly);
            float half = Mathf.Max(0.01f, FlashDuration * 0.5f);

            // Flash up…
            float t = 0f;
            while (t < half)
            {
                SetFlash(tint, Mathf.Lerp(0f, FlashPeakAlpha, t / half));
                t += Time.deltaTime;
                yield return null;
            }
            // …and back down.
            t = 0f;
            while (t < half)
            {
                SetFlash(tint, Mathf.Lerp(FlashPeakAlpha, 0f, t / half));
                t += Time.deltaTime;
                yield return null;
            }
            SetFlash(tint, 0f);

            onComplete?.Invoke();
        }

        private void SetFlash(Color rgb, float a)
        {
            if (_flash != null) _flash.color = new Color(rgb.r, rgb.g, rgb.b, a);
        }

        private void EnsureUI()
        {
            if (_canvas != null) return;

            var go = new GameObject("PrepEffectsCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30; // above HUD, below nothing important
            go.AddComponent<CanvasScaler>();

            var flashGO = new GameObject("Flash");
            var rt = flashGO.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _flash = flashGO.AddComponent<Image>();
            _flash.raycastTarget = false; // never block clicks
            _flash.color = new Color(0f, 0f, 0f, 0f);
        }
    }
}
