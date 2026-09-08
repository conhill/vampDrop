using System.Collections.Generic;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Reskins the drop board as a post-apocalyptic town: the left/right edges become
    /// building facades framing an alley, and a backdrop stands in for the town behind
    /// the drop channel. Balls fall down the street between the buildings.
    ///
    /// Everything is greyboxed procedurally so it works with ZERO art assigned — assign
    /// facade Materials and a backdrop Sprite per tier to drop in real assets later.
    ///
    /// "The town thrives from your contributions": pick a <see cref="Tiers"/> entry from
    /// player progress, so the buildings go from ruined → rebuilt as the run count / bank
    /// grows. Swap tier live with <see cref="SetTier"/> (or the context-menu cycler).
    ///
    /// Attach to the same object as PuzzlePrefabLoader. Keys off
    /// <see cref="PuzzlePrefabLoader.OnBoardAligned"/> so wall positions are final.
    /// If you use this, disable DropPuzzleTheme to avoid it fighting over wall materials
    /// and the camera background.
    /// </summary>
    public class TownSkinController : MonoBehaviour
    {
        [System.Serializable]
        public class TownTier
        {
            public string Name = "Ruined";

            [Header("Building facades (left/right edges)")]
            [Tooltip("PBR facade material (brick / concrete). Overrides FacadeColor when set.")]
            public Material FacadeMaterial;
            public Color FacadeColor = new Color(0.20f, 0.19f, 0.18f);

            [Header("Town backdrop (behind the drop channel)")]
            [Tooltip("Skyline / ruins image. Overrides BackdropColor when set.")]
            public Sprite BackdropSprite;
            public Color BackdropColor = new Color(0.12f, 0.11f, 0.13f);

            [Header("Ambience")]
            public Color CameraBackground = new Color(0.05f, 0.05f, 0.08f);
        }

        [Header("Tiers (ruined → thriving)")]
        [Tooltip("Ordered worst → best. Higher tiers show a more rebuilt town.")]
        public TownTier[] Tiers = new TownTier[0];

        public enum TierSource { Manual, TotalRunsCompleted, TotalCurrency }

        [Header("Tier Selection")]
        public TierSource Source = TierSource.Manual;
        [Tooltip("Used when Source is Manual (or no PlayerDataManager is present).")]
        public int ManualTier = 0;
        [Tooltip("Ascending stat thresholds. Tier = how many thresholds the stat meets.")]
        public int[] Thresholds = { 3, 8, 15 };

        [Header("Facade Layout")]
        [Tooltip("Leave OFF when TownWallBuilder dresses the edges — it owns the buildings. " +
                 "This is the simple greybox facade fallback for when there's no wall kit.")]
        public bool BuildFacades = false;
        [Tooltip("World-unit width of each building facade at the screen edge.")]
        public float FacadeWidth = 0.8f;
        [Tooltip("Facade thickness in Z (kept slim — this is a flat side wall).")]
        public float FacadeDepth = 0.2f;
        [Tooltip("Extra height beyond the camera frame so facades never show a seam.")]
        public float FacadeOverhang = 2f;

        [Header("Backdrop Layout")]
        public bool BuildBackdrop = true;
        [Tooltip("Z depth of the backdrop. More negative = further behind the play field.")]
        public float BackdropZ = -20f;
        [Tooltip("Backdrop is oversized by this factor so edges never peek in.")]
        public float BackdropPadding = 1.15f;

        private GameObject _skinRoot;
        private PuzzlePrefabLoader.BoardFrame _lastFrame;
        private bool _hasFrame;

        private void OnEnable()  { PuzzlePrefabLoader.OnBoardAligned += Rebuild; }
        private void OnDisable() { PuzzlePrefabLoader.OnBoardAligned -= Rebuild; }

        // ── Build ─────────────────────────────────────────────────────────────

        private void Rebuild(PuzzlePrefabLoader.BoardFrame frame)
        {
            _lastFrame = frame;
            _hasFrame  = true;

            if (_skinRoot != null) Destroy(_skinRoot);
            _skinRoot = new GameObject("TownSkin");
            _skinRoot.transform.SetParent(transform, false);

            TownTier tier = ResolveTier();

            if (BuildBackdrop) BuildBackdropObject(frame, tier);
            if (BuildFacades)  BuildFacadeObjects(frame, tier);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags      = CameraClearFlags.SolidColor;
                cam.backgroundColor = tier.CameraBackground;
            }

            Debug.Log($"[TownSkin] Built tier '{tier.Name}' " +
                      $"(backdrop={BuildBackdrop}, facades={BuildFacades}).");
        }

        private void BuildBackdropObject(PuzzlePrefabLoader.BoardFrame f, TownTier tier)
        {
            // BoardFrame's OrthoSize is the half-height at the BOARD plane (Z=0). Under an
            // orthographic camera that half-height holds at every depth, but under perspective
            // the frame widens with distance — so a backdrop parked at BackdropZ has to be
            // scaled by the ratio of their distances or its edges show inside the frame.
            float depthScale = 1f;
            var cam = Camera.main;
            if (cam != null && !cam.orthographic)
            {
                float boardDist = Mathf.Abs(cam.transform.position.z);
                float backDist  = Mathf.Abs(cam.transform.position.z - BackdropZ);
                if (boardDist > 0.0001f) depthScale = backDist / boardDist;
            }

            float w = f.OrthoSize * f.Aspect * 2f * BackdropPadding * depthScale;
            float h = f.OrthoSize * 2f * BackdropPadding * depthScale;
            var pos = new Vector3(f.CenterX, f.CenterY, BackdropZ);

            if (tier.BackdropSprite != null)
            {
                var go = new GameObject("Town_Backdrop");
                go.transform.SetParent(_skinRoot.transform, false);
                go.transform.position = pos;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = tier.BackdropSprite;
                sr.sortingOrder = -200;

                var size = tier.BackdropSprite.bounds.size;
                if (size.x > 0.0001f && size.y > 0.0001f)
                    go.transform.localScale = new Vector3(w / size.x, h / size.y, 1f);
            }
            else
            {
                var go = MakeFlatCube("Town_Backdrop", pos, new Vector3(w, h, 0.1f));
                Tint(go, tier.BackdropColor, null);
            }
        }

        private void BuildFacadeObjects(PuzzlePrefabLoader.BoardFrame f, TownTier tier)
        {
            float height = f.OrthoSize * 2f + FacadeOverhang * 2f;
            var scale = new Vector3(FacadeWidth, height, FacadeDepth);

            if (f.LeftWall != null)
            {
                var l = MakeFlatCube("Facade_Left",
                    new Vector3(f.LeftWall.position.x, f.CenterY, f.LeftWall.position.z), scale);
                Tint(l, tier.FacadeColor, tier.FacadeMaterial);
            }
            if (f.RightWall != null)
            {
                var r = MakeFlatCube("Facade_Right",
                    new Vector3(f.RightWall.position.x, f.CenterY, f.RightWall.position.z), scale);
                Tint(r, tier.FacadeColor, tier.FacadeMaterial);
            }
        }

        // ── Tier resolution ───────────────────────────────────────────────────

        private TownTier ResolveTier()
        {
            if (Tiers == null || Tiers.Length == 0)
                return new TownTier(); // greybox default

            return Tiers[Mathf.Clamp(ComputeTierIndex(), 0, Tiers.Length - 1)];
        }

        private int ComputeTierIndex()
        {
            var pdm = PlayerDataManager.Instance;
            if (Source == TierSource.Manual || pdm == null) return ManualTier;

            int stat = Source == TierSource.TotalCurrency
                ? pdm.TotalCurrency
                : pdm.TotalRunsCompleted;

            int tier = 0;
            if (Thresholds != null)
                foreach (int th in Thresholds)
                    if (stat >= th) tier++;
            return tier;
        }

        // ── Runtime control ───────────────────────────────────────────────────

        /// <summary>Force a tier and rebuild against the last aligned frame.</summary>
        public void SetTier(int index)
        {
            Source     = TierSource.Manual;
            ManualTier = index;
            if (_hasFrame) Rebuild(_lastFrame);
        }

        [ContextMenu("Cycle Tier")]
        private void CycleTier()
        {
            int count = Mathf.Max(1, Tiers?.Length ?? 1);
            SetTier((ManualTier + 1) % count);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private GameObject MakeFlatCube(string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_skinRoot.transform, false);
            go.transform.position   = pos;
            go.transform.localScale = scale;

            // Purely decorative — never interfere with ball physics or gate triggers.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return go;
        }

        private static void Tint(GameObject go, Color color, Material overrideMat)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;

            if (overrideMat != null) { r.sharedMaterial = overrideMat; return; }

            var mat = r.material; // instance — safe to mutate
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        }
    }
}
