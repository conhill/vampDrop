# Drop Puzzle — Tier 0 "Shambles" Asset Spec

Assets for the VAMP4 drop-puzzle board. The balls fall down an **alley between two
buildings** in a town that is currently in shambles. These props are the junk that
collapsed into that alley and now deflects the falling rice balls.

Consumed by `PuzzlePropDresser` (`Assets/Scripts/PuzzlePropDresser.cs`), which fits props
onto invisible collider boxes at runtime. **Read the Global Rules first** — the fitter
imposes real constraints, and a prop that ignores them will look wrong in ways that are
not obvious in Blender.

---

## 1. Global Rules

### 1.1 Orientation

Model with the long axis **pointing up (+Z in Blender)**. On default FBX import that
becomes **+Y in Unity**, which is what the fitter expects.

| Bucket | Unity long axis | Unity thickness | Unity depth |
|---|---|---|---|
| **LongThin** (beams, planks, sheets) | **+Y** | X | Z |
| **WideShort** (awnings, slabs) | **+X** | Y (height) | Z |
| **Chunky** (piles, bins) | n/a | — | — |

If something lands rotated, the dresser has a per-prop `RotationOffset` escape hatch —
but **only multiples of 90° are safe**. Arbitrary angles skew under the piece's
non-uniform scale and cannot be corrected.

### 1.1b REVISION — props need real extruded thickness

**This supersedes the "depth is free" guidance below for the LongThin bucket.** It was
written for an orthographic camera; the board camera is now **perspective**, and flat props
read as cards.

The delivered tin and plank are **0.108** and **0.064** thick. Every route to giving them
depth in-engine failed, and the numbers say why:

| Attempt | Result |
|---|---|
| Stretch depth in-engine (`DepthFill 0.5`) | 19× stretch — inflates each corrugation into a fat tube |
| Rotate 90° so the width becomes depth | Silhouette collapses to a sliver; tiling drops from 5 copies to 1 |
| Shrink silhouette to restore tiling | Visual ends up 0.11–0.19 wide against a 0.75 collider — balls bounce off empty space |

Edge-on, the plank is a **23:1** sliver, so "matches the collider" and "tiles several times"
are mutually exclusive. No engine-side tuning fixes it.

**Author LongThin props with a real extruded thickness of ~0.3–0.4 units** (roughly a third
of their width, not a twentieth). Then broad-face-forward gives all three at once: correct
tiling, visible depth under perspective, and a silhouette that matches its collider.

Applies to `CorrugatedTin`, `ScrapPlank`, and any future LongThin prop. Chunky props already
have volume; WideShort should get the same treatment where it's a flat panel.

### 1.2 The silhouette is everything; depth is free

The board camera is **orthographic, looking straight down −Z**. Balls travel on the Z=0
plane. Consequences:

- **Only the X/Y silhouette matters.** All readability, all theming, all
  recognisability lives in the outline as seen head-on.
- **The back face is never seen.** Do not spend geometry on it.
- **Z depth is stretched by the fitter and is invisible.** A piece is 4 units deep vs
  0.75 wide, so props get pulled deep. Nobody will ever see it. **Do not compensate.**

### 1.3 Nothing may poke out of the bounding box

The fitter maps each prop into an invisible collider box, and that box is what balls
actually bounce off. A prop whose silhouette extends past it makes balls look like they
bounce off thin air — the single fastest way to make a Plinko board feel like it's
cheating. The dresser logs a warning past 15% overhang.

**Rule: no nails, hooks, wires, or splinters extending beyond the X/Y bounding box.**
Interior detail is free. Protrusions are not. Cut them, or fold them inward.

### 1.4 Tiling — the repeat-count rule

Long props are **tiled**: the fitter repeats one module end-to-end down the piece, so a
6-unit deflector becomes a row of planks, not one absurdly stretched mesh.

The repeat count falls out of the module's own proportions. For a standard deflector
(0.75 wide × 6 long):

```
repeats  ≈  8 × (module width / module length)
```

| Module length : width | Repeats on a 6-unit deflector |
|---|---|
| 1 : 1 (chunky block) | ~8 |
| 2 : 1 (short plank)  | ~4 |
| 4 : 1 (long plank)   | ~2 |

