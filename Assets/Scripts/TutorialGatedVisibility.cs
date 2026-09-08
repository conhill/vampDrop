using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Hides this object (renderers, colliders, canvases) until the tutorial is finished,
    /// so only Snerd is visible in the house during the tutorial. Put it on the other
    /// vendors/shops (e.g. FinkBuyZone, VorkinBuyZone). Self-contained — no cross-scene
    /// references — it reads TutorialManager state on load and listens for completion.
    /// </summary>
    public class TutorialGatedVisibility : MonoBehaviour
    {
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private Canvas[]   _canvases;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _canvases  = GetComponentsInChildren<Canvas>(true);
        }

        private void OnEnable()
        {
            TutorialManager.OnTutorialComplete += Show;
            Apply();
        }

        private void OnDisable()
        {
            TutorialManager.OnTutorialComplete -= Show;
        }

        private void Apply()
        {
            // Unlocked once the tutorial is no longer active (completed OR skipped).
            var tm = TutorialManager.Instance;
            bool unlocked = tm == null || !tm.tutorialActive;
            SetVisible(unlocked);
        }

        private void Show() => SetVisible(true);

        private void SetVisible(bool v)
        {
            if (_renderers != null) foreach (var r in _renderers) if (r) r.enabled = v;
            if (_colliders != null) foreach (var c in _colliders) if (c) c.enabled = v;
            if (_canvases  != null) foreach (var c in _canvases)  if (c) c.enabled = v;
        }
    }
}
