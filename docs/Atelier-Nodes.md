# Atelier — nodes

The layer stack and the effect stack stay the default. Nodes are an optional second mode for
people who want to control the order of effects and how layers are combined. Turning nodes on
is **one-way**. The current stack becomes the starting graph once, and from then on only the
graph applies. That is the decision taken on 2026-09-23. It resembles Blender's split between
modifiers and geometry nodes.

Why one-way: a free graph can express things a stack cannot, such as reusing one image
twice, feeding one mask into three places, or sorting pixels before the correction. A way
back would have to throw those away or fake them.

## 1. What flows along a wire

| Socket | Colour | What it carries |
|---|---|---|
| Image | yellow | Colour plus coverage (alpha), and a flag saying whether that alpha is a **matte** |
| Value | grey | One number per pixel, such as a mask factor or depth |
| Vector | purple | A pass read as data, such as normals or motion |

The matte flag is the one piece of meaning the stack already had implicitly. An image from a
PNG or JPEG, or any placed layer, carries a matte: its alpha says where the image is, and
mixing it applies that alpha to the colour. A pass from an EXR does not. There, alpha 0 means
"nothing was hit here", yet the colour next to it is real light (the sky), and multiplying
by it would erase that sky. The flag travels with the image, so the Mix node can decide the
same way the composer does.

## 2. The nodes of phase 1

Only what is needed to express everything the stack can do today. New node kinds come
later, with the editor.

**Inputs**
- *Datei* (the rendered file): `Bild`, the semantic passes `Tiefe`, `Vektor` and `Normale`
  (resolved per file, as the tools do today), and any named pass a layer or pass mask uses.
  More passes can be switched on as outputs of their own (phase 4).
- *Bilddatei*: another picture on disk, optionally following the sequence number.

**Layers**
- *Platzieren*: offset, scale, rotation and crop onto the canvas. With nothing to place and
  the same size, it is a plain lookup, exactly as in the composer.
- *Belichtung & Tönung*: exposure and tint of one layer.
- *Masks* (produce a Value): brightness of the layer, brightness underneath, colour range,
  pass, Cryptomatte, painted, gradient. Each has black point, white point, softness and invert.
  A pass mask reads its pass through the `Pass` input. Without a wire it falls back to the
  pass it names, as graphs saved before phase 4 expect.
- *Ebenenkorrektur*: the correction a layer or adjustment layer carries today (`LayerGrade`,
  including its borrowed display transform).
- *Begrenzen*: blends the uncorrected and corrected layer by a mask. This is today's
  "mask limits the colour" scope, as its own node.
- *Mischen*: bottom, top and factor. Takes mode, opacity, display space, matte floor and
  reveal. With **Anschneiden** (clip) on, the result keeps the coverage and matte of the
  bottom input. That is what a clipping mask means.
- *Schwarz*: the empty canvas the stack starts from.

**Picture** (after compositing, in the order the pipeline fixes today)
- *Belichtung & Sättigung* (linear), *point tools* (white balance, curves, LGG, HSL, LUT),
  *lens* and *film* (vignette, grain, dither), *light* (glow, halation), *geometry*
  (distortion, colour fringe), *render data* (depth of field, motion blur, displacement),
  *Anzeige* (the view transform, such as AgX), *Tonwert* (black and white point, gamma,
  contrast), *local* (clarity, sharpening, noise, texture, haze), *Obenauf* (watermark, in
  display values), *frame passes* (dither diffusion, pixel sorting).
- *Ausgabe*.

**Added after phase 5** (see section 6)
- *Masken verrechnen*: A and B added, intersected (multiplied), subtracted, minimum,
  maximum, difference. An input without a wire counts as a fixed value.
- *Maske formen*: grow or shrink, then soften, in image pixels.
- *Wertebereich* (Map Range): by default related to the input's own range, exactly like
  the pass mask. Otherwise it works in the input's units, for example metres of depth.
- *Farbverlauf* (Color Ramp): a value turned into colour through stops. The colours are
  meant as seen: behind the view transform they go out as they are, before it they are
  converted to light. Which side counts is decided by whoever reads the ramp.

## 3. How the graph is computed

- **Sources and grid images.** Inputs are full-resolution frames. Everything computed from
  them lives on the grid the display uses: every pixel normally, and every n-th while a
  slider is dragged. Nodes that must read a source at full resolution (placing, a pass mask,
  Cryptomatte, render data) take sources only.
- **Order.** Topological. Each buffer is released once its last reader has run, so memory
  follows the widest point of the graph, not its length. With a pool (the preview, and
  each worker of the export), a released buffer goes back into the pool and the next
  render reuses it. The engine counts how many results hold a buffer, because results
  share buffers: coverage passes through many nodes unchanged.
