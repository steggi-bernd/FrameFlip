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
  (resolved per file, as the tools do today), and any named pass a layer uses.
- *Bilddatei*: another picture on disk, optionally following the sequence number.

**Layers**
- *Platzieren*: offset, scale, rotation and crop onto the canvas. With nothing to place and
  the same size, it is a plain lookup, exactly as in the composer.
- *Belichtung & Tönung*: exposure and tint of one layer.
- *Masks* (produce a Value): brightness of the layer, brightness underneath, colour range,
  pass, Cryptomatte, painted, gradient. Each has black point, white point, softness and invert.
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

## 3. How the graph is computed

- **Sources and grid images.** Inputs are full-resolution frames. Everything computed from
  them lives on the grid the display uses: every pixel normally, and every n-th while a
  slider is dragged. Nodes that must read a source at full resolution (placing, a pass mask,
  Cryptomatte, render data) take sources only.
- **Order.** Topological. Each buffer goes back to a pool once its last reader has run, so
  memory follows the widest point of the graph, not its length.
- **Merging where the stack merges.** Consecutive local tools share their blur and
  consecutive geometry tools resample once. In the stack this is how they are computed, not
  an optimisation. Two separate passes would give a different picture. The engine merges
  such chains so that a converted graph gives the same pixels.
- **Frame passes** work on 8-bit pixels, as they do today. They are skipped while dragging
  and in 16-bit output, as today.
- **Caching** of outputs above the edited node, and fusing point-wise chains into one pass,
  are phase 5. Phase 1 aims to be right, not fast.

## 4. From stack to graph

The converter walks the stack from the bottom, as the composer does:

- **Plain layer:** source → Platzieren → Belichtung & Tönung → (Ebenenkorrektur, or
  Begrenzen for colour scope) → Mischen onto what lies below. The mask goes into the factor.
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
2. **Overlay.** The node button in the tool column shows the graph over the dimmed picture.
   You can show, move, zoom and select nodes. The selected node's settings appear in the
   colour panel. The first thing to try in the app.
3. **Wiring.** Drag nodes in from the palette, connect, disconnect, delete, with cycle and
   type checks.
4. **Masks, passes and layers as wires, and branching.**
5. **Speed.** Cache per node, and fuse point-wise chains.
