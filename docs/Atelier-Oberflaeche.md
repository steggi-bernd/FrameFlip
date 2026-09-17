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
| 3 | The placement frame sits in the wrong place | open — comes with the Move tool, where the frame belongs to the tool rather than to the selection |
| 4 | Dragging affects the wrong layer, or none | open — same: the mode is the fix, not a patch |
| 5 | ~~Reopening shows no images until one is added~~ | **fixed.** The recipe was remembered, the picture was not; the Atelier now brings both back the first time it is looked at. |
| 6 | A newly added layer shows as hidden, and the eye toggles the wrong way | probably a consequence of 1: a layer whose transparent areas covered everything in black looks like a layer that does nothing. To be re-checked against the fixed build. |
| 7 | Some PNGs do not appear at all | all fourteen test files now read and compose correctly. Same suspicion as 6 — to be re-checked. |

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
| Dither — Bayer, blue noise, Floyd–Steinberg | place-aware (`IOpticsTool`) | the threshold comes from *where* the pixel is |
| Wave glitch, displacement mapping | pixel-moving (`IGeometryTool`) | reads from a displaced position, exactly like the lens tools |
| Reveal what is under the matte | the composer | a switch, not a filter — it is the bug, inverted |
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
5. **The filter gallery** (section 8), and in this order: the matte switch first,
   because it is three lines and it is the effect that started this; then dither and
   displacement, which need nothing new; then pixel sorting, which needs a sixth pass
   kind and deserves to be decided on its own merits rather than carried in by the
   other three.
6. **Tear-off windows**, stage 3, if the second monitor turns out to matter.

Steps 1 and 2 are worth doing whatever happens to the rest. Step 3 is the one that
changes how the program feels. Step 4 is comfort, and comfort is worth less than a
mouse that does what it looks like it does.

Step 5 is the only one that adds something the program cannot do at all today, and it
is deliberately last — not because it matters least, but because a gallery of
procedural effects without a tool column is a list of sliders, and a mask that cannot
be drawn with the mouse is a mask nobody will use.
