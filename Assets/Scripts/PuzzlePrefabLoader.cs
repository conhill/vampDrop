using UnityEngine;
using Unity.Entities;
using System.Linq;
using Vampire.DropPuzzle;

namespace Vampire.DropPuzzle
{
    public class PuzzlePrefabLoader : MonoBehaviour
    {
        /// <summary>Fired after a puzzle prefab is instantiated and repositioned.</summary>
        public static event System.Action OnPuzzleLoaded;

        /// <summary>True once the first puzzle has been loaded this scene.</summary>
        public static bool IsPuzzleReady { get; private set; }

        /// <summary>The currently active puzzle instance (used by camera for bounds).</summary>
        public static GameObject CurrentPuzzle { get; private set; }

        /// <summary>The map index currently loaded — the drop-puzzle "level".</summary>
        public static int CurrentLevel { get; private set; }

        /// <summary>
        /// Multiplier the currently loaded board is PROMISED to show (0 = no promise).
        ///
        /// Set when the board is built with a guaranteed x2 — the tutorial drop and the first
        /// post-tutorial run. GateRollController reads it so the pre-drop slot machine forces
        /// that result instead of rolling the player's real odds. A new player's odds row is
        /// 96% no-match (SlotMachine3Reel.Tiers[0]), and a losing spin used to remove every
        /// gate on the board — so without this the "guaranteed" gate was thrown away ~96% of
        /// the time immediately after being placed.
        /// </summary>
        public static int GuaranteedGateMultiplier { get; private set; }

        /// <summary>Skrilla goal for the currently loaded map (0 if none configured).</summary>
        public int CurrentLevelGoal =>
            (currentPuzzleIndex >= 0 && LevelSkrillaGoals != null && currentPuzzleIndex < LevelSkrillaGoals.Length)
                ? LevelSkrillaGoals[currentPuzzleIndex] : 0;

        /// <summary>Final framing of the aligned board, for reskin / backdrop systems.</summary>
        public struct BoardFrame
        {
            public float CenterX, CenterY, BottomY, OrthoSize, Aspect;
            public Transform LeftWall, RightWall;
        }

        /// <summary>
        /// Fired after the board is fully aligned — side walls placed AND puzzle scaled/
        /// positioned. Unlike <see cref="OnPuzzleLoaded"/>, wall transforms are FINAL here.
        /// Town skin / parallax backdrop systems should key off this, not OnPuzzleLoaded.
        /// </summary>
        public static event System.Action<BoardFrame> OnBoardAligned;

        private void OnEnable()  { IsPuzzleReady = false; CurrentPuzzle = null; GuaranteedGateMultiplier = 0; }
        private void OnDisable() { IsPuzzleReady = false; CurrentPuzzle = null; GuaranteedGateMultiplier = 0; }

        [Header("Puzzle Prefabs")]
        [Tooltip("Add your manually designed puzzle prefabs here")]
        public GameObject[] PuzzlePrefabs;

        [Header("Guaranteed x2 Gate")]
        [Tooltip("Promise a x2 gate on post-tutorial drops. The gate is placed AND stamped as " +
                 "guaranteed, and the pre-drop slot machine is forced to land on it, so the " +
                 "player wins the roll instead of facing the ~96% no-match odds. The tutorial " +
                 "drop itself is always promised, independently of this.")]
        public bool PromiseX2AfterTutorial = true;

        [Tooltip("Restrict the promise above to the very first post-tutorial drop. Requires " +
                 "PlayerDataManager.EndRun() to actually be called by something — nothing does " +
                 "today, so leaving this ON would make the promise permanent anyway. Off = every " +
                 "post-tutorial drop is promised, which is the behaviour the game has shipped.")]
        public bool PromiseX2OnlyOnFirstRun = false;
        
        [Header("Puzzle Positioning")]
        [Tooltip("Offset position for loaded puzzles (to fix centering)")]
        public Vector3 PuzzlePositionOffset = Vector3.zero;