- **Merging where the stack merges.** Consecutive local tools share their blur and
  consecutive geometry tools resample once. In the stack this is how they are computed, not
  an optimisation. Two separate passes would give a different picture. The engine merges
  such chains so that a converted graph gives the same pixels.
- **Frame passes** work on 8-bit pixels, as they do today. They are skipped while dragging
  and in 16-bit output, as today.
- **Point-wise chains** (light, point tools, lens and film, view, tone) run in one pass per
  grid point, as in the stack's processor. The same operations in the same order give
  the same bytes, without an intermediate picture per node.
- **Caching.** The preview remembers what flows into the selected node. See phase 5.

## 4. From stack to graph

The converter walks the stack from the bottom, as the composer does:

- **Plain layer:** source → Platzieren → Belichtung & Tönung → (Ebenenkorrektur, or
  Begrenzen for colour scope) → Mischen onto what lies below. The mask goes into the factor.
  A pass mask gets its pass as a wire from *Datei*.
- **Adjustment layer:** below → Ebenenkorrektur → Mischen onto below.
- **Clipping group:** the carrier's colour is the bottom input of the clipped layers'
  Mischen (clip on). The result is mixed onto what lies below using the carrier's mode,
  opacity and mask.
- **Group:** its children continue from what lies below. The result is mixed onto that
  saved state using the group's mode, opacity and mask.
- **On top:** Obenauf nodes after the local tools.
- **Picture:** the stack's own order (linear, lens, geometry, data, light, film, Anzeige,
  Tonwert, display tools, local, on top, frame passes).

A test renders many random stacks both ways and demands the same bytes. It covers layers of
every kind, every mask, groups, clipping, placement, every tool type, and both the full and
the coarse grid. That test is the safety net under everything that follows.

## 5. What the stack had to learn first

Writing down the node semantics made three places visible where the stack computed
something it did not mean:

1. **The 16-bit export dropped geometry and render data** unless glow was also on. Fixed
   (`6e79d90`), with a parity test covering every tool type.
2. **A clipped layer on a placed carrier leaked out** wherever the carrier (a logo, say) did
   not cover the canvas. It became an ordinary layer there. A clipping mask shows only where
   its carrier is.
3. **Coverage of groups and clipping groups** ignored their opacity. A group at 50 % over
   nothing reported full coverage, and a clipped layer added coverage outside its carrier.
   Now a group contributes its coverage times its opacity, and clipped layers share the
   carrier's coverage.

Only the alpha channel of exports changes, and only in those cases.

## 6. Phases

1. **Model and engine without an interface.** Converter, engine, and the equality test
   above. Nothing to click yet.

   *Done (2026-09-23).* All named cases and 264 random stacks give the same bytes as the
   stack: full grid, coarse grid and 16-bit. A saved and reloaded graph computes the same
   picture. At 1920×1080, a typical graph (5 layers, 5 picture tools, 24 nodes) takes
   150 ms against the stack's 85 ms (1.8×), and 13 ms against 8 ms while dragging. It
   is usable before phase 5.

   Found and fixed in the stack along the way: the 16-bit export dropped geometry and
   render data, and took no moved alpha from them; clipped layers leaked outside placed
   carriers; groups and clipping reported the wrong coverage; hiding the picture layer
   while an adjustment layer was visible crashed the composer.
2. **Overlay.** The node button in the tool column shows the graph over the picture (dimmed at first; since section 9 the picture stays undimmed).
   You can show, move, zoom and select nodes. The selected node's settings appear in the
   colour panel. The first thing to try in the app.

   *Done (2026-09-23).*
   - **Node button (N).** Opens an offer that says the switch is one-way; converting
     keeps the picture byte-identical. The graph is stored as text in the settings
     (`AtelierNodes`), so a graph this version cannot read does not break the settings
     file.
   - **Preview, histogram and export.** All computed from the graph. The histogram is
     measured on the finished picture; the export gives each worker thread its own copy
     of the graph. A test checks that the export writes the same files as the stack.
   - **Editor.** Drawn, not built from elements. Click selects, drag moves, dragging on
     empty space or with the middle button pans, the wheel zooms around the pointer, Home
     shows everything, M mutes, right-click or Escape cancels a drag.
   - **Colour panel.** A tool node shows its card, which edits the node's own tool. A
     layer correction shows the cards a layer can have. Mix, Mask, Place, the basic
     correction and On top show simple fields with the layers panel's ranges.
   - **Picture tools.** Another tool hides the graph. Move then grabs the selected Place
     or On top node, and the brush paints the selected painted mask node.
   - **Layout.** Columns follow the distance to the output, so each layer's branch sits
     right before its Mix. Long graphs wrap into rows. Tools left at their defaults no
     longer become nodes.

   Not yet in node mode: picking a Cryptomatte in the picture (phase 4). Wiring, adding
   and deleting nodes are phase 3.
