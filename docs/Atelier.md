# FrameFlip Atelier — design

[← back to the start page](../README.md)

A second mode inside FrameFlip: grade one frame, then apply that grade to the whole
sequence and write it out. Layers, masks driven by render data, and a colour toolset
aimed at Photoshop and Lightroom rather than at a node graph.

**Status:** steps 1 to 7 are built and running; of step 8 the local path exists, with
clarity, sharpening and noise reduction on it. Step 5 is complete —
pass, adjustment, image and group layers, with order, duplication, opacity, colour,
blend modes and clipping masks. Of step 6, masks are data-driven: cryptomatte, any
pass, luminance and gradient; painted masks are not built. Where an earlier estimate or
assumption turned out wrong, the measurement replaced it and the error is named —
those passages are worth more than the numbers.

---

## 1. What this is, and what it is not

FrameFlip judges a sequence. Atelier changes one.

That is the whole distinction, and it is worth holding on to, because almost every
question below is decided by it. Atelier is **not** a compositor: no node graph, no
Blender compositor import, no attempt to reproduce Glare or Denoise bit for bit. The
reason is in the viewer's own promise — a picture you can trust. A near-enough
reproduction of someone else's algorithm is worse than not offering it, because the
error stays invisible until the final render disagrees.

What Atelier does instead is the part Blender's compositor is awkward at: sitting
with one frame, pulling sliders, seeing it immediately, and then committing that
decision to three hundred frames without a node in sight.

### Not a separate program

The obvious cut would be a second binary — the viewer stays light, the editor gets
heavy. Against it:

**The editor needs the viewer.** A grade set on one frame has to hold for all of
them. A sequence with a sunset, a lamp coming on, a camera moving into shadow — the
reference frame is a sample, not the truth. Setting a grade means jumping through the
sequence constantly to check it still works at frame 300. That is timeline,
scrubbing, playback, A/B — the viewer, in full.

Two programs would mean opening one to check and the other to correct, which rebuilds
the context switch FrameFlip exists to remove.

**The shared base is nearly everything.** Sequence detection with gap handling, the
decoder registry, both cache stages, the ffmpeg export, the project library,
localisation, settings. Splitting means duplicating it, or introducing a shared
project and maintaining two releases, two test runs and two bug streams.

So: **one binary, two modes.** *Atelier* names the mode, not the product.

**Where it actually landed** — this section was written before the fact and guessed
wrong. The plan was a mode of the preview window, opened with a key on the held
frame. Built that way, it was in the wrong place: the preview opens on a hotkey and
closes on Escape, which is right for judging a sequence and wrong for sitting with
one image. The tools now live on their own page in the main window, beside Overview,
Projects and Settings, and the preview keeps only what judging needs.

The argument above still holds, only one step further out: the *binary* stays one,
and both places share the same controls — the grading panel is a single control used
by whoever needs it, not a copy.

The weight argument mostly dissolves on inspection: .NET loads and JITs types on
first use, so Atelier code sitting in the binary costs nothing in the working set
until someone enters it. The file grows — which is already a decision made in favour
of memory over size in the `.csproj`.

One thing does need care: **leaving Atelier must give everything back.** Someone who
graded a 4K multilayer EXR and returned to the tray must not still be holding two
gigabytes while Blender renders. The existing `MemoryTrimmer` and the ring buffer
budget have to cover the Atelier allocations too, or the tray promise quietly breaks.

---

## 2. The working model

```
  pick frame  →  build recipe  →  check across sequence  →  done  →  export
                      ↑                    │
                      └────────────────────┘
```

**The recipe** is the entire editing state: an ordered layer stack, each layer with
its tools, mask, blend mode and opacity. It is plain data — serialisable, nameable,
savable, reusable on the next shot. This is what `AdjustmentPreset` already is, one
size up: `ImageAdjustments` is a serialisable recipe today, and the export already
asks whether it should be applied. Atelier is that idea finished, not a new one.

**The batch** applies the recipe to every frame of the sequence, in sequence-detection
order — the same prefix/digits/suffix logic the viewer already uses, gaps included.

**The export** is the existing one, plus an image-sequence target.

### The rule the whole model rests on

> **No tool may measure the image it is working on.**

Auto white balance, auto levels, histogram stretch, adaptive anything — each lands
slightly differently on each frame, and the sequence flickers. In a single-image
editor an automatic is a convenience; here it is a defect.

The way out is not to ban automatics but to freeze them: an automatic **measures once
on the reference frame** and is stored as fixed numbers in the recipe. The batch then
runs the same arithmetic for every image. That has to be settled before the first
tool is written, because retrofitting it means touching all of them.

---

## 3. Numbers: the working format

The current pipeline is `Bgra32` end to end — `DecodedFrame` says so, and the ring
buffer, the raw cache and `FrameProcessor`'s fixed-point weights all follow. That is
right for playback and wrong for grading.

### Why not 16-bit integer

Worth separating two things that both get called "16 bit":

| | Range | What survives |
|---|---|---|
| 16-bit integer (PNG16, TIFF16, Photoshop) | 0…1, 65536 steps | finer gradients, **clipped highlights** |
| 16-bit half float (EXR) | roughly ±65504 | **values above 1.0** |

For renders the second is the entire point. A sky that is flat white in the PNG still
has structure in the EXR, and the exposure slider brings it back. Normalising to
16-bit integer on read discards exactly what EXR was added for.

### float32 throughout

One path for everything: 8-bit sources come in as 0…1, EXR brings its overbrights
along. `Vector256<float>` gives eight channels per instruction with no third-party
package, which matters for a batch run over hundreds of frames.

