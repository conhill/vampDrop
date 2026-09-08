# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**VAMP4** is a Unity 2022.3 LTS roguelite combining FPS rice collection with a Plinko-style drop puzzle. Uses Unity ECS/DOTS (Entities 1.0+) for high-performance entity simulation.

**Core game loop:** Collect rice (FPS) → Craft riceballs (5:1) → Drop through gates → Earn currency → Buy upgrades → Repeat

---

## Development Workflow

### Testing in Unity Play Mode
There is no CLI build command — development happens directly in the Unity Editor (Unity 2022.3 LTS).

**Debug hotkeys (work in any scene):**
- `G` — Add 50 rice
- `E` — Craft riceballs
- `C` — Add $100 currency
- `R` — Reset all progress (destructive)
- `SPACE` — Drop balls (DropPuzzle scene)
- `1–5` — Buy upgrades

**Quick drop test (30 seconds):** Open `DropPuzzle.unity` → Play → `G` → `E` → `SPACE`

### Scene Build Order
1. `ComicScene` — Narrative cutscenes
2. `FPS_Collect` — Rice collection phase
3. `DropPuzzle` — Ball drop phase

---

## Architecture

### Namespaces
All scripts use namespaces: `Vampire.DropPuzzle` (most systems), `Vampire.Helpers` (HelperAI), `Vampire.Rice` (rice ECS components).

### Persistent Systems (DontDestroyOnLoad)
The `GameSystems` GameObject persists across all scenes and carries these components:
- `PlayerDataManager` — Master data store: rice count, riceball inventory, currency, stats
- `UpgradeShop` — Purchase logic, price scaling (base × scalingFactor^level)
- `ProgressionSystem` — Bridges upgrades → gameplay effects (quality chances, gate configs)
- `DayNightCycleManager` — 5-minute countdown timer for collection phase; `CanEnterBallDrop()` gates night-only access
- `TutorialManager` — Quest flow, unlocks `BuyZone` and `FlinkCharacter` after tutorial completes; `tutorialStep` suppresses UI elements during early steps
- `QuestManager` — Tracks one active quest at a time; progress for `CraftRiceBalls`/`CollectCurrency` types is initialized from lifetime stats (not 0) so they reflect cumulative progress
- `DebugUpgradeUI` — In-editor testing interface

### ECS Architecture Pattern
The project uses **hybrid ECS/MonoBehaviour**:
- **ECS (ISystem + IJobEntity + Burst):** Hot paths — physics simulation, hover detection, rice lifecycle
- **MonoBehaviour:** Unity integration — triggers, UI, scene management, gate detection

**Key ECS rule:** Use `ISystem` over `SystemBase`, `IEnableableComponent` instead of Add/Remove for toggleable state (avoids archetype moves = 1000× faster), `EntityCommandBuffer` for structural changes from jobs.

