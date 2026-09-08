using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;
using Vampire.DropPuzzle;

namespace Vampire.DropPuzzle.EditorTools
{
    /// <summary>
    /// Builds playable multiplier-gate prefabs from the raw Gate_Tier_*.fbx models.
    ///
    /// The gate models share a naming convention across all three tiers, which is what makes
    /// this automatable: parts whose name contains "Post" are the side supports, everything
    /// else (banner, sign panel, neon, chain, crossbar, brackets) hangs in the middle. So:
    ///
    ///   • Post parts  → solid BoxCollider, tagged "Wall". Rice balls bounce off them.
    ///   • Everything else → NO collider. Balls pass straight through the banner.
    ///   • The gap between the posts → one trigger volume carrying MultiplierGate.
    ///     Reaching the middle IS the bonus.
    ///
    /// Menu: Tools ▸ VAMP4 ▸ Build Multiplier Gate Prefabs
    ///
    /// Re-running overwrites the generated prefabs, so re-export the FBX and rebuild whenever
    /// the gate art changes.
    /// </summary>
    public static class GatePrefabBuilder
    {
        private const string SourceFolder = "Assets/DropPuzzleAssets";
        private const string OutputFolder = "Assets/Prefabs/Gates";

        // Parts matching this become solid colliders. Covers Post, PostCap, PostFinial.
        private const string SolidToken = "Post";

        // Depth of the generated bonus trigger. Balls travel on the Z = 0 plane, and the board
        // pieces are 4 units deep, so this spans the play plane with room to spare.
        private const float TriggerDepth = 4f;

        // Y rotation applied to the model so its signage faces the board camera. Keep this a
        // multiple of 90 — arbitrary angles shear under the marker's non-uniform fit.
        private const float ModelYaw = 180f;

        // How far in front of the sign the number sits. Small — just enough to avoid z-fighting
        // with the banner face.
        private const float LabelForwardOffset = 0.12f;

        [MenuItem("Tools/VAMP4/Build Multiplier Gate Prefabs")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                    AssetDatabase.CreateFolder("Assets", "Prefabs");
                AssetDatabase.CreateFolder("Assets/Prefabs", "Gates");
            }

            // Reuse the RiceBallPrefab already wired on the existing placeholder gate so the
            // legacy pooled-spawn path keeps working on the new prefabs.
            GameObject riceBall = null;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Multiplier2x.prefab");
            if (existing != null)
            {
                var mg = existing.GetComponentInChildren<MultiplierGate>(true);
                if (mg != null) riceBall = mg.RiceBallPrefab;
            }
            if (riceBall == null)
                Debug.LogWarning("[GatePrefabBuilder] Could not read RiceBallPrefab from " +
                                 "Multiplier2x.prefab — assign it on the generated prefabs by hand.");

            var built = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { SourceFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("Gate_Tier_")) continue;

                string outPath = Build(path, name, riceBall);
                if (outPath != null) built.Add(outPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (built.Count == 0)
                Debug.LogWarning("[GatePrefabBuilder] No Gate_Tier_* models found under " + SourceFolder);
            else
                Debug.Log($"[GatePrefabBuilder] Built {built.Count} gate prefab(s):\n  " +
                          string.Join("\n  ", built));
        }

        private static string Build(string fbxPath, string name, GameObject riceBall)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null) { Debug.LogError("[GatePrefabBuilder] Can't load " + fbxPath); return null; }

            // Root wrapper. The FBX goes underneath with its transform UNTOUCHED — the importer
            // bakes the Blender Z-up→Y-up conversion into the model root's own rotation, and
            // overwriting it drops the model onto the wrong axis.
            var root = new GameObject(name + "_Gate");

            // Intermediate carries the facing correction. The banner and sign panels are
            // zero-thickness single-sided planes, so with the model's authored front pointing
            // away from the board camera they cull to nothing and the gate reads as "reversed
            // with a missing banner". 180 is a multiple of 90, so it only permutes axes and
            // can't shear under the marker fit.
            var pivot = new GameObject("ModelPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(pivot.transform, false);

            int solids = 0;
            var postBounds = new List<Bounds>();

            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;

                bool isSolid = mf.gameObject.name.IndexOf(SolidToken, System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isSolid) continue;

                // BoxCollider auto-fits the MeshFilter's bounds when added.
                var col = Undo.AddComponent<BoxCollider>(mf.gameObject);
                col.isTrigger = false;

                // RiceBallWallCollisionSystem looks up GameObject.FindGameObjectsWithTag("Wall")
                // and then GetComponent<BoxCollider>() on that same object — so the tag and the
                // collider have to live together on the part itself.
                mf.gameObject.tag = "Wall";

                postBounds.Add(WorldBounds(mf));
                solids++;
            }