**Aim for 3–6 repeats** — enough rhythm to read as constructed, not so many it turns to
visual noise. That means **length ≈ 1.3–2.5× width**.

Tiling requirements:
- **Flat, flush ends** at both extremes of the long axis. Any lip or bevel at the ends
  creates a visible seam or overlap when modules butt together.
- **The module must read correctly when mirrored** on its long axis — the dresser
  randomly flips copies so a repeated prop doesn't look like wallpaper.
- Keep depth ≤ 5× the width, or depth becomes the limiting axis and the module shrinks.

### 1.5 Budgets and materials

- **≤ 400 tris** per tiled module (they multiply — worst case ~80 instances on screen).
- **≤ 1500 tris** for one-off Chunky hero pieces.
- **Single material per prop.** Share one material across the whole tier set if you can —
  it batches, and the whole set can be re-skinned in one edit.
- Ship on **URP/Lit**. The project has a custom `Vampire/LivingSurface` shader with a
  procedural wear layer (water streaks, grime, damp staining) that reuses URP Lit
  property names, so materials can be upgraded later with no re-authoring.
- **One UV set, non-overlapping.** A single 1K–2K atlas for the tier is ideal.
- Renderer is **URP Forward+**. Do not author custom shaders for these — plain Lit only.

### 1.6 Naming

`Tier0_<Bucket>_<Name>` — e.g. `Tier0_LongThin_ScrapPlank`, `Tier0_Chunky_CratePile`.

---

## 2. Tier 0 Asset List

Overall art direction: **improvised, salvaged, nothing built on purpose.** Everything
here was scavenged and wedged into place. Wood is split and grey with age. Metal is
rusted through at the edges. Nothing is painted except where paint is peeling off
something that used to be painted. No clean right angles — every piece should look
slightly wrong, like it was cut with the wrong tool by someone in a hurry.

Keep values **mid-to-dark** — the balls are bright and must pop against this. Nothing in
this set should be light enough to compete with a rice ball for attention.

---

### LongThin bucket — the diagonal deflectors

These are the workhorses. Most of the board is these, at various angles.

#### 2.1 `Tier0_LongThin_ScrapPlank`

- **Proportions:** length ≈ 2× width. Target ~4 repeats.
- **Form:** A single rough-sawn board, laid flat. Slightly cupped along its length —
  not a perfect rectangle. One long edge should be visibly less straight than the other.
- **Detail:** Deep grain running the long axis. Two or three splits at the ends. One
  corner broken off entirely (but *inside* the bounding box — see 1.3). A couple of nail
  heads, sunk flush, rust-stained streaks bleeding downward from them.
- **Colour:** Weathered grey-brown. Bleached on the exposed face, darker in the grain.
- **Tiling note:** Ends flush and square. Vary the grain enough that a mirrored copy
  doesn't obviously twin.

#### 2.2 `Tier0_LongThin_CorrugatedTin`

- **Proportions:** length ≈ 1.5× width. Target ~5 repeats.
- **Form:** A section of corrugated roofing sheet. The corrugation runs **across** the
  short axis (perpendicular to the run) so the repeats create a strong ribbed rhythm
  down the deflector.
- **Detail:** 4–6 corrugation ridges per module. One ridge dented inward. Edges ragged
  and slightly curled — but curled *inward*, never past the bounding box. A bolt or two
  through the flats.
- **Colour:** Galvanised grey going to orange-brown rust from the edges inward. Rust
  should be heaviest at the ends so the tiled seams read as intentional joints.
- **Why it matters:** This is the most "town in shambles" read in the set. Make it the
  strongest silhouette.

#### 2.3 `Tier0_LongThin_PipeRun`

- **Proportions:** length ≈ 2.5× width. Target ~3 repeats.
- **Form:** A run of salvaged pipe with a coupling collar at one end. The collar is the
  tiling seam — it should look like the *reason* the modules join there.
- **Detail:** Slightly out of round. A wire or rag wrapped near the coupling. Dents
  along the length. Keep it a simple cylinder, low segment count (8–10 sides is plenty
  at this scale).
- **Colour:** Dark iron, rust blooming at the coupling and dripping down.
- **Note:** The coupling is the one place a small protrusion is acceptable, because it
  reads as part of the pipe's own diameter. Keep it under 15% of the width.