Half only as a *storage* format where memory bites — a 4K multilayer EXR with eight
passes is roughly twenty times a single 4K frame, so the stack in memory is the thing
to watch, not the arithmetic.

### The view transform — the trap that ruins first impressions

Blender renders linear. Writing a PNG applies the **view transform** (AgX since 4.0,
Filmic before, Standard for raw). Writing an EXR does **not** — it stores scene-linear
values.

So opening a Blender EXR and displaying it naively produces a washed-out, dark image
that looks nothing like Blender's viewport. Users will call this a bug, and they will
be right to.

The cheap way to be correct: **read Blender's own OCIO configuration.** It ships at
`<blender>/<version>/datafiles/colormanagement/config.ocio` with its LUTs beside it,
and `BlenderFinder` already locates the installation. Parsing that config and its
`.spi1d` / `.spi3d` / `.cube` files is considerably less work than reimplementing AgX,
and it is identical by construction rather than by effort — including for whatever
replaces AgX in a future version.

This is the one place where deep Blender compatibility is cheap, and it is worth
taking.

The view transform sits at the **end** of the chain, as a display step. Grading
happens in scene-linear, above it.

---

## 4. Reading EXR

The registry comment calls an EXR decoder "the only external dependency in the
project". It does not have to become one.

**Half to float** is bit manipulation; `System.Half` has been in the framework since
.NET 5.

**Compression** is where the work is, and the odds are good: Blender writes **ZIP** by
default, which is Deflate, which is in `System.IO.Compression`. ZIP, ZIPS, RLE and
uncompressed cover essentially everything coming out of Blender. PIZ (wavelet plus
Huffman) and DWAA (DCT-based) are real work and should wait until someone actually
turns up with such a file — at which point the decoder can fail cleanly on that one
format, the way WebP does today without its extension.

**Channel names** are the fiddly part, not the compression. Multilayer EXR encodes
layer and pass into strings like `ViewLayer.Combined.R`, and the conventions are not
perfectly stable across Blender versions. Expect the tedious work here, and expect to
tolerate variants rather than assume a grammar.

The decoder fits the existing `IFrameDecoder` shape for the viewer's purposes
(tone-mapped to Bgra32, so EXR sequences simply play). Atelier needs a second entry
point that keeps the float data and the pass structure — the same file read two ways
for two purposes.

---

## 5. The layer stack

Four kinds of layer, one stack:

| Kind | Source | Typical use | State |
|---|---|---|---|
| **Pass** | a pass from the multilayer EXR | light mixing: glossy down, emission up | **built** |
| **Adjustment** | nothing — it is a tool | the colour tools of section 7 | **built** |
| **Image** | a still, or a second sequence | watermark, overlay, version compare | **built** |
| **Group** | other layers | one mask over several operations | **built** |

Making colour correction a *layer* rather than a panel is what keeps this one system
instead of two. An adjustment layer has opacity, a blend mode and a mask like any
other, which means every tool in section 7 can be masked to a Cryptomatte selection
without any tool knowing that masks exist — and clipped to the layer below, "warm
up only the glossy pass" is two clicks rather than a node tree. The clipping mask was
built before adjustment layers precisely because it is the thing that makes that
sentence sayable.

### The one place an adjustment layer differs from the final grade

An adjustment layer sits **inside** the stack. What it outputs is composited further
up, so it has to hand back linear light — and AgX cannot do that. AgX is a 3D LUT:
it has a way there and no way back.

So an adjustment layer borrows a display transform instead: the same `x / (x + 0.18)`
the contrast blend modes and the luminance masks already use. Middle grey lands
exactly on 0.5, nothing is clipped, and the return trip is exact — measured, a
three-point straight curve on an adjustment layer leaves the image unchanged to within
0.5 % across twenty stops, which is the invariant the whole construction rests on.

The consequence has to be said out loud: **a curve on an adjustment layer bites on a
different scale than the same curve in the panel below.** Both are curves on a
display-like encoding, but one acts while compositing and the other on the finished
image. That is the same distinction every node-based program has; here it is simply
named.

The colour tools therefore sit **below** the stack in the panel, and the panel says
which of the two it is currently editing — in a bar that stays put while the rest
scrolls. That bar is not decoration: there is one set of tools and any number of
layers, and someone who misses the switch adjusts the whole image while meaning one
layer.

### Image layers follow the frame number

An image layer names another file. If that file carries a frame number, it follows the
sequence: frame 47 here means frame 47 there, spliced through the **same** regular
expression the sequence scanner uses everywhere else. A second rule would eventually
let one file fall under one and not the other, and the layer would sit one frame off —
visible only in motion. A name without a counter is a still and stays put; a counter
you want pinned has a toggle.

The case that earns the image layer its place is not the logo. It is: drop the previous
render version in, set **Difference**, and see in one glance what changed. Files can be
dragged onto the layer list or onto the picture; the menu and the file dialog are three
clicks for something that is a gesture.

### Watermarks sit above everything

An image layer can be marked **on top**, and that means more than last in the stack: it
is applied *after* the view transform and after the grade. An ordinary image layer goes
through AgX with the picture and is graded along with it — pure white would come out
grey, and lifting a curve would lift the watermark too. On top, it looks the same in
every frame, exactly as it is in the file.

The test therefore does not check that it is *there*. It checks that it does not change
when the picture underneath is pulled up by four stops — and, as a counter-check, that
the same grade does change the picture.

### Placing a layer

Offset, size and crop, per layer, **in fractions of the image rather than in pixels**.
A recipe set up at 1080p then holds at 4K; in pixels the watermark would sit in a
corner nobody meant. The resting state is the placement with nothing set: same size
means pixel for pixel, a different size is fitted and centred, and scale and offset
count from there.