        [Tooltip("World-space gap reserved at the TOP of the camera so Knox (the dropper) has room to move above the puzzle.")]
        public float TopPadding = 2f;
        
        [Tooltip("Rotation for loaded puzzles")]
        public Vector3 PuzzleRotation = Vector3.zero;
        
        [Tooltip("Scale multiplier for loaded puzzles")]
        public Vector3 PuzzleScale = Vector3.one;
        
        [Header("Puzzle Selection")]
        [Tooltip("Auto-select puzzle based on player level")]
        public bool UsePlayerLevel = true;

        [Tooltip("Manual puzzle index (only used if UsePlayerLevel is false)")]
        public int ManualPuzzleIndex = 0;

        [Header("Level Skrilla Goals")]
        [Tooltip("Skrilla goal per puzzle index (parallel to PuzzlePrefabs). Beat it to unlock the next map.")]
        public int[] LevelSkrillaGoals = { 100, 250, 500, 1000, 2000 };

        [Header("Debug / Testing")]
        [Tooltip("Always load the last prefab in the array regardless of other settings")]
        public bool ForceLastPuzzle = false;
        
        [Header("Background")]
        [Tooltip("Optional background prefab (can be image plane or quad)")]
        public GameObject BackgroundPrefab;
        
        [Tooltip("Optional background sprite/texture for image backgrounds")]
        public Sprite BackgroundSprite;
        
        public Vector3 BackgroundPosition = new Vector3(-2f, 1f, -50f);
        public Vector3 BackgroundScale = new Vector3(50f, 50f, 1f);
        
        [Header("Gate Shape")]
        [Tooltip("Cancel the board's X/Y-only fit scale on multiplier gates so a gate keeps " +
                 "the proportions it was authored with. The board is scaled (s, s, 1) to fit " +
                 "the camera, and s differs per board (1.09 / 1.00 / 0.86 / 0.80 / 1.00 for " +
                 "puzzle1..5), so without this a gate's depth drifts against its width by up " +
                 "to 25% depending on which map is loaded. Turn off to get the raw inherited " +
                 "scale back.")]
        public bool NormalizeGateShape = true;

        [Header("Dynamic Enhancement")]
        [Tooltip("Enable dynamic zone enhancement based on user stats")]
        public bool EnableDynamicEnhancement = true;
        
        [Header("Side Walls")]
        [Tooltip("Optional: drag the 'Wall' prefab here. If empty the puzzle's own wall material is used.")]
        public GameObject SideWallPrefab;
        [Tooltip("Wall thickness in world units")]
        public float WallThickness = 0.3f;
        [Tooltip("Extra height added above and below the puzzle bounds")]
        public float WallOverhang = 3f;
        [Tooltip("Wall depth in Z")]
        public float WallDepth = 2f;

        private GameObject currentPuzzleInstance;
        private GameObject backgroundInstance;
        private GameObject _leftWall;
        private GameObject _rightWall;
        private GameObject _spawnPoint;   // invisible anchor at bottom-center of walls, Z=0
        private PuzzleEnhancer puzzleEnhancer;
        private int currentPuzzleIndex = 0;

        private void Start()
        {
            puzzleEnhancer = GetComponent<PuzzleEnhancer>();
            if (puzzleEnhancer == null)
                puzzleEnhancer = gameObject.AddComponent<PuzzleEnhancer>();

            int puzzleToLoad = ForceLastPuzzle
                ? Mathf.Max(0, PuzzlePrefabs.Length - 1)
                : UsePlayerLevel ? SelectPuzzleIndex() : ManualPuzzleIndex;
            // Guarantee a 2x gate on the tutorial puzzle AND post-tutorial runs so the player
            // always sees the multiplier system work early. "Guarantee" here is the full
            // contract, not just placement: PuzzleEnhancer stamps the gate as promised, and
            // GateRollController forces the pre-drop slot to land on it so the roll is WON
            // rather than left to the 96% no-match odds a new player actually has.
            bool guaranteeX2 = !IsTutorialComplete() || ShouldPromiseX2PostTutorial();

            currentPuzzleIndex = puzzleToLoad;
            LoadPuzzle(puzzleToLoad, guaranteeX2);
        }

