using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Assigns real, textured PBR materials to the drop board on load — wet crypt stone
    /// on the inner walls, iron on the outer frame — plus a cold, near-black background.
    /// This is material-driven (not flat colour tints), so walls read as actual surfaces
    /// with grain, depth and specular. Attach to the PuzzlePrefabLoader object.
    /// </summary>
    public class DropPuzzleTheme : MonoBehaviour
    {
        [Header("Wall materials (drag dungeon stone / iron here)")]
        [Tooltip("Applied to the inner puzzle walls (the deflectors)")]
        public Material WallMaterial;
        [Tooltip("Applied to the generated outer side walls (Wall_Left / Wall_Right)")]
        public Material FrameMaterial;

        [Header("Background")]
        public bool SetBackground = true;
        [Tooltip("Cold black-blue crypt background")]
        public Color BackgroundColor = new Color(0.030f, 0.040f, 0.060f);

        private void OnEnable()  { PuzzlePrefabLoader.OnPuzzleLoaded += Apply; }
        private void OnDisable() { PuzzlePrefabLoader.OnPuzzleLoaded -= Apply; }

        private void Start()
        {
            if (PuzzlePrefabLoader.IsPuzzleReady) Apply();
        }

        [ContextMenu("Apply Theme Now")]
        public void Apply()
        {
            var walls = GameObject.FindGameObjectsWithTag("Wall");
            int inner = 0, frame = 0;

            foreach (var w in walls)
            {
                var r = w.GetComponent<Renderer>();
                if (r == null) r = w.GetComponentInChildren<Renderer>();
                if (r == null) continue;

                // Generated side walls are named "Wall_Left"/"Wall_Right" → iron frame.
                // Everything else (inner deflectors) → wet crypt stone.
                if (w.name.StartsWith("Wall_"))
                {
                    if (FrameMaterial != null) { r.sharedMaterial = FrameMaterial; frame++; }
                }
                else
                {
                    if (WallMaterial != null) { r.sharedMaterial = WallMaterial; inner++; }
                }
            }

            if (SetBackground)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = BackgroundColor;
                }
            }

            Debug.Log($"[DropPuzzleTheme] Applied stone to {inner} inner walls, iron to {frame} frame walls.");
        }
    }
}