3. **Wiring.** Drag nodes in from the palette, connect, disconnect, delete, with cycle and
   type checks.

   *Done (2026-09-23).*
   - **Connect.** Drag from an output to an input or the other way round. A wire grabbed
     at its input comes loose and can be plugged in elsewhere, or dropped on empty space
     to disconnect it. Right-click or Escape puts it back where it was.
   - **Checks while dragging.** Only sockets the wire fits get a ring. Hovering a socket
     that does not fit shows why: a mask is not an image, a read image is needed, or the
     connection would make a loop. The checks live in one place (`NodeEdits`), which the
     editor and the tests both call.
   - **Add.** Right-click or Shift+A opens a menu with layers, masks, picture and all
     effects, under the palette's names. "Image as layer …" creates an image file node,
     a Place node and a Mix node in one step. In node mode the colour panel's palette
     inserts an effect behind the selected node. A loose node dropped onto a wire falls
     into it.
   - **Delete.** Del or X removes the selected node and closes the gap, so the picture
     keeps flowing. Deleting an effect gives the same bytes as the stack without it, and
     inserting one the same bytes as the stack with it.
   - **Undo.** Ctrl+Z / Ctrl+Y cover every structural change, mute and move.
   - **Disconnected output.** An empty picture and a notice, not the old stack.
   - **Arrange.** "Arrange" in the menu lays the graph out again.
4. **Masks, passes and layers as wires, and branching.**

   *Done (2026-09-23).*
   - **Passes as outputs.** Selecting *Datei* lists the file's passes as switches (not the
     Cryptomatte levels, which are IDs, not light). A pass switched on becomes an output of
     its own. Switched off, it takes its wires with it. Undo covers both.
   - **Pass as layer.** In the Layers menu: the pass, a Place node and a Mix on Add, just as
     the layers panel adds a pass. With nothing selected it goes on top of the layers,
     before the picture tools, where the stack would put it. The result has the same bytes
     as the stack with that layer, at full grid, coarse grid and 16-bit.
   - **Pass masks as wires.** The converter wires a pass mask to its pass on *Datei*. Plug
     in another pass and the mask equals the stack's mask on that pass. "Pass as mask"
     in the Masks menu creates one already wired. The colour panel says where the mask
     reads from.
   - **Cryptomatte.** "Cryptomatte: <set>" in the Masks menu creates a mask node. With the
     node selected, Select (W) picks objects in the picture into it. A second click
     removes one, and the colour panel lists the picks with a Clear button. Picking works
     before the node is wired anywhere, because its levels load when it is selected.
   - **Branching.** Shift+D or Ctrl+D (or the menu) duplicates the selected node with the
     same inputs and no readers. The copy reads what the original reads. Where its picture
     goes is up to you. A copy mixed in as a second layer gives the same bytes as the same
     layer twice in the stack. *Datei* and *Ausgabe* cannot be duplicated.
5. **Speed.** Cache per node, and fuse point-wise chains.

   *Done (2026-09-24).* At 1920×1080, the typical graph from phase 1:

   | | before | now | stack |
   |---|---|---|---|
   | full render | 155 ms | 98 ms | 86 ms |
   | while dragging | 13 ms | 9 ms | 7 ms |
   | dragging Tone / releasing | 13 / 155 ms | 5 / 40 ms | |
   | dragging the third layer's Mix / releasing | 13 / 155 ms | 8 / 85 ms | |

   - **Point-wise chains** run in one pass (seven picture tools: 50 → 37 ms).
   - **A pool of buffers.** Writing 18 fresh 1080p buffers takes 49 ms, reused ones 19 ms.
     Nodes take their output buffers from the pool and must write every element; where a
     node leaves a point out (outside a placed layer), it now writes black explicitly.
     A buffer returns only when no result holds it any more.
   - **Cache above the selected node.** The key of each result covers everything that
     goes into it:
     - each node as it would be saved, without position and ID, so moving a node costs
       nothing;
     - the keys of its inputs;
     - the identity of every source frame (a newly read frame is a new object);
     - the frame number, the canvas and the view transform.

     A painted mask adds a hash of its unpacked field for the current frame, because a
     stroke in progress changes only that field. The saved text follows only after the
     stroke. The key alone decides whether a result is still valid. What is kept (the
     inputs of the selected node, for the full and the coarse grid) only decides how
     often it pays off. A point chain splits at the selected node; local and geometry
     chains do not, because splitting them changes the picture.
   - **Export.** Each batch export worker has its own pool. Video renders frames
     concurrently and keeps allocating.

   Tests show the cache gives the stack's bytes after changes at the selected node, on
   the coarse grid and after switching grids. The layers before the node demonstrably do
   not run again, a moved node triggers no work, a newly read frame recomputes everything,
   and a stroke in progress is seen. Counter-checks (one bug each) turn these tests red.