        /// <summary>
        /// Returns true if the tutorial is done (runtime flag or persisted flag).
        /// </summary>
        private bool IsTutorialComplete()
        {
            // TutorialCompleted is set to true by PuzzleManager after the first drop,
            // while tutorialActive stays true until the money quest (step 7) finishes.
            // Check the persisted flag first so post-tutorial puzzles load on the second visit.
            if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.TutorialCompleted)
                return true;

            if (TutorialManager.Instance != null)
                return !TutorialManager.Instance.tutorialActive;

            return true;
        }

        /// <summary>
        /// Whether this post-tutorial drop should be handed a promised x2 gate.
        ///
        /// This used to read <c>TotalRunsCompleted == 0</c> and was described as "the very
        /// first post-tutorial run". It never behaved that way: nothing in the project calls
        /// <see cref="PlayerDataManager.EndRun"/>, so TotalRunsCompleted is pinned at 0 and
        /// the promise has silently applied to EVERY post-tutorial run. That is also the
        /// behaviour the game has actually shipped and been tuned against, so it is kept as
        /// the default — but it is now an explicit toggle instead of a side effect of dead
        /// code. Wiring EndRun() up later will no longer silently kill the promise.
        /// </summary>
        private bool ShouldPromiseX2PostTutorial()
        {
            if (!PromiseX2AfterTutorial) return false;
            if (!IsTutorialComplete())   return false;

            var pdm = PlayerDataManager.Instance;

            // No save data at all — e.g. DropPuzzle opened directly in the Editor rather than
            // entered from FPS_Collect, so the DontDestroyOnLoad GameSystems object never
            // came along. Promise it, otherwise the multiplier path is untestable in isolation.
            if (pdm == null) return true;

            return !PromiseX2OnlyOnFirstRun || pdm.TotalRunsCompleted == 0;
        }

        /// <summary>
        /// Chooses puzzle index:
        ///   Tutorial active → 0
        ///   Post-tutorial   → random from 1..N-1 (skips tutorial puzzle)
        /// </summary>
        private int SelectPuzzleIndex()
        {
            // --- Diagnostic dump ---
            var pdm = PlayerDataManager.Instance;
            var tm  = TutorialManager.Instance;
            string prefabList = PuzzlePrefabs == null ? "null" :
                string.Join(", ", System.Array.ConvertAll(PuzzlePrefabs,
                    p => p != null ? p.name : "NULL"));

            Debug.Log(
                $"[PuzzlePrefabLoader] === Puzzle Selection ===\n" +
                $"  PuzzlePrefabs ({(PuzzlePrefabs?.Length ?? 0)}): [{prefabList}]\n" +
                $"  UsePlayerLevel: {UsePlayerLevel}\n" +
                $"  PlayerDataManager.TutorialCompleted: {(pdm != null ? pdm.TutorialCompleted.ToString() : "PDM missing")}\n" +
                $"  TutorialManager.tutorialActive: {(tm != null ? tm.tutorialActive.ToString() : "TM missing")}\n" +
                $"  IsTutorialComplete(): {IsTutorialComplete()}\n" +
                $"  TotalRunsCompleted: {(pdm != null ? pdm.TotalRunsCompleted.ToString() : "PDM missing")}\n" +
                $"  ShouldPromiseX2PostTutorial(): {ShouldPromiseX2PostTutorial()}\n" +
                $"  => promised x2 this drop: {(!IsTutorialComplete() || ShouldPromiseX2PostTutorial())}"
            );
            // -----------------------

            if (PuzzlePrefabs == null || PuzzlePrefabs.Length == 0)
            {
                Debug.LogError("[PuzzlePrefabLoader] No puzzle prefabs assigned!");
                return 0;
            }

            if (!IsTutorialComplete())
            {
                Debug.Log("[PuzzlePrefabLoader] REASON: Tutorial not complete → loading tutorial puzzle (index 0)");
                return 0;
            }

            if (PuzzlePrefabs.Length <= 1)
            {
                Debug.LogWarning("[PuzzlePrefabLoader] REASON: Only 1 prefab assigned — add puzzle2+ to PuzzlePrefabs array");
                return 0;
            }

            // Post-tutorial: load the map the player selected (linear progression), clamped
            // to what's unlocked. Replaces the old random pick so skrilla goals gate advance.
            int sel = pdm != null ? pdm.SelectedLevel : 0;
            sel = Mathf.Clamp(sel, 0, PuzzlePrefabs.Length - 1);
            if (pdm != null && !pdm.IsLevelUnlocked(sel))
                sel = Mathf.Clamp(pdm.HighestLevelUnlocked, 0, PuzzlePrefabs.Length - 1);
            Debug.Log($"[PuzzlePrefabLoader] REASON: Post-tutorial → loading selected level {sel} " +
                      $"(unlocked ≤ {(pdm != null ? pdm.HighestLevelUnlocked : 0)})");
            return sel;
        }