#### 2.4 `Tier0_LongThin_RebarBundle`

- **Proportions:** length ≈ 1.3× width. Target ~6 repeats.
- **Form:** Three or four lengths of rebar lashed together with wire. Uneven — one bar
  sits proud of the others.
- **Detail:** Ribbed rebar texture (can be normal-mapped rather than modelled). Wire
  wrap at the tiling seam. Bars should not be perfectly parallel.
- **Colour:** Uniform heavy rust, nearly matte.
- **Purpose:** The busiest, most chaotic option in the bucket — use it sparingly via a
  low `Weight` so it reads as an accent, not the default.

---

### WideShort bucket — the flat shelves and ledges

Fewer of these on a board, but they're bigger and catch the eye. Target box is roughly
**5 wide × 2 tall × 4 deep** — remember the long axis here is **+X**.

#### 2.5 `Tier0_WideShort_SaggingAwning`

- **Proportions:** length ≈ 0.8× height. Target ~3 repeats.
- **Form:** A stretch of fabric awning over a bent metal frame. The fabric **sags
  between supports** — this is the whole point of the asset. Each module is one bay of
  the awning; the sag makes the repeat read as structural rather than tiled.
- **Detail:** Frame tube visible at the module edges (the seam), fabric slumping between.
  One tear with the flap hanging (inside the bounding box). Fabric should be visibly
  slack, not taut.
- **Colour:** Faded stripes — once red-and-white or green-and-white, now dust-grey with
  the pattern only just legible. Water stains pooling in the low points of the sag.
- **Why it matters:** The most legible "this was a shop once" signal in the whole set.

#### 2.6 `Tier0_WideShort_BoardedPlatform`

- **Proportions:** length ≈ 1× height. Target ~2–3 repeats.
- **Form:** A crude platform of mismatched boards nailed across two joists. Boards run
  **across** the long axis. Gaps between them.
- **Detail:** Boards of visibly different widths and thicknesses. One board missing,
  leaving a gap through to the joist. Nail heads, some bent over.
- **Colour:** Mixed woods — some grey and weathered, one noticeably newer and paler, as
  if patched recently. That single fresh board is a nice hint that someone is *trying*.

#### 2.7 `Tier0_WideShort_SheetMetalSlab`

- **Proportions:** length ≈ 1.2× height. Target ~2 repeats.
- **Form:** A flat panel of salvaged sheet metal — a door, a sign, a vehicle panel —
  wedged horizontally. Read: something big and flat that got repurposed.
- **Detail:** A bolt pattern or hinge scar suggesting a previous life. One edge folded
  over for rigidity. Slightly bowed under its own weight.
- **Colour:** Peeling paint over rust — a strong candidate for a ghost of old signage or
  lettering, mostly illegible. Keep any text abstract; it shouldn't read as any real
  language or brand.

---

### Chunky bucket — the blobs

Roughly square pieces. These are usually **stretch-fitted rather than tiled**, so they
need to survive being squashed on one axis. Avoid strong regular patterns that would
make the distortion obvious.

#### 2.8 `Tier0_Chunky_CratePile`

- **Form:** Three or four wooden crates stacked at angles — none square to another. The
  stack should look like it's about to go over.
- **Detail:** Slat construction with visible gaps. One crate stove in on one side. A
  broken slat. Keep the outer silhouette compact and roughly square.
- **Colour:** Pale dry timber, darker where damp has wicked up from the bottom crate.

#### 2.9 `Tier0_Chunky_RubbleHeap`

- **Form:** A heap of broken masonry and brick. The most forgiving asset in the set —
  irregular by nature, so distortion won't show.
- **Detail:** Mixed brick fragments and chunks of rendered concrete with the render
  still attached to some faces. A twist of rebar emerging (inside the box). Dust and
  smaller debris at the base.
- **Colour:** Dull terracotta brick against grey concrete. Dusty, low saturation.

#### 2.10 `Tier0_Chunky_BinCluster`

- **Form:** Two or three dented metal bins wedged together, one on its side.
- **Detail:** Lids — one missing, one askew. Deep dents. A handle bent out of shape
  (folded inward, not protruding).
- **Colour:** Galvanised grey, heavy rust at the base rims where they've stood in water.

