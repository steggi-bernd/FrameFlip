# The glitch gallery — what is built, what is parked

This started from a bug: a cut-out PNG shown without its matte revealed the colour hidden
under the transparency — real image data, in the picture's own palette, where nobody
expects it. That turned into a tool, and the tool turned into a family.

This document exists because the family outgrew the sitting. Chasing one reference image
produced six useful additions and a lot of guessing; the rest is written down here rather
than built on a hunch.

---

## Built

| | Where | What it does |
|---|---|---|
| **Show what is hidden** | layer, under *Matte* | Lifts a layer's own coverage towards full, so whatever sits under its transparency comes into the picture. Steered by the layer's mask — a cryptomatte picks an object, a depth pass picks a distance. Needs material: a cleanly premultiplied file carries black there and nothing happens. |
| **Ordered (Bayer 8×8)** | colour, *Dither* | Threshold from a matrix built by recursive doubling. The crosshatch of old screens. |
| **Random** | colour, *Dither* | Threshold from a position hash — grainy, no pattern, and fixed per position so nothing shimmers while a slider moves. |
| **Line screen** | colour, *Dither* | Brightness carried in the **thickness** of a line, with an angle. The hatching of engravings. |
| **Error diffusion ×6** | colour, *Dither* | Floyd–Steinberg, Atkinson, Jarvis–Judice–Ninke, Stucki, Burkes, Sierra. Serpentine traversal. Atkinson discards a quarter of the error on purpose — that is where its contrast comes from. |
| **Cell width / height** | colour, *Dither* | The difference between a raster and gravel. A square cell gives dots; a wide flat one gives dashes that merge into lines following the form. |
| **Two colours** | colour, *Dither* | Rasters the **brightness** — one decision per cell instead of three — and maps it onto black plus a chosen hue. Without it the three channels drift apart and a silhouette falls into three clouds of dots. |

**The sixth pass kind** (`IFramePass`) exists because of this: a pass over the finished
frame, in order, on one thread. Error diffusion cannot be threaded and cannot run on the
coarse preview grid, so it stays out while a slider is dragged and joins on release. It
does not run in the 16-bit export, where two levels in sixteen bits would be a
contradiction.

---

## Parked — and why

### 1. Palettes

The real gap, and bigger than it sounds. Dither Boy organises its 63 algorithms around
palettes: built in, extracted from an image, shareable. FrameFlip quantises **levels per
channel**; *two colours* is a duotone special case bolted onto that, not a palette.

A palette means: a list of colours, and per pixel the nearest one wins, with the error
diffused as a colour vector. That is a different data model and a real piece of UI —
adding, reordering, sampling from the picture, saving a set.

**Worth doing when** someone wants more than two colours. Until then the duotone covers
the common case.

### 2. Pixel sorting — **built**

The one effect in this family with no substitute, and the reason is worth stating as an
invariant rather than a description: it **invents nothing and loses nothing**. A raster
decides per pixel, a displacement fetches from elsewhere — both make up values. Sorting
moves pixels along a line; what comes out holds exactly the same colours as what went in,
only in a different order. That is what the tests measure, and it is strict enough to
catch almost any mistake: write back one index off and you duplicate one pixel and lose
another.

**The threshold is the whole tool.** Sorting a row completely gives a gradient, not a
picture — boring by the second try. Only pixels whose key lies *between* two thresholds
join a run, so the image falls into runs with everything else standing still, and the form
stays readable while the surfaces run. The window is therefore the strength control, and
it starts closed.

Keys: brightness (runs become gradients), hue (runs become bands), saturation (separates
pale from vivid and leaves the form standing). Plus rows or columns, ascending or
descending, and a **longest run** that turns an effect into a composition — without it a
single run eats half a row of flat sky.

**Revised against the established toolkits** — satyarth's `pixelsort`, Kim Asendorf's
original ASDFPixelSort, and the glitch apps. What they all have and this lacked:

- **What limits a run.** Besides the threshold: *edges* (a run also ends where brightness
  jumps, so outlines hold while surfaces melt — the difference between "the picture
  melts" and "the surfaces in the picture melt") and *random lengths* around a mean.
- **Skip** — a share of runs left unsorted. The control that does most for the look: if
  every line melts alike the picture looks combed; if some stay, it looks disturbed.
- **Any angle**, not just rows and columns. This needed care: rotated lines with rounded
  coordinates hit some pixels twice and others never, and sorting then duplicates one
  and loses another. The lines are a *shear* instead — along the major axis every line
  stands on every position exactly once, and the minor offset is shared by all lines, so
  pixel (m, n) belongs to line n − offset(m) and to no other. Twelve angles are tested
  for exactly that.
- **Crosswise** — a second pass turned by 90°, as Asendorf does it: columns, then rows.
- Two more keys: **intensity** (unweighted sum) and **minimum** (how much grey is in it).
- **Speed** — random lengths and skipping change over the sequence.

Lines are independent of each other, so they now run in parallel; only *within* a line
does order matter. Measured: 4K with the window fully open 221 → 45 ms; at 30° and
crosswise, the most expensive new case, 96 ms. Old recipes with the *columns* switch sort
byte for byte as before — the switch is translated into 90° on load.

### 3. Wave glitch and displacement — **built, and revised**

The reason it is interesting here rather than in fifty other programs held up: it is
driven by a **render pass**. The normal pass lays the distortion along the geometry — a
sphere pushes outward, a wall not at all; the vector pass lays it along the motion —
what stands still stays put, what moves tears along its path. A wave modulates the
magnitude into bands, and a channel offset gives them the colour fringe that reads as
interference rather than blur.

The guess in this document was wrong on one point, and it is worth writing down: it does
**not** fit `IGeometryTool`. That interface is radial — `Factors(radius, ref r, ref g,
ref b)` — and cannot express an arbitrary displacement field. `IOpticsTool` knows a
pixel's place but cannot sample from anywhere else. The only kinds that can move pixels
are `IDataTool`, which gets the whole buffer plus a render pass, and `IFramePass`, which
gets the finished frame. `IDataTool` was the right home, which also settles the
behaviour without a file: **it rests when the pass is missing**, exactly like depth of
field and motion blur.

That cost one thing, and it was worth closing straight away: a plain wave on a PNG was
not reachable, because a data tool without its pass does not run at all. A tool can now
declare its pass **optional**, and the displacement has a third setting — *wave only* —
which needs nothing but the picture. It then pushes across the wave's direction of
travel: with a wave running top to bottom, horizontal bands slide sideways, which is the
tear people mean. Along it would be a zoom in stripes. `PassNeed.None` says that nothing
should be looked for, so no pass is read that nobody will look at.

Depth of field and motion blur keep the old rule: without their pass they rest, because a
slider that invents a distance is worse than one that does nothing.

**Revised against After Effects** (Wave Warp, Displacement Map) **and the glitch
apps** (Glitch Lab's slice, shift and block glitch). Taken over:

- **Wave shapes** — sine, triangle, square, sawtooth, as in Wave Warp. And the two actual
  glitch shapes, which do not oscillate but *jump*: **slices** (each band offset by its own
  random amount) and **blocks** (each band cut again into pieces of varying length). A
  **density** says what share of bands moves at all — a real signal fault hits a few
  lines and leaves the rest.
- **Time.** For a sequence the most important point: a displacement that stands still
  across all frames looks like a sticker on the lens. **Speed** moves waves along and
  makes the tear change in whole steps. For that the data tools and frame passes now
  receive the frame number.
- **Wrap around**, as in Displacement Map: what goes out on one side comes back on the
  other — a television that cannot hold the picture.
- **Phase**, and **reroll** instead of a seed number — nobody picks a tear by number; one
  rolls until it fits.

The tests found a real bug on the way: in float, sin(90°) is 0.99999994 and cos(90°)
is −4.4e-8. Every band boundary sat one row off, and in some boundary rows the tiny
x-term tipped the left and right half of a row into *different* bands — a row tearing in
the middle when a band should jump as a whole. Values are now snapped to exact 0 and ±1
where exact ones are meant.

A related gap closed with it: **a resting tool now says why it rests.** All three name the
missing pass and where it is switched on in Blender. A tool that quietly does nothing is
the worst failure mode this program has — it sends you looking for the bug in the program
instead of in the file, and it did exactly that here.

### 4. Post effects: chromatic aberration on the raster, JPEG glitch, scan lines

Chromatic aberration already exists as an optics tool but runs *before* the raster pass,
so it cannot fringe the dots. JPEG glitch means encoding and decoding on purpose with
damage in between. Scan lines are a special case of the line screen.

**Low priority.** The first is a plumbing question, the second a novelty, the third
already reachable.

---

## What was learned, so it is not re-learned

- **The raster pass runs last, after every colour tool.** Nothing colours its output. That
  is why "two colours" is not one way among several to get colour here — it is the only
  one.
- **Resolution decides whether it reads as a pattern.** A 4K picture at two levels gives
  dots one pixel wide, which is not a raster but even noise. Every program built for this
  look reduces first, rasters, and enlarges back with hard edges.
- **A square cell can never make a line.** Width and height are two numbers for a reason.
- **Per-channel quantisation destroys form.** Measured on one flat colour: three separate
  channels give eight mixtures, one brightness decision gives two.
- **Floyd–Steinberg is not "the" error diffusion.** Six of them differ enough that
  offering one and calling it error diffusion is like offering one brush.

---

## Where the effort actually went

Four rounds of "no change" that were not about dithering at all:

1. The section stayed editable while a **layer** was selected and wrote into that layer's
   stack, where a picture-wide tool is never computed.
2. The kernel list was a **second** dropdown that only appeared after choosing error
   diffusion — so Atkinson was invisible unless you already knew it existed.
3. The frame pass ran in only **one of the processor's two full paths**; the other one is
   taken as soon as any local tool (glow, clarity, sharpen) is on.
4. `Snapshot()` listed two of the stack's six lists, and `OnToolsChanged` never wrote the
   grading back to the settings — so any click elsewhere silently undid the setting.

The fourth was the real one, and it had nothing to do with rasters: it also affected
vignette, grain, distortion, chromatic aberration, depth of field and motion blur. It was
found by a test that drives **the page** — the three layers below it were all green.