        public void LoadPuzzle(int puzzleIndex, bool guaranteeX2Gate = false)
        {
            Debug.Log($"[PuzzlePrefabLoader] Loading puzzle {puzzleIndex} (guaranteeX2={guaranteeX2Gate})");

            currentPuzzleIndex = puzzleIndex;
            CurrentLevel       = puzzleIndex;
            // Published BEFORE the gates are built, so anything reacting to OnPuzzleLoaded
            // (GateRollController in particular) already sees the promise.
            GuaranteedGateMultiplier = guaranteeX2Gate ? 2 : 0;

            ClearPuzzle();
            CreateBackground();

            if (puzzleIndex >= 0 && puzzleIndex < PuzzlePrefabs.Length && PuzzlePrefabs[puzzleIndex] != null)
            {
                currentPuzzleInstance = Instantiate(PuzzlePrefabs[puzzleIndex], transform);
                currentPuzzleInstance.name = $"Puzzle_{puzzleIndex}";
                CurrentPuzzle = currentPuzzleInstance;

                currentPuzzleInstance.transform.localPosition = PuzzlePositionOffset;
                currentPuzzleInstance.transform.localRotation = Quaternion.Euler(PuzzleRotation);
                currentPuzzleInstance.transform.localScale = PuzzleScale;

                Debug.Log($"[PuzzlePrefabLoader] Loaded puzzle prefab: {PuzzlePrefabs[puzzleIndex].name}");

                // Side walls get initial Z from the puzzle bounds; AlignPuzzleToWalls
                // (called later by the camera's SnapToOverview) handles scale + final position.
                Bounds pb = CalculatePrefabBounds(currentPuzzleInstance);
                CreateSideWalls(pb);

                if (EnableDynamicEnhancement && puzzleEnhancer != null)
                    puzzleEnhancer.EnhancePuzzle(currentPuzzleInstance, guaranteeX2Gate);
                else if (!EnableDynamicEnhancement)
                    Debug.LogWarning("[PuzzlePrefabLoader] Dynamic enhancement is DISABLED");
                else
                    Debug.LogError("[PuzzlePrefabLoader] PuzzleEnhancer component is NULL!");

                RefreshGateSystem();

                IsPuzzleReady = true;
                OnPuzzleLoaded?.Invoke();
            }
            else
            {
                Debug.LogError($"[PuzzlePrefabLoader] Invalid puzzle index {puzzleIndex} or null prefab");
            }
        }