---

## 3. Delivery

- **Format:** FBX, one file per prop, Z-up (Blender default). No need to pre-rotate.
- **Scale:** Any consistent scale — the fitter normalises everything. Roughly 1–2 m per
  module keeps things sane to inspect.
- **Origin:** Anywhere sensible; the fitter works from combined renderer bounds, not the
  pivot. Object centre is fine.
- **Transforms:** Apply scale and rotation before export.
- **Textures:** Alongside the FBX, or atlased per tier.
- **Drop location:** `Assets/RiceballKit/` alongside the existing Village/Industrial
  modules, or a new `Assets/RiceballKit/Tier0/`.

---

## 4. Priority

If the whole set is too much for one pass, build in this order — these four alone will
carry the board:

1. **`Tier0_LongThin_CorrugatedTin`** — most deflectors are LongThin, and this is the
   strongest shambles read in the set
2. **`Tier0_LongThin_ScrapPlank`** — the necessary contrast to the tin
3. **`Tier0_WideShort_SaggingAwning`** — the "this was a shop" signal
4. **`Tier0_Chunky_CratePile`** — fills the remaining pieces

That covers every bucket, so no piece on the board falls back to a grey cube.

---

## 5. Later tiers (context, do not build yet)

The same layouts get re-dressed as the town recovers, so **tier 1 and 2 props must
occupy the same buckets and follow the same rules** — only the material and condition
change. Rough direction:

- **Tier 1 "Patched"** — same forms, partially repaired. Fresh timber among the grey.
  Tin sheets straightened and overlapped properly. Awning fabric patched with mismatched
  cloth. String lights appear.
- **Tier 2 "Rebuilt"** — planed and painted timber, clean galvanised steel, taut awnings
  in saturated colour, proper joinery. Neon on the signage.

The player replays identical layouts and watches them get repaired — that's the "town
thrives from your contributions" payoff, and it only works if the silhouettes stay
recognisable across tiers. **Keep tier 1 and 2 silhouettes close to their tier 0
counterparts.**

---
---

# Part 2 — Interactive Props

These are the moving/reactive board elements. **The Unity systems that drive them do not
exist yet** — the art can be built in parallel, but these props are placed by hand rather
than fitted by `PuzzlePropDresser`, so several Part 1 rules change.

## 6. Rules that differ from Part 1

### 6.1 The pivot now matters — a lot

Part 1 props are fitted from renderer bounds, so their origin is irrelevant. **These are
not.** Each one rotates or animates around a specific point, and a wrong origin means the
prop wobbles instead of spinning.

| Prop | Origin must be at |
|---|---|
| Spinning wheel | Exact centre of rotation, on the wheel's axis |
| Seesaw | The fulcrum contact point, not the plank centre |
| Conveyor | Centre of the belt run, on the belt surface plane |
| Everything else | Base/contact point, resting on the ground plane |

### 6.2 Author at real board scale

The dresser normalises Part 1 props; nothing normalises these. Author them to sit
sensibly against a **deflector that is 0.75 units thick and 4–6 units long**, and state
the intended size in the delivery notes so placement doesn't need guesswork.

### 6.3 Silhouette rules still apply, harder

Same orthographic camera, same −Z view, same "back face never seen". But because these
*move*, a confusing silhouette is worse here than anywhere else — the player is trying to
predict a bounce off a thing that is in motion. Keep outlines bold and simple.

### 6.4 Rotational symmetry must be broken

Anything that spins needs **visible asymmetry** or it looks stationary while rotating.
One broken spoke, one bright rag tied to the rim, one missing tooth. This is the single
most common failure on spinning game props.

---

## 7. Interactive Asset List

### 7.1 `Interact_SpinningWheel`

- **Form:** A salvaged cart or wagon wheel mounted on a spindle, spinning in the alley.
  Balls glance off the rim and the spokes.
- **Size:** Diameter roughly 1.5–2× a deflector's thickness run — big enough to read as
  an obstacle, small enough not to dominate the board.
- **Detail:** 6–8 spokes, wooden, **one broken or missing** (see 6.4). Iron tyre band
  around the rim, rusted and coming loose at one point. Hub with a visible spindle hole.
