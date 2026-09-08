using System.Collections.Generic;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Builds the drop-puzzle bounding walls out of stacked building modules from
    /// /RiceballKit. Each edge becomes a building: a Door module at the base, random
    /// body modules stacked up the height, and a Roof module as the cap.
    ///
    /// Module heights are measured from their real bounds at runtime, so pieces tile
    /// seam-free no matter how the FBX is scaled. The stacks are purely visual — the
    /// existing invisible edge-wall cubes (Wall_Left / Wall_Right) remain the physics
    /// boundary, so ball bouncing is unchanged. Their renderers are hidden so only the
    /// modules show.
    ///
    /// Attach to the PuzzlePrefabLoader object. Keys off
    /// <see cref="PuzzlePrefabLoader.OnBoardAligned"/> so wall positions are final.
    /// If you also run TownSkinController, set its BuildFacades = false (keep its
    /// backdrop) so the two don't both dress the edges.
    ///
    /// Editor: right-click → "Auto-Populate From RiceballKit" to sort the Village kit
    /// into Bottom / Body / Roof by filename (Door → bottom, Roof → top, else body).
    /// </summary>
    public class TownWallBuilder : MonoBehaviour
    {
        [Header("Modules (populate via context menu or by hand)")]
        [Tooltip("Placed once at the base of each building. Kit token '_B_' (e.g. Village_B_Door).")]
        public GameObject[] BottomModules = new GameObject[0];
        [Tooltip("Stacked to fill the height. Kit token '_M_' (e.g. Village_M_Plain).")]
        public GameObject[] BodyModules = new GameObject[0];
        [Tooltip("Caps the top of each building. Kit token '_R_' (e.g. Village_R_Steep).")]
        public GameObject[] RoofModules = new GameObject[0];

        [Header("Placement")]
        [Tooltip("Uniform scale applied to every module before stacking.")]
        public Vector3 ModuleScale = Vector3.one;
        [Tooltip("Base rotation (Euler XYZ) applied to every module. Use this to un-flip " +
                 "the kit. Blender→FBX often needs X=-90; face the camera with Y=90/180.")]
        public Vector3 ModuleRotation = Vector3.zero;
        [Tooltip("Extra Y rotation for the RIGHT building so it mirrors the left.")]
        public float RightSideYRotation = 180f;
        [Tooltip("Z offset from the wall plane (push modules behind the play field).")]
        public float ZOffset = 0f;
        [Tooltip("Align each module by its own pivot (ON) so asymmetric pieces like Balcony " +
                 "keep their wall face on the same line. Turn OFF to center by bounds instead.")]
        public bool AlignHorizontalByPivot = true;
        [Tooltip("Pulls BOTH buildings toward the screen center by this many world units " +
                 "(mirrored per side). Use it when kit pivots sit at the wall edge and push " +
                 "the stacks out of view.")]
        public float InwardXOffset = 0f;
        [Tooltip("Extend the base below the camera bottom so no gap shows.")]
        public float BottomExtend = 1f;
        [Tooltip("Allow the roof to rise this far above the camera top.")]
        public float TopExtend = 1f;

        [Header("Behaviour")]
        [Tooltip("Hide the grey placeholder edge walls (keeps their colliders).")]
        public bool HidePlaceholderWalls = true;
        [Tooltip("Optional fixed seed for the body sequence. -1 = random each build.")]
        public int RandomSeed = -1;

        // Editor auto-populate settings (harmless at runtime).
        [Header("Auto-Populate (editor)")]
        public string KitFolder = "Assets/RiceballKit";
        [Tooltip("Only modules whose filename contains this are loaded. e.g. Village.")]
        public string SetFilter = "Village";

        private GameObject _root;
        private int _placed;
        private PuzzlePrefabLoader.BoardFrame _lastFrame;
        private bool _hasFrame;

        private void OnEnable()  { PuzzlePrefabLoader.OnBoardAligned += Build; }
        private void OnDisable() { PuzzlePrefabLoader.OnBoardAligned -= Build; }

        // Re-run the last build — lets you tune rotation / scale / offsets in Play
        // without reloading the scene.
        [ContextMenu("Rebuild")]
        private void Rebuild()
        {
            if (_hasFrame) Build(_lastFrame);
            else Debug.LogWarning("[TownWallBuilder] No board frame yet — enter Play once so the board aligns.");
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void Build(PuzzlePrefabLoader.BoardFrame frame)
        {
            _lastFrame = frame;
            _hasFrame  = true;

            if (_root != null) Destroy(_root);
            _root = new GameObject("TownWalls");
            _root.transform.SetParent(transform, false);
            _placed = 0;

            Debug.Log($"[TownWallBuilder] Build() fired — pools: bottom={CountValid(BottomModules)}, " +
                      $"body={CountValid(BodyModules)}, roof={CountValid(RoofModules)}. " +
                      $"leftWall={(frame.LeftWall != null ? frame.LeftWall.position.ToString("F1") : "NULL")}, " +
                      $"rightWall={(frame.RightWall != null ? frame.RightWall.position.ToString("F1") : "NULL")}");

            if (CountValid(BottomModules) == 0 && CountValid(BodyModules) == 0 && CountValid(RoofModules) == 0)
            {
                Debug.LogWarning("[TownWallBuilder] No modules assigned (all slots empty or missing). " +
                                 "Re-run 'Auto-Populate From RiceballKit' — references likely broke on re-import.");
                return;
            }

            if (frame.LeftWall == null && frame.RightWall == null)
            {
                Debug.LogWarning("[TownWallBuilder] BoardFrame has no wall transforms — nothing to anchor to.");
                return;
            }

            if (RandomSeed >= 0) Random.InitState(RandomSeed);

            float bottomY = frame.BottomY - BottomExtend;
            float topY    = frame.CenterY + frame.OrthoSize + TopExtend;

            Quaternion leftRot  = Quaternion.Euler(ModuleRotation);
            Quaternion rightRot = Quaternion.Euler(ModuleRotation + new Vector3(0f, RightSideYRotation, 0f));

            // Left pulls in +X (rightward), right pulls in -X (leftward) — both toward center.
            if (frame.LeftWall != null)
                BuildColumn(frame.LeftWall, bottomY, topY, leftRot, InwardXOffset);
            if (frame.RightWall != null)
                BuildColumn(frame.RightWall, bottomY, topY, rightRot, -InwardXOffset);

            if (HidePlaceholderWalls)
            {
                HideRenderer(frame.LeftWall);
                HideRenderer(frame.RightWall);
            }

            Debug.Log($"[TownWallBuilder] Done — placed {_placed} modules, fill Y {bottomY:F1}..{topY:F1}.");
        }

        private void BuildColumn(Transform wall, float bottomY, float topY, Quaternion rot, float xOffset)
        {
            float wallX = wall.position.x + xOffset;
            float z     = wall.position.z + ZOffset;
            float cursor = bottomY;

            // 1. Door / base piece.
            var door = Pick(BottomModules);
            if (door != null)
                cursor += Place(door, wallX, z, cursor, rot);

            // 2. Reserve room for the roof so it caps cleanly at the top.
            var roof = Pick(RoofModules);
            float roofH = roof != null ? MeasureHeight(roof, rot) : 0f;
            float bodyCeiling = topY - roofH;

            // 3. Stack body modules until the next one would breach the roof line.
            int guard = 0;
            while (cursor < bodyCeiling && guard++ < 64)
            {
                var body = Pick(BodyModules);
                if (body == null) break;

                float h = MeasureHeight(body, rot);
                if (h <= 0.0001f) break;                       // degenerate — avoid infinite loop
                if (cursor + h > bodyCeiling && cursor > bottomY) break; // don't overshoot into the roof

                cursor += Place(body, wallX, z, cursor, rot);
            }

            // 4. Roof cap sits directly on the last piece — no gap.
            if (roof != null)
                Place(roof, wallX, z, cursor, rot);
        }

        // Instantiates a module, strips colliders, and positions it bottom-anchored at
        // bottomY. Horizontally it aligns by the module's own pivot (default) so asymmetric
        // pieces — balconies, awnings, downspouts — keep their wall face on the same line as
        // their neighbours; a protruding balcony would otherwise skew a bounds-center anchor.
        // Returns the module height.
        private float Place(GameObject prefab, float wallX, float z, float bottomY, Quaternion rot)
        {
            var go = Instantiate(prefab, _root.transform);
            go.transform.localScale = ModuleScale;
            go.transform.rotation   = rot;

            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Destroy(col);

            var b = WorldBounds(go);
            Vector3 p = go.transform.position;
            if (AlignHorizontalByPivot)
            {
                // Anchor the module's pivot to the wall line — consistent kit pivots keep
                // every piece's face aligned regardless of what sticks out.
                p.x = wallX;
                p.z = z;
            }
            else
            {
                p.x += wallX - b.center.x;
                p.z += z     - b.center.z;
            }
            p.y += bottomY - b.min.y; // bottom-anchor by bounds so pieces tile with no gaps
            go.transform.position = p;

            _placed++;
            if (b.size.y < 0.001f)
                Debug.LogWarning($"[TownWallBuilder] '{prefab.name}' has ~zero bounds height " +
                                 $"(size={b.size}). No renderers, or scale is too small — it won't be visible.");

            return b.size.y;
        }

        // Height a module would occupy at the given rotation/scale (temp instance).
        private float MeasureHeight(GameObject prefab, Quaternion rot)
        {
            var temp = Instantiate(prefab);
            temp.transform.localScale = ModuleScale;
            temp.transform.rotation   = rot;
            float h = WorldBounds(temp).size.y;
            Destroy(temp);
            return h;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        private static GameObject Pick(GameObject[] pool)
        {
            if (pool == null || pool.Length == 0) return null;
            // Compact out nulls so an empty slot doesn't waste a pick.
            int valid = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i] != null) valid++;
            if (valid == 0) return null;
            int target = Random.Range(0, valid);
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && target-- == 0) return pool[i];
            return null;
        }

        private static void HideRenderer(Transform wall)
        {
            if (wall == null) return;
            // Wall.prefab keeps its Renderer on a child, so hide the whole subtree.
            foreach (var r in wall.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }

        private static int CountValid(GameObject[] pool)
        {
            if (pool == null) return 0;
            int n = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i] != null) n++;
            return n;
        }

        private enum Category { Bottom, Body, Roof }

        // Classifies a module by the kit's B/M/R token (bottom/middle/roof), e.g.
        // "Village_B_Door" → Bottom, "Village_M_Plain" → Body, "Village_R_Steep" → Roof.
        // Falls back to the older keyword naming (Door → bottom, Roof → roof).
        private static Category Classify(string name)
        {
            foreach (var token in name.Split('_', '-', ' '))
            {
                if (token.Equals("B", System.StringComparison.OrdinalIgnoreCase)) return Category.Bottom;
                if (token.Equals("R", System.StringComparison.OrdinalIgnoreCase)) return Category.Roof;
                if (token.Equals("M", System.StringComparison.OrdinalIgnoreCase)) return Category.Body;
            }
            if (name.IndexOf("Door", System.StringComparison.OrdinalIgnoreCase) >= 0) return Category.Bottom;
            if (name.IndexOf("Roof", System.StringComparison.OrdinalIgnoreCase) >= 0) return Category.Roof;
            return Category.Body;
        }

#if UNITY_EDITOR
        [ContextMenu("Auto-Populate From RiceballKit")]
        private void AutoPopulate()
        {
            var bottom = new List<GameObject>();
            var body   = new List<GameObject>();
            var roof   = new List<GameObject>();

            var guids = UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { KitFolder });
            foreach (var guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(SetFilter) &&
                    name.IndexOf(SetFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                switch (Classify(name))
                {
                    case Category.Bottom: bottom.Add(go); break;
                    case Category.Roof:   roof.Add(go);   break;
                    default:              body.Add(go);   break;
                }
            }

            BottomModules = bottom.ToArray();
            BodyModules   = body.ToArray();
            RoofModules   = roof.ToArray();

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[TownWallBuilder] Populated '{SetFilter}': " +
                      $"{bottom.Count} bottom, {body.Count} body, {roof.Count} roof.");
        }
#endif
    }
}