        private void CreateBackground()
        {
            if (BackgroundPrefab != null)
            {
                // Use custom prefab
                backgroundInstance = Instantiate(BackgroundPrefab, transform);
                backgroundInstance.transform.position = BackgroundPosition;
                backgroundInstance.transform.localScale = BackgroundScale;
            }
            else if (BackgroundSprite != null)
            {
                // Create image plane with sprite
                backgroundInstance = new GameObject("Background_ImagePlane");
                backgroundInstance.transform.SetParent(transform);
                backgroundInstance.transform.position = BackgroundPosition;
                backgroundInstance.transform.localScale = BackgroundScale;
                
                // Add SpriteRenderer for 2D image
                var spriteRenderer = backgroundInstance.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = BackgroundSprite;
                spriteRenderer.sortingOrder = -100; // Behind everything
                
                Debug.Log("[PuzzlePrefabLoader] Created sprite-based background");
            }
            else
            {
                // Create default quad background
                backgroundInstance = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backgroundInstance.name = "Background_Default";
                backgroundInstance.transform.position = BackgroundPosition;
                backgroundInstance.transform.localScale = BackgroundScale;
                backgroundInstance.transform.SetParent(transform);
                
                Debug.Log("[PuzzlePrefabLoader] Created default quad background");
            }
        }

        private void RefreshGateSystem()
        {
            var gateSystem = FindObjectOfType<RiceBallGateInteractionSystem>();
            if (gateSystem != null)
            {
                gateSystem.RefreshGates();
                Debug.Log("[PuzzlePrefabLoader] Refreshed gate interaction system");
            }

            var wallSystem = FindObjectOfType<RiceBallWallCollisionSystem>();
            if (wallSystem != null)
            {
                wallSystem.RefreshWalls();
                Debug.Log("[PuzzlePrefabLoader] Refreshed wall collision cache");
            }
        }

        /// <summary>
        /// Called by DropPuzzleCameraController.SnapToOverview after AlignSideWalls has run.
        /// Reads _spawnPoint (placed at the bottom-center of the walls at Z=0 by AlignSideWalls)
        /// and aligns the puzzle to it: center X = spawnPoint.x, bottom Y = spawnPoint.y, Z = 0.
        /// Scales the puzzle vertically (X+Y only, never Z) to fill the camera height.
        /// </summary>
        public void AlignPuzzleToWalls(float camCenterX, float camBottomY, float orthoSize, float aspect)
        {
            if (currentPuzzleInstance == null) return;

            if (_spawnPoint == null)
            {
                Debug.LogWarning("[PuzzlePrefabLoader] _spawnPoint missing — AlignSideWalls must run first.");
                return;
            }

            var bc = FindPuzzleBoundsCollider(currentPuzzleInstance);
            if (bc == null)
            {
                Debug.LogWarning("[PuzzlePrefabLoader] PuzzleBounds not found — can't align puzzle.");
                return;
            }

            // Anchor: bottom-center of the walls, world Z = 0 (set by AlignSideWalls).
            Vector3 anchor = _spawnPoint.transform.position;

            // Reset to inspector scale before computing — avoid compounding across calls.
            currentPuzzleInstance.transform.localScale = PuzzleScale;
            // PhysicsOptimizer disables Physics.autoSyncTransforms, so Collider.bounds
            // will NOT reflect the transform change until we sync manually. Without this
            // every bc.bounds read below is stale (pre-scale), breaking scale + placement.
            Physics.SyncTransforms();

            Bounds b = bc.bounds;
            if (b.size.y < 0.001f) return;

            // Vertical scale only (X and Y). Never scale Z — that amplifies child Z-offsets
            // and pushes puzzle geometry in front of the side walls.
            // Reserve TopPadding at the top so Knox (the dropper) has headroom to move.
            float usableHeight = Mathf.Max(0.1f, orthoSize * 2f - TopPadding);
            float s = usableHeight / b.size.y;
            currentPuzzleInstance.transform.localScale = new Vector3(
                PuzzleScale.x * s,
                PuzzleScale.y * s,
                PuzzleScale.z);

            // Re-read bounds after scale (sync again — see note above).
            Physics.SyncTransforms();
            b = bc.bounds;

            // Place puzzle so the BOUNDS (not the root) land where we want:
            //   center X on anchor, bottom Y on anchor, bounds-center Z on the wall plane (0).
            // The prefab geometry is authored ~11.6 units forward in Z, so zeroing the root
            // alone leaves the puzzle in front of the walls — subtract the bounds' Z center.
            Vector3 pos  = currentPuzzleInstance.transform.position;
            pos.x += anchor.x - b.center.x;
            pos.y += anchor.y - b.min.y;
            pos.z += anchor.z - b.center.z;
            currentPuzzleInstance.transform.position = pos;
            Physics.SyncTransforms();

            // Knox Z = 0 to match puzzle and walls; X clamped to wall center.
            var dropper = FindObjectOfType<DropperControllerECS>();
            if (dropper != null)
            {
                dropper.transform.position = new Vector3(
                    dropper.transform.position.x,
                    dropper.transform.position.y,
                    0f);
                dropper.SetDropCenter(anchor.x);
            }

            NormalizeGateProportions();

            Debug.Log($"[PuzzlePrefabLoader] Aligned: anchor=({anchor.x:F2},{anchor.y:F2},0) " +
                      $"scale×{s:F2} puzzleBottom={bc.bounds.min.y:F2}");

            FitBackgroundToFrame(anchor.x, camBottomY + orthoSize, orthoSize, aspect);

            // Board is now fully aligned — walls and puzzle are in their final spots.
            // Reskin / backdrop systems build against this frame.
            OnBoardAligned?.Invoke(new BoardFrame
            {
                CenterX   = anchor.x,
                CenterY   = camBottomY + orthoSize,
                BottomY   = camBottomY,
                OrthoSize = orthoSize,
                Aspect    = aspect,
                LeftWall  = _leftWall  != null ? _leftWall.transform  : null,
                RightWall = _rightWall != null ? _rightWall.transform : null,
            });
        }