- **Critical:** Origin dead centre on the rotation axis. Silhouette must be circular
  enough that a ball glancing off the rim looks physically sensible.
- **Colour:** Grey weathered timber, rust-orange tyre band.

### 7.2 `Interact_ConveyorSegment`

- **Form:** A short run of scavenged industrial conveyor wedged across the alley,
  carrying balls sideways. This is the cheapest mechanic to implement, so it's worth
  making it look good.
- **Detail:** Belt surface with a **repeating cleat/tread pattern**, side rails, exposed
  roller at each end, a bent support leg.
- **Critical — UV layout:** The belt surface must be UV'd so that **scrolling U (or V)
  animates the belt convincingly**, with the tread pattern tiling seamlessly across the
  wrap. Lay the belt on its own UV island running the full 0–1 range along the scroll
  axis. Flag which axis you used in the delivery notes.
- **Colour:** Black rubber belt gone grey and cracked, rusted rollers, oil staining below.

### 7.3 `Interact_Seesaw`

- **Form:** A heavy plank balanced on a fulcrum — a pipe, an oil drum, a stack of
  masonry. Tips as balls land on either end.
- **Detail:** Plank visibly worn in the middle where it pivots. Fulcrum should look
  improvised and *slightly too small* for the job. Optionally a shallow lip at each end
  so balls look like they'd be caught.
- **Critical:** Origin at the fulcrum contact point. Deliver plank and fulcrum as
  **separate objects in one FBX** — the plank rotates, the fulcrum does not.
- **Colour:** Dark oiled timber, contrasting with the pale scrap in Part 1.

### 7.4 `Interact_BreakableRoof`

- **Form:** A small tiled or corrugated roof section spanning the alley. Balls hit it,
  it takes a few hits, then it gives way.
- **Deliver three states** as separate objects in one FBX:
  1. `_Intact` — clean, whole
  2. `_Cracked` — sagging, tiles slipped, a hole starting
  3. `_Broken` — collapsed into loose pieces
- **Critical:** All three must occupy the **same bounding box and origin** so they swap
  without popping. The `_Broken` pieces should be separate mesh islands so they can be
  scattered later.
- **Colour:** Clay tiles or rusted tin, matching the Part 1 palette.

### 7.5 `Interact_WindowThrower`

- **Form:** A townsperson leaning out of a window, hurling junk into the alley to knock
  balls off course. Mounted into the left/right building facades.
- **Detail:** **Keep this very simple.** Seen small, head-on, orthographic, often in
  motion — fine facial or hand detail is wasted. A strong readable pose silhouette
  (torso, arms, head) matters far more than anatomy. Bust only; nothing below the window
  line is ever visible.
- **Deliver alongside:** 2–3 small **thrown junk objects** (a boot, a bottle, a can) —
  each ≤ 100 tris, origin centred, these get spawned as projectiles.
- **Colour:** Drab layered clothing, muted. Should not out-compete the balls.

### 7.6 `Interact_AlleyAnimal`

The most important interactive prop — it eats balls, then opens a path.

- **Form:** A scruffy stray sat in the alley blocking a channel. **Dog and cat variants**
  if there's time; dog first.
- **Deliver three poses** as separate objects in one FBX, same origin:
  1. `_Hungry` — alert, sat upright, blocking the gap. Reads as an obstacle.
  2. `_Eating` — head down, mid-mouthful
  3. `_Full` — flopped over, sprawled, asleep, **clearly out of the way** — the silhouette
     must obviously communicate "the path is open now" without any UI
- **Critical:** The `_Hungry` and `_Full` silhouettes must be **unmistakably different at
  a glance**, because that shape change *is* the game telling the player the path opened.
  Tall and compact vs. low and spread out. Get this wrong and the mechanic is invisible.
- **Detail:** Matted fur, ribs showing, one chewed ear. Sympathetic, not grotesque.
- **Colour:** Muted browns and greys. A single small colour accent (a frayed collar) is
  fine and helps it read as a character rather than scenery.

---

## 8. Interactive props — priority

1. **`Interact_AlleyAnimal`** (dog, three poses) — the best mechanic on the list
2. **`Interact_ConveyorSegment`** — cheapest to implement, so art unblocks it fastest
3. **`Interact_SpinningWheel`** — classic pachinko read
4. Everything else
