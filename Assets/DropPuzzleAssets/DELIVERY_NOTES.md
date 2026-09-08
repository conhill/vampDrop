# Drop Puzzle — Tier 0 + Interactive: Delivery Notes

Drop location: copy `Tier0/` and `Interactive/` into `Assets/RiceballKit/`.

All FBX: Z-up Blender source, exported `axis_up=Y / axis_forward=-Z` (default Unity import),
scale + rotation applied, textures embedded **and** supplied loose alongside.
All materials are plain Principled BSDF → import as **URP/Lit**; property names match the
`Vampire/LivingSurface` upgrade path.

Axis mapping reminder: Blender X → Unity X, Blender Z → Unity Y (long axis), Blender Y → Unity Z (depth).

---

## Tier 0 — fitted by `PuzzlePropDresser`

| Prop | Tris | Unity X (thickness) | Unity Y (long) | Unity Z (depth) | Repeats on a 6-unit deflector |
|---|---|---|---|---|---|
| `Tier0_LongThin_CorrugatedTin` | 264 | 0.75 | 1.13 | 0.11 | ~5 |
| `Tier0_LongThin_ScrapPlank` | 254 | 0.75 | 1.50 | 0.06 | ~4 |
| `Tier0_WideShort_SaggingAwning` | 226 | 1.27 (long axis = +X) | 1.53 (height) | 0.23 | ~3 |
| `Tier0_Chunky_CratePile` | 192 | 1.13 | 1.76 | 0.92 | n/a (stretch-fitted) |

- All within the ≤400 tri tiled-module budget (≤1500 for Chunky).
- No geometry extends past the X/Y bounding box — no overhang warnings expected.
- Both LongThin modules have flat, flush ends on the long axis for seam-free tiling.
- CorrugatedTin rust is weighted to **both ends**, so tiled seams read as intentional joints.
- One UV set per prop, one material per prop.

## Interactive — hand-placed, NOT dresser-fitted

### `Interact_ConveyorSegment.fbx`
Two objects in one FBX: `..._Belt` and `..._Frame`.

- **Size:** 2.42 long × 0.90 deep × 1.42 tall (incl. leg). Sits against a 0.75-thick deflector.
- **Origin:** centre of the belt run, on the belt surface plane. Belt Z spans −0.41 → **0.00**,
  i.e. the top belt surface is exactly at the origin plane.
- **SCROLL AXIS: U.** 24 cleats across U 0→1; the belt island spans the full 0–1 U range and
  wraps seamlessly, so a continuous U offset loops with no visible jump.
- **Two materials on purpose.** UV scroll is a material property — a single shared material
  would drag the rails and rollers along with the belt. Scroll `Interact_ConveyorSegment_Belt_mat`
  only; `..._Frame_mat` stays static.
- Every belt face (including the side edges) shares the same arc-length U parameterisation, so
  one scroll value animates all visible surfaces coherently. **The board camera sees the belt
  edge-on**, so the treads are authored to read on that edge profile, not the broad surface.

### `Interact_SpinningWheel.fbx`
- **Size:** 1.30 diameter × 0.18 deep (≈1.7× a deflector's 0.75 thickness).
- **Origin:** verified **exactly** at the geometric centre (0.000, 0.000, 0.000 offset) on the
  rotation axis.
- **Rotation axis:** Blender Y → **Unity local Z**. Rotate about Z for the down-−Z board camera.
- **Rotational symmetry is broken three ways** so it doesn't look static while spinning:
  one spoke missing, one snapped to a stub, and the iron tyre band lifting off the felloe at one point.
- 484 tris, single material.

---

## NOT delivered

- **`Interact_AlleyAnimal` (dog/cat), `Interact_WindowThrower`** — dropped. These are organic
  characters and the procedural approach used here did not produce acceptable results.
  They need a different authoring method (sculpt or hand-modelled).
- **Remaining Tier 0:** `PipeRun`, `SheetMetalSlab`, `RubbleHeap`, `BoardedPlatform`,
  `BinCluster`, `RebarBundle` — not started.
- **Remaining interactive:** `Seesaw`, `BreakableRoof` — not started (both hard-surface, viable).

The four Tier 0 props delivered cover **all three buckets**, so no piece on the board falls back
to a grey cube.