        /// <summary>
        /// Resizes the background plane to cover the camera frame AT ITS OWN DEPTH.
        ///
        /// Under an orthographic camera the frame is the same width at every depth, so a
        /// generously-sized quad covers it wherever you park it. Under perspective the frame
        /// widens with distance, so a backdrop sitting further back than the board needs to be
        /// scaled by the ratio of their distances or its edges show and the skybox leaks in.
        /// </summary>
        private void FitBackgroundToFrame(float centerX, float centerY, float orthoSize, float aspect)
        {
            if (backgroundInstance == null) return;

            float bgZ   = backgroundInstance.transform.position.z;
            float halfH = orthoSize;
            float halfW = orthoSize * aspect;

            var cam = Camera.main;
            if (cam != null && !cam.orthographic)
            {
                float boardDist = Mathf.Abs(cam.transform.position.z);       // board plane is Z = 0
                float bgDist    = Mathf.Abs(cam.transform.position.z - bgZ);
                if (boardDist > 0.0001f)
                {
                    float k = bgDist / boardDist;
                    halfH *= k;
                    halfW *= k;
                }
            }

            const float pad = 1.1f;   // slight over-size so no edge ever creeps into frame
            backgroundInstance.transform.position   = new Vector3(centerX, centerY, bgZ);
            backgroundInstance.transform.localScale = new Vector3(halfW * 2f * pad, halfH * 2f * pad, 1f);
        }