6. **Opening up** (feedback after phase 5).

   *Done (2026-09-24).*
   - **Dropping onto wires.** A free node now falls into a wire that runs under its
     *body*. Before, the middle of its title bar had to lie on the wire, which almost
     never happened. The wire lights up while dragging; with several, the one nearest
     the pointer wins, and only if it fits at both ends. "Free" means the image path is
     free, so a depth-of-field node with its depth wire falls in too. Nodes to the right
     move only as far as needed.
   - **From the palette.** In node mode a palette tile can be dragged into the editor,
     dropped freely or onto a wire, with a ghost node under the pointer.
   - **New nodes** as listed in section 2. A data pass fits into a value input, so
     depth can feed a map range. A muted mask node passes its mask through.
   - **Previews.** A node can show a small picture of its output below its sockets. The
     eye in its header or V toggles it; converting sets it on Place and mask nodes.
     Previews are read from results that exist anyway. A preview in the middle of a
     point chain splits the chain so it shows its own state; the picture stays the same.
   - **Layer list.** The layers tab no longer goes dark in node mode. It lists every Mix
     as a layer, with a thumbnail of what flows into "Oben" and a name taken from its
     source (pass, image file, adjustment, group). The base sits at the bottom. A click
     selects the node and brings it into view, and the dot mutes the layer (with undo).
   - **Passes with thumbnails.** *Datei* shows its passes as tiles with thumbnails, as do
     the "pass as layer/mask" menus. Thumbnails are read once per file in the background.
     Each pass is read downscaled and only its own channels; depth is shown against its
     own range.

   - **Hidden layers.** They used to be dropped when converting (a stack with hidden
     "Glare" layers lost them for good). Now a hidden layer, or one at zero opacity,
     becomes its full branch plus a *muted* Mix. That Mix passes the picture below
     unchanged, so the picture keeps the stack's bytes. It also does not close an open
     group unless doing so is harmless. Switched on, the layer does what it would do in
     the stack. A muted node's other inputs are neither read nor computed, so hidden
     layers cost nothing. Their previews show the source file instead.
   - **Hidden groups and watermarks.** A hidden group is built like a visible one, with
     all its children, and gets a muted Mix. That is done only where closing the open group
     before it changes nothing; otherwise it stays out, as before. An "on top" watermark
     becomes an Overlay node carrying its name, muted when hidden. The layer list shows
     watermarks at the very top and indents group children under their group. Fetching
     into an older graph covers both: a group with its children, and a watermark behind
     the previous visible one or, without one, before the frame passes.
   - **Names.** Every Mix carries its layer's name ("Mask", "Glare_2…"), shown in its title
     and in the layer list, and editable in its fields. The list shows each layer's mask
     next to its thumbnail.
   - **Fetching hidden layers into an older graph.** A graph converted before this, and
     edited since, gets the missing hidden layers without being rebuilt. The stack is
     converted again, and the two chains of layers are laid side by side. The visible
     layers are recognised by their source (file, pass, adjustment, group), not by their
     settings. Each hidden layer is placed at its position with its branch, muted. The
     picture and the user's own nodes stay as they are. The layer list says which layers
     are missing and offers a button. If the chain no longer matches the stack, it
     offers a rebuild instead.
   - **Rebuild from the stack.** Rebuilds the graph from the stored stack via the menu.
     Ctrl+Z brings the old graph back.

   Tests:
   - Map range on depth has the same bytes as the pass mask, full and coarse.
   - Hidden layers in every position (above the base, between carrier and clipped
     layer, clipped, as an adjustment with a painted mask, at zero opacity) keep the
     bytes. Switched on, each equals the stack with that layer visible.
   - Every new node is checked against its formula.
   - Previews change no output.
   - The drop test places the wire under the body, far from the title bar.
   - Counter-checks turn these tests red.