That changed an earlier rule. A layer of a different size used to be dropped, because
scaling was a different job from mixing. It is not a different job any more — it is a
logo, and dropping a logo because it is smaller than the picture would be the wrong
answer. The test that pinned the old behaviour was rewritten rather than deleted.

Rotation is about the centre — the same point the scale counts from, so the two do not
shove each other around.

Placement is also editable **directly in the picture**: grab the layer and drag, pull a
corner to resize, take the handle above the top edge to turn. The arithmetic lives apart
from the control, in `PlacementDrag`, because it holds the kind of mistake you cannot
see — a corner that drifts instead of staying put feels like a bad mouse, not a bad
formula. The invariant the tests hold it to is that **dragging one corner leaves the
opposite one exactly where it was**, at every angle, which is why both scale *and* offset
change during a corner drag: the placement counts from the centre, and the centre moves
when a corner stays.

Rotation also settled a question the earlier version had got away with. The drag maths
first worked **in fractions of the canvas**, which is fine until something turns: the two
axes are different lengths in that space, so a rotation there *shears* rather than turns
— a square comes out a rhombus, and the further the frame is from square the worse it
gets. It now works in canvas pixels throughout and converts back to fractions only when
writing the offset. The test that holds this is a rotated square on a 16:9 canvas: four
equal edges, two equal diagonals.

**The edge had to go soft at the same time.** An axis-aligned rectangle hides a hard edge
— it lies along the pixel rows. A turned one lies across them, and decided hard, all
four sides look like staircases. Coverage now feathers across one canvas pixel, measured
as the distance to the nearest edge in layer space, so it narrows correctly when the
layer is scaled down. That coverage multiplies the layer's opacity, which is the same
place a mask attaches — one mechanism, not two.

Not built: skew, and dragging the crop edges in the picture.

### Groups pass through

A group holds layers and gives them one mask, one opacity and one blend mode. The
decision that matters is what its children see below them, and it goes the opposite way
from the obvious one: **a group passes through**. Its children composite onto what lies
beneath the group, and the group's result is then blended back over that same starting
point with its own mode, opacity and mask.

Isolated would have been simpler to implement and wrong for the headline case: a group
of adjustment layers would find black underneath and erase the picture, and nobody
would suspect the group. Pass-through also gives the invariant a group has to satisfy —
**with nothing set, a group is indistinguishable from not being there** — and that is
the first thing its tests check.

In the panel the stack is a tree and the list is flat, so rows indent by depth and two
arrows move a layer in and out of the group above it. Drag-and-drop was the alternative
and the worse one: in a 300-pixel strip a dragged row lands next to where it was aimed,
and you find out afterwards.

One direction caught me out and is worth recording: **the group is above the layer in
the list, which is a higher index in the stack.** The list runs top-down, the stack runs
bottom-up. The first version indented into `index - 1` and the button was simply dead.

### What it costs, measured

Compositing runs on every slider tick, so this had to be measured rather than assumed.
At 1080p, two passes plus one adjustment layer carrying a curve and a white balance:

| | |
|---|---|
| first attempt | **141 ms** — unusable |
| reusing the output buffer instead of allocating 33 MB per pass | **94 ms** |
| computing only the grid the coarse preview actually reads | **7.5 ms** |

The last step is the one that matters and it is not a trick: while a slider is moving,
the display already computes on a grid of every fourth pixel and interpolates between.
Compositing every pixel meant computing fifteen sixteenths of them for nothing. The
two grids must agree exactly, including the last column on the edge, so there is a test
that composes coarsely into a buffer pre-filled with nonsense and checks the drawn
result is byte-for-byte what a full composite gives — if the composer ever skips a
pixel the display reads, the nonsense shows up.

### Two kinds of mixing — and the reason there is only one

This section used to say something confident and wrong. It is left here corrected,
because the error is the most useful thing in this document.

**What it said:** passes are summed linearly — Diffuse + Glossy + Transmission +
Emission + Volume = Combined — while creative layers blend display-referred, in
gamma, the way Photoshop does. Two mixing systems, kept apart.

**What a real render says.** A 16×12 Cycles frame carrying all ten light passes,
the three colour passes and the finished image (it is now a test fixture) settles
the question:

```
Combined = (DiffDir  + DiffInd ) · DiffCol
         + (GlossDir + GlossInd) · GlossCol
         + (TransDir + TransInd) · TransCol
         + (VolumeDir + VolumeInd)
         + Emit + Env
```

Cycles does not split the image into summands. It splits it into **light times
colour**. The colour passes are factors, and adding them gives a picture that is too
bright while looking entirely plausible — which is the kind of wrong that survives
a review. Measured against Blender's own Combined, the corrected arithmetic lands
within **0.09 %**, which is the precision of half-float storage rather than of the
calculation.

A factor needs to be confined to its own light pass, not applied to the whole stack
beneath it. Photoshop already has that idea and calls it a **clipping mask**. So the
pass reconstruction is not a special mode: it is an ordinary stack in which the
colour passes are clipped Multiply layers.

**And the second claim fell with the first.** Blending display-referred would mean
tone-mapping every pass before compositing, which destroys exactly the values above
white that EXR is read for — a glossy pass reaches 40. Everything therefore composites
in **linear light**, and there is one blend system rather than two.

What that costs, stated plainly:

- **Multiply, Screen, Darken, Lighten, Difference, Add** are *more* correct in linear
  than in gamma, and identical to Photoshop's below white. Screen deviates only above
  white, where its usual formula `a + b − ab` turns negative: two passes at 3 would
  give −3, a black image. The product term is capped at 1, so the result keeps rising
  instead of collapsing.
