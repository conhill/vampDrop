using System.Collections.Generic;
using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Dresses the MIDDLE of the drop board — the grey 'smallwall' cubes inside the puzzle
    /// prefab — with themed street junk, so the play field matches the building facades
    /// that <see cref="TownWallBuilder"/> already stacks along the edges.
    ///
    /// The puzzle prefabs are never modified. Each Wall-tagged piece keeps its collider,
    /// position, rotation and scale exactly as authored — ball physics is unchanged. Only
    /// its MeshRenderer is switched off, and a collider-free visual is fitted into the
    /// space that cube occupied. Design a new board by placing cubes as usual; it gets
    /// dressed automatically.
    ///
    /// Pieces are sorted into buckets by silhouette (long thin beam / wide slab / chunky
    /// blob) so props land somewhere sensible with no per-piece setup. Drop a
    /// <see cref="PuzzlePropStyle"/> on a cube to override that for one piece.
    ///
    /// "The town thrives from your contributions": <see cref="PropSets"/> are ordered
    /// ruined → rebuilt and selected from player progress, so the same layout visibly gets
    /// repaired as the player banks runs. Mirrors TownSkinController's tier resolution —
    /// keep the two on the same Source so edges and middle upgrade together.
    ///
    /// Attach to the PuzzlePrefabLoader object. Keys off
    /// <see cref="PuzzlePrefabLoader.OnBoardAligned"/> so the puzzle's final scale and
    /// position are already baked in.
    /// </summary>
    public class PuzzlePropDresser : MonoBehaviour
    {
        private const string PropPrefix = "Prop_";

        public enum PropBucket
        {
            /// <summary>Long and thin — planks, corrugated sheet, pipe runs, lamp posts.</summary>
            LongThin,
            /// <summary>Wide and shallow — awnings, dumpster lids, stall canopies, pallets.</summary>
            WideShort,
            /// <summary>Roughly square — crate piles, bins, rubble heaps.</summary>
            Chunky,
        }

        public enum FitMode
        {
            /// <summary>Stretch the prop to fill the piece exactly. Best for sheets and tarps.</summary>
            Stretch,
            /// <summary>Keep the prop's natural aspect and repeat it along the long axis.
            /// Best for planks, crates and pipes — a 6-unit deflector becomes a row, not
            /// one absurdly stretched mesh.</summary>
            TileAlongLength,
        }

        [System.Serializable]
        public class PropEntry
        {
            public GameObject Prefab;

            [Tooltip("Euler correction applied before fitting. Blender→FBX often needs X=-90; " +
                     "use Y to turn the prop's good side toward the camera. Stick to multiples " +
                     "of 90 — arbitrary angles skew under the piece's non-uniform scale.")]
            public Vector3 RotationOffset;

            public FitMode Fit = FitMode.TileAlongLength;

            [Tooltip("TileAlongLength only. >1 spaces copies further apart (gappy, ruined); " +
                     "<1 packs them tighter (dense, rebuilt).")]
            public float TileSpacing = 1f;

            [Tooltip("Randomly mirror copies so a repeated prop doesn't read as a pattern.")]
            public bool RandomFlip = true;

            [Range(0.05f, 1f)]
            [Tooltip("Shrinks the prop's SILHOUETTE (visible thickness and length) without " +
                     "touching its depth, so pieces get smaller and MORE of them tile in to " +
                     "fill the same deflector. 1 = fill the piece's full thickness. Use this " +
                     "rather than TileSpacing to get more tiles — spacing squashes each copy " +
                     "along its length, which crushes the mesh detail; this scales it evenly.")]
            public float SilhouetteScale = 1f;

            [Range(0f, 1f)]
            [Tooltip("How much of the piece's DEPTH (Z) the prop fills. 0 keeps the prop's " +
                     "real cross-section proportions — correct under an orthographic camera, " +
                     "but it leaves thin props as flat cards with no side to see. 1 fills the " +
                     "full depth so the prop reads as a solid slab under perspective. Raising " +
                     "this stretches the prop's side faces, so back it off if the texture " +
                     "smears. Does not affect tile count or the silhouette.")]
            public float DepthFill = 0f;

            [Tooltip("What this prop should SOUND like when a ball hits it. Impact audio is " +
                     "resolved from the piece GameObject, whose name is 'smallwall (2)' and so " +
                     "on — which matches no keyword and falls through to the generic Wall " +
                     "profile. So a deflector dressed as corrugated tin still thudded like a " +
                     "bare wall. Leave on Wall to auto-detect from the prop prefab's name " +
                     "instead (ScrapPlank -> Wood, CorrugatedTin -> Metal, ...).")]
            public BallImpactSurface ImpactSurface = BallImpactSurface.Wall;

            [Tooltip("Relative pick weight within its bucket. 0 disables this entry.")]
            public float Weight = 1f;

            // Measured bounds at RotationOffset, scale 1 — computed once, reused per board.
            [System.NonSerialized] public bool   Measured;
            [System.NonSerialized] public bool   Valid;
            [System.NonSerialized] public Bounds LocalBounds;
        }

        [System.Serializable]
        public class PropSet
        {
            public string Name = "Shambles";

            [Tooltip("Beams, planks, corrugated sheet, pipe runs.")]
            public PropEntry[] LongThin = new PropEntry[0];

            [Tooltip("Awnings, dumpster lids, canopies, pallet stacks.")]
            public PropEntry[] WideShort = new PropEntry[0];

            [Tooltip("Crate piles, bins, rubble heaps.")]
            public PropEntry[] Chunky = new PropEntry[0];
        }

        [Header("Prop sets (ordered ruined → rebuilt)")]
        public PropSet[] PropSets = new PropSet[0];

        public enum TierSource { Manual, TotalRunsCompleted, TotalCurrency }

        [Header("Tier Selection")]
        [Tooltip("Keep this matching TownSkinController so the edges and the middle " +
                 "upgrade at the same moment.")]
        public TierSource Source = TierSource.Manual;
        public int ManualTier = 0;
        [Tooltip("Ascending stat thresholds. Tier = how many thresholds the stat meets.")]
        public int[] Thresholds = { 3, 8, 15 };

        [Header("Bucket Classification")]
        [Tooltip("Silhouette aspect (long side / short side) at or above which a piece " +
                 "counts as elongated rather than chunky.")]
        public float ElongatedAspect = 1.8f;
        [Tooltip("Elongated pieces thinner than this go to LongThin; thicker ones to WideShort.")]
        public float ThinThreshold = 1.5f;

        [Header("Behaviour")]
        [Tooltip("Switch off the original grey cube renderer. Colliders are always kept.")]
        public bool HideOriginalRenderer = true;

        [Tooltip("Stamp an ImpactSurfaceTag on each dressed piece so its impact sound matches " +
                 "the prop it was skinned with. Without this every deflector sounds like a " +
                 "generic wall, because impact audio resolves from the piece's own name " +
                 "('smallwall (2)') and never sees the prop.")]
        public bool StampImpactSurface = true;
        [Tooltip("Also dress the catch-basket pieces (BasketFloor / BasketWall_*).")]
        public bool DressBasket = true;
        [Tooltip("Names containing any of these are skipped (case-insensitive).")]
        public string[] NameBlocklist = { "PuzzleBounds", "DropperAnchor" };

        [Header("Determinism")]
        [Tooltip("Re-roll props every time the board loads. OFF (recommended) seeds from the " +
                 "level index so a given board looks the same on every visit and reads as a " +
                 "real place the player recognises.")]
        public bool RerollEachLoad = false;
        [Tooltip("Extra seed mixed in. Change it to reshuffle every board at once.")]
        public int SeedSalt = 0;

        [Header("Readability Guard")]
        [Tooltip("Warn when a fitted prop pokes past the collider silhouette by more than " +
                 "this fraction of the piece. Overhang makes bounces look like cheating.")]
        public float OverhangTolerance = 0.15f;
        [Tooltip("Log a per-piece breakdown of bucket choices — useful when tuning thresholds.")]
        public bool VerboseLogging = false;

        private int _dressed;
        private PuzzlePrefabLoader.BoardFrame _lastFrame;
        private bool _hasFrame;

        private void OnEnable()  { PuzzlePrefabLoader.OnBoardAligned += Dress; }
        private void OnDisable() { PuzzlePrefabLoader.OnBoardAligned -= Dress; }

        /// <summary>Re-run the last dress pass — tune sets and thresholds without reloading.</summary>
        [ContextMenu("Rebuild")]
        private void Rebuild()
        {
            if (_hasFrame) Dress(_lastFrame);
            else Debug.LogWarning("[PuzzlePropDresser] No board frame yet — enter Play once so the board aligns.");
        }

        [ContextMenu("Cycle Tier")]
        private void CycleTier()
        {
            Source     = TierSource.Manual;
            ManualTier = (ManualTier + 1) % Mathf.Max(1, PropSets?.Length ?? 1);
            Rebuild();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void Dress(PuzzlePrefabLoader.BoardFrame frame)
        {
            _lastFrame = frame;
            _hasFrame  = true;
            _dressed   = 0;

            var puzzle = PuzzlePrefabLoader.CurrentPuzzle;
            if (puzzle == null)
            {
                Debug.LogWarning("[PuzzlePropDresser] No current puzzle instance — nothing to dress.");
                return;
            }

            StripPrevious(puzzle);

            PropSet set = ResolveSet();
            if (set == null)
            {
                Debug.LogWarning("[PuzzlePropDresser] No PropSets configured — board left greybox.");
                return;
            }

            var pieces = CollectPieces(puzzle);
            if (pieces.Count == 0)
            {
                Debug.LogWarning("[PuzzlePropDresser] No Wall-tagged pieces found inside " +
                                 $"'{puzzle.name}'. Puzzle deflectors should be tagged 'Wall'.");
                return;
            }

            // Seed once and iterate in a stable order so a given board dresses identically
            // on every visit. Saved/restored so we never disturb gameplay rolls elsewhere.
            var prevState = Random.state;
            Random.InitState(RerollEachLoad
                ? System.Environment.TickCount
                : PuzzlePrefabLoader.CurrentLevel * 73856093 + SeedSalt);

            foreach (var piece in pieces)
                DressPiece(piece, set);

            Random.state = prevState;

            Debug.Log($"[PuzzlePropDresser] Dressed {_dressed}/{pieces.Count} pieces " +
                      $"with set '{set.Name}' (level {PuzzlePrefabLoader.CurrentLevel}).");
        }

        /// <summary>
        /// Wall-tagged pieces inside the puzzle instance. Scoping to the instance excludes
        /// the generated Wall_Left / Wall_Right edge walls (TownWallBuilder owns those),
        /// and the Wall tag excludes PuzzleBounds, GoalGate and the multiplier zones —
        /// those already carry their own art.
        /// </summary>
        private List<Transform> CollectPieces(GameObject puzzle)
        {
            var list = new List<Transform>();

            foreach (var t in puzzle.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (!t.CompareTag("Wall")) continue;
                if (t.GetComponent<Renderer>() == null) continue;
                if (IsBlocked(t.name)) continue;

                // Multiplier gates carry their own art. Their POSTS are tagged "Wall" so balls
                // bounce off them (RiceBallWallCollisionSystem only sees tagged BoxColliders),
                // but they are not deflectors to dress — without this the dresser hides the
                // gate's renderers and stacks a prop on every post.
                // includeInactive: the default overload ignores inactive ancestors, so a gate
                // that is disabled (a losing slot roll deactivates them) would slip through
                // and get dressed anyway.
                if (t.GetComponentInParent<MultiplierGate>(true) != null) continue;

                bool isBasket = t.name.IndexOf("Basket", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (isBasket && !DressBasket) continue;

                var style = t.GetComponent<PuzzlePropStyle>();
                if (style != null && style.LeaveBare) continue;

                list.Add(t);
            }

            return list;
        }

        private bool IsBlocked(string name)
        {
            if (NameBlocklist == null) return false;
            foreach (var b in NameBlocklist)
            {
                if (string.IsNullOrEmpty(b)) continue;
                if (name.IndexOf(b, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void DressPiece(Transform piece, PropSet set)
        {
            var style  = piece.GetComponent<PuzzlePropStyle>();
            var bucket = (style != null && style.OverrideBucket)
                ? style.Bucket
                : Classify(piece.localScale);

            PropEntry[] pool  = BucketPool(set, bucket);
            PropEntry   entry = (style != null && style.ForcePropIndex >= 0)
                ? PickByIndex(pool, style.ForcePropIndex)
                : PickWeighted(pool);

            if (entry == null || entry.Prefab == null)
            {
                if (VerboseLogging)
                    Debug.Log($"[PuzzlePropDresser] '{piece.name}' → {bucket}: bucket empty, left bare.");
                return;   // leave the grey cube visible rather than punching a hole in the board
            }

            if (!Measure(entry)) return;

            // The cube mesh is a unit cube, so the piece's own transform already describes
            // the exact box to fill: in the piece's local space the target is 1×1×1 at the
            // origin, whatever the piece's world scale and rotation happen to be.
            var container = new GameObject(PropPrefix + piece.name);
            container.transform.SetParent(piece, false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale    = Vector3.one;

            int axis = LongestSilhouetteAxis(piece.localScale);

            if (entry.Fit == FitMode.Stretch) FitStretch(container.transform, entry);
            else                              FitTiled(container.transform, entry, axis, piece.localScale);

            // Tiled props honour SilhouetteScale and can end up thinner than the collider
            // they stand in; slide them onto the face the balls actually land on.
            // (Stretch fills the piece exactly, so it never has the gap.)
            if (entry.Fit != FitMode.Stretch)
                AlignSkinToContactFace(piece, container.transform, entry, axis);

            ApplyImpactSurface(piece, entry);

            if (HideOriginalRenderer)
                foreach (var r in piece.GetComponents<Renderer>())
                    r.enabled = false;

            CheckOverhang(piece, container.transform, entry);

            _dressed++;
            if (VerboseLogging)
                Debug.Log($"[PuzzlePropDresser] '{piece.name}' scale={piece.localScale} " +
                          $"→ {bucket} / {entry.Prefab.name} ({entry.Fit}).");
        }

        // ── Fitting ───────────────────────────────────────────────────────────

        /// <summary>
        /// Make the piece SOUND like the prop that was just dressed onto it.
        ///
        /// RiceBallWallCollisionSystem.CacheWalls() resolves each obstacle's impact surface
        /// once, from the piece GameObject, via ImpactSurfaceTag.Resolve. The pieces are named
        /// "smallwall (2)", "smallwall (5)" and so on, which match none of Resolve's keywords,
        /// so every deflector on the board resolved to the generic Wall profile — including
        /// the ones visibly dressed as corrugated tin or scrap planking. Stamping the tag here
        /// closes that gap, because this runs during AlignPuzzleToWalls and CacheWalls only
        /// fires 0.1s later (RefreshWalls' Invoke delay), so the tag is in place first.
        /// </summary>
        private void ApplyImpactSurface(Transform piece, PropEntry entry)
        {
            if (!StampImpactSurface) return;

            var surface = entry.ImpactSurface;

            // Left at the default? Infer it from the prop prefab's own name, so an existing
            // prop set gets the right sound with no per-entry authoring.
            if (surface == BallImpactSurface.Wall && entry.Prefab != null)
                surface = ImpactSurfaceTag.Resolve(entry.Prefab);

            var tag = piece.GetComponent<ImpactSurfaceTag>();
            if (tag == null) tag = piece.gameObject.AddComponent<ImpactSurfaceTag>();
            tag.Surface = surface;
        }

        /// <summary>
        /// Slide a skinny prop onto the collider face the balls actually rest on.
        ///
        /// A prop with SilhouetteScale &lt; 1 is thinner than the BoxCollider it stands in,
        /// and HideOriginalRenderer hides the full-thickness box — so balls stop against an
        /// invisible collider face while the visible plank sits centred and thin, and they
        /// read as floating. Measured on the Shambles LongThin ramps: collider 0.818 vs a
        /// 0.205 / 0.123 skin, i.e. a 0.307 / 0.348 gap per side, about 1.5-1.7 ball
        /// diameters at BallRadius 0.1.
        ///
        /// Shrinking the collider to match is NOT an option: RiceBallPhysicsSystem clamps
        /// ball speed to 15 u/s and the wall job clamps dt to 0.033, so a ball can cross
        /// 0.495 units in a single step. A 0.205-thick wall would be tunnelled straight
        /// through. So the collider keeps its thickness and the skin moves instead.
        ///
        /// The skin is aligned to the UPWARD-facing collider face, decided in world space so
        /// mirrored ramps (45 vs 315) each resolve on their own. The residual gap ends up on
        /// the underside, which nothing rests against and the head-on board camera cannot see.
        /// </summary>
        private void AlignSkinToContactFace(Transform piece, Transform container,
                                            PropEntry entry, int axis)
        {
            float sil = Mathf.Clamp(entry.SilhouetteScale, 0.05f, 1f);
            if (sil >= 0.999f) return; // already fills the collider — nothing to align

            var box = piece.GetComponent<BoxCollider>();
            if (box == null) return;   // no collider to align against; leave centred

            // Thickness axis is the one FitTiled treats as in-plane cross-section.
            int plane = (axis == 0) ? 1 : 0;

            // Both quantities are in the piece's local units: the collider is box.size, and
            // the skin's local thickness is exactly SilhouetteScale (FitTiled sets the copy's
            // plane scale so world thickness == SilhouetteScale x piece lossyScale).
            float halfGap = (box.size[plane] - sil) * 0.5f;
            if (halfGap <= 0f) return; // skin is already as thick as the collider

            Vector3 axisWorld = (plane == 0) ? piece.right : piece.up;
            float   dir       = Vector3.Dot(axisWorld, Vector3.up) >= 0f ? 1f : -1f;

            Vector3 lp = container.localPosition;
            lp[plane]  = box.center[plane] + halfGap * dir;
            container.localPosition = lp;
        }

        /// <summary>Single copy stretched to fill the unit cube exactly.</summary>
        private void FitStretch(Transform parent, PropEntry entry)
        {
            Bounds b = entry.LocalBounds;
            var k = new Vector3(SafeInv(b.size.x), SafeInv(b.size.y), SafeInv(b.size.z));
            SpawnCopy(parent, entry, k, -Vector3.Scale(b.center, k), false, 0);
        }

        /// <summary>
        /// Uniform scale that fits the prop's cross-section, then repeated end to end along
        /// the piece's long axis. Copies are stretched by the leftover remainder so the run
        /// still fills the piece exactly — no gap at the ends, no overhang.
        ///
        /// Tile count is worked out in the piece's PHYSICAL proportions, not in normalised
        /// unit-cube space. A deflector is authored 0.75 wide by 6 long, so a plank with
        /// natural real-world proportions has to yield ~8 copies; normalising first would
        /// collapse that to one stretched copy and force artists to author deliberately
        /// mis-proportioned meshes to compensate.
        /// </summary>
        private void FitTiled(Transform parent, PropEntry entry, int axis, Vector3 pieceScale)
        {
            Bounds b = entry.LocalBounds;

            var s = new Vector3(Mathf.Abs(pieceScale.x), Mathf.Abs(pieceScale.y), Mathf.Abs(pieceScale.z));
            if (s[axis] < 0.0001f) { FitStretch(parent, entry); return; }

            // Cross-section is split into the IN-PLANE axis (the thickness you see in
            // silhouette) and DEPTH (Z, which the board camera looks along). They're treated
            // separately: the in-plane axis always fits tight so the visual hugs the collider,
            // while depth is dialled by DepthFill — a prop at natural proportions is a flat
            // card with no side face, which reads as dead-on flat under any camera.
            const int depth = 2;
            int plane = (axis == 0) ? 1 : 0;

            float fitPlane = Ratio(s[plane], b.size[plane]);
            float fitDepth = Ratio(s[depth], b.size[depth]);
            if (fitPlane <= 0.0001f || fitDepth <= 0.0001f) { FitStretch(parent, entry); return; }

            // In-plane always fills exactly — that's what keeps the visual hugging the
            // collider. Depth starts at the prop's natural proportion (capped so it can't
            // bulge past the piece) and lerps toward filling the piece outright. Tiling reads
            // the in-plane fit only, so DepthFill never changes how many copies appear.
            // SilhouetteScale shrinks the in-plane fit only. Because physLen is derived from
            // it, a smaller silhouette automatically yields MORE tiles at their natural
            // proportions — no per-copy squashing. Depth is deliberately excluded so pieces
            // can be made smaller without going flat again.
            float usePlane = fitPlane * Mathf.Clamp(entry.SilhouetteScale, 0.05f, 1f);
            float natural  = Mathf.Min(fitPlane, fitDepth);
            float useDepth = Mathf.Lerp(natural, fitDepth, Mathf.Clamp01(entry.DepthFill));

            float physLen = b.size[axis] * usePlane;
            if (physLen <= 0.0001f) { FitStretch(parent, entry); return; }

            float spacing  = Mathf.Max(0.05f, entry.TileSpacing);
            int   count    = Mathf.Max(1, Mathf.RoundToInt(s[axis] / (physLen * spacing)));
            float stepPhys = s[axis] / count;

            // Convert physical targets back into local scale: local size × piece scale = physical.
            var k = Vector3.one;
            k[plane] = usePlane / s[plane];
            k[depth] = useDepth / s[depth];
            k[axis]  = stepPhys / (b.size[axis] * s[axis]);

            float step = 1f / count;
            for (int i = 0; i < count; i++)
            {
                bool flip = entry.RandomFlip && Random.value < 0.5f;

                // Centring must use the SIGNED scale. A mirrored copy reflects about the
                // holder origin, so a prop whose bounds centre is off its own pivot lands
                // twice that offset away if the flip is ignored here.
                var kSigned = k;
                if (flip) kSigned[axis] = -kSigned[axis];

                var offset = -Vector3.Scale(b.center, kSigned);
                offset[axis] += -0.5f + step * (i + 0.5f);

                SpawnCopy(parent, entry, k, offset, flip, axis);
            }
        }

        private static float Ratio(float target, float size)
            => Mathf.Abs(size) < 0.0001f ? 0f : target / size;

        /// <summary>
        /// Places one fitted copy. <paramref name="scale"/> and <paramref name="localPos"/>
        /// are expressed in the PIECE's axes; the prop's own RotationOffset is applied
        /// underneath, so the scale is permuted into the prop's local axes first. Without
        /// that permutation a rotated prop would be scaled along the wrong axes and skew
        /// under the piece's non-uniform scale.
        /// </summary>
        private void SpawnCopy(Transform parent, PropEntry entry, Vector3 scale,
                               Vector3 localPos, bool flip, int flipAxis)
        {
            var rot = Quaternion.Euler(entry.RotationOffset);

            Vector3 permuted = Quaternion.Inverse(rot) * scale;
            permuted = new Vector3(Mathf.Abs(permuted.x), Mathf.Abs(permuted.y), Mathf.Abs(permuted.z));

            if (flip)
            {
                // Mirroring is meant in the piece's axes — find which prop-local axis that
                // maps to, so the flip lands on the run direction and not some other one.
                int localAxis = MapAxis(rot, flipAxis);
                permuted[localAxis] = -permuted[localAxis];
            }

            // The fit transform goes on a holder, NEVER on the prefab root. An imported FBX
            // bakes its axis conversion (Blender Z-up → Unity Y-up) into the root's own
            // rotation and scale; writing localRotation/localScale there destroys it and the
            // prop comes in on the wrong axis. The prefab is attached underneath untouched.
            var holder = new GameObject("c");
            holder.transform.SetParent(parent, false);
            holder.transform.localRotation = rot;
            holder.transform.localScale    = permuted;
            holder.transform.localPosition = localPos;

            var go = Instantiate(entry.Prefab, holder.transform, false);

            // Visual only — the piece's own collider stays the single source of truth for
            // bounces, and stray prop colliders would knock balls off the tuned layout.
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Destroy(col);
        }

        /// <summary>Which prop-local axis a piece-space axis maps to under a rotation.</summary>
        private static int MapAxis(Quaternion rot, int parentAxis)
        {
            Vector3 dir = Quaternion.Inverse(rot) * AxisVector(parentAxis);
            float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y), az = Mathf.Abs(dir.z);
            if (ax >= ay && ax >= az) return 0;
            return ay >= az ? 1 : 2;
        }

        private static Vector3 AxisVector(int axis)
            => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        /// <summary>
        /// World-space bounds of every mesh under <paramref name="root"/>, computed from mesh
        /// bounds pushed through each renderer's own matrix.
        ///
        /// This exists because <c>Renderer.bounds</c> is not reliable in the same frame an
        /// object is instantiated — it can return mesh-local bounds with child transforms not
        /// yet applied. Transform matrices are evaluated on demand and have no such lag, so
        /// this is correct whenever it is called.
        /// </summary>
        private static bool TryMeasureWorldBounds(Transform root, out Bounds bounds)
        {
            bool started = false;
            Bounds acc = default;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;

                var r = mf.GetComponent<Renderer>();
                if (r == null) continue;

                Bounds mb = mesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix;

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
            }

            bounds = acc;
            return started;
        }

        /// <summary>
        /// Bounds of the prop at its RotationOffset with scale 1, measured once per entry.
        /// Instantiated at the origin, so world bounds are the piece-frame bounds we want.
        ///
        /// Deliberately does NOT use <c>Renderer.bounds</c>. Read in the same frame as the
        /// Instantiate, that property returns raw mesh bounds with child transforms not yet
        /// applied — and Blender FBX children carry a 270° X rotation converting Z-up to
        /// Y-up, so the long axis silently comes back on the wrong component and the tile
        /// count is computed off the prop's thickness. Measuring mesh bounds through the
        /// transform matrices is timing-independent.
        /// </summary>
        private bool Measure(PropEntry entry)
        {
            if (entry.Measured) return entry.Valid;

            entry.Measured = true;
            entry.Valid    = false;

            // Measured through the same holder arrangement SpawnCopy uses — holder carries
            // the RotationOffset at scale 1, prefab hangs underneath with its baked TRS
            // intact. Holder sits at the origin, so world bounds are the piece-frame bounds.
            var holder = new GameObject("measure");
            holder.transform.position   = Vector3.zero;
            holder.transform.rotation   = Quaternion.Euler(entry.RotationOffset);
            holder.transform.localScale = Vector3.one;

            Instantiate(entry.Prefab, holder.transform, false);

            bool got = TryMeasureWorldBounds(holder.transform, out Bounds b);
            DestroyImmediate(holder);   // immediate: a deferred Destroy would render for a frame

            if (!got)
            {
                Debug.LogWarning($"[PuzzlePropDresser] '{entry.Prefab.name}' has no readable meshes — skipped.");
                return false;
            }

            entry.LocalBounds = b;

            if (b.size.x < 0.0001f || b.size.y < 0.0001f || b.size.z < 0.0001f)
            {
                Debug.LogWarning($"[PuzzlePropDresser] '{entry.Prefab.name}' is flat on an axis " +
                                 $"(size={b.size}) — fitting would be degenerate, skipped.");
                return false;
            }

            entry.Valid = true;
            return true;
        }

        // ── Classification ────────────────────────────────────────────────────

        /// <summary>
        /// Buckets from the X/Y silhouette only. Z is board depth — the camera looks down
        /// -Z, so it never affects how a piece reads.
        /// </summary>
        private PropBucket Classify(Vector3 localScale)
        {
            float longSide  = Mathf.Max(Mathf.Abs(localScale.x), Mathf.Abs(localScale.y));
            float shortSide = Mathf.Min(Mathf.Abs(localScale.x), Mathf.Abs(localScale.y));
            if (shortSide < 0.0001f) return PropBucket.LongThin;

            float aspect = longSide / shortSide;
            if (aspect < ElongatedAspect) return PropBucket.Chunky;
            return shortSide <= ThinThreshold ? PropBucket.LongThin : PropBucket.WideShort;
        }

        /// <summary>Local axis (0=X, 1=Y) the piece runs along. Z is never the run axis.</summary>
        private static int LongestSilhouetteAxis(Vector3 localScale)
            => Mathf.Abs(localScale.y) > Mathf.Abs(localScale.x) ? 1 : 0;

        private static PropEntry[] BucketPool(PropSet set, PropBucket bucket)
        {
            switch (bucket)
            {
                case PropBucket.WideShort: return set.WideShort;
                case PropBucket.Chunky:    return set.Chunky;
                default:                   return set.LongThin;
            }
        }

        // ── Tier resolution ───────────────────────────────────────────────────

        private PropSet ResolveSet()
        {
            if (PropSets == null || PropSets.Length == 0) return null;
            return PropSets[Mathf.Clamp(ComputeTierIndex(), 0, PropSets.Length - 1)];
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

        /// <summary>Force a tier and re-dress against the last aligned frame.</summary>
        public void SetTier(int index)
        {
            Source     = TierSource.Manual;
            ManualTier = index;
            Rebuild();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static PropEntry PickWeighted(PropEntry[] pool)
        {
            if (pool == null || pool.Length == 0) return null;

            float total = 0f;
            foreach (var e in pool)
                if (e != null && e.Prefab != null && e.Weight > 0f) total += e.Weight;
            if (total <= 0f) return null;

            float roll = Random.Range(0f, total);
            foreach (var e in pool)
            {
                if (e == null || e.Prefab == null || e.Weight <= 0f) continue;
                roll -= e.Weight;
                if (roll <= 0f) return e;
            }

            // Float drift on the last step — fall through to the last valid entry.
            for (int i = pool.Length - 1; i >= 0; i--)
                if (pool[i] != null && pool[i].Prefab != null && pool[i].Weight > 0f) return pool[i];
            return null;
        }

        private static PropEntry PickByIndex(PropEntry[] pool, int index)
        {
            if (pool == null || pool.Length == 0) return null;
            if (index < 0 || index >= pool.Length) return PickWeighted(pool);
            return pool[index] != null && pool[index].Prefab != null ? pool[index] : null;
        }

        private static float SafeInv(float v) => Mathf.Abs(v) < 0.0001f ? 1f : 1f / v;

        /// <summary>
        /// Plinko lives on the player being able to read where a ball will bounce. A prop
        /// whose silhouette sticks out past its collider makes bounces look wrong, so flag
        /// it loudly rather than letting it ship as a "feels cheaty" bug.
        ///
        /// Measured by projecting each renderer's own local bounds into the piece's frame,
        /// where the collider box is exactly 1×1×1. Using world AABBs instead would
        /// over-report on the rotated deflectors and cry wolf constantly.
        /// </summary>
        private void CheckOverhang(Transform piece, Transform container, PropEntry entry)
        {
            Matrix4x4 toPiece = piece.worldToLocalMatrix;
            float over = float.NegativeInfinity;

            foreach (var mf in container.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;

                Bounds mb = mesh.bounds;
                Matrix4x4 m = toPiece * mf.transform.localToWorldMatrix;

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? mb.min.x : mb.max.x,
                        (c & 2) == 0 ? mb.min.y : mb.max.y,
                        (c & 4) == 0 ? mb.min.z : mb.max.z);

                    Vector3 p = m.MultiplyPoint3x4(corner);
                    over = Mathf.Max(over, Mathf.Abs(p.x) - 0.5f);
                    over = Mathf.Max(over, Mathf.Abs(p.y) - 0.5f);
                }
            }

            if (float.IsNegativeInfinity(over)) return;

            if (over > OverhangTolerance)
                Debug.LogWarning($"[PuzzlePropDresser] '{entry.Prefab.name}' on '{piece.name}' " +
                                 $"overhangs its collider by {over:P0} — balls will look like they " +
                                 $"bounce off nothing. Trim the mesh, or use Fit=Stretch.");
        }

        /// <summary>
        /// Removes props from a previous pass and restores the grey cubes, so the
        /// context-menu Rebuild is idempotent. Containers are gathered before any are
        /// destroyed — destroying mid-walk would leave dangling child transforms.
        /// </summary>
        private void StripPrevious(GameObject puzzle)
        {
            var stale = new List<GameObject>();

            foreach (var t in puzzle.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.name.StartsWith(PropPrefix)) { stale.Add(t.gameObject); continue; }
                if (!t.CompareTag("Wall")) continue;
                foreach (var r in t.GetComponents<Renderer>()) r.enabled = true;
            }

            // Immediate so a Rebuild doesn't briefly render old and new props together.
            foreach (var go in stale)
                if (go != null) DestroyImmediate(go);
        }
    }
}