7. **Viewer** (like Blender's).

   *Done (2026-09-24).*
   - **Ctrl+Shift+click** on a node shows its first output in the big picture instead of
     the output. Another click on the same node shows its next output. After the last
     one, the output comes back. A click on *Ausgabe* also returns to the output, and so
     does the button on the badge above the picture or the node menu.
   - **What it shows.** A picture from before the view transform (*Anzeige*) passes
     through it, so it looks the way it would at the output with an *Anzeige* node behind
     it. A picture from after the view transform is shown as it is. A mask or a value is
     shown in grey. A data pass is scaled to its own range, so depth appears as a
     gradient from near to far. Only what leads to the node is computed. A pass shown
     there is read even if no wire uses it.
   - **Always marked.** The node gets an orange outline with a tag naming the output. A
     badge above the picture says what is shown, even when the graph is hidden. The
     viewer stays when you switch to another tool, so you can paint a mask while you
     see it. If the node or its output goes away, the output comes back.
   - **Export unchanged.** The export always writes the output. The histogram measures
     what the picture shows, as Blender's scopes follow the viewer, so a mask's
     distribution can be read off directly.

   Tests:
   - A picture before the view transform equals the graph cut at that point with an
     *Anzeige* node behind it; after it, the same without.
   - A loose mask appears grey. Depth runs dark to bright from near to far.
   - An unknown node shows the output, and the pass shown is read.
   - In the page: the badge and the outline appear, the picture changes and comes back,
     and several outputs are shown in turn.

8. **Working layer list** (like the layers strip).

   *Done (2026-09-24).*
   - **A layer is a Mix and its branch:** source, Place, correction, mask, a group's
     children, the layers clipped to a carrier. Moving, deleting and duplicating act on
     all of it. Whatever in the branch reads the layer below (an adjustment layer, a mask
     on the underlying picture, the lowest child of a group) reads the layer below at the
     new position afterwards. That is what the stack does when a layer moves there.
   - **Chains.** The list is read from chains, not from the computation order: the main
     chain below the fallback, one chain per group (its children, lying on what the group
     lies on) and per carrier (its clipped layers, lying on its picture), and the runs of
     watermarks. Children follow their group and clipped layers their carrier, indented.
     Mixes in no chain (built by hand) are listed at the end and cannot be dragged.
   - **Dragging.** Rows can be dragged; a line shows where the layer will land, indented
     like the chain it would join. The lower half of a group or carrier row means "into it,
     on top". Layers move freely between the main chain and groups. Clipped layers stay
     with their carrier, and watermarks stay among themselves: moving them anywhere else
     would turn them into something else. A group cannot go into itself.
   - **Buttons under the list:** + (adjustment layer, image as layer, pass as layer,
     above the selected layer or on top of the layers), duplicate, delete, one place up
     or down. A new layer in a clip chain is clipped like its neighbours.
   - **Blend mode and opacity** of the selected layer or watermark, below the buttons.
   - **Undo** covers every structural change; a drop that changes nothing leaves no step.
     After moving or duplicating, the graph is laid out again, since the branch would
     otherwise stay at its old place with its wires across the graph.

   Tests:
   - 528 random changes (move, one step, delete, duplicate, add an adjustment layer) to
     random stacks with groups, carriers, clipped and hidden layers, masks on the
     underlying picture and adjustment layers. The same change is made in the stack.
     Both give the same bytes on the full grid, the coarse grid and in 16 bit, and
     nothing is left behind that nobody reads.
   - Three counter-checks turn them red: the branch keeps reading the old layer below;
     all readers follow when inserting, not just those above; delete leaves the branch.
   - In the page: dropping, the buttons, blend mode and opacity change picture and list,
     and undo restores the bytes.

9. **Safety and clarity** (feedback after section 8).

   *Done (2026-09-25).*
   - **No self-connection, no lost wires.** The editor already refused a node wired to
     itself, and any loop. But a wire grabbed at a connected input and dropped on a socket
     that does not fit (its own node's output, or any socket of the same direction) was
     treated as dropped into empty space: the connection was gone, and the picture went
     black. That looked like a crash. Now any socket that does not fit puts the wire back
     and says why. Only empty space disconnects.
   - **A failing computation is named, not swallowed.** The app swallows unhandled errors,
     so an exception while computing the graph left the picture frozen. The page now
     catches it, clears cache and pool, shows the error in the editor and suggests Ctrl+Z.
     The next successful computation clears it.
   - **The picture stays true.** The 60 % black veil behind the nodes is gone, because it
     falsified exactly what one grades with the graph open. Node bodies are opaque anyway;
     wires get a dark halo so they stay visible on bright pictures.
   - **Brush size and hardness on the picture.** Ctrl with the left button dragged
     sideways sets the size, Ctrl with the right button the hardness. The ring stays where
     the drag began and shows the brush falloff, with both values beside it. The sliders
     follow live. Nothing is painted, and no mask layer is created.

   Tests:
   - 1500 random editing steps on random graphs (connect, disconnect, insert, delete,
     duplicate, mute, move layers, viewer on any output). After each step the graph is
     computed, laid out, listed and saved. None throws, none builds a loop, and no node
     accepts itself.
   - On the page: every node wired to itself in both directions loses no wire, and 80
     random drags with computing behind them throw nothing. A torn pass picture (arrays
     shorter than it claims) throws in the model, shows as an error on the page and clears
     once the picture is whole again. Without the safety net that test turns red.
   - The brush knobs move size and hardness both ways within their limits, the sliders
     follow, and no mask layer appears.

10. **The hub and the layer menus** (feedback after section 9).

    *Done (2026-09-25).*
    - **The hub replaces the right-click menu** in the node editor (right-click or
      Shift+A) and the plus of the layer list. It is drawn in the app's own style instead
      of the Windows menu. At the top is a search: type and press Enter. It searches all
      categories, ignores case, and "ae" finds "ä". On the left are the categories, with
      the view actions below them (all previews, arrange, show all, rebuild, viewer off).
      On the right are the tiles:
      - Layers: the passes of the file with thumbnails (click adds it as a layer,
        Shift+click as a mask), new layers (adjustment layer, image as layer, image file),
        the building blocks, and the layers in the graph with thumbnails to jump to.
      - Masks: the mask kinds, every pass as a mask, the cryptomattes of the file.
      - The effects in palette order with the palette's glyphs. They can also be dragged
        into the editor, freely or onto a wire.
      - Picture, convert, and "recent" (what was taken last, newest first).
      At the bottom are the selected node's actions (mute, viewer, preview, duplicate,
      delete), each with a short name.
    - **Right-clicking a wire** opens the hub with a note: new nodes drop into that wire.
    - **The layer lists get a menu per row**, in the same style. The stack strip offers
      rename (new there), visible, duplicate, remove, up, down, into or out of a group,
      and clip. Each entry takes the same path as its button. The node list offers
      rename, visible, show in the graph, alone in the viewer (what flows into the Mix's
      top), duplicate and delete with the branch, up, down, and clipped. Entries that
      cannot act (the top layer up, out of no group) are greyed out and do nothing.

    Tests:
    - Hub: a right-click on the wire into the output finds "vignet" in the search, and
      Enter inserts the vignette into that wire. All passes stand as tiles; a click adds
      one as a layer, Shift+click another as a mask; "recent" lists both, newest first.
      The bottom bar mutes the selected node.
    - Menus: the stack row menu renames, hides, clips and duplicates, and omits what
      cannot act. The node row menu renames the Mix, duplicates the layer with its
      branch, and shows the Mix's top input alone in the viewer. The test found that
      swapping the rename row for its text field threw in WPF; it is now removed and
      inserted instead.

11. **Fixes after trying it** (feedback after section 10).

    *Done (2026-09-25).*
    - **The brush in node mode** only caught the mouse while a painted mask node was
      selected. Otherwise it was dead, and so were the Ctrl drags for size and hardness.
      Now it always catches. With a painted mask selected, or a layer whose factor
      comes from one, it paints that mask. Otherwise the first stroke creates a mask
      layer, as in the stack: an adjustment layer with a painted mask above the selected
      layer (or on top of the layers). It is named like the stack's, changes nothing until
      its correction is turned, and the mask is selected so painting continues.
    - **A layer chosen in the hub** only jumped to it. Now a click adds its picture as a
      new node: an image file as a new image file node, a pass as a Place wired from the
      file (which exists only once). A layer without a picture of its own (adjustment,
      group) is duplicated with its branch. Shift+click jumps to it as before.
    - **The hub opened on pressing the right button**, so releasing it landed in the
      hub's search field, whose built-in Windows menu popped up over it. The hub now
      opens on release, beside the pointer rather than under it, and allows no Windows
      menu at all.
    - **Backspace deletes nodes**, like Del and X.
    - **The visibility mark** in the node layer list is the same dot and ring as in the
      stack strip (the style moved to the theme); the boxed button with a dash is gone.

    Tests: the hub opens on release and not on press; a layer tile adds a Place with the
    file's wire, Shift jumps; Backspace deletes; the brush catches without a mask, the Ctrl
    drag sets the size, the first stroke adds exactly three nodes (correction, Mix,
    painted mask) and leaves the picture unchanged, the next stroke and the selected
    layer paint the same mask. The old test that demanded a dead brush now demands the
    opposite.

12. **Undo everywhere, snappier edits, node menu, clearer marks** (feedback after 11).

    *Done (2026-09-25).*
    - **Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z** act on the whole page in node mode, not only while
      the editor has the keyboard focus (after a click in the layer list, the colour strip
      or the hub it lay elsewhere). A text field keeps its own undo.
    - **Value edits are steps too:** a slider in the colour strip, blend mode and opacity in
      the layer list, a brush stroke, moving a Place. Each gesture is one step. What goes
      into the history is the last kept state, because the change has already happened
      when it is reported; a report without a change (a panel filling itself when a node
      is chosen) leaves no step.
    - **Snappier edits.** A build step did the full computation at once and held the page
      until the whole picture was done (22 ms of 27 ms at 640x360, many times that at
      1080p or 4K). It now takes the path of a slider: the editor shows the change at once,
      the picture follows coarsely in the next frame and sharp after a pause (180 ms). A
      build step now costs about 3 ms. The graph cache also forms its keys only for what
      lies before the selected node, and none without a selection; forming them for every
      node made a full pass almost twice as slow as without a cache.
    - **Right-click on a node** opens its own menu: rename, muted, preview, viewer, insert
      after (opens the hub; the chosen node goes behind this one), duplicate, disconnect
      all wires, delete. Right-click on empty space or a wire opens the hub, as before.
    - **Node marks.** The preview switch is a small picture (filled with a hill when on, a
      faint frame when off) instead of an eye that was crossed out on almost every node. A
      muted node gets a grey header, a "muted" tag instead of a sign after the title, and
      a dashed line showing the path the picture takes past it, as in Blender.

    Tests: a stroke on a painted mask is taken back by Ctrl+Z with the focus in the layer
    list, a text field keeps Ctrl+Z; blend mode and opacity are separate steps before the
    layers; a slider move is its own step before the node; after a build step the page
    computes coarse first and sharp shortly after; right-click on a node opens its menu,
    on empty space the hub; "insert after" places the vignette behind the node.


13. **Right-click menus outside the editor** (the ideas from 10, now built).

    *Done (2026-09-25).*
    - **Picture in the Atelier:** pick the colour here (switches to the pick tool and reads
      the point), one row per Cryptomatte of the file with "object here as mask", compare
      with the original, 100 % / fit. The object mask is the same kind of layer the brush
      makes on its first stroke: an adjustment layer whose Cryptomatte picks exactly that
      object, named after it. In node mode it arrives as correction, Mix and mask node on
      the factor; in stack mode as a mask layer in the strip. Compare shows the original
      until the next click or key press; it is a glance, not a switch that stays on. 100 %
      puts the clicked point in the middle. Not with the brush, where the right button
      erases.
    - **Layer lists** (from 10): rename, duplicate, delete, clip, into a group, show in the
      graph.
    - **Tool cards in the colour strip:** on/off (the values stay), reset, copy settings,
      paste settings. Paste is offered only on a card of the same kind, once something has
      been copied. What is copied is a copy: turning the card afterwards does not change it.
    - **Dashboard:** a picture in the film strip offers open in the Atelier, show in
      Explorer and copy path. The shown sequence offers the same for its current picture;
      another sequence has no picture chosen yet and offers only Explorer and the path.

    Tests: the picture menu offers pick, one object row per Cryptomatte, compare and 100 %;
    the object mask in node mode and in the stack picks exactly the object under the
    pointer and is named after it; compare returns on the next click; 100 % and back. The
    card menu copies, pastes, switches off and on and resets; the dashboard menu opens the
    picture in the Atelier. Explorer and the clipboard are not triggered by the tests.

14. **Painting recomputes only what the brush touched** (from the plan in
    [Projekte und Masken](Projekte-und-Masken.md), phase A).

    *Done (2026-09-25).* A brush tick in node mode used to render the whole picture on the
    coarse grid (4K: 33 ms, and blurred until release). `GraphEvaluator.RenderRegion` now
    evaluates the same graph with the touched rectangle as its grid, at full resolution,
    and writes it in place (4K, radius 80: 4.6 ms). What lies before the selected node comes
    from the graph cache and is cut to the rectangle; only point-wise nodes are recomputed
    (mix, mask, colour, place, light, tone, optics, value nodes, and grading without local,
    geometry, data or frame tools). Optics (vignette, grain, dither) need the position of a
    pixel but no neighbours, so they count as point-wise. Anything spatial behind the mask,
    a missing whole picture to paint on, or no selection makes it refuse, and the page
    renders as before.

    *Smoother still (same day, after "still stutters now and then").* Three costs that hit
    every stroke, independent of the rectangle:
    - Every mouse report asked the undo history whether a step was due, and wrote the
      whole graph with all painted masks as text to find out (0.4 ms with six masks, many
      times per frame with a fast mouse). The step is the whole stroke; it is remembered
      once, on release.
    - Release rendered the whole picture at once, a fifth of a second at 4K between two
      strokes. Now release only writes what the last ticks left, and the whole picture (for
      previews and histogram) follows once the brush has rested for 0.7 s. If a tick fell
      back to the coarse whole picture, release renders sharp at once, as before.
    - The rectangles came from the page's pool, which holds four sizes; their changing
      sizes pushed out the whole-picture grids. They now use their own pool, and the
      rectangle is snapped outward to 32 pixels so its sizes repeat.

    Measured at 4K with six painted masks: 0.013 ms per mouse report, 3.1 ms per tick
    with vignette and grain behind the mask, 1.1 ms on release (the whole picture, 171 ms,
    follows at rest).

    Tests: the rectangle written into the old picture equals the new whole picture byte for
    byte, with the mask, its mix or the output selected, with vignette and grain in the
    mask layer and at the end, and with pooled grids that still hold an older rectangle;
    sharpening behind the mask is refused and leaves the picture untouched; on the page,
    a brush tick changes the picture without a coarse pass and matches a full pass
    afterwards, a stroke of many reports adds exactly one undo step on release, the picture
    at release already holds the stroke's last piece, and the whole picture follows at
    rest - at once after a coarse tick.

15. **Masks as elements of their own** (from the plan in
    [Projekte und Masken](Projekte-und-Masken.md), phase D, points 3, 4, 10 and 11).

    *Done (2026-09-26).*
    - **Mask menu:** the menu of a mask node offers cut out as layer, detach, duplicate and
      connect to layer; a painted mask also offers its history. Detach removes the mask
      from the factor of its layers, which then act everywhere; the mask stays as a free
      node. Duplicate makes a free copy with an id of its own (`LayerMask.Id`, a fixed id
      that history and "shared with" hang on). Connect lists the other layers; a layer that
      had a mask before gets this one instead. In the picture menu, node mode adds "object
      here as layer" per Cryptomatte.
    - **Cut out as layer** uses the new `CutoutNode` (image times mask gives an image with
      coverage) and puts a normal mix directly above the mask's first layer, or on top of
      the layers for a free mask. What is cut out is what that point already shows: the same
      image that flows into the new mix from below. So the picture stays byte for byte the
      same, at soft mask edges and under every blend mode. Cutting the layer's own image
      would double it at a soft edge (by m(1-m)(S-B)), and cutting the file's picture would
      lose what the layers below did to it. The original layer keeps its mask.
    - **In the graph:** wires that carry a mask are dashed and have their own colour. A mix
      with a mask shows a tab above its header with a small picture of the mask and its
      name. A mask node says "free" when it is plugged in nowhere and "-> n layers" when
      several layers use it. A selected mask outlines its layers in the graph and in the
      layer list. `MaskUse` answers who uses a mask for the menu, the graph and the list.
    - **Layer list:** free masks get a section "Masks" below the layers, with the same menu
      as on the node plus show in graph and delete.
    - **Mask history** (`MaskHistory`, `MaskHistoryKeeper`): every stroke is recorded on
      release. A new state appears once 1 % of the mask's cells differ from the last
      state (a tenth at first; finer on request); the same spot painted ten times counts once. At most 20 states. Every fifth
      state is a snapshot, the others hold only their strokes, which are replayed from the
      snapshot before. On recording, the stroke is replayed on the previous mask and
      compared: if the mask changed any other way (undo, restore, a stroke in the stack),
      the next state is a snapshot. Histories are stored per mask id (and per frame for
      unlocked masks) next to the project file, in `FrameFlip/<name>.ffdata/verlauf/`, and
      written in the background when a state appears. "Mask history ..." lists the states
      with small pictures; a click restores one as a single undo step. What changed since
      the last state is kept as a state first. Recording costs 0.9 ms on release at 4K.
      The history is also reached from the context menu of a layer in the layer list
      (with the other handles of its mask) and from a button in the brush panel. Since
      almost every stroke now makes a state, the history is turned into text on the
      background writer; states never change after they are made, so a list copy suffices.

    - **Moving the cut-out** (same day): `CutoutNode` has a placement of its own. The mask
      selects at the old spot, and what it selected moves; the original stays underneath.
      With the cut-out layer (or its cutout node) selected, the placement frame of the move
      tool drags the piece, one undo step. The piece is as large as the canvas and sampled
      backwards, bilinear between grid points. Moved, the node reads outside a region, so
      region rendering while painting refuses it and renders whole; unmoved it stays safe.

    Not done: mask rasters still live in the project file (3.3 planned them by content in
    `.ffdata`); only the history is stored there.

    Tests: cutting out keeps the picture byte for byte under normal, screen and multiply,
    for a mask with a layer and for a free one; changing the cut-out layer changes the
    picture only where the mask is; region rendering with a cutout matches the whole
    picture; detach, duplicate (new id, free), connect and "object here as layer" on the
    page, each one undo step and the picture unchanged; mask wires, tags, the mix tab and
    the highlight in graph and list. History: replay equals painting, a state per tenth of
    area with unique cells, every state returns byte for byte (snapshots at 1, 6, 11),
    foreign changes and restores lead to snapshots, 27 states trim to 20 and stay correct,
    save and load in JSON and in the folder next to the project, recording under 10 ms at
    4K, and on the page: painting makes states, the menu shows them newest first, a click
    restores one as an undo step and Ctrl+Z takes it back. Moving: the piece lands byte for
    byte 60 pixels to the right, the original stays, nothing else changes, region rendering
    refuses a moved cutout, the placement survives saving; on the page the frame drags the
    selected cut-out layer as one undo step.
