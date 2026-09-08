# Drop Puzzle — Setup Guide

Status of everything built in this work stream, what is already wired into
`Assets/Scenes/DropPuzzle.unity`, and what still needs doing.

**Read section 1 first.** Most of the wiring is already done and saved — redoing it by hand
would create duplicate components.

---

## 1. Already wired — do NOT redo

These were added via the Unity MCP bridge and the scene was saved each time. Open
`DropPuzzle.unity` and you should find them already in place.

| What | Where | State |
|---|---|---|
| `PuzzlePropDresser` | on the **`PuzzlePrefabLoader`** GameObject | Added + configured with the 4 Tier 0 props |
| `SlotMachine3Reel` | on the **`DropPuzzleManagers`** GameObject | Added + configured (3 odds tiers, reel strip) |
| Legacy `SlotSpinner` | on the **`SlotSpinner`** GameObject | **Disabled** (component kept, not deleted) |
| Main Camera | `Main Camera` | Switched to **perspective, FOV 49**, moved to Z = 28.53 |

### Verify rather than re-add

1. Select `PuzzlePrefabLoader` → confirm **PuzzlePropDresser** is present with one
   PropSet named "Shambles" holding:
   - LongThin: `Tier0_LongThin_CorrugatedTin`, `Tier0_LongThin_ScrapPlank`
   - WideShort: `Tier0_WideShort_SaggingAwning`
   - Chunky: `Tier0_Chunky_CratePile`
2. Select `DropPuzzleManagers` → confirm **SlotMachine3Reel** is present with 3 tiers
   (Level 1 / Level 2 / Level 3) and `Source = PlayerUpgrade`.
3. Select `SlotSpinner` → confirm the **SlotSpinner component checkbox is unticked**. If it
   is ticked, both slot machines will render at once.
4. Select `Main Camera` → confirm **Projection = Perspective**, **FOV = 49**.

If any of the above is missing, add the component and refer to section 5 for values.

---

## 2. Scripts created (no manual setup needed)

These compile and work with no Inspector wiring beyond what section 1 covers.

| Script | Purpose |
|---|---|
| `PuzzlePropDresser.cs` | Dresses the grey `smallwall` cubes with themed props at runtime |
| `PuzzlePropStyle.cs` | **Optional** per-cube override. Only add it to a cube you want to force |
| `SlotMachine3Reel.cs` | The 3-reel match-3 gate roll |

## 3. Scripts modified

| Script | Change | Action needed |
|---|---|---|
| `DropPuzzleCameraController.cs` | Perspective support; zoom is now a dolly. New `BoardPlaneZ` field | None — leave `BoardPlaneZ` at 0 |
| `PuzzlePrefabLoader.cs` | Background plane now resized for perspective depth | None |
| `TownSkinController.cs` | Backdrop resized for perspective depth | None |
| `GateRollController.cs` | Prefers `SlotMachine3Reel`, falls back to the old spinner | None |
| `PlayerDataManager.cs` | New `DropPuzzle.slotOddsLevel` int | None — defaults to 0 = Level 1 |

---

## 4. Still to do

### 4a. Multiplier gate prefabs — DONE

Built and assigned. Kept here for reference and for rebuilding when the art changes.

**Rebuild:** `Tools ▸ VAMP4 ▸ Build Multiplier Gate Prefabs` (`Assets/Editor/GatePrefabBuilder.cs`).
Re-export the FBX, run the menu item, done. Output goes to `Assets/Prefabs/Gates/`.

| Prefab | Solid parts (tag `Wall`) | No collider | Bonus trigger |
|---|---|---|---|
| `Gate_Tier_01_Gate` | Post ×2, PostCap ×2 | 3 | 2.78 × 2.33 × 4 |
| `Gate_Tier_02_Gate` | Post ×2, PostFinial ×2 | 20 | 2.87 × 2.60 × 4 |
| `Gate_Tier_03_Gate` | Post ×2 | 9 | 2.87 × 2.50 × 4 |

Assigned on `PuzzlePrefabLoader` → PuzzleEnhancer: **both** `Multiplier2xPrefab` and
`Multiplier3xPrefab` point at `Gate_Tier_01_Gate`. One model serves every multiplier now
that the number is TextMeshPro, so those slots are really "which town tier", not "which
value". Swap both to Tier 02 / Tier 03 as the town rebuilds.

> Found while wiring: `Multiplier3xPrefab` was **empty**, so every 3x and 4x roll fell
> through `ProcessEnhancementMarker` and the marker was destroyed — 3x gates never spawned.
> Both slots are now filled.

Scaling is handled by `PuzzleEnhancer.FitToMarkerBox()`: the gate is scaled **uniformly**
to fit inside the marker's box and re-centred on its bounds. For puzzle1's `5 × 2 × 4`
marker that is `min(5/3.22, 2/2.33, 4/0.22) = 0.86×`, height-bound, aspect preserved.

### 4a-old. The decision that led here (resolved)

`Gate_Tier_01/02/03.fbx` are imported but not rigged and not assigned.

All three share a naming convention that makes the physics rule automatable:

| Part | X position | Physics |
|---|---|---|
| `*_Post_1`, `*_Post_-1` | ±1.500 | **Solid collider**, tag `Wall` |
| `*_PostCap_*`, `*_PostFinial_*` | ±1.500 | Solid collider, tag `Wall` |
| Banner / SignPanel / Neon / Chain / Crossbar / Arm / Bracket | ≈ 0 | **No collider** |
| (generated) centre gap | 0 | **Trigger** + `MultiplierGate` |

**The blocker:** `PuzzleEnhancer.ReplaceWithEnhancement()` copies the marker's scale onto
the spawned prefab:

```csharp
enhanced.transform.localScale = scale;   // marker's scale
```

In `puzzle1` that marker is `5 × 2 × 4`, while the gate model is `3.22 × 2.33 × 0.22`.
Applied directly that stretches the gate 1.55× wide, 0.86× tall and **18× deep** — posts
smear into slabs and the banner becomes unreadable.

Pick one before the rig gets built:
- **Fit by bounds** (recommended) — scale the gate to fit the marker box preserving aspect,
  same approach `PuzzlePropDresser` uses. Marker scale keeps meaning "how big is this gate".
- **Ignore marker scale** — gates always render at authored size. Simpler, but marker scale
  silently stops doing anything.

Once decided: build rigged prefabs, then assign them to **PuzzleEnhancer** on the
`PuzzlePrefabLoader` object → `Multiplier2xPrefab` / `Multiplier3xPrefab`.

> Note: `PuzzleEnhancer` currently reuses the 3x prefab for a 4x roll
> (`label = "4x→3x"`), so a 4x gate displays as 3x. Driving the number with TextMeshPro
> instead of baked geometry fixes this.

### 4b. Interactive props — needs code that does not exist

`Interact_ConveyorSegment.fbx` and `Interact_SpinningWheel.fbx` are imported but unused.

- **Conveyor** — needs a trigger script that adds tangential velocity. `BoostBlock.cs`
  already does exactly this pattern; it's a variant, not a new system.
- **Spinning wheel** — **blocked**. `RiceBallWallCollisionSystem` caches wall data **once**
  into a persistent `NativeArray` and never updates it, so a rotating collider would spin
  visually while its collision box stayed frozen. Needs a dynamic-wall refresh first.

### 4c. Optional tuning

- **`PuzzlePropDresser.ElongatedAspect`** is 1.8. At that value essentially no puzzle piece
  classifies as WideShort, so `Tier0_WideShort_SaggingAwning` never appears on any board.
  Dropping it to **~1.4** routes `5×3` (puzzle3) and `1.99×3` (puzzle2) into that bucket.
- **CorrugatedTin `RotationOffset`** is currently `(0, 90, 0)`. That gives it real depth but
  drops tiling from 5 copies to 1 and hides the corrugated read. Set back to `(0,0,0)` to
  restore the five ribbed sheets.
- **`DepthFill`** is 0 on all props. Leave it there for thin sheets — it stretches the mesh,
  and on a 0.108-thick sheet even 0.5 is a 19× stretch that inflates the corrugations.

---

## 5. Reference values

### SlotMachine3Reel

| Field | Value |
|---|---|
| PanelSize | 510 (matches WeatherWheelUI) |
| SortingOrder | 24 |
| Strip | `2, 3, 2, 5, 2, 3, 2, 5, 3, 2` |
| DimBackground / DimAlpha | true / 0.92 |
| Source | PlayerUpgrade |
| RequireKeyPress / StartKey | true / Space |
| InputGraceTime | 0.25 |
| BaseSpinTime / ReelStagger | 0.85 / 0.38 |
| SuspenseTime / SlowMoSpeedFactor | 0.7 / 0.08 |
| SettleTime / ResultHold | 0.35 / 0.7 |
| NearMissChance | 0.55 |

Odds tiers (weights read directly as percentages):

| Level | No Match | 2x | 3x | 5x | Win rate |
|---|---|---|---|---|---|
| 1 (default) | 96 | 3 | 0.9 | 0.1 | 4% |
| 2 | 85 | 11 | 3 | 1 | 15% |
| 3 (target) | 70 | 25 | 3 | 2 | 30% |

Level is read from `PlayerDataManager.DropPuzzle.slotOddsLevel`. Set `Source = Manual` and
use `ManualTier` to test a level without an upgrade.

### Camera

| Field | Value |
|---|---|
| Projection | Perspective |
| Field of View | 49 |
| Position Z | 28.53 |
| `DropPuzzleCameraController.OverviewOrthoSize` | 13 (still the framing control) |
| `DropPuzzleCameraController.BoardPlaneZ` | 0 |

FOV 49 at Z 28.53 frames the board plane identically to the old orthographic size of 13.
Changing FOV alone will re-frame the board — the controller dollies to compensate at
runtime, but the scene-view position won't match until you press Play.

---

## 6. Quick test

1. Open `DropPuzzle.unity`, press Play.
2. The slot modal should appear over a **blacked-out** screen with `PRESS SPACE TO ROLL`.
3. Press Space **once**. Reels spin and stop left → right.
4. If the first two match, the third drops into slow motion.
5. Modal dismisses. On a win, gates pop into the board at the matched multiplier; on a
   loss, no gates appear.
6. Weather roll follows, then prep effects, then player control.

Console should show `[PuzzlePropDresser] Dressed 6/6 pieces` with **no overhang warnings**.

**Do not call `GateRollController.StartRoll()` manually** while the flow manager is running
— that produces two concurrent rolls.