- **Overlay, Soft Light and Hard Light** genuinely need a white point to pivot around,
  and linear light has none. They borrow one: `x / (x + 0.18)` maps black to 0, middle
  grey to exactly 0.5 and infinity to 1, the Photoshop formula runs there unchanged,
  and the result is mapped back. Nothing is clipped, and a layer of middle grey leaves
  the image untouched — which is the invariant the whole construction rests on and the
  test that guards it.

The only visible difference from Photoshop is on two images that both stay below
white, where Multiply in linear looks a little different from Multiply in gamma. In
exchange, everything works on values above white — and those are the reason EXR is
read at all.

### Blend modes

Ten, not the full Photoshop set. These are the ones that mean something in linear
light; the rest (Colour Burn, Vivid Light, Hard Mix and the component modes) are
defined against a white point that does not exist here and would each need the same
borrowed-domain treatment to be more than decoration.

| Group | Modes |
|---|---|
| Normal | Normal |
| Additive | Add — the natural mode for a render pass |
| Darken | Multiply, Darken |
| Lighten | Screen, Lighten |
| Contrast | Overlay, Soft Light, Hard Light |
| Inversion | Difference |

Difference deserves a specific mention: dropping a second render version in as an
image layer and setting Difference is the fastest way to see what actually changed
between two renders. That alone earns the image layer its place — and it is built.

### What a layer carries

| | |
|---|---|
| **Source** | a pass named in the file, or the image itself |
| **Visible** | on or off |
| **Blend mode** | one of the ten above |
| **Opacity** | 0 to 1, mixing toward the blended result — not toward the layer |
| **Exposure** | f-stops on this layer alone; "more glossy" in one grip |
| **Colour** | a per-channel factor, set on a wheel, multiplicative so black stays black |
| **Clipped** | acts on the layer below only |

A layer holds no pixels. It names a pass — which is what makes the same stack apply
to every frame of the sequence, and is the whole point of the Atelier: set up one
frame, compute three hundred.

---

## 6. Masks

This is where Atelier has something Photoshop does not, and it is the difference
between a toy and a tool.

**A painted mask does not survive a sequence.** Painted on frame 50, it sits wrong on
frame 200 unless the camera is locked. In an apply-to-all workflow, hand-painted masks
are usable for static shots and misleading everywhere else.

Masks therefore have to be **derived from the data**, so they are recomputed per frame
and always fit:

**Cryptomatte** — **built.** The EXR records which object, material or asset lies
under each pixel. Click the sphere, get a pixel-accurate matte, sub-pixel correct,
through motion blur and through glass. No selecting, no magic wand, no edge cleanup.

What is actually in the file is not a picture but a lookup table per pixel. Each level
carries *two* pairs in its four channels — `r` = id, `g` = coverage, `b` = id,
`a` = coverage — so three levels hold six objects per pixel, enough for hair in
front of glass. An id is the 32-bit MurmurHash3 of the object's name reinterpreted as
a float, and the header carries a JSON manifest mapping names to those hashes.
Selecting an object means reading the id under the cursor, then summing, across every
level, the coverage of the pairs whose id matches. Viewed as an image a level is
coloured noise, and correctly so: those are hashes, not colours.

Two things about the file format were **not** what the specification led me to expect,
and both would have produced a mask that silently never matched anything:

- Blender writes the cryptomatte channels **lowercase** — `ViewLayer.CryptoObject00.r`
  — while every other pass in the same file is uppercase. Checking only for `R`
  leaves the crypto passes unrecognised as colour, and the reader falls back to
  treating the first channel as greyscale: alpha instead of id.
- They are **float32 while the rest of the image is half**. An id rounded to sixteen
  bits is a different id. Mixed pixel types within one file are the normal case here,
  not an edge case.

Both are now fixture-tested against a real render, and the strongest check is this:
every id that appears *in the picture* must have a name in the manifest. That only
holds if the right channel was read, the floats survived the decoder intact, and the
manifest's hex was reinterpreted rather than converted.

**Passes as masks** — **built.** Mist, shadow, ambient occlusion, an index matte:
any pass can drive a layer. Those passes are already fractions between 0 and 1, so the
value goes in unchanged and gets a black and a white point, exactly as one pulls a
matte.

This differs from the luminance mask below on purpose, and the difference cost a
rewrite. The first version sent a mask pass through the same *range window* the
luminance mask uses — but every value between 0 and 1 lies inside the window 0 to 1,
so a freshly chosen pass mask did precisely nothing. Two different operations, and
therefore two different labels in the panel: a range says "From/To", a matte says
"black point / white point". Labelled the same, it would be a trap.

**Luminance range** — **built**, in two flavours: the brightness of the layer
itself ("only where this pass is bright") and the brightness of what already lies
below it ("only in the shadows of the picture"). Both read through the same
`x / (x + 0.18)` mapping the contrast blend modes use, so middle grey lands exactly on
0.5 and "highlights" means what the eye means — in raw light an ordinary pixel sits
near 0.05, and everything above 0.5 would be almost nothing.

The range window feathers **outward**, which is the detail that makes it usable: at 0
to 1 both ramps fall outside the value range and the mask passes everything, however
soft it is set. Feathered inward, the neutral setting would already darken both ends,
and nobody would suspect the mask.

**Gradient** — **built.** Direction, centre, width; 0° runs left to right and
90° top to bottom, in image coordinates where y grows downward, because that is the
direction in which one darkens a sky. Static by nature, and honest about it: fine for
a vignette, wrong for anything that tracks.