        // Searches all children (recursive, case-insensitive) for the PuzzleBounds BoxCollider.
        /// <summary>
        /// Undoes the X/Y-only board fit on multiplier gates so each gate keeps the
        /// proportions it was authored and fitted with.
        ///
        /// The board is scaled (s, s, 1) above — X and Y take the camera fit, Z deliberately
        /// does not. Every child inherits that, so a gate ends up with world proportions
        /// x:y:z of s:s:1 instead of 1:1:1, and the drift changes per board because s comes
        /// from that board's own height (measured: 1.09, 1.00, 0.86, 0.80, 1.00 for
        /// puzzle1..5). Gates carry readable signage, so cancelling the anisotropy on their
        /// local Z keeps every gate a consistent shape whatever the board fit turns out to be.
        ///
        /// Done here rather than at spawn time because PuzzleEnhancer runs during LoadPuzzle,
        /// long before the camera hands us a fit scale.
        /// </summary>
        private void NormalizeGateProportions()
        {
            if (currentPuzzleInstance == null) return;
            if (!NormalizeGateShape) return;

            foreach (var gate in currentPuzzleInstance.GetComponentsInChildren<MultiplierGate>(true))
            {
                Transform parent = gate.transform.parent;
                if (parent == null) continue;

                Vector3 pl = parent.lossyScale;
                if (Mathf.Abs(pl.z) < 0.0001f) continue;

                // Match the gate's world depth to its world width: local.z * pl.z == local.x * pl.x
                float k = Mathf.Abs(pl.x) / Mathf.Abs(pl.z);
                if (Mathf.Approximately(k, 1f)) continue;

                Vector3 ls = gate.transform.localScale;
                gate.transform.localScale = new Vector3(ls.x, ls.y, ls.z * k);
            }
            Physics.SyncTransforms();
        }

        private static BoxCollider FindPuzzleBoundsCollider(GameObject root)
        {
            foreach (var bc in root.GetComponentsInChildren<BoxCollider>(true))
            {
                if (bc.gameObject.name.Equals("PuzzleBounds", System.StringComparison.OrdinalIgnoreCase))
                    return bc;
            }
            return null;
        }

        private void CreateSideWalls(Bounds pb)
        {
            if (_leftWall   != null) { DestroyImmediate(_leftWall);   _leftWall   = null; }
            if (_rightWall  != null) { DestroyImmediate(_rightWall);  _rightWall  = null; }
            if (_spawnPoint != null) { DestroyImmediate(_spawnPoint); _spawnPoint = null; }

            float height = pb.size.y + WallOverhang * 2f;
            float cy     = pb.center.y;

            // Walls spawn at placeholder positions — AlignSideWalls() sets final X,Y.
            // Z is always 0 — forced here and in AlignSideWalls.
            _leftWall  = MakeWall("Wall_Left",  0f, cy, 0f, height);
            _rightWall = MakeWall("Wall_Right", 0f, cy, 0f, height);

            // Invisible anchor at bottom-center between the walls, always at Z=0.
            // AlignSideWalls() sets the final world position.
            _spawnPoint = new GameObject("PuzzleSpawnPoint");
            _spawnPoint.transform.SetParent(transform);
            _spawnPoint.transform.position = Vector3.zero;
        }

        /// <summary>
        /// Called by DropPuzzleCameraController after it computes its overview position.
        /// Sizes walls to fill the camera viewport exactly and places them at the edges.
        /// </summary>
        public void AlignSideWalls(float camX, float camCenterY, float orthoSize, float aspect)
        {
            if (_leftWall == null || _rightWall == null) return;

            float halfW      = orthoSize * aspect;
            float wallHeight = orthoSize * 2f + WallOverhang * 2f;

            // Force Z=0 on walls — puzzle, Knox, and balls are all at Z=0.
            var ls = _leftWall.transform.localScale;
            _leftWall.transform.localScale = new Vector3(ls.x, wallHeight, ls.z);
            _leftWall.transform.position   = new Vector3(camX - halfW + WallThickness * 0.5f, camCenterY, 0f);

            var rs = _rightWall.transform.localScale;
            _rightWall.transform.localScale = new Vector3(rs.x, wallHeight, rs.z);
            _rightWall.transform.position   = new Vector3(camX + halfW - WallThickness * 0.5f, camCenterY, 0f);

            // Spawn point sits at the exact bottom-center between the two walls, Z=0.
            // This is the single source of truth for puzzle alignment.
            if (_spawnPoint != null)
            {
                float centerX  = (_leftWall.transform.position.x + _rightWall.transform.position.x) * 0.5f;
                float bottomY  = camCenterY - orthoSize;
                _spawnPoint.transform.position = new Vector3(centerX, bottomY, 0f);
            }
        }