            if (solids == 0)
                Debug.LogWarning($"[GatePrefabBuilder] {name}: no parts matched '{SolidToken}' — " +
                                 "the gate will have no physics at all.");

            // Gap between the innermost post faces = the scoring lane.
            Bounds gap = ComputeGap(postBounds, model.transform);

            // Trigger + MultiplierGate go on the ROOT, not a child. PuzzlePropDresser skips
            // anything with a MultiplierGate above it, and GetComponentInParent only walks
            // ancestors — on a child object the posts would never see it and the dresser would
            // hide the gate and stack a prop on every post.
            var tc = root.AddComponent<BoxCollider>();
            tc.isTrigger = true;
            tc.center = gap.center - root.transform.position;
            tc.size   = new Vector3(gap.size.x, gap.size.y, TriggerDepth);

            var gate = root.AddComponent<MultiplierGate>();
            gate.Multiplier    = 2;
            gate.RiceBallPrefab = riceBall;

            var trigger = root;   // label anchors to the same object

            // Number label. Driven at runtime so one prefab serves x2/x3/x4/x5 — baking digits
            // into the mesh would need a separate model per value, and PuzzleEnhancer already
            // falls back to the 3x prefab for a 4x roll because of exactly that.
            // Sit the label just proud of the signage, NOT at the trigger's front face. The
            // trigger is deliberately 4 units deep to span the ball plane, so offsetting by
            // half of that would park the number ~2 units toward the camera — visibly detached
            // from the banner and inflating the gate's bounds.
            var labelGO = new GameObject("MultiplierLabel");
            labelGO.transform.SetParent(trigger.transform, false);
            labelGO.transform.localPosition = tc.center + new Vector3(0f, 0f, -LabelForwardOffset);

            var tmp = labelGO.AddComponent<TextMeshPro>();
            tmp.text      = "x2";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 24f;
            tmp.rectTransform.sizeDelta = new Vector2(gap.size.x * 0.9f, gap.size.y * 0.6f);

            gate.MultiplierText = tmp;

            string outPath = $"{OutputFolder}/{name}_Gate.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, outPath);
            Object.DestroyImmediate(root);

            Debug.Log($"[GatePrefabBuilder] {name}: {solids} solid post part(s), " +
                      $"gap {gap.size.x:F2} x {gap.size.y:F2} → {outPath}");
            return outPath;
        }

        /// <summary>
        /// The scoring lane: horizontally between the innermost post faces, vertically spanning
        /// the posts. Derived from the posts themselves so it stays correct per tier without
        /// hand-tuned numbers.
        /// </summary>
        private static Bounds ComputeGap(List<Bounds> posts, Transform fallback)
        {
            if (posts.Count == 0)
                return new Bounds(fallback.position, new Vector3(2.8f, 2.3f, TriggerDepth));

            Bounds all = posts[0];
            for (int i = 1; i < posts.Count; i++) all.Encapsulate(posts[i]);

            // Innermost faces: the largest min.x among left-side posts and the smallest max.x
            // among right-side posts, split around the overall centre.
            float centreX = all.center.x;
            float leftEdge = float.NegativeInfinity, rightEdge = float.PositiveInfinity;

            foreach (var b in posts)
            {
                if (b.center.x < centreX) leftEdge  = Mathf.Max(leftEdge,  b.max.x);
                else                      rightEdge = Mathf.Min(rightEdge, b.min.x);
            }

            if (float.IsInfinity(leftEdge) || float.IsInfinity(rightEdge) || rightEdge <= leftEdge)
            {
                // Single post, or posts on one side only — fall back to the full span.
                return new Bounds(all.center, new Vector3(all.size.x, all.size.y, TriggerDepth));
            }

            float width = rightEdge - leftEdge;
            var centre = new Vector3((leftEdge + rightEdge) * 0.5f, all.center.y, all.center.z);
            return new Bounds(centre, new Vector3(width, all.size.y, TriggerDepth));
        }

        /// <summary>Mesh bounds pushed through the renderer's matrix — never Renderer.bounds.</summary>
        private static Bounds WorldBounds(MeshFilter mf)
        {
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = mf.transform.localToWorldMatrix;

            Bounds acc = default;
            bool started = false;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) == 0 ? mb.min.x : mb.max.x,
                    (c & 2) == 0 ? mb.min.y : mb.max.y,
                    (c & 4) == 0 ? mb.min.z : mb.max.z);

                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!started) { acc = new Bounds(p, Vector3.zero); started = true; }
                else acc.Encapsulate(p);
            }
            return acc;
        }
    }
}