**Z-Depth, Position, Normal** — designed. Depth works through the pass mask today
but saturates, because a depth in metres is not a fraction; it wants its own range in
world units. Position and normal need a mask that reads three channels as a vector
rather than as brightness.

**Where a mask attaches.** At exactly one point: it makes the opacity local. That is
the whole integration, and it is deliberate — every blend mode, every clipping group
and every layer then obeys the same rule, and there is no case in which a mask means
something else. A mask that behaved differently under Multiply than under Add could
not be explained to anyone.

Painted masks and set operations between masks are not built.

---

## 7. The colour tools

Grouped by what they do to the image. The ★ marker is what makes Atelier worth opening
at all; the rest is what makes it worth staying in.

### 7.1 Tone

| Tool | Parameters | Notes |
|---|---|---|
| **Exposure** ★ | stops | multiplicative in linear, which is the only place it means anything. Exists today |
| **Contrast** ★ | amount, pivot | the pivot should be exposed — 0.18 (mid grey) behaves differently from 0.5 |
| **Highlights / Shadows** ★ | two sliders | zone-based recovery. On EXR data these finally do what they claim, because there is something above 1.0 to recover |
| **Whites / Blacks** | two sliders | the endpoints, not the same thing as highlights/shadows — this is where clipping gets set |
| **Levels** | black point, white point, output range | exists today as black/white point |
| **Gamma** | single value | exists today |
| **Curves** ★ | master + R, G, B | the most important single tool, and entirely missing today. Spline and linear interpolation, arbitrary control points |
| **Highlight rolloff** | mode, shoulder | Reinhard / filmic-style compression for overbrights, for when the view transform is Standard |
| **Log conversion** | encoding | in and out of a log curve, so curve work lands where colourists expect it |

### 7.2 Colour

| Tool | Parameters | Notes |
|---|---|---|
| **White balance** ★ | temperature (K), tint | proper chromatic adaptation (Bradford/CAT02), not an RGB nudge. Eyedropper picks a neutral |
| **Lift / Gamma / Gain** ★ | three colour wheels + master | shadows, midtones, highlights tinted independently. The grading tool, and more useful on renders than Photoshop's levels |
| **Offset / Slope / Power (ASC CDL)** | 3×3 values + saturation | industry standard, and **exportable** — a grade that can leave the program and arrive in Resolve or Nuke intact |
| **Saturation** | amount | exists today |
| **Vibrance** ★ | amount | raises unsaturated colours more than saturated ones, protects skin. The slider people actually reach for |
| **HSL selective** ★ | 8 ranges × hue/sat/lum | red, orange, yellow, green, aqua, blue, purple, magenta. The Lightroom model — catch one garish green without touching anything else |
| **Hue vs. curves** | Hue×Hue, Hue×Sat, Hue×Lum, Lum×Sat, Sat×Sat | the Resolve model: the same idea as HSL but continuous. Strictly more powerful, strictly more intimidating |
| **Colour balance** | C–R, M–G, Y–B per tonal range | the Photoshop model, with preserve-luminosity |
| **Selective colour** | CMYK per colour range, relative/absolute | Photoshop's, and genuinely useful for pushing one material's colour |
| **Channel mixer** | 3×3 matrix + constant, monochrome flag | each output channel as a combination of inputs. Also the good way to make black and white |
| **Split toning / colour grading** | hue + sat for shadows/mids/highlights, balance | warm highlights, cool shadows — the most-used look, in one control |
| **Photo filter** | colour, density, preserve luminosity | a filter pane over the lens |
| **LUT** ★ | .cube / .3dl, 1D and 3D, strength | show LUTs and looks from elsewhere. Also how Atelier stays compatible with a studio pipeline it knows nothing about |

### 7.3 Local contrast and detail

| Tool | Parameters | Notes |
|---|---|---|
| ~~**Clarity**~~ ★ **built** | amount, radius | midtone local contrast — unsharp mask at a large radius |
| **Texture** | amount | the same at a small radius: surface detail without the halo — which is Sharpen's radius turned up, not a third tool |
| **Dehaze** | amount | dark-channel prior. Also runs backwards, to add atmosphere |
| ~~**Sharpen**~~ **built** | amount, radius, threshold | unsharp mask on luminance only; the threshold keeps noise out of it |
| ~~**Noise reduction**~~ **built** | luminance, colour, threshold | for renders with the sample count cut short. Luminance and colour separately, because they deserve very different amounts |
| **Bloom / Glare** ★ | threshold, radius, intensity | on HDR data this is finally correct, because the overbrights are real rather than clipped to white. On an 8-bit PNG the same filter is guesswork |
| **Halation** | threshold, radius, tint | red-orange bleed around highlights; most of what reads as "filmic" |

**The second path — built.** Everything else in section 7 is *point-wise*: a pixel goes
in, a pixel comes out, and tools chain without a buffer and without an ordering problem.
The tools in this table cannot be said that way. They ask what it looks like *around* a
pixel, and `IGradingTool` has promised them their own path since it was written. That
path now exists, and clarity, sharpening and noise reduction run on it:

1. the whole chain into a scratch buffer, up to and including the display tools;
2. that buffer blurred — once per radius, not once per tool;
3. value and blur together through the tools, then out.

**Both values come from the same stage.** That is the correctness condition, and it is
the whole reason the buffer is a buffer rather than a second read of something earlier.
A blur taken from a different stage is not local contrast but an offset: on a flat area
the two values would differ and the area would tip, although there is nothing there to
boost. The test says exactly that — clarity at full strength leaves a flat field
byte-for-byte unchanged, and an ordinary contrast slider on the same field does not.

