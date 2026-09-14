# FrameFlip Atelier — design

[← back to the start page](../README.md)

A second mode inside FrameFlip: grade one frame, then apply that grade to the whole
sequence and write it out. Layers, masks driven by render data, and a colour toolset
aimed at Photoshop and Lightroom rather than at a node graph.

**Status:** design. Nothing here is built yet. Numbers marked *(estimate)* have not
been measured — they are there to size decisions, not to be quoted.

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

So: **one binary, two modes.** The viewer starts as it does today. One key opens
Atelier on the frame currently held. *Atelier* names the mode, not the product.

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

| Kind | Source | Typical use |
|---|---|---|
| **Pass** | a pass from the multilayer EXR | light mixing: glossy down, emission up |
| **Adjustment** | nothing — it is a tool | the colour tools of section 7 |
| **Image** | a still, or a second sequence | logo, overlay, gradient, version compare |
| **Group** | other layers | one mask over several operations |

Making colour correction a *layer* rather than a panel is what keeps this one system
instead of two. An adjustment layer has opacity, a blend mode and a mask like any
other, which means every tool in section 7 can be masked to a Cryptomatte selection
without any tool knowing that masks exist.

### Two kinds of mixing, and they are not the same

This matters more than it looks.

**Passes are summed, linearly.** Diffuse + Glossy + Transmission + Emission +
Volume = Combined. That is not a blend mode; it is how Blender took the image apart,
and putting it back together is addition. Anything else produces a result Blender
would never have rendered.

**Creative layers blend display-referred**, in gamma, the way Photoshop does.
Multiply, Overlay and Soft Light are defined on gamma-encoded values. The same formula
on linear light gives a visibly different picture — usually darker and harsher — and
someone coming from Photoshop will file it as a bug.

These are two different operations that happen to share the word "layer". Kept apart,
both are right. Mixed together, neither is, and no one can explain why.

### Blend modes

| Group | Modes |
|---|---|
| Normal | Normal, Dissolve |
| Darken | Darken, Multiply, Colour Burn, Linear Burn, Darker Colour |
| Lighten | Lighten, Screen, Colour Dodge, Linear Dodge (Add), Lighter Colour |
| Contrast | Overlay, Soft Light, Hard Light, Vivid Light, Linear Light, Pin Light, Hard Mix |
| Inversion | Difference, Exclusion, Subtract, Divide |
| Component | Hue, Saturation, Colour, Luminosity |

Difference deserves a specific mention: dropping a second render version in as an
image layer and setting Difference is the fastest way to see what actually changed
between two renders. That alone earns the image layer its place.

---

## 6. Masks

This is where Atelier has something Photoshop does not, and it is the difference
between a toy and a tool.

**A painted mask does not survive a sequence.** Painted on frame 50, it sits wrong on
frame 200 unless the camera is locked. In an apply-to-all workflow, hand-painted masks
are usable for static shots and misleading everywhere else.

Masks therefore have to be **derived from the data**, so they are recomputed per frame
and always fit:

**Cryptomatte** — the EXR records which object, material or asset lies under each
pixel. Click the red vase, get a pixel-accurate matte, sub-pixel correct, through
motion blur and through glass. No selecting, no magic wand, no edge cleanup. Cool down
just the vase and nothing else.

This is the single strongest argument for the whole feature. Without it, Atelier is a
weaker Photoshop. With it, it is a different thing.

**Z-Depth** — distance per pixel. Depth haze, post depth of field, "only the
background", "only the foreground". The Mist pass is the pre-normalised variant.

**Position** — world coordinates per pixel, so a mask can be a box in 3D space.
Everything left of that wall, everything below that height.

**Normal** — surface direction. Everything facing the light, everything facing up.
Enough for coarse relighting without a rerender.

**Luminance range** — a soft mask from brightness, with range and falloff. The fallback
when there are no passes, and how the highlight and shadow tools work internally
anyway.

**Gradient and shape** — linear and radial ramps, ellipses and rectangles with
feathering. Static by nature, and honest about it: fine for a vignette or a sky
gradient, wrong for anything that tracks.

Every mask gets the usual modifiers — invert, feather, expand/contract, opacity, blend
with the mask above — and masks combine with the same set operations as layers.

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
| **Clarity** ★ | amount | midtone local contrast — unsharp mask at a large radius |
| **Texture** | amount | the same at a small radius: surface detail without the halo |
| **Dehaze** | amount | dark-channel prior. Also runs backwards, to add atmosphere |
| **Sharpen** | amount, radius, threshold | unsharp mask; the threshold keeps noise out of it |
| **Noise reduction** | luminance, colour, detail | for renders with the sample count cut short. The EXR's albedo and normal passes make this markedly better than it can be on a PNG |
| **Bloom / Glare** ★ | threshold, radius, intensity | on HDR data this is finally correct, because the overbrights are real rather than clipped to white. On an 8-bit PNG the same filter is guesswork |
| **Halation** | threshold, radius, tint | red-orange bleed around highlights; most of what reads as "filmic" |

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

**Rough batch sizing**: a 4K frame at ~370 ms through one layer, so ten layers put a
300-frame sequence on eight cores in the tens of minutes rather than single digits.
Worth optimising before step 4 — the LUT lookup is the obvious target, and a slab of
`Vector256<float>` work is the obvious means.

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
| **Video** | as today — `ExportQuality`, `ExportSpeed`, codecs, in/out points, scaling |
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

1. **EXR decoder, converting.** Tone-mapped to Bgra32 through Blender's OCIO config.
   Changes nothing structurally, makes the viewer properly useful for renders, and
   surfaces how much a hand-written reader hurts before anything else depends on it.
2. **Float frames as a second path**, for the held frame only. Exposure, curves and the
   histogram become real. Reveals what the time budget actually looks like.
3. **Adjustment layers with the ★ tools.** Curves, white balance, LGG, HSL, vibrance,
   clarity, LUT. Single layer, no masks. Already a genuine grading tool.
4. **The batch run and sequence export.** At this point the workflow closes, and Atelier
   is finished as a product even if nothing further is built.
5. **Layer stack, blend modes, luminance and gradient masks.**
6. **Passes and light mixing.**
7. **Cryptomatte.** The distinguishing feature, and last, because it needs the stack and
   the masks in place to be worth anything.
8. **The remaining tools from section 7**, in the order people ask for them.

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