### Rice Spawning (FPS_Collect scene)
- `RiceSpawnerECS` spawns up to 40,000 rice entities with random positions via raycast onto ground
- `RiceHoverHighlightSystem` — Burst-compiled parallel job, uses `IEnableableComponent` for highlight toggling (no structural changes)
- `RiceCollectionSystem` — Burst parallel job finds clicked rice via ray-sphere math (no `Physics.OverlapSphere`)
- Entity↔GameObject bridging via `RiceEntityRef` component or dictionary lookup
- When transitioning to DropPuzzle: add `RiceHidden` tag (don't destroy); remove it on return

### Ball Drop (DropPuzzle scene)
- `DropperControllerECS` spawns `RiceBallPhysics` entities from inventory
- `RiceBallPhysicsSystem` — gravity, drag (0.99/frame), wall bounce with energy loss
- `RiceBallRendererECS` — GPU instanced rendering in batches of 1023 (`Graphics.DrawMeshInstanced`)
- `RiceBallCleanupSystem` — destroys entities that fall below floor Y or exceed lifetime
- Gates use MonoBehaviour `OnTriggerEnter` with `RiceBallEntityRef` to find and score the ECS entity
- `BallDropCompletionManager` — polls every 0.5s; fires `OnDropComplete` when all balls are scored or stuck (≥3.5s without movement). Uses cached `EntityQuery` to avoid per-frame sync points. Also handles daylight-forced salvage via `DayNightCycleManager` events.
- `PhysicsOptimizer` — applied in DropPuzzle scene; reduces solver iterations to 3, sets `Physics.autoSyncTransforms = false`, runs `FindObjectsOfType<RiceBall>` cleanup every 120 frames as a fallback.
- Scene entry: `BallDropSceneEntry` (trigger + F key in FPS scene); denies entry during day or with zero riceballs.

### Helper System (FPS_Collect scene)
- `HelperAI` — NavMesh-based goblin/ghoul that auto-collects rice; states: Idle → Seeking → Collecting → Returning → Depositing. Queries ECS rice entities via `Vampire.Rice.RiceEntity` component. Deposits collected rice via `PlayerDataManager.AddCurrency()` (note: deposits as currency, not rice count).
- `HelperDeploymentSystem` / `HelperShop` — manage deploying helpers to named zones.

### Quality & Economy
Quality rolls: Fine 70% (1×), Good 20% (2×), Great 8% (3×), Excellent 2% (5×)
Upgrades shift these percentages upward via `ProgressionSystem.GetQualityChances()`
Score = `ball quality value × gate multiplier`; awarded via `PlayerDataManager.Instance.AddCurrency()`

### Comic System
ScriptableObject-based cutscenes (`ComicSequenceConfig`). Load with:
```csharp
ComicSceneLoader.LoadComic(myConfig); // sets static CurrentSequence, loads ComicScene
```
Panels use `PanelWidthMode` (FullScreen/HalfScreen/Custom) and `ElementSizeMode` (FillPanel/FitToSprite/Custom). 12 animation types available. The config's `nextSceneName` field controls where to go after the comic.

---

## Key Script Locations

```
Assets/Scripts/
├── PlayerDataManager.cs         # Master data store (singleton, DontDestroyOnLoad)
├── UpgradeShop.cs               # Purchase logic, upgrade dictionary
├── ProgressionSystem.cs         # Upgrade → gameplay effect bridge
├── RiceCollectionSystem.cs      # ECS hover highlight + click collection (Burst)
├── RiceCraftingSystem.cs        # 5 rice → 1 riceball UI + quality roll
├── DropperControllerECS.cs      # Spawns riceball entities from inventory
├── RiceBallPhysicsECS.cs        # ECS physics simulation
├── RiceBallRendererECS.cs       # GPU instanced rendering
├── BallDropUI.cs                # Drop scene UI, scene transitions
└── ComicScene/
    ├── ComicSceneManager.cs     # Horizontal scroll controller
    ├── ComicSequenceConfig.cs   # ScriptableObject data
    └── ComicSceneLoader.cs      # Static load helper
```

---

## Extending the Game

**New shop upgrade:** Add entry to `UpgradeShop.InitializeUpgrades()` → apply effect in `ProgressionSystem.UpdateProgression()` → call `ProgressionSystem.Instance.UpdateProgression()` after purchase.

**New system (persistent):** Create MonoBehaviour manager → attach to `GameSystems` GameObject → add `DontDestroyOnLoad` in `Awake()`.

**New ECS component for toggleable state:** Implement `IComponentData, IEnableableComponent` → pre-add in Baker with `SetComponentEnabled(..., false)` → toggle with `state.EntityManager.SetComponentEnabled<T>(entity, bool)`.

---

## Required Tags & Layers

**Tags:** `Wall`, `RiceGrain`, `Player`, `Gate`, `RiceBall`
**Layers:** Default (0), UI (5)

## Common Setup Issues

| Problem | Fix |
|---------|-----|
| "PlayerDataManager not found" | Add all persistent components to `GameSystems` GameObject (PlayerDataManager, UpgradeShop, ProgressionSystem, DayNightCycleManager, TutorialManager, QuestManager, DebugUpgradeUI) |
| Balls invisible | Assign material with GPU Instancing enabled to `BallMeshSetup` and `RiceBallRendererECS` |
| Rice persists in DropPuzzle | Call `HideRiceEntities()` on scene exit, `UnhideRiceEntities()` on return |
| Gates not detecting balls | Check `RiceBallEntityRef` is attached; verify trigger is enabled |
| Ball drop never completes | Ensure `BallDropCompletionManager` is in scene and `StartDropSession()` is called when balls are dropped |
| HelperAI deposits rice as currency | Known behavior — `DepositCollectedRice()` calls `AddCurrency()` not `AddRice()`; fix if helpers should add to rice count instead |
| Ball drop inaccessible | Check `DayNightCycleManager.CanEnterBallDrop()` — entry is night-only |

---