        private GameObject MakeWall(string name, float x, float y, float z, float height)
        {
            GameObject wall;
            if (SideWallPrefab != null)
            {
                wall = Instantiate(SideWallPrefab, transform);
            }
            else
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                // Steal material from the puzzle's own Wall-tagged pieces so we match visually
                var srcRenderer = currentPuzzleInstance != null
                    ? currentPuzzleInstance.GetComponentsInChildren<MeshRenderer>()
                        .FirstOrDefault(r => r.CompareTag("Wall") || r.transform.parent != null && r.transform.parent.CompareTag("Wall"))
                    : null;
                if (srcRenderer != null)
                    wall.GetComponent<MeshRenderer>().sharedMaterial = srcRenderer.sharedMaterial;
            }

            wall.name = name;
            wall.tag  = "Wall";
            wall.transform.SetParent(transform);
            wall.transform.position   = new Vector3(x, y, z);
            wall.transform.localScale = new Vector3(WallThickness, height, WallDepth);

            if (wall.GetComponentInChildren<Collider>() == null)
                wall.AddComponent<BoxCollider>();

            return wall;
        }

        private static Bounds CalculatePrefabBounds(GameObject root)
        {
            // Primary: PuzzleBounds collider (recursive, case-insensitive search)
            var bc = FindPuzzleBoundsCollider(root);
            if (bc != null) return bc.bounds;

            // Fallback: renderer scan
            var rends = root.GetComponentsInChildren<Renderer>();
            bool started = false;
            Bounds b = default;
            foreach (var r in rends)
            {
                if (!started) { b = r.bounds; started = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!started)
                b = new Bounds(root.transform.position, Vector3.one * 10f);
            return b;
        }

        public void ClearPuzzle()
        {
            if (currentPuzzleInstance != null)
            {
                DestroyImmediate(currentPuzzleInstance);
                currentPuzzleInstance = null;
                CurrentPuzzle = null;
            }
            if (backgroundInstance != null)
            {
                DestroyImmediate(backgroundInstance);
                backgroundInstance = null;
            }
            if (_leftWall   != null) { DestroyImmediate(_leftWall);   _leftWall   = null; }
            if (_rightWall  != null) { DestroyImmediate(_rightWall);  _rightWall  = null; }
            if (_spawnPoint != null) { DestroyImmediate(_spawnPoint); _spawnPoint = null; }
        }

        // Method to switch puzzles at runtime
        public void SwitchToPuzzle(int newIndex)
        {
            if (newIndex != currentPuzzleIndex)
            {
                currentPuzzleIndex = newIndex;
                LoadPuzzle(currentPuzzleIndex);
            }
        }

        // Inspector buttons for easy testing
        [ContextMenu("Reload Current Puzzle")]
        public void ReloadCurrentPuzzle()
        {
            LoadPuzzle(currentPuzzleIndex);
        }

        [ContextMenu("Load Next Puzzle")]
        public void LoadNextPuzzle()
        {
            int nextIndex = (currentPuzzleIndex + 1) % PuzzlePrefabs.Length;
            SwitchToPuzzle(nextIndex);
        }

        [ContextMenu("Load Previous Puzzle")]
        public void LoadPreviousPuzzle()
        {
            int prevIndex = currentPuzzleIndex - 1;
            if (prevIndex < 0) prevIndex = PuzzlePrefabs.Length - 1;
            SwitchToPuzzle(prevIndex);
        }
    }
}