**It costs three float buffers** — values, blur, and the field the box filter runs
over — three channels each: 75 MB at 1080p, 300 MB at 4K, *per thread*, because the
batch run grades several frames at once and a shared buffer would be a race. At four
workers and 4K that is a gigabyte. Hence the path is only taken when a local tool is
actually set to something; a tool sitting at zero does not count, or every frame in the
program would be dragged through a buffer for nothing.

The blur is three box passes rather than one Gaussian. Not a shortcut: three boxes are
already closer to a bell than the noise floor, and a box runs on a running sum in the
same time whatever the radius — at radius forty a real Gaussian would cost eighty times
as much.

| 1080p, clarity at full strength | |
|---|---|
| no local tool — the straight path | **29.2 ms** |
| clarity, every pixel | **66.9 ms** |
| clarity, while a slider is moving | **9.7 ms** |

The third row is the one that decides whether this is usable, and it is the same trick
as the compositor's: while a slider moves, only the grid the coarse preview reads is
computed, and the blur runs on that grid with a correspondingly smaller radius. That is
not an approximation — a reduced image blurred by a reduced radius is the large one
blurred by the large. The grid formula is now shared by the composer, the image path and
this one, and so is the interpolation back up; two copies would drift, and the drift
would look like a preview artefact rather than a bug.

Two details that are easy to get wrong. The radius is given **relative to 1080p** and
converted where the frame size is known, so the same number means the same thing on 4K
instead of a quarter of it. And clarity's weighting runs to zero at black and at white
(`4b(1-b)`): without it the highlights blow and the shadows block up, precisely where
there is no room left.

#### One blur per radius — the assumption that broke first

The first version blurred **once, at the largest radius any tool asked for**. That was
correct while clarity stood there alone, and it was the first thing to fail when
sharpening arrived: sharpening at radius forty is not sharpening, it is clarity again.
So the tools are grouped by radius, in order, and each group blurs what is standing at
that moment — not what stood at the start.

That second half is not a detail. It is the reason noise reduction and sharpening
together are worth more than either alone: sharpening sees the cleaned-up picture, so
the grain that was just removed does not come back. Blurring once at the start and
handing the same blur to both would have sharpening reach for exactly what noise
reduction had put down. There is a test with two probe tools that reads back which
blur each one was given.

The order is fixed and does not follow the list: **noise reduction → clarity →
sharpening.** Cleaning up first, because everything after it amplifies local differences
and grain *is* a local difference. Sharpening last, because it should see the finished
picture. Which slider somebody reached for first must not decide what comes out.

#### Sharpening

The same handle as clarity with a small radius, and three differences that each look
minor on their own:

It works **on luminance**, not per channel. At a coloured edge the three channels run
apart by different amounts; sharpened per channel the colour moves, and every edge
carries a coloured fringe. One increment on all three leaves the hue where it was — the
test checks that the distances between the channels come out unchanged.

It has a **threshold**, with a soft falloff rather than a cut. A hard cut would show up
along a gradient as a step, exactly where the grain crosses the threshold.

And it does **not** let go at the ends, unlike clarity. A specular edge lives at the
white end; stopping there means stopping precisely where the picture is sharpest. It
clamps, and that is all.

#### Noise reduction

Built from the same two numbers as everything here. Small difference to the
surroundings: probably grain, so the value moves toward the blur. Large difference: an
edge, so it stays. That is the bilateral filter's idea with one blur instead of a window
per pixel, and the honest version of what it can do: **this is for fine grain over an
otherwise finished picture.** A render with too few samples is blotchy in *large*
patches, and from close up those look like content. Nothing here helps with that;
rendering longer does.

Luminance and colour are separated, and that is the part that earns its keep. Colour
noise is the ugly kind — coloured blotches in a grey wall — and genuinely fine colour
detail is rare. So colour takes a threshold four times as wide and can be smoothed far
harder than luminance, without anyone missing anything.

#### What the three of them cost, and one thing the preview cannot show

| 1080p | every pixel | while a slider moves |
|---|---|---|
| no local tool | **29.9 ms** | — |
| clarity | **62.5 ms** | **9.2 ms** |
| all three, three different radii | **125.5 ms** | **14.7 ms** |

Three radii mean three blurs, and that is what the full pass costs. While dragging it
collapses, and not only because the grid is smaller: a radius of 2 and a radius of 4
both land on 1 once the grid is a quarter of the picture, so they share a blur.

Which is also the one thing to be honest about. A small radius **cannot be previewed
truthfully on the coarse grid.** Radius 2 at quarter resolution becomes radius 1 on the
grid, which is radius 4 in the picture — the drag preview oversharpens. The release
recomputes at full resolution and the picture settles. Detail at a scale finer than the
preview grid is not something a preview at that grid can show, and pretending otherwise
would mean computing every pixel on every tick.

**They act on the finished picture, not on a single layer.** An adjustment layer is
computed pixel by pixel in the middle of the stack; a neighbourhood does not exist there
yet. So the three sections are switched off while a layer is selected, with the reason
written next to them — a slider that moves while the picture does not would be worse
than one that is visibly unavailable.

### 7.4 Optics

| Tool | Parameters | Notes |
|---|---|---|
| **Vignette** | amount, midpoint, roundness, feather, highlight protection | |
| **Chromatic aberration** | amount, direction | adding it is a look; removing it is a fix |
| **Lens distortion** | barrel/pincushion, scale | |
| **Film grain** | amount, size, roughness, colour | **must vary per frame** — see section 10 |

### 7.5 Render data — the tools no image editor has

