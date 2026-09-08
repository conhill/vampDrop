# VAMP4 Polish Backlog

Shared checkpoint file. This — not conversation context — is the source of truth across sessions.
Rule: every completed item ends in its own git commit referencing the item below. No item is "done" without a commit hash here.

Status: `[ ]` not started · `[~]` in progress · `[x]` done (commit hash)

---

## 0. Baseline safety (blocking — needs your call, not an agent's)
- [x] (`1437f20`) ~150 files were sitting uncommitted on `shader-revamp` (last real commit before this was `3bf1bca`). Committed as one WIP snapshot: new UI, DropPuzzle flow/juice scripts, gate roll/weather/slot systems, new asset packs. Excluded from the commit (still untracked, harmless): `*.bak` files, Windows `.lnk` shortcuts, `Packages/Coplay/`, `ProfilerCaptures/`, `blender_mcp/`. Not line-by-line reviewed — it's a checkpoint, not a certified-clean state.

## 1. Shader / material finish-up
- [ ] Review what's actually changed in `rice.mat` + the dungeon-pack materials vs. what `shader-revamp` intended; finish or revert stragglers. Skill: `unity-shaders`. Model: Opus (design/visual judgment).

## 2. ECS perf audit (read-only first)
- [x] (findings: `docs/ecs_audit_findings.md`) Audited `Assets/Scripts` for `SystemBase`/`.WithoutBurst()`/structural `AddComponent`/`RemoveComponent` outside the already-optimized rice systems. 1 likely-regression, 7 worth-checking, 6 likely-fine. Model: Sonnet, background.
- [x] (`11d77d0`) Fixed the 4 highest-confidence findings: `RiceBallCollisionSystem` wasn't actually disabled despite the comment (added `[DisableAutoCreation]`), `RiceBallDeletionSystem` missing per-method `[BurstCompile]`, `PlayerTransformSyncSystem` dropped an unneeded `.WithoutBurst()`, `BallDropUI.CleanupRiceEntities` switched to batched query-destroy.
- [ ] **Needs Unity Editor open to verify** — none of the above has been compiled/run yet (unity-mcp wasn't connected this session). Open the project, let it compile, and confirm no errors before trusting these.
- [ ] Follow-up, deferred (lower confidence, want Editor verification first): `RiceGPURenderer`'s per-entity `AddComponents` loop at 40k-rice spawn time; unbatched `DestroyEntity` calls in `HelperAI.CollectRice` / `HelperWorker.TryCollect`.
- [x] (`59c1b3e`) **Correction:** the `[DisableAutoCreation]` fix above was wrong — user wants ball-to-ball collision *kept*, just fast at ~2000 balls. Reverted to properly re-enabled (explicit `[UpdateInGroup]`/`[UpdateAfter]`, `RequireForUpdate<RiceBallTag>` guard added). Not yet profiled.

## 2b. Riceball-collision performance at ~2000 balls (new, explicit goal)
- [x] unity-mcp reconnected (new session, `relay_win.exe` live).

### Protocol for whichever session runs this next (don't collect 10,000 rice by hand — script it)
Real spawn entry point is `Assets/Scripts/DropperControllerECS.cs`:
- `SpawnBallFromInventory()` (line 279) → `SpawnBallEntity(RiceBallQuality? quality)` (line 295) is what actually creates a ball entity. This is the code path to call directly, not the `SPACE` hotkey — the hotkey just loops this per-inventory-item, and getting 2000 real inventory riceballs via `G`/`E` would take forever.
- `PlayerDataManager.cs` has `AddRice()` (139) and rice→riceball crafting (~147); either set the riceball count/inventory directly for the test, or just call `SpawnBallEntity(null)` (random quality) in a loop — inventory realism doesn't matter for a load test, only ball count does.

Steps:
1. `Unity_ManageScene` → open `DropPuzzle`.
2. `Unity_ManageEditor` → enter Play Mode.
3. `Unity_RunCommand` → find the live `DropperControllerECS` instance and call `SpawnBallEntity(null)` ~2000 times (or however its access level allows — may need a temporary public/internal test hook if it's private; check accessibility before assuming reflection is needed). Space calls slightly if spawning all 2000 in one frame causes an unrepresentative spike — real gameplay trickles them in via `DropInterval`.
4. Let it run ~5-10 seconds of real time after spawn completes so balls settle into steady-state (resting + colliding), not just the initial burst — that steady state is what "smooth with 2000 on screen" actually means.
5. Capture profiler data across that steady-state window: `Unity_Profiler_GetFrameRangeTopTimeSummary`, `Unity_Profiler_GetWorstCpuFrames`, `Unity_Profiler_GetOverallGcAlloc`. This tells us the actual hot path — don't assume it's `RiceBallCollisionSystem` just because that's what we already touched.
6. `Unity_GetConsoleLogs` → check for errors/warnings/exceptions during the stress run.
7. Optional: `Unity_Camera_Capture` for a visual sanity screenshot (balls not clipping/exploding).
8. Write findings back here (real numbers, which system actually dominates frame time) before changing any more code. If `RiceBallCollisionSystem` genuinely is the hot path, candidate next steps (evaluate against the data, don't apply speculatively): parallelizing the spatial-hash pass (currently single-threaded despite being Burst-compiled), reducing the 3x3-cell neighbor check radius, persistent reused native containers instead of per-frame `Allocator.Temp` (Temp is already a cheap bump allocator — likely not the win it looks like on paper).

### Session findings: how to get a valid DropPuzzle test state (verified live in Editor, Unity 6000.3.11f1)
Everything below was confirmed in Play Mode, not inferred from source.

- **`GameSystems` is NOT in `DropPuzzle`.** Scene-YAML grep by script GUID: `FPS_Collect.unity` carries PlayerDataManager / UpgradeShop / ProgressionSystem / DayNightCycleManager / TutorialManager / QuestManager; `DropPuzzle.unity` has **none** of them (only `DropperControllerECS` + `BallDropCompletionManager`). `Base.unity` has none either. They reach DropPuzzle purely via `DontDestroyOnLoad` from FPS_Collect — there is no `RuntimeInitializeOnLoadMethod` bootstrap for them.
- **DropPuzzle's own systems do not need it.** Opening `DropPuzzle` directly and pressing Play is a valid physics/collision test bed. Verified at runtime: all six manager singletons `= NULL`, yet `PuzzlePrefabLoader.IsPuzzleReady = True`, `DropPuzzleFlowManager.CurrentPhase = PlayerControl`, `AllowPlayerInput = True`, `timeScale = 1`, ECS world live, 12 Wall-tagged BoxColliders present. **No keypress or tutorial click-through was needed** — the flow manager reaches `PlayerControl` on its own.
- **`DayNightCycleManager.CanEnterBallDrop()` is irrelevant here.** Its only callers are `BallDropSceneEntry.cs:90` and `:173` — the FPS_Collect-side trigger. Nothing inside DropPuzzle consults it, so entering the scene directly bypasses the night-gate legitimately. `BallDropCompletionManager` null-guards `DayNightCycleManager.Instance`.
- **What IS degraded without GameSystems** (irrelevant to collision cost, but note it before trusting any *scoring* numbers): `PuzzleEnhancer` logs "PlayerDataManager.Instance not found! Using defaults" and applies default gate multipliers instead of upgrade-scaled ones; HUD / scoring / inventory are inert; `PuzzlePrefabLoader` falls back to tutorial puzzle index 0 (`Puzzle_0`); `DropPuzzleTutorialOverlay.ShouldPlay()` returns true when `pdm == null`, so the overlay plays — it locks input only, and does **not** set `timeScale = 0` (nothing in the DropPuzzle path does).
- **`SpawnBallEntity` is private** (`DropperControllerECS.cs:295`, param `System.Nullable<RiceBallQuality>`). Access level was **not** changed. Called by reflection instead — `GetMethod("SpawnBallEntity", NonPublic|Instance).Invoke(dropper, new object[]{ null })` — 2000 calls in ~35 ms, no code modified. `SpawnBallEntity` itself never touches `playerData` (only `SpawnBallFromInventory` does), which is why inventory-less spawning works.
- **2000 concurrent balls is inherently transient.** Balls are not retained: board wall bounds are Y[-15.5, 20.5] with no catch basin, so they drain out the bottom and free-fall. `RiceBallDeletionSystem` culls at **Y < -200** (hardcoded; note `RiceBallLifetime.DestroyBelowY = -20` is written at spawn but never read), giving ~6.6 s of free-fall before deletion, and `RiceBallCleanupSystem` culls at the 30 s lifetime. Measured: 2000 to 0 balls in under ~9 s. A sustained 2000-ball steady state needs continuous re-spawn; a single burst only gives a decaying window. Real gameplay trickles 1 ball per `DropInterval` (0.25 s), so 2000 concurrent is a pure stress target, not a reachable gameplay state.
- **`Unity_RunCommand` gotchas** (these cost real time — read before scripting the Editor again): the dynamic assembly has **no compile-time reference to `Assembly-CSharp` or `Unity.Entities`**, so `using Vampire.DropPuzzle;` fails and everything must go through `System.Type.GetType("Vampire.DropPuzzle.X, Assembly-CSharp")` reflection. Extra `using` directives (`using System;`, `using System.Text;`, `using System.Reflection;`) make the wrapper throw a bare NRE *before your code runs* — keep only `using UnityEngine; using UnityEditor;` and fully-qualify everything else. `System.Diagnostics.Stopwatch` is not referenced. `World.Systems` is a `NoAllocReadOnlyCollection` that throws if cast to `IEnumerable` — index it via `Count` / `Item`. ISystem (unmanaged) systems do not appear in `World.Systems` at all.
- **BLOCKER: the `Unity_Profiler_*` MCP tools are non-functional here.** Every one returns `Error executing tool: Exception has been thrown by the target of an invocation` — including the zero-argument `GetOverallGcAllocations`, and regardless of frame indices, whether the profiler is recording, or whether the Profiler window is open. Step 5 of the protocol above cannot be executed as written. Working alternative: read `UnityEditorInternal.ProfilerDriver.GetRawFrameDataView(frame, 0)` from a `Unity_RunCommand` script (`.frameTimeMs`, `.sampleCount`, `.GetSampleName(i)`, `.GetSampleTimeMs(i)`) and aggregate per-system totals manually. Arming recording *from script* proved unreliable — `ClearAllFrames()` + `enabled = true` repeatedly came back with `firstFrameIndex = -1`, especially once the Profiler window was open — and the 2000-frame ring buffer scrolls the window out in ~10 s at the ~200 fps the scene idles at. **Have a human press Record in the Profiler window**, then spawn, then stop; the frames are then readable via RawFrameDataView.
### MEASURED (2026-09-08) — `RiceBallCollisionSystem` is NOT the bottleneck
Method: `DropPuzzle` opened directly, Play Mode, human pressed Record in the Profiler window (deep profile OFF), 2000 balls spawned in one frame by reflection on `SpawnBallEntity`, ~5 s window, frames read back via `ProfilerDriver.GetRawFrameDataView`. Spawn marker = profiler frame 3554. Saved capture: `vamp4_2026-09-08_14-16-03`. **Editor Play Mode, not a build — treat absolutes as inflated and compare columns, not rows.**

| | baseline (0 balls, 119 frames) | 2000 balls (398 frames) |
|---|---|---|
| avg frame | 12.34 ms | 29.24 ms (~26 ms excl. artifact) |
| median | 10.74 ms | **24.77 ms** |
| p95 | 24.07 ms | 44.55 ms |
| avg FPS | 81 | **34** |

**The 1217.70 ms max frame (f3649) is measurement artifact, not the game — ignore it.** Breakdown: 1166.79 ms sat inside `Update.ScriptRunDelayedTasks` -> `UnitySynchronizationContext.ExecuteTasks()` with 185,754 `GC.Alloc` samples in that single frame; that is the unity-mcp command pump, i.e. our own tooling. It alone inflates the average by ~3 ms and accounts for 185,754 of the window's 234,436 `GC.Alloc` samples.

Per-system main-thread cost, avg ms/frame:

| System | 2000 balls | baseline |
|---|---|---|
| **`RiceBallGateInteractionSystem`** | **3.999** | 0.042 |
| `RiceBallRendererECS` | 1.120 | 0.042 |
| `RiceBallCollisionSystem` | **0.732** | 0.000 |
| `SyncBallTransformSystem` | 0.166 | 0.001 |
| `RiceBallPhysicsSystem` | 0.073 | 0.001 |
| `RiceBallDeletionSystem` | 0.017 | 0.001 |
| `RiceBallWallCollisionECSSystem` | 0.016 | 0.000 |
| `RiceBallCleanupSystem` | 0.009 | 0.000 |

Confirmed on individual frames, where it is the largest single game-code item every time: f3600 (15.57 ms total) -> `RiceBallGateInteractionSystem.FixedUpdate()` 2.95 ms; f3700 (18.29 ms total) -> 3.50 ms.

- **Conclusion: `RiceBallCollisionSystem` costs 0.73 ms/frame — about 3% of a 25 ms frame, and 5.5x cheaper than the gate system.** The optimisation work in `11d77d0` / `59c1b3e` was aimed at the wrong target. Do **not** spend effort parallelising the spatial hash, shrinking the 3x3 neighbour radius, or swapping out `Allocator.Temp` (all previously floated above) — the data does not support any of it. For the record it is enabled, Burst-compiled, single-threaded on the main thread, and its `NativeParallelMultiHashMap` starts at capacity 1000 and must grow at 2000 balls — and it still does not matter at this ball count.
- [ ] **Real hot path: `RiceBallGateInteractionSystem.FixedUpdate()`** (`RiceBallGateInteractionSystem.cs:86`) — a MonoBehaviour at 50 Hz, ticking more than once per rendered frame at this load (530 samples across 398 frames). Four compounding problems, in cost order:
  1. `:104-106` — three full `ToEntityArray` / `ToComponentDataArray<LocalTransform>` / `ToComponentDataArray<RiceBallGateTracker>` copies of **all 2000 balls** into `Allocator.Temp` every tick. Each is a sync point that stalls on outstanding ECS jobs.
  2. `:124` — `gate.GetComponent<Collider>()` called **inside the per-ball x per-gate double loop**: 2000 x gateCount `GetComponent` calls per tick.
  3. `:149` — the same `GetComponent<Collider>()` again inside the goal-gate loop.
  4. `:157` — `_em.GetComponentData<RiceBallType>(entity)` per scoring ball, another sync point each.
  Cheapest real win, not applied: cache the colliders alongside the gate arrays already cached in `RefreshGates()` (`:82-83`) and hoist both `GetComponent<Collider>()` calls out of the loops entirely. Only then consider jobifying the containment test.
- GC: excluding the tooling artifact frame, ~123 `GC.Alloc` samples/frame (48,682 across 397 frames) vs ~24/frame at baseline; sampled typical frames showed 27 and 245. Real but secondary to the CPU cost, and most plausibly the per-tick Temp arrays plus the `GetComponent` path above.
- Context for the 60 fps target: median frame is 24.77 ms against a 16.67 ms budget. But baseline with **zero** balls is already 10.74 ms median in the Editor, and non-ball UI work is visible in the same frames (`UGUI.Rendering.UpdateBatches` 3.32 ms, `TMP Layout Text` 2.16 ms across 21 calls). Re-measure in a build before treating any absolute number as the real budget.

### FIXED (`79777e3`) — gate-system hot path (`RiceBallGateInteractionSystem`)
Applied and re-measured with the identical 2000-ball procedure. **`RiceBallGateInteractionSystem`: 3.999 -> 1.591 ms/frame (-60%, 2.5x faster).** Frame median 24.77 -> 22.51 ms. `RiceBallCollisionSystem` unchanged at ~0.73 ms, confirming it was never the problem. No new console errors; `ValidateScript` clean.

What changed, all in `RiceBallGateInteractionSystem.cs`:
- `RefreshGates()` now resolves each gate's `Collider` once into `_multiplierColliders` / `_goalColliders`, parallel to the existing gate arrays. `GetComponent<Collider>()` is gone from both inner loops — it was running ballCount x gateCount times *per physics step*.
- New per-step prepass computes `_multiplierUsable` / `_multiplierBounds` (and the goal equivalents) once per FixedUpdate — gateCount iterations instead of ballCount x gateCount. `Collider.bounds` is still re-read every step, so gates moved or toggled by the pre-drop roll still behave correctly. Arrays stay index-parallel because the gate's identity bit is `1 << g`.
- Early-out when no gate is usable, which skips the full-array ECS snapshots (each one a sync point) entirely.
- `RiceBallType` added to `_ballQuery` and batched via `ToComponentDataArray`, replacing the per-scoring-ball `_em.GetComponentData<RiceBallType>()` — every ball eventually scores, so that was ~1 ECS random-access read per ball.
- Active-scene test caches on `Scene.handle`; `GetActiveScene().name` was allocating a managed string every FixedUpdate.

### IMPORTANT — most of the measured frame time is the Editor, not the game
The "1-second hard stalls" visible while testing are **not game code**. Per-frame split across the 398-frame ball window: `EditorLoop` **10.46 ms/frame (36%)**, unity-mcp command pump 2.46 ms/frame (8%), `PlayerLoop` (the actual game) 18.36 ms/frame (63%). Every stall frame breaks down the same way:

| frame | total | EditorLoop | PlayerLoop | mcp pump |
|---|---|---|---|---|
| f5943 | 168.2 ms | **142.3** | 25.3 | 0.1 |
| f6079 | 118.7 ms | **102.4** | 15.9 | 0.0 |
| f6173 | 617.1 ms | **597.0** | 19.4 | 0.0 |
| f6222 | 1008.5 ms | 38.5 | 969.4 | **953.3** |

Three of four are Editor UI / Profiler-window repaint; the ~1 s one is the MCP pump, i.e. the measuring apparatus. **None of this exists in a build.** Consequence for future sessions: do not chase stalls seen while the Profiler window is open and an agent is issuing MCP calls — the sampling itself costs more than the thing being sampled at this entity count (7,641 profiler samples in one stall frame, 193,082 in the pump frame). Any further perf work on this feature needs a **standalone build** to get a trustworthy number.
- [ ] Next candidates, if a build shows the game still misses budget (in measured order, post-fix): `RiceBallGateInteractionSystem` 1.591 ms — remaining cost is now the four `ToComponentDataArray`/`ToEntityArray` snapshots of every ball per FixedUpdate, which would need jobifying or chunk iteration to remove; `RiceBallRendererECS` 1.275 ms; `RiceBallCollisionSystem` 0.749 ms (leave alone). GC is ~116 samples/frame, down slightly from ~123.

## 2c. Ramp/ball visual gap — root cause found, fix deferred (owner: you)
- [ ] Balls rest a visible ~1.5 ball-diameter gap off the diagonal ramps (most obvious on the top two 45/315-degree ramps). **Root cause confirmed, and it is authoring data, not collision code.** The OBB-vs-sphere math in `RiceBallWallCollisionSystem.cs` is correct, and every BoxCollider in `puzzle1.prefab` matches its mesh exactly (measured `DELTA=0.000` on all 9). What breaks it is `PuzzlePropDresser`: at runtime it re-skins each wall piece and sets `HideOriginalRenderer: 1`, hiding the true full-thickness box, then shrinks the replacement visual at `PuzzlePropDresser.cs:365` — `float usePlane = fitPlane * Mathf.Clamp(entry.SilhouetteScale, 0.05f, 1f);` — directly under a comment reading "In-plane always fills exactly, that's what keeps the visual hugging the collider." In `DropPuzzle.unity:1485` / `:1493` the `Shambles` -> `LongThin` bucket (which is what the diagonals resolve to; console: `smallwall (2) -> LongThin / Tier0_LongThin_CorrugatedTin`) has `SilhouetteScale: 0.25` (CorrugatedTin) and `0.15` (ScrapPlank). Collider thickness 0.75 x the x1.09 align scale = ~0.82 world units versus a ~0.20 / ~0.12 thick visual, giving ~0.31 / ~0.35 of gap per side against a ball radius of 0.1 (scene override; script default is 0.17). `WideShort` and `Chunky` are both `1.0`, which is exactly why only the thin diagonals show it.
- Two candidate fixes, **not applied — your call:** (a) data-only, set the two `LongThin` entries to `SilhouetteScale: 1`; balls land flush immediately, but the ramps go back to full chunky thickness, undoing a deliberate art choice. (b) code, make `PuzzlePropDresser` shrink the piece's `BoxCollider` in-plane by the same `SilhouetteScale` so physics follows the art; keeps the look, and the ordering works out because dressing runs inside `AlignPuzzleToWalls` while `CacheWalls` fires 0.1 s later and would pick up the resized collider.

## 2d. Camera shake pins during mass drops
- [ ] `DropPuzzleCameraController.Punch()` (`:102`) is max-based, not additive: `_shakeMag = Mathf.Min(MaxShake, Mathf.Max(_shakeMag, strength));`. `DropPuzzleJuice` calls it on **every score** (`:202`, `:208`, `:217`). Decay is `ShakeDecay = 1.4`/sec, about 0.023 per frame at 60 fps, so when hundreds of balls score per frame `_shakeMag` is re-armed to ~0.15-0.25 every frame and never decays — it reads as a continuous rumble rather than discrete kicks. Self-limits in normal play; only pins on a mass drop (observed during the 2000-ball stress spawn, which is the pathological case). Needs a design call on whether a big drop *should* rumble continuously before anything changes — e.g. a per-frame punch budget, or decay scaled to score rate.

## 3. Known-behavior review (design call, not a bug fix)
- [ ] `HelperAI.DepositCollectedRice()` (`HelperAI.cs:401`) calls `AddCurrency()` not `AddRice()`. `CLAUDE.md`'s own issue table flags this as "known behavior — fix if helpers should add to rice count instead." That's a design decision, not a confirmed bug — needs your call before any agent touches it. **Owner: you.**

## 4. Console/error cleanup
- [ ] Play-mode pass on `FPS_Collect` + `DropPuzzle` via `Unity_GetConsoleLogs`/`Unity_ReadConsole`, turned into a checklist. Needs unity-mcp — live-Editor lane, run one at a time. Model: Sonnet.

## 5. Lighting / visual bugs
- [ ] Close out the `CLUSTER_LIGHT_LOOP`/`FORWARD_PLUS`/`RealtimeLights.hlsl` investigation from earlier work. Needs unity-mcp scene capture for before/after. Model: Opus (nontrivial render-pipeline reasoning).

---

## Standing rules for every item
- Default every subagent to **Sonnet**. Escalate to Opus only for genuinely ambiguous/creative reasoning (visual/design judgment, unclear architecture tradeoffs) — not for mechanical fixes or audits.
- Static-analysis items (Read/Grep only, no MCP) can run **in parallel**. Anything touching the live Editor via unity-mcp runs **one at a time**.
- Every subagent gets a closed stop condition, not an open mandate. Point it at exact files/lines when known.
- Subagents report a short summary + commit hash back — never a pasted diff. Keeps the orchestrator's context (and usage) cheap.
- If a session runs out of usage mid-item, nothing is lost beyond the current uncommitted step — resume from this file.
