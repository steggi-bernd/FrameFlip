# FrameFlip Bridge — research and design

[← back to the start page](../README.md)

Groundwork for the Blender add-on that reports running renders to FrameFlip, and for
remote control from a phone. Every API claim here is verified against the Blender
source rather than recalled — `docs.blender.org` refuses automated requests, so the
references point into the code instead.

**As of:** 5 September 2026, checked against `blender/blender`, branch `main`.

---

## 1. The hooks in Blender

`bpy.app.handlers`, defined in `source/blender/blenkernel/BKE_callbacks.hh` and bound
to Python in `source/blender/python/intern/bpy_app_handlers.cc`:

| Handler | Argument | What it means here |
|---|---|---|
| `render_init` | Scene | job begins — report resolution, frame range, output path once |
| `render_pre` | Scene | a frame starts |
| `render_post` | Scene | a frame has finished rendering |
| **`render_write`** | Scene | **file written** — the most important handler |
| **`render_stats`** | **String** | progress text, see section 3 |
| `render_complete` | Scene | job finished normally |
| `render_cancel` | Scene | job cancelled |
| `load_post` | path | a different .blend was loaded |

The source describes `render_write` as “on writing a render frame (directly after the
frame is written)”. That is exactly what FrameFlip needs: **the add-on transfers no
pixels.** It reports a path and FrameFlip reads the file itself — which it already
knows how to do, buffer, raw cache and image correction included.

`BKE_callbacks.hh` also records how the events fit together:

> `PRE/POST` handlers may be used along side modal task handlers as is the case for
> rendering, where rendering an animation uses modal task handlers, rendering a single
> frame has `PRE/POST` handlers.

So an animation gives `INIT` → n × (`PRE`/`POST`/`WRITE`) → `COMPLETE` or `CANCEL`.

### Registering for good

Handlers without `@persistent` are removed when a new file is loaded. With the
decorator they survive every file change. That is the whole technique behind “paired
permanently”: the add-on is installed once, registers its handlers on load, and from
then on reports every render in that Blender instance — with nothing to switch on
anywhere.

---

## 2. What Blender does **not** offer

Two limits that shape the cut of the entire project.

### 2.1 A running render cannot be cancelled

`source/blender/editors/render/render_internal.cc` defines exactly one render
operator: `RENDER_OT_render`. Starting works, cancelling does not.

The animation loop in `source/blender/render/intern/pipeline.cc` shows why:

```c
for (nfra = sfra, scene->r.cfra = sfra; scene->r.cfra <= efra; scene->r.cfra++) {
  ...
  if (G.is_break == true) break;
```

The loop breaks on the global `G.is_break` flag, which the window manager sets when
Escape is pressed. Python has no access to it.

The obvious detour does not work either: `efra` is a **parameter**, captured when the
loop starts. Lowering `scene.frame_end` afterwards does not end the run.

### 2.2 The progress text — a correction

This section first claimed that remaining time, memory and the sample counter were
missing in the interface. **That was wrong.** It was based on this passage from
`intern/cycles/blender/session.cpp`:

```cpp
if (background) {
    timestatus = "Remaining: " + time_human_readable_from_seconds(remaining_time) + " | ";
    timestatus += string_printf("Mem: %dM | ", (int)ceilf(mem_used));
}
RE_engine_update_stats(&b_engine, "", (timestatus + status).c_str());
```

The conclusion was tempting but did not survive measurement. A recording from a **GUI
render** with Cycles shows:

```
Remaining: 01:19.38 | Mem: 6543M | Sample 2304/4096
```

All there. The source passage was read correctly, the inference from it was not — a
lesson in how a single code path does not establish behaviour.

The recording shows something else: the text is **two-part** when a phase has a
detail.

```
Mem: 1M | Synchronizing object | Fingernails.001
Mem: 6544M | Updating Volume | Building octree for CryoMist
Mem: 198M | Updating Geometry BVH Model_0_mesh0000.017 119/172 | Building BVH
Time: 02:01.99 (Saving: 00:00.18)
```

Three rules for the parser follow: **join** the descriptive parts rather than taking
only the last; the closing `Time: …` message is not a current activity; and `119/172`
without the word *Sample* is not a sample counter.

### 2.3 What follows from that

For the **metrics**, nothing any more — they are fully available in the interface.

For **cancelling and queueing** it still stands: both require a process of their own
(`blender -b file.blend -a`), because 2.1 leaves no other route and a queue needs
something outside Blender anyway. Renders started by hand in the interface are
reported in full — they just cannot be stopped from outside.

---

## 3. Where each number comes from

| Metric | Source | Reliability |
|---|---|---|
| Overall progress | `render_write` counts frames against `frame_start`/`frame_end` | exact, no parsing |
| Sample progress | `render_stats` string | format is engine- and version-specific |
| Remaining time, Cycles memory | `render_stats` string | present in the interface too, see 2.2 |
| Time per frame | difference between two `render_write` | exact |
| CPU, RAM of the machine | **FrameFlip** | exact |
| GPU load, VRAM, temperature | `nvidia-smi`, invoked by FrameFlip | exact, NVIDIA only |
| Preview image | FrameFlip reads the written file | exact |