| Tool | Parameters | Notes |
|---|---|---|
| **Light mixing** ★ | per pass: gain, tint | diffuse, glossy, transmission, emission, volume, each direct/indirect. Relight without rerendering — the reason to open the EXR at all |
| **AO multiply** | amount | contact shadows dialled after the fact |
| **Depth haze** | near, far, colour, density | from Z or Mist |
| **Post depth of field** | focus distance, aperture, bokeh shape | from Z. Approximate at edges, and honest about it |
| **Post motion blur** | shutter, samples | from the Vector pass |
| **Cryptomatte picker** ★ | click to add/remove | not a correction — the mask source of section 6, listed here because it is what people will come for |

### 7.6 The order they run in

Fixed within a layer, because the sequence is not arbitrary — the same reasoning is
already written into `ImageAdjustments`:

```
  white balance → exposure → highlights/shadows → whites/blacks → levels
  → curves → contrast → colour tools (LGG, CDL, HSL, selective, mixer)
  → vibrance/saturation → local contrast (clarity, texture, dehaze)
  → detail (sharpen, noise) → optics (bloom, vignette, CA, grain)
  → LUT → [layer blend] → … → view transform
```

Anyone who wants a different order uses a second adjustment layer. That is what the
stack is for, and it is a better answer than a configurable pipeline.

**One correction to this order, from building it.** The local tools do not sit in the
middle of that line; they run at the end, after the view transform and the display
tools, on values between 0 and 1. Two reasons, and the first only became clear with the
code in front of me: local contrast means *this pixel against its neighbours*, and in
scene-linear the neighbourhood of a bright pixel is dominated by whatever overbright
sits next to it — a value of 60 in a neighbourhood of 0.2 is a difference of 59.8, and
multiplying that by an amount is not a look, it is an explosion. The second is the
correctness condition above: value and blur must come from the same stage, and the stage
where "flat" means flat to the eye is the display stage. The line above still describes
the point-wise chain correctly; the local tools hang off its end.

Among themselves they also run in a different order than the line suggests: **noise
reduction, then clarity, then sharpening** — the cleaning up has to happen before
anything amplifies local differences, because grain is one.

---

## 8. Measuring

Grading without scopes is guessing, and the header line (`EV -1.2  γ 1.3`) stops being
enough once there is a stack.

* **Histogram** — exists. Needs to handle values above 1.0 without pretending they are
  1.0
* **Waveform** — luminance against horizontal position. The `Histogram` class comment
  already anticipates it
* **RGB parade** — three waveforms side by side; where colour casts become obvious
* **Vectorscope** — hue and saturation on a disc, with skin-tone line
* **False colour** ★ — exposure zones as flat colours. On HDR the fastest read of
  whether anything is genuinely blown
* **Pixel probe** — hover for numbers, scene-linear and display, plus the pass values
  under the cursor
* **Clipping warning** — exists, and means something different now: clipped in the
  *file* and clipped by the *view transform* are separate problems

Scopes measure the graded image, as the histogram already does — the diagram should
show what the eye is seeing.

---

## 9. The batch run

The part that looked expensive in the first sketch, and turns out to be the easy one.

**Parallel across frames.** Frames know nothing about each other, so this scales across
cores far better than the row-splitting inside `FrameProcessor` — and the two compose:
rows within a frame, frames across a pool.

**Throttled by the existing load monitor.** `SystemLoadMonitor` already decides thread
count and priority for decoding. The batch uses the same signal, so a grade export
started while Blender renders runs slower instead of fighting it. The program's core
promise holds inside Atelier without inventing anything.

**Preview at reduced resolution — measured, and not optional.** Step 2 is built, so
these are numbers rather than guesses (1080p, eight cores):

| Path | Per frame |
|---|---|
| 8-bit, no correction | 0.3 ms |
| 8-bit, full correction | 9.5 ms |
| float, Standard view transform | 46.9 ms |
| **float, AgX** | **91.7 ms** |
| float, AgX, 4K | ~367 ms |

Three things follow. Float with AgX is **ten times** the 8-bit path, so playback stays
on 8-bit — at 24 fps there are 41.7 ms between frames and a single 1080p frame does not
fit. A slider on 4K at 367 ms per update is unusable, so dragging has to work on a
reduced decode; at 25 % that is around 23 ms, which is fluid. And the cost is dominated
by the view transform, not by the correction — the 3D LUT lookup roughly doubles it.

**Batch sizing — measured, and my estimate was wrong.** Step 4 is built, so a real
run replaces the guess: 4K frames through five tools (white balance, curves,
lift/gamma/gain, vibrance, HSL) plus AgX, written as 16-bit PNG.

| Frames in parallel | Per frame | 300 frames would be |
|---|---|---|
| 1 | 1085 ms | 5.4 min |
| 2 | 569 ms | 2.8 min |
| 4 | 398 ms | 2.0 min |
| 8 | 407 ms | 2.0 min |

I had estimated "tens of minutes"; it is two. The error was in the reasoning, not the
arithmetic: I priced each tool as if it cost what the view transform costs. It does
not — the 3D LUT lookup dominates, and five point-wise tools on top of it barely
register. Optimising the LUT would still be the right target if this needed to be
faster, but it does not.

The second surprise is that **eight in parallel is no better than four**. The frames
are already parallel inside — rows are spread across cores within one image — so the
outer pool starts competing with the inner one. Four is the sweet spot on this
machine, which is roughly half the cores, and that is what the load governor hands
out anyway.

Small frames stay cheap: the same recipe on 960×540 runs at 21 ms per frame, so a
24-frame sequence is done in half a second.

