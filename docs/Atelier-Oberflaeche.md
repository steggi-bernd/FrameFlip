# Atelier — the interface, rethought

[← back to the start page](../README.md) · [the Atelier itself](Atelier.md)

The tools are built. Section 7 of the design document is complete: thirteen of them
across five kinds of pass, all measured, all tested. What is not built is a way to
*use* them that anybody would call comfortable. This document is about that, and it
starts from what went wrong rather than from a picture of a nicer screen.

---

## 1. What is actually wrong

Eight things were reported in one sitting. Sorted by what causes them, they are three
problems, not eight.

**The mouse in the picture has no visible mode.** Today a click means "pick a
cryptomatte" or "drag the selected layer" depending on state that lives in fields and
is shown nowhere. So dragging "doesn't really work, at least not with the layer that
is selected" — of course not: whether it works depends on invisible conditions. The
frame around a layer appears because a layer is selected, which is not the same
question as "do I want to move something right now".

**The panels are furniture, not tools.** One fixed column, 300 px wide, layers on top,
colour below, everything else in it. Nothing can be moved, resized, torn off or put
away. Eleven collapsed sections in one scrolling column is a filing cabinet, not a
workspace.

**The list is a list, not a stack.** Reordering works with buttons because there is no
drag; the keyboard does nothing because no key is bound; the bottom layer cannot be
deleted and does not say why; files can be dropped on the list but not on the picture.
Every one of these is a missing affordance in the same control.

Everything else on the list was a bug, and the worst of them — cut-out PNGs showing
pixelated fog — is fixed and has test files of its own now. The rest are listed in
section 6 below.

---

## 2. What this must not become

Photoshop is the right reference for *arrangement*, and the wrong one for *purpose*.
Copying it wholesale would cost the three things that make this program worth opening:

- **A frame stands for a sequence.** Everything here is done once and applied to three
  hundred images. The interface has to keep saying so — the frame number, the gaps in
  the sequence, the batch run, the jump to the darkest and brightest frame. Photoshop
  has no idea what a sequence is.
- **Layers are passes.** The stack was born to reassemble a render, not to hold
  photographs. A pass is a layer with a blend mode, and light mixing came out of that
  for free. That has to stay visible, not be renamed into "smart objects".
- **The selection comes from the file.** Cryptomatte is not a selection tool that
  guesses; it is the render telling you what it drew. That deserves to be the second
  button in the tool column, not an item in a submenu.

So: the *shape* of Photoshop, because it costs nothing to learn, and none of its
assumptions about what a picture is.

---

## 3. The shape

```
┌──────────────────────────────────────────────────────────────────────┐
│  file · frame 0047 · view transform · compare ▸                      │  head
├────┬────────────────────────────────────────────┬────────────────────┤
│ V  │                                            │ ┌ properties ─────┐│
│ W  │                                            │ │ (the tool that  ││
│ C  │                the picture                 │ │  is active)     ││
│ H  │                                            │ └─────────────────┘│
│ I  │                                            │ ┌ colour ─────────┐│
│    │                                            │ │ …               ││
│ ── │                                            │ └─────────────────┘│
│ ▣  │                                            │ ┌ layers ─────────┐│
│    │                                            │ │ …               ││
├────┴────────────────────────────────────────────┴─┴─────────────────┴┤
│  ◀ ▮▮▯▯▮ ▶   frame strip, gaps marked            export ▸            │  foot
└──────────────────────────────────────────────────────────────────────┘
```

Four regions. The head says what is on screen, the foot says which frame and what
happens to all of them, the left column says what the mouse does, the right column
says what the picture is made of.

Three splitters are draggable: left column width (or collapsed to icons), right column
width, and the division between panels in the right column. Each remembers itself.

---

## 4. The tool column — the part that is genuinely new

A tool column is not decoration. It gives the mouse a mode, and every one of these
modes is a behaviour the program already needs and currently hides or lacks:

| | Tool | What the mouse does | Today |
|---|---|---|---|
| **V** | Move | move, scale, rotate the selected layer | hidden mode, appears with the selection |
| **W** | Select | click an object → cryptomatte; shift-click adds | buried behind a mode flag |
| **C** | Crop | drag the crop edges of the layer or the picture | **missing** — crop exists only as numbers |
| **H** | Hand | pan; space held anywhere does the same | scrollbars only |
| **I** | Pick | read a colour, a value — **and set the focus distance** | **missing**; the depth-of-field focus is a slider in metres because there is no picker |

The last row is the clearest case: post depth of field asks for a distance in scene
units, and the honest way to answer is to click on the thing that should be sharp.
That tool does not exist, so the control is a logarithmic slider and a hope.

**The rule that makes a tool column worth having:** the picture does what the active
tool says and nothing else. No tool, no frame. Selecting a layer selects a layer; it
does not arm the mouse.

---

## 5. The right column

Three panels, top to bottom: **properties**, **colour**, **layers**.

- **Properties** shows the active tool's settings — the placement numbers for Move, the
  cryptomatte list for Select, the crop values for Crop. Today the placement numbers
  live in the layer panel and the cryptomatte list somewhere else; both belong to the
  tool that uses them.
- **Colour** is the existing grading panel, unchanged in content. Its eleven sections
  stay collapsible, and they keep the order of the computation, because that order is
  the answer to "what does this slider act on".
- **Layers** goes to the **bottom**, where every program that has layers puts them.
  The old argument — reading order equals computation order — was a good argument and
  it lost to twenty years of muscle memory. It survives inside the column: within the
  colour panel the sections still run in the order things are computed.

**Modularity, honestly costed.** Real docking — tear off, float, re-dock, remember the
arrangement — is what people mean when they say "modular", and in WPF without
third-party libraries it is weeks of work and a permanent source of odd bugs. This
project has no third-party packages and should keep it that way. So, in two stages:

1. **Splitters, tabs, and memory.** Panels can be resized, collapsed, reordered within
   the column, and stacked as tabs when there is no room. The arrangement is saved.
   This covers most of what "less rigid" actually means and costs days, not weeks.
2. **Tear-off into a window.** A panel can be pulled out into a plain window — useful
   on a second monitor, which is the real reason people want floating panels. It is a
   `Window` holding the same control, not a docking manager. Re-docking is a button,
   not a drag target.

Anything beyond that is a docking framework, and it should be written down as *not
chosen* rather than left as an expectation.

---

## 6. The stack, as a stack

The layer panel keeps its contents and gains the behaviour people expect from a list
of layers:

- **Drag to reorder**, with an insertion line, including into and out of groups. The
  buttons stay for the keyboard and for precision.
- **Keys:** Delete/Backspace removes, Ctrl+J duplicates, Ctrl+G groups, Ctrl+Shift+G
  ungroups, Ctrl+, toggles visibility. Arrow keys move the selection, Alt+arrow moves
  the layer.
- **The bottom layer is the picture, and it says so.** It gets a lock badge and no
  delete. "You cannot delete the last layer" is correct behaviour with no explanation;
  a padlock is the same behaviour with one.
- **Drop a file anywhere** in the window and it becomes an image layer — over the
  picture, over the list, over the tool column. Dropping on the picture places it where
  it was dropped.
- **Thumbnails show the layer's own coverage** on a checkerboard, so a cut-out layer
  looks cut out in the list.

---

## 7. What is a bug and gets fixed regardless

These are defects in the current build, not consequences of its shape. They are listed
here so that they do not get folded into a redesign and disappear:

| | Symptom | What it was |
|---|---|---|
| 1 | ~~Cut-out PNGs show pixelated fog in the transparent areas~~ | **fixed**, and not where it first looked. Two separate defects had to be closed before it went away, and the second one had been hiding the first: an image layer the same size as the picture ignored its own alpha, **and** the picture itself - the bottom layer - is a *pass*, for which the composer deliberately does not apply coverage, because in an EXR alpha zero means "no ray hit here" while the colour beside it is real light. A PNG opened as the picture was therefore unmatted, and under the matte most files carry whatever was left in the buffer. A third path made the fix invisible: a single unaltered layer is passed straight through rather than copied, so nobody applied a matte on that route either. Measured on one file: 83.78 steps of neighbour noise as the picture, 0.00 as a layer. |
| 2 | ~~Dragging in the picture lags far behind the mouse~~ | **fixed.** It recomputed once per mouse *message* rather than once per frame, so the queue backed up and the pointer ran away from the picture. |
| 3 | ~~The placement frame sits in the wrong place~~ | **fixed.** The Move tool gave the frame an owner, and a later pass found why it could still drift: the mapping between mouse and picture existed **three times** — once for clicks, once for drawing, once for the grab radius. Three copies of one formula are three chances to let them diverge, and when click and drawing diverge you pull a corner that is not where it looks. They now all ask `ImageHit.Scale` / `ImageHit.Exact`. The mapping had no test at all until then; it has 106 now. |
| 4 | ~~Dragging affects the wrong layer, or none~~ | **fixed**, and it had two causes rather than one. The mode fixed the first. The second was a side effect nobody would look for: `SetVisible` also *selected* the layer, so clicking an eye moved the frame to a different layer — you grabbed one and moved another. The third was that the drag handler re-read the current selection on every message instead of using the layer the frame was built for; the selection can shift mid-drag because the strip rebuilds itself per frame. |
| 5 | ~~Reopening shows no images until one is added~~ | **fixed.** The recipe was remembered, the picture was not; the Atelier now brings both back the first time it is looked at. |
| 6 | ~~A newly added layer shows as hidden, and the eye toggles the wrong way~~ | **fixed**, and it was a consequence of the canvas gap rather than of the eye: with no *visible* pass layer nobody set a canvas size, so nothing composed at all and every layer looked inert. New layers start visible by default, and the eye uses `Click` with `IsChecked` set before the handler is attached, so it cannot fire on a rebuild. What it *did* do wrong was carry the selection with it — see 4. |
| 7 | ~~Some PNGs do not appear at all~~ | **fixed.** All fourteen test files read and compose correctly, and the two causes behind it are closed: the missing canvas size (see 6) and the pass-through shortcut, which returned a single "unaltered" layer without applying its matte. `IsNeutral` now also counts the layer's own tools, so a layer carrying a curve is no longer waved through unrendered. |

Also fixed while in there: **dropping a file on the empty Atelier did nothing**, because
the handler required an open picture — which is what the person was trying to open. The
first file becomes the picture now, the rest become layers. And the layer list got what a
layer list needs: **drag to reorder**, with a line showing where the row will land, and
**keys** — Delete, Ctrl+J, Ctrl+G, Ctrl+Shift+G, Alt+arrow. The list had
`Focusable="False"`, so no key had ever reached it.

The test material for all of these is in `FrameFlip-Testsequenzen/png_ebenen`,
generated by `werkzeug/pngs_bauen.py`: fourteen files covering garbage under the
alpha, black under the alpha, premultiplied, hard edge, greyscale with alpha, palette
with transparency, sixteen bit, interlaced, and smaller than the picture. No private
project is needed to reproduce any of it, which is the point.

---

## 8. The filter gallery

This started as a bug. A cut-out PNG shown without its matte revealed the colour hidden
under the transparency — real image data, in the picture's own palette, in a place
nobody expects it. That is the family Photoshop calls a filter gallery and the rest of
the world calls glitch art, and it turns out FrameFlip is unusually well placed to host
it.

**Why it fits.** Four of the five pass kinds already exist. Three of the four effects
land on one of them without inventing anything:

| Effect | Pass kind | Why that one |
|---|---|---|
| ~~Dither~~ | place-aware (`IOpticsTool`) | **built.** Ordered (Bayer 8×8, built by recursive doubling rather than written out as 64 numbers) and random, with levels, cell size and amount. ~~Floyd–Steinberg is not there and will not be~~ — **that was too absolute, and it is built now.** Error diffusion cannot be a place-aware tool, because it writes into neighbours that have not been reached yet; it cannot be threaded and it cannot run on the coarse preview grid. But it is exactly the **sixth pass kind** this document already wanted for pixel sorting: a pass over the finished frame, in order, on one thread. So that kind now exists (`IFramePass`), and error diffusion is its first entry — serpentine, so the error does not always drift right. The cost is stated where it is paid: it stays out while a slider is dragged and joins on release, and it does not run in the 16-bit export, where dithering to two levels would contradict the format. The test that matters is not "does it quantise" but "does an 8×8 cell keep its average brightness": within 0.02 at 2, 3 and 6 levels. |
| Wave glitch, displacement mapping | pixel-moving (`IGeometryTool`) | reads from a displaced position, exactly like the lens tools |
| ~~Reveal what is under the matte~~ | the composer | **built.** A per-layer amount, 0 to 100%, that lifts the layer's own coverage towards full. Off, the layer is cleanly cut out; at 100% it covers everywhere and whatever sits under its transparency is in the picture. Measured on the test file: 0.00 steps of neighbour noise off, 62.61 at half, 83.78 full. The layer's existing mask decides *where* — so a cryptomatte already steers it, which was the whole point. And the limit is in the test too, because it should surprise nobody: a cleanly premultiplied file carries black under its matte, and then nothing happens. The effect needs material. |
| **Pixel sorting** | **none of them** | see below |

**The one that does not fit.** Pixel sorting re-orders whole *runs* along a row or a
column. It is neither point-wise nor a neighbourhood, and it cannot be done in the grid
preview the other tools share, because a coarse grid changes which pixels are in a run
and therefore changes the result rather than approximating it. It needs a sixth pass
kind — a run pass — and it needs to be honest about being slow. That is real work and
should not be smuggled in as "one more filter".

**What makes it worth doing here rather than in Photoshop.** Every tool in the stack
already takes a mask, and a mask here can be a cryptomatte, a depth pass or a vector
pass. That makes sentences sayable that Photoshop cannot say:

- sort only the pixels belonging to *this object*
- displace by the **normal** pass, so the distortion follows the geometry
- dither only what lies further away than twelve metres
- reveal the hidden colour only inside the character's matte

The original question was "can a cryptomatte decide where it shows". The answer is that
this is the only reason to build it at all. A procedural filter driven by a slider
exists in fifty programs; one driven by render data exists in none of them.

**References worth reading — and not copying.** FrameFlip carries no third-party
packages, so these are sources for the *algorithms*, to be reimplemented. Licences must
be checked before reading, not after:

- Pixel sorting: [ndarray-pixel-sort](https://github.com/hughsk/ndarray-pixel-sort)
  (MIT) is the most compact statement of Kim Asendorf's original technique;
  [a-gratton/PixelSort](https://github.com/a-gratton/PixelSort) (MIT) is a readable
  full implementation; [volfegan/PixelGlitch](https://github.com/volfegan/PixelGlitch)
  is useful less as code than as a catalogue of which variants are worth having.
- Dithering: [robertkist/libdither](https://github.com/robertkist/libdither) is the most
  complete catalogue anywhere — Floyd–Steinberg, Bayer 2×2 through 32×32, blue noise;
  [makew0rld/dither](https://github.com/makew0rld/dither) is MPL-2.0, which is
  file-level copyleft, so read the description and not the files;
  [didder](https://github.com/makew0rld/didder) is worth having installed as a second
  opinion to test our own output against.
- **Avoid:** GEGL is LGPL and G'MIC is CeCILL. Reading either and then writing the same
  thing in C# is a risk that a hobby project does not need to take.

**Where it lives.** Not in a modal dialog. Photoshop's Filter Gallery is a window you
enter and leave, and that is a historical accident from a time when a filter could not
be undone. Here every effect is already a row in a stack with a mask and a blend mode,
and a glitch belongs in that stack like everything else. The gallery is a *section of
the tool list*, not a place you go.

---

## 9. The order this should be built in

1. **The bugs in section 7.** They are cheap, they are independent of any redesign, and
   every one of them makes the current build feel unfinished.
2. **The stack as a stack** (section 6). Contained in one control, immediately useful,
   and it does not depend on the new shape.
3. **The tool column** (section 4). This is the real change: it introduces a mode for
   the mouse and gives Crop and Pick a home. The placement frame becomes the Move
   tool's frame, which fixes bugs 3 and 4 by construction rather than by patching.
4. **The right column** (section 5), stage 1: splitters, tabs, memory, layers moved to
   the bottom.
5. **Masks that can be drawn.** There are six kinds today - none, luminance,
   underlying, pass, gradient, cryptomatte - and cryptomatte works end to end, picked by
   clicking an object with the Select tool. What is missing is a **painted** mask: a
   brush, and a way to see what it covers. Everything else in this program can already
   say *where* it applies except the one way people reach for first.
6. **The filter gallery** (section 8), and in this order: the matte switch first,
   because it is three lines and it is the effect that started this; then dither and
   displacement, which need nothing new; then pixel sorting, which needs a sixth pass
   kind and deserves to be decided on its own merits rather than carried in by the
   other three.
7. **Tear-off windows**, stage 4, if the second monitor turns out to matter.

Steps 1 and 2 are worth doing whatever happens to the rest. Step 3 is the one that
changes how the program feels. Step 4 is comfort, and comfort is worth less than a
mouse that does what it looks like it does.

### Where this stands

Steps 1 to 5 are built. Step 6 is half built: the matte switch and dither are in,
displacement and pixel sorting are not (see `Atelier-Glitch.md`). Step 7 was not chosen.

Three things arrived alongside them that are not in the list above, because they only
became necessary once masks could be drawn:

- **A colour-range mask.** The question a luminance mask cannot answer — sky and skin
  can be equally bright. Measured as hue on the colour wheel, reading the *backdrop*
  rather than the layer itself, so a white fill has something to select and a hue shift
  does not pull its own mask out from under itself.
- **What a mask scopes.** A mask used to make opacity local, always. Paint a spot on
  your picture to brighten it, and everything *except* the spot disappeared. A mask can
  now say whether it limits *visibility* (right for a glow, a watermark) or *colour*
  (right for the picture — as if a copy of the layer sat on top showing only that area).
- **Who the colour tools belong to.** They bound only to adjustment layers, so a picture
  layer could not own tools at all: you set something, it landed in the whole-picture
  correction — which runs *after* compositing — and no mask could reach it. The target
  bar above the sliders is a switch now.

### The right column, second pass

Looked at with synthetic test data rather than read from XAML, the right column had one
root problem: **three sections stacked in one narrow column fought for the same height.**
Give the layers room and the colour section shrank to its tab row — not a single slider
left. Pick a tool in the colour section and its sliders sat below the edge.

- **Two tabs, Colour and Layers, each with the full height.** Which layer the colour tools
  mean is shown by the target switch at the top of the colour tab; the layers tab shows
  how many layers there are.
- **Tool settings moved to a bar above the canvas**, as in every image editor. The brush
  size sits where one paints; the old properties section spent a permanent hundred
  points on a description.
- **Colour tab:** histogram fixed at the top, categories in one row with short names,
  tiles as an actual grid. Idle tiles used to be drawn at 62 % opacity — which reads as
  *disabled*, and most tiles are idle. They are fully legible now; an active tool carries
  an accent dot.
- Found on the way: the *vibrance* slider sat outside every section and appeared under
  every tab. It is with saturation now.

Steps 5 and 6 are the only ones that add something the program cannot do at all today, and
the gallery
is deliberately last — not because it matters least, but because a gallery of
procedural effects without a tool column is a list of sliders, and a mask that cannot
be drawn with the mouse is a mask nobody will use.