The principle behind it: **the add-on reports what only Blender knows. Everything
about the machine is measured by FrameFlip.** Blender ships no `psutil`; an add-on
collecting system values itself would need a third-party dependency or would load
Blender's main thread. FrameFlip measures CPU and GPU anyway.

The `render_stats` string is parsed **defensively**: what cannot be parsed is simply
absent — it must never cause a message to be dropped or the add-on to throw.

---

## 4. Compared with Render Control

[rendercontrol.solutions](https://www.rendercontrol.solutions) is the closest model.
What it advertises, and how it maps onto this:

| Render Control feature | Feasible | Note |
|---|---|---|
| Percent, frames, samples, elapsed, remaining | yes | complete, even for a render in the interface |
| Load, VRAM, temperature per card | yes | via `nvidia-smi`; AMD would need another route |
| Last minute as a graph | yes | FrameFlip already samples on a clock |
| Multiple cards separately | yes | `nvidia-smi` lists all of them |
| Live preview of the current frame | yes | FrameFlip reads the file and scales it to phone size |
| Scrubbing back through finished frames | **better** | that is FrameFlip's core business |
| Stop the render | yes | **only** as a background process, see 2.1 |
| Queue another file | yes | requires background processes |
| Save the .blend | yes | `bpy.ops.wm.save_mainfile` from a timer |
| Sleep / shut down the PC | yes | FrameFlip, not the add-on |
| Pairing by QR code or six-digit code | yes | see section 5 |
| End-to-end encrypted | yes | see section 5 |
| Download single frames or a video | yes | FrameFlip already exports through ffmpeg |
| Alarm on completion | yes | push notification from the app |
| Up to three render PCs | yes | the relay distinguishes devices anyway |

**A conjecture, explicitly labelled as one:** Render Control names no PC application
of its own, yet offers a queue and “Stop render”. A queue only makes sense if
something outside Blender starts the jobs, and stopping cannot work any other way
per 2.1. Much suggests that Render Control also runs renders as separate processes.
It cannot be proven from the outside.

*(The sample counts were originally part of this conjecture — until 2.2 showed they
exist in the interface as well. The argument is weaker for it, not void.)*

---

## 5. Transport and encryption

### Layout

```
Blender add-on ──local──> FrameFlip ──encrypted──> relay ──encrypted──> app
    (thin)               (the hub)           (sees ciphertext only)
```

The add-on talks exclusively to FrameFlip on the same machine. It therefore needs
**no network, no cryptography and no third-party packages** — which keeps it small
and fast, and keeps the GPL boundary clean: the add-on is its own repository under
GPL, FrameFlip stays MIT.

### Pairing

A QR code displayed by FrameFlip carries a random 256-bit key. It therefore **never
crosses the network**. Anyone who has not seen the code can decrypt nothing — the
relay included. A six-digit code as a fallback for when the screen cannot be
photographed.

### Encryption

AES-256-GCM, session keys derived through HKDF from the paired secret. Both are
available without a third-party library: .NET 8 ships `AesGcm`, `HKDF` and
`RandomNumberGenerator`, Android has `javax.crypto` and the Jetpack Security
building blocks.

That is genuine end-to-end: the relay forwards bytes it cannot read. It needs neither
certificates for the payload nor trust.

### Relay

A small service that pairs connections and forwards packets — nothing more. No
storage, no decryption, no state beyond the pairing. Runs as a container behind a
reverse proxy that provides TLS for the transport layer.

Because it only forwards, it is undemanding: a few hundred kilobytes per second for
metrics, plus a preview image on request. Previews are generated **on demand only**,
at phone size and as JPEG — never the 72 MB PNG from the render folder.

> The relay's operational details (host, domains, credentials) do **not** belong in
> this public repository. They live in the private infrastructure documentation.

---

## 6. Staying frugal

The one principle everything else follows from:

> **Handlers must do nothing except put an entry on a queue.**

They run on Blender's main thread and block it. A network call at that point stalls
the render on network latency — on a poor mobile connection that is seconds per
frame. A background thread drains the queue and writes to the socket; it never
touches `bpy` data.

Further:

* `render_stats` fires up to once a second in the interface and more often in
  background mode — it is throttled.
* Previews on request only, not on every frame.
* The add-on transfers paths and events, no image data.
* Idle — with no render running — the add-on costs nothing beyond having its handlers
  registered.

---

## 7. Open points

* **AMD graphics cards** — `nvidia-smi` covers NVIDIA only. AMD would need a
  different route; until then VRAM and temperature are missing there.
* **Other render engines** — EEVEE's `render_stats` string looks different from
  Cycles'. The parser has to tolerate both or cleanly deliver nothing.
* **Blender version** — the handlers have existed unchanged since 2.8x, whereas the
  layout of the status text changes between versions. That is why no feature depends
  on it being parseable.