**Failure has to be survivable.** A missing frame (the viewer already marks gaps red),
a file still being written, a disk filling up mid-run. The batch reports which frames
failed and carries on — a three-minute run that throws away its work on frame 280 is
worse than no batch at all. Resuming from a partial run belongs here too.

---

## 10. Three ways to get this wrong

**Measuring per frame.** Section 2. The single most important constraint, and the one
most easily lost when a tool gets added late.

**Film grain that does not move.** A grain pattern generated identically for every
frame is fixed-pattern noise — it reads as dirt on the lens, not as film. The seed has
to advance with the frame number. The same applies to any noise-based tool, and it is
the exact mirror of the previous rule: measurement must be frozen, noise must not be.

**Trusting one reference frame.** The interface has to make checking easy, not merely
possible — jump to the darkest and brightest frames of the sequence, scrub with the
grade live, compare graded against ungraded at any point. If checking is awkward,
people will skip it and find out at export.

---

## 11. Output

The existing export, plus an image-sequence target:

| | Notes |
|---|---|
| **Video** | H.264, H.265, ProRes — the frames go into ffmpeg **raw**, not as file paths |
| **Image sequence** | PNG 8/16, TIFF 8/16, EXR (graded, still linear, passes flattened), JPEG |
| **Bit depth** | 16-bit output where the target allows — a graded sequence going on to another program should not be narrowed on the way out |
| **Colour space** | which view transform was baked in, recorded in the file where the format has a place for it |
| **Recipe** | saved beside the output, so a rerun is reproducible |

One thing gets simpler. `ImageAdjustments.ToFfmpegFilter()` exists today to make the
export match the preview, and it is an approximation by its own admission — the comment
says exposure is fitted through the image centre and that black and white point have no
`eq` equivalent. Atelier renders the pixels itself and hands ffmpeg finished frames, so
the approximation disappears. The viewer's quick path can keep it.

---

## 12. Order of construction

Each step is worth having on its own, which is the point — none of them is a bet on the
next one landing.

1. ~~**EXR decoder, converting.**~~ **Done.** Tone-mapped to Bgra32 through Blender's OCIO config.
   Changes nothing structurally, makes the viewer properly useful for renders, and
   surfaces how much a hand-written reader hurts before anything else depends on it.
2. ~~**Float frames as a second path**~~ **Done**, for the held frame only. Exposure, curves and the
   histogram become real. Reveals what the time budget actually looks like.
3. ~~**Adjustment layers with the ★ tools.**~~ **Done.** Clarity was held back here — it needs a pixel's neighbourhood and does not fit the point-wise form — and arrived with step 8, on a path of its own. Curves, white balance, LGG, HSL, vibrance,
   clarity, LUT. Single layer, no masks. Already a genuine grading tool.
4. ~~**The batch run and sequence export.**~~ **Done**, images and video. At this point the workflow closes, and Atelier
   is finished as a product even if nothing further is built.
5. ~~**Layer stack, blend modes.**~~ **Done**, all four kinds of layer: order,
   duplication, opacity, colour, ten blend modes, clipping masks and nesting, all in
   linear light, with the stack applied unchanged by the batch run. This step is also
   where the pass arithmetic in section 5 turned out to be wrong and was corrected
   against a real render.
6. ~~**Passes and light mixing.**~~ **Done** with step 5. ~~**Luminance and gradient
   masks.**~~ **Done**, plus any pass as a mask — which turned out to be the same
   plumbing the cryptomatte needed, so it was worth building first. Painted masks are not.
7. ~~**Cryptomatte.**~~ **Done.** The distinguishing feature, and it needed the stack
   and the mask slot in place first, exactly as this list assumed. Click an object in
   the picture, and the selection holds for the whole sequence — the file names
   objects, and a name does not move.
8. **The remaining tools from section 7**, in the order people ask for them. The
   **local path is built** — the second pass the neighbourhood tools need — and carries
   ~~clarity~~, ~~sharpening~~ and ~~noise reduction~~. Building the second and third
   tool on it is what showed up the one wrong assumption in the first: a single blur
   for every tool. Glow and halation are further tools on that path rather than further
   mechanism, which is what the step was for.

Steps 1–4 are the product. 5–8 are what makes it uncontested.

---

## 13. Open points

* **Memory ceiling for the stack.** The ring buffer has a budget and a policy. The layer
  stack needs its own, and the two have to agree — particularly on the way back out to
  the tray.
* **Cryptomatte manifests.** The object-name mapping sits in EXR metadata as JSON and is
  large on complex scenes. Worth reading lazily.
* **Painted masks.** Deliberately left out. If they arrive later it should be with a
  clear statement about static cameras, not as an equal citizen beside Cryptomatte.
* ~~**OCIO parsing scope.**~~ **Settled.** Blender 4.5's config defines `AgX Base sRGB`
  as a chain of exactly five steps: a 3×3 matrix (CIE-XYZ E → FilmLight E-Gamut), a
  log2 allocation over [-12.474, 12.526], the 3D LUT `AgX_Base_sRGB.cube` with
  tetrahedral interpolation, an exponent of 2.4, and Rec.709 → sRGB. No general OCIO
  parser is needed — only those five transform types, and the LUT ships as a file.
  Filmic (3.x) and Standard resolve the same way.
* **Multiple view layers.** A multilayer EXR can hold several. Whether Atelier treats
  them as separate stacks or flattens to the one is undecided.
* **Undo.** A stack with masks needs real undo, and the viewer has never needed any.
  This is more work than it sounds, and it belongs inside step 3 rather than after it.
* **Where the recipe lives.** Beside the sequence, in the project library, or both —
  which bears on whether a grade is portable between machines.
