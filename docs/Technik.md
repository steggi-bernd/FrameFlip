# FrameFlip – Technical notes

[← back to the start page](../README.md)

A preview for rendered image sequences. Lives in the tray, opens the sequence of the
file selected in Explorer as a video at a keystroke — and keeps adapting to the
machine's load, so a Blender render running alongside does not notice.

C# / .NET 8 / WPF. No external dependencies, no ffmpeg, no OpenCV.

## Building

```bash
dotnet publish FrameFlip/FrameFlip.csproj -c Release
```

Result: `bin/Release/net8.0-windows/win-x64/publish/FrameFlip.exe` — self-contained, a
single file, no installer, no installed .NET required. Copy it somewhere and run it
(a shortcut in the startup folder, say).

The file is ~160 MB because compression is deliberately **off**: with compression the
host unpacks the assemblies into private memory (~130 MB committed instead of
~60 MB). If file size matters more to you than memory, set
`EnableCompressionInSingleFile` to `true` in the `.csproj` — that gives ~72 MB.

## Controls

| Input | Effect |
|---|---|
| Hotkey (default **Ctrl+Alt+Space**) | open or close the preview |
| `Esc` / click outside | close |
| `Space` / click on the image | play / pause |
| `→` / `←` | one frame forward / back (pauses playback) |
| Mouse wheel **while paused** | zoom; the point under the cursor stays put |
| Mouse wheel **during playback** | scrub |
| **Ctrl + wheel** | whichever of the two is not currently bound |
| Drag on the image / middle mouse button | pan, once zoomed in |
| Double-click / right-click / `Ctrl+0` | switch between fit and 100 % |
| `Ctrl` + `+` / `-` | zoom without the mouse |
| Drag the header | move the window |
| Drag the window edge | resize |
| `Home` / `End` | first / last frame |
| `L` | toggle looping |
| `I` / `O` | set the in / out point (looping and export follow it) |
| `Del` | clear in/out again |
| `D` | show or hide the image details in the header |
| `E` | open the export dialog |
| `1` / `2` / `3` | decode size 100 % / 50 % / 25 % — lengthens the buffer ahead |
| `Tab` | show or hide image adjustment |
| `M` | show or hide render metrics |
| `A` | keep the current frame for comparison |
| `C` | switch between the kept and the current frame |
| `Ctrl+,` | settings |

The window opens borderless at media size (at most 90 % of the work area, at least
400 × 300), centred on the monitor Explorer is on, and fades in over 120 ms. The
header shows file name, system load, resolution, bit depth, file size and zoom level;
the control bar at the bottom fades out after 2 s of inactivity and stays put while
the mouse is over it, the scrubber is being dragged or the fps menu is open.

If the preview is already open and Explorer meanwhile points at a **different**
sequence, the hotkey swaps the content instead of opening a second window. If it
points at the same one, it closes as usual.

Clicking outside closes the preview (QuickLook behaviour). If you want to work in
Blender alongside and keep the sequence up, turn that off in the settings.

There is also a direct route, bypassing Explorer:

```bash
FrameFlip.exe --preview "D:\renders\shot_010\render_0001.png"
```

For automated tests the environment variable `FRAMEFLIP_CONFIG` can point at an
alternative configuration path; an instance started that way runs alongside a normal
one and leaves `%APPDATA%\FrameFlip\config.json` untouched.

> **On this machine Ctrl+Alt+Space is already taken** (`RegisterHotKey` reports error
> 1409) — as is Alt+Space; that looks like an installed launcher. FrameFlip reports
> this with a balloon tip at startup. Ctrl+Shift+Space, Ctrl+Alt+V and Ctrl+Alt+Q
> tested free, among others. Change it in the tray menu under *Settings*.

## What happens where

| File | Responsibility |
|---|---|
| `AppHost.cs` | tray icon, menu, toggle logic, window geometry |
| `Interop/HotKeyService.cs` | `RegisterHotKey` on an invisible window, `WM_HOTKEY` |
| `Interop/ExplorerSelectionProvider.cs` | `Shell.Application` → `ShellWindows` → `Document.SelectedItems` |
| `Diagnostics/SystemLoadMonitor.cs` | measure CPU/GPU/RAM, derive the resource profile |
| `Diagnostics/GpuLoadCounter.cs` | PDH counter `GPU Engine(*)` via `pdh.dll` |
| `Sequencing/SequenceScanner.cs` | file name → prefix/digit group/extension, scan the folder |
| `Decoding/` | `IFrameDecoder` plus the WIC implementation |
| `Caching/FrameCache.cs` | ring buffer, decoder threads, RAM budget |
| `Playback/PlaybackClock.cs` | time-based playback position |
| `Bridge/` | receives the Blender add-on's messages |
| `Localization/` | the German and English string dictionaries |
| `Views/ViewerWindow.xaml` | display, zoom, overlays, input |

### Buffer first, then play

Playback does not start with the first frame but once enough is in the ring: by
default one and a half seconds of material (`WarmupFrames` in the configuration
overrides this), or once the whole sequence is buffered. Until then a discreet
*Buffering …* sits in the top left. Emergency exit after 8 s, so a slow disk cannot
block indefinitely.

If the ring runs dry during playback, it stops and reloads rather than stuttering on.
Individual missing frames are dropped instead — only when **nothing at all** lies
ahead does it buffer.

Next to the frame rate actually being displayed, the header shows **how far the
buffer reaches ahead**. Once that number turns yellow or red, the next stall is
foreseeable. And the *Buffering …* notice names the reason — *ring empty*, *seek*,
*new resolution*, *new sequence* — so it stays distinguishable whether the supply ran
out or something else discarded the ring.

### The prefetch follows the frame rate

`PrefetchAhead` is a **frame count**, and that means something entirely different
depending on the frame rate: 60 frames are two and a half seconds of reserve at
24 fps, but only one at 60 fps. So the configured value acts as a lower bound, and
beyond it at least **two seconds** of material are kept.

### The ring knows the in/out range

With a range set, playback stays inside it — but the ring buffer computed over the
**whole** sequence for a long time. At the loop jump from the out point back to the
in point, prefetching therefore wrapped around the end of the *sequence* rather than
the end of the *range*:

* The frame at the in point was neither prefetched nor retained — precisely the one
  the next step goes to. Result: re-buffering on **every** pass.
* Instead the ring loaded the frames *beyond* the out point, which are never shown.
  For a sequence of 2077 frames at 8.3 MB each that is up to 120 decodes per pass,
  each additionally written to the raw cache — close to a gigabyte of write load for
  images nobody sees.

`SequenceMath.OffsetInRange` already existed for this; it simply was not wired up.
Load order, scoring (`Score`), falling back to an older frame and counting the supply
now all refer to the range. Frames outside it are dropped immediately: the space
belongs to the range.

The side effect is where the real gain lies: a short excerpt often fits **entirely**
into the ring, even when the sequence never would. Then there is no re-buffering at
all. The regression test records this — without the fix exactly one of a ten-frame
range sits in the ring, with the fix all ten.

### The ring uses its budget

Ring size used to follow **solely** from prefetch plus rewind — with a 2 GB budget
and 1080p it therefore stopped at 151 frames although 259 would have fitted.
Everything beyond that fell out and had to be decoded again on every loop pass, even
though the memory had long been committed.

Two rules now apply:

* **If the sequence fits the budget entirely, all of it is kept.** After the first
  pass nothing is ever reloaded — the loop runs without any buffering. Measured with
  150 frames over three passes: 0 reloads.
* **If it does not fit, the whole space is used anyway.** With 600 frames and 2 GB
  that is 258 instead of 151 in the ring and 227 instead of 120 frames of prefetch —
  at 60 fps, 3.8 instead of 2.0 seconds of reserve.

How many frames fit depends on the image size:

| Budget | 1080p (7.9 MB) | 1080p heavy (8.5 MB) | 4K (33 MB) |
|---|---|---|---|
| 1 GB | 129 | 120 | 31 |
| 2 GB | 259 | 240 | 62 |
| 4 GB | 518 | 481 | 124 |

### Second stage: raw cache on disk

If the sequence does not fit in memory, part of it falls out of the ring on every
loop pass and has to be obtained again. Rather than decoding the PNG a second time,
FrameFlip stores the decoded frames as **raw Bgra32 blocks** under
`%TEMP%\FrameFlip\rawcache`. Measured on 1080p:

| Route | Time per frame |
|---|---|
| decode the PNG | 31 ms |
| **read the raw block** | **6 ms** |
| write the raw block | 3 ms |
| from memory | 0.02 ms |

Measured on the loop, 300 frames with a ring holding 100: the second pass takes
**0.59 s instead of 1.74 s**. In exchange the first pass costs 15 % more, because it
writes alongside.

Deliberately **without compression** — that would give back exactly the compute time
this is meant to save. And deliberately **session-scoped**: the folder is keyed to
sequence *and* decode size and is deleted on close; leftovers from earlier sessions
are cleared away by the next start. Every entry carries the source file's
modification time and length in its header — if a frame is overwritten during a
running render, the old block is invalid immediately. Without that check the preview
stubbornly showed the earlier image.

Switched off through `RawCacheEnabled`; the limit is in `RawCacheMaxGb` (default 16).
On a mechanical disk it is not worth it — there, reading is barely faster than
decoding.

So for a sequence to sit entirely in memory the budget has to be at least
`frames × image size`. The decode step (`2` / `3`) drops the image size to a quarter
or a sixteenth and often does fit a long sequence in after all.

If the frame rate is switched at runtime, the ring adjusts its window without
discarding the frames it has already decoded.

### Decode size as a buffer step

`1` / `2` / `3` or the button in the control bar switch decoding to 100 %, 50 % or
25 %. This does **not** affect zoom — zooming in still works, the image is just
coarser.

The gain is not where one would expect it. Measured on 1080p material (5.9 MB per
PNG) a frame costs **35.7 ms at full and 31.1 ms at quarter size** — shrinking
therefore saves almost nothing during decoding, because WIC has to unpack the PNG
completely before it can scale. The lever is **memory**:

| Step | Memory per frame | Fit in 1 GB | Prefetch at 24 fps |
|---|---|---|---|
| 100 % | 7.91 MB | 129 | 5.4 s |
| 50 % | 1.98 MB | 517 | 21.5 s |
| 25 % | 0.49 MB | 2070 | 86 s |

Which is why it is called a buffer step and not a quality step. In a reduced step the
image is scaled up bilinearly rather than with `NearestNeighbor` — hard blocks would
hide exactly what you are trying to judge.

For comparison, what the decoder delivers: on this material **one** thread manages
about 30 frames/s, four threads 116, six threads 161. A single thread is therefore
barely above the 24 fps of playback — under system load that is not enough, and then
the supply alone carries it.

### Playback is time-based

`PlaybackClock` counts no ticks; it computes `anchor + seconds × fps`. The clock
comes from `CompositionTarget.Rendering` — no extra thread, and on pause the event is
unsubscribed (0 % CPU while paused).

With looping active the position is `raw mod frame count` — the jump from the last to
the first frame is therefore not a special case, neither in playback nor in the
buffer's prefetching. That is why it is seamless.

### Exception: locking to the display refresh

A deliberate departure from time-based playback, switchable under *Playback → lock to
the display refresh* (default: on).

When the target rate sits on the display refresh, the timeline has **no headroom at
all**: at 60 fps on 60 Hz every single composition step has to carry a new frame, and
every step that is skipped swallows one immediately. Measured on a 60 Hz display with
real drawing load:

| Target rate | Method | shown | held frames | skips |
|---|---|---|---:|---:|
| 60 fps | time-based | 40.3/s | 9.3/s | **15.0/s** |
| 60 fps | locked | 59.0/s | 0 | **0** |
| 30 fps | time-based | 30.0/s | 13.5/s | 0 |
| 30 fps | locked | 18.3/s | – | 0 |

The last row is the reason for the narrow condition: at 30 fps on 60 Hz every skipped
step costs half a frame, and locking collapses. There the clock is right — it shows
zero skips anyway, because there is twice the headroom in between.

Locking therefore happens only when **all** conditions hold:

* the target rate differs from the measured refresh by at most 5 %,
* the display delivers at least 80 % of its own refresh (otherwise: slow motion),
* the setting is on.

The refresh is not asked for but measured (`RefreshEstimator`): the display settings
say 60 Hz, many connections run at 59.94 — and that difference is what decides. The
median of the last 64 intervals gives the display's refresh, the number of steps per
second gives what it actually delivers; when the two diverge, composition is dropping
steps.

Locking is paid for with the difference between the target rate and the true display
refresh, so roughly one part in a thousand. If you need the timeline exact, turn it
off — then `PlaybackClock` alone applies.

### Adaptive resources

While a preview is open, FrameFlip measures every 10 seconds (configurable):

* **CPU** through `GetSystemTimes` — its own consumption is subtracted, otherwise
  FrameFlip sees the load it creates itself and throttles for no reason.
* **RAM** through `GlobalMemoryStatusEx` (free physical memory, and the total, which
  is what turns "free" into a utilisation figure).
* **GPU** through the PDH counters `GPU Engine(*)\Utilization Percentage`, the same
  source the Task Manager uses. Instances of the same engine type are summed; across
  engine types the maximum counts.

From this follows one of four levels, with a dead band against oscillation:

| Level | Utilisation | Decoder threads | Thread priority | Process |
|---|---|---|---|---|
| Idle | < 20 % | up to `MaxDecoderThreads` | Normal | Normal |
| Moderate | < 45 % | two thirds of that | BelowNormal | BelowNormal |
| Busy | < 80 % | 1 | Lowest | BelowNormal |
| Critical | ≥ 80 % | 1 | Lowest | BelowNormal |

Less than 2 GB free downgrades to *Busy*, less than 1 GB to *Critical* — regardless
of the CPU. The thread ceiling is additionally capped at the core count minus two (on
this machine: 10 of 12). With no preview open nothing is measured, and the process is
back on `BelowNormal`.

While a render is being reported, the sampling interval drops to 2 seconds. The
normal 10 seconds are enough for the governor but useless for a graph: six points a
minute make a staircase, not a curve. The measurement itself is a single system call.

**The thread count decides which frame rate is reachable at all** — this is the point
at which too cautious a setting looks like a bug in the program. A 1080p PNG of
8.5 MB takes about 46 ms to unpack, because `zlib` has to decompress the whole image:

| Threads | Reachable frame rate | Enough for |
|---|---|---|
| 1 | ~18 fps | not even 24 fps |
| 2 | ~37 fps | 24 fps, not 30 |
| 4 | ~73 fps | 60 fps, barely |
| 6 | ~110 fps | 60 fps with reserve |

Meanwhile CPU and GPU look **unloaded** in the Task Manager: two working threads out
of twelve cores are 17 % total load. If playback falls behind the frame rate while the
buffer is empty at the same time, FrameFlip now says so as a notice — including the
number of threads currently permitted.

Earlier defaults were too tight: a ceiling at half the core count, `MaxDecoderThreads`
of 4, and halved again at moderate load. Enough for 24 fps, not for 60.

**The buffer size explicitly does *not* follow CPU load, only free memory.** An
earlier version cut both together — to 70 % at *Busy*, to 40 % at *Critical*. That was
a thinking error: precisely when the decoder is down to one thread, a **large** supply
is the only reserve playback still has. Measured with 1080p material (7.91 MB per
frame): at a 512 MB budget, 64 frames hold 2.7 seconds — cut to 70 % that leaves
1.8 seconds, and the ring runs dry at the first hiccup. Cutting now happens only on
genuine memory pressure (below 2 GB to 70 %, below 1 GB to 40 %), because there,
paging would hit playback harder than a short buffer.

For perspective: **GPU load is measurable but barely controllable.** FrameFlip uses
the GPU only to composite its window; if Cycles saturates the card, throttling the
decoder does the GPU little good. The value serves as an indicator that "the machine
is working"; the effective levers are threads, priority and buffer.

Switched off under *Settings → Adaptive load*. Then it stays at exactly one decoder
thread at `Lowest`, as originally specified.

### Memory

Frames live in the ring as `Bgra32` pixel buffers from a pool of their own. In steady
state the same arrays keep rotating — no allocation per frame, no GC pauses, and the
RAM budget is exact rather than estimated
(`capacity = budget / (width × height × 4)`). If the configured window does not fit
the budget, the window shrinks; nothing is ever allocated beyond it. Display goes
through **one** reused `WriteableBitmap`.

The budget applies to the ring buffer, not to the process: about 60 MB of baseline
(WPF, WIC, runtime) comes on top.

### Window placement and DPI

The window is not positioned through a DIP conversion but through a small control
loop: set the size, measure the position (`GetWindowRect`), centre it against the
monitor (`MonitorFromWindow` + `GetMonitorInfo`), adjust, until the deviation is under
two pixels. The reason is uncomfortably concrete: on mixed-scaling systems, window
size, monitor query and WPF coordinates come from different spaces. Any fixed
conversion applies the scaling factor twice somewhere and pushes the window off the
screen; the control loop needs no assumption about the factor.

> **Known limitation:** on the test machine (two monitors, both at 175 %) the DPI
> declaration from the manifest does not take effect in the single-file build — the
> process runs DPI-unaware as far as Windows is concerned, `GetMonitorInfo` returns
> virtualised values, and Windows scales the finished window up afterwards. Position
> and size are therefore correct, but the rendering is not pixel-exact, just slightly
> soft. `SetProcessDpiAwarenessContext` can no longer be set at runtime
> (`ERROR_ACCESS_DENIED`), and `ApplicationHighDpiMode` changes nothing. On systems at
> 100 % scaling the effect does not occur.

### Zoom and resolution

Decoding targets the actual display size in **device** pixels, never above it and
never beyond the source resolution. Zooming in outgrows that: the zoom responds
immediately (scaled up), and 150 ms after the last wheel event the ring is rebuilt at
the matching resolution while paused. Because larger frames need more room, the
number of buffered frames shrinks automatically — the budget is kept. The old ring is
emptied before the new one is built, otherwise the budget would briefly sit in memory
twice.

Refining is jump-free because the scale is carried absolutely (image pixels per device
pixel) and the product of content width and matrix factor stays constant across the
buffer swap. The zoom itself lives exclusively in a `MatrixTransform`: no code path
changes a buffer size, a bitmap size or a layout size along the way.

The image area sits in a **`Canvas`**, not in a `Grid`. That is not cosmetic: a grid
arranges its child at the cell size and, as soon as the child is larger, applies a
layout clip. That clip acts in the child's coordinates, so **before** the
`RenderTransform` — the subsequently translated image was cut off on the right and at
the bottom by exactly the amount of the offset, and the offset grows with the zoom. A
canvas measures its children unbounded; clipping happens only at the outer viewport.
`FrameFlip.Tests` pins this down with a test that genuinely renders and counts pixels
— on matrix values alone the bug would not have shown, the matrix was right the whole
time.

### Locking discipline

Only dictionary operations run under the cache lock. Decoding and copying happen
outside it — otherwise the UI thread could wait on a `Lowest`-priority decoder, and
under render load that would be a priority inversion costing tens of milliseconds.
What makes it possible is a refcount per buffer: presentation holds it while copying,
eviction may take it out of the window in parallel, and it only returns to the pool
once both are done. With several decoder threads an `_inFlight` set prevents two of
them decoding the same frame.

### After closing

Decoder threads are signalled and joined **off** the UI thread, dictionary and pool
are emptied, then LOH compaction with `GCCollectionMode.Aggressive` and
`SetProcessWorkingSetSize(-1,-1)`. The buffers live on the Large Object Heap — without
compaction the memory would stay put even when nothing managed points at it any more.

## Formats

PNG, JPG/JPEG, TIFF, BMP through WIC. WebP works if Microsoft's *WebP Image
Extension* is installed — without it, that one format drops out cleanly.

EXR is **not** implemented; the architecture keeps the place free: another
`IFrameDecoder` implementation, registered in `FrameDecoderRegistry.CreateDefault()`.
Cache, playback and UI are unaffected.

## Sequence detection

`render_0042.png` → prefix `render_`, 4 digits, extension `.png`. What is recognised
is the **last** digit group in the name, so it also works with digits in the prefix
(`shot2_0001.png`) and with an empty prefix (`0001.png`, output path `//render/`).
View suffixes after the number (`f_0001_L.png`) separate left and right view into
sequences of their own.

The **padding** is derived from the inventory, not from the file that was clicked.
Blender pads to N digits and lets the number grow past it — after `f_99` comes
`f_100`, not `f_00100`. A leading zero proves the padding; otherwise the shortest
number present counts. That way FrameFlip finds the same sequence whether `f_99` or
`f_100` was selected.

The timeline spans the **number range**, not the list position — only that way are
gaps representable; over positions, 250 rendered of 500 frames would look like a
complete sequence. Missing frames appear as a red marker, with their count and
location above (`2 gaps: 7–9, 13`). A click on *Copy Blender command* puts a complete
command for re-rendering on the clipboard:

```
blender -b "PATH/TO/PROJECT.blend" -o "D:/renders/shot_010/render_####" -F PNG -x 1 -f 7..9,13
```

The frame counter shows the **real** frame number — `0042 / 0250` is current number /
highest number. During playback gaps are skipped without shifting the time base,
because playback runs over list positions. A seek into a gap lands on the nearest
existing frame.

If nothing (or something unreadable) was selected in Explorer, FrameFlip takes the
first displayable image in the active folder.

## Image adjustment

`Tab` opens a panel on the right. The window grows to the right as long as the screen
allows; otherwise the image area gives up the space.

Everything in it affects **the display only** — the files on disk stay untouched. So
that this is not forgotten while judging, an active correction appears in short form
in the header (`EV -1.2  γ 1.3  C 1.15  S 1.2`).

**Sliders:** exposure (in stops), gamma, contrast, saturation, black and white point.
Double-clicking a slider resets it. The order of operations is the one usual in colour
correction: exposure, then black/white point, then gamma, then contrast, saturation
last.

**Distribution:** a histogram over RGB or luminance, measured on the *corrected*
image — the diagram shows what you actually see. If more than 0.5 % of the pixels sit
at the top or bottom, a bar appears at the edge and a line below. The curve is
square-root scaled, because a single tall spike would otherwise sink the rest of the
distribution into the floor.

**A/B comparison:** `A` keeps the current frame, `C` switches. The kept frame is
explicitly **copied** — a reference into the ring buffer later showed some arbitrary
image, because the buffer is passed straight on to the next frame.

**Presets:** correction settings can be named and saved; they appear in the drop-down
next time.

### How fast that is

The correction runs on the CPU while copying into the display bitmap, without an
intermediate buffer. Measured on 1080p:

| Case | Time per image |
|---|---|
| no correction | 0.3 ms |
| tone only (exposure, gamma, contrast, levels) | 2.4 ms |
| plus saturation and channel view | 9.6 ms |
| histogram separately, every 4th pixel | 3.8 ms |

At 24 fps there are 41.7 ms between two images — so there is room. Two things were
needed for that: **integer arithmetic** instead of `double` per pixel (the first
version took 72 ms and would have cost frames) and **spreading the rows across the
cores**. With no correction set it is a plain memory copy, so that an unused feature
does not cost playback a single beat.

## Video export

`E` or the *Export …* button in the tool bar. Formats: H.264/MP4, H.265/MP4,
ProRes 422 HQ, WebM/VP9 and GIF (two-pass with its own palette). The range is either
the whole sequence or in to out, the frame rate is pre-filled from the player, the
resolution original or reduced.

The export runs through the **concat demuxer** with an explicit frame list — not
through `-i "render_%04d.png"`. The obvious route has two failure points, both of
which occur regularly with render output: it stops at the first missing number, and it
does not understand padding overflow (after `f_99` it looks for `f_00100`). Where
there are gaps you can choose whether they are skipped or the previous frame is held
as a brief still — usually the better behaviour for judging motion.

The player stays usable meanwhile, the export can be cancelled, and an incomplete file
is deleted when it is. Process priority and `-threads` follow the load profile, so the
encoder does not crowd out a running render.

Two details of the list were measured against the widespread advice, with
**ffmpeg 9.0.1**:

- **No `-framerate`.** That is an option of the image-file demuxer (`image2`) and does
  not exist on the concat demuxer — ffmpeg aborts with *“Option framerate not found”*
  before it has even read a file. The input frame rate lives in the `duration` lines
  of the list instead; `-r` on the output forces a constant output frame rate.
- **The last file name is *not* repeated.** The usual recommendation to repeat it
  dates from a time when concat discarded the `duration` of the final entry. Today it
  is honoured: 16 frames produced 17 images with the repetition (0.708 s instead of
  0.667 s at 24 fps), and exactly 16 without. The repetition now causes precisely the
  error it once prevented.

If a correction is set in the panel, **the dialog asks** whether it should be baked
into the video. The answer is remembered but stays changeable per export. Exposure,
gamma, contrast, saturation and black and white point are carried over (as `eq` and
`curves` filters). **Channel views are not** — a video of the red channel alone is
practically never wanted; that is a judging tool.

The target name is freely editable, but not every codec fits every container. ProRes
in MP4, for instance, makes ffmpeg fail with *“Could not find tag for codec prores”*.
If the extension does not match the format, the dialog corrects it on start and says
so in the status line.

### ffmpeg is not bundled

Common ffmpeg builds contain **libx264 and are therefore GPL**. Were ffmpeg part of
the delivery, FrameFlip would have to be GPL as well. Looked up at runtime, the
licence question stays with the user and FrameFlip can be licensed permissively. An
automatic download does not happen for the same reason.

The search order is: configured path, an `ffmpeg` subfolder next to the exe, `PATH`,
then the locations used by winget, Chocolatey and Scoop. That last step is not a
luxury — after a fresh installation an already running process does not know the
extended `PATH` yet; it inherited it at startup.

If nothing is found, the dialog explains that and offers a file picker. Install for
example with:

```bash
winget install Gyan.FFmpeg
```

The chosen path is verified with `ffmpeg -version`: a file of the right name is not
yet proof that a working ffmpeg is behind it.

> **Note:** Blender does ship `avcodec`, `avformat` and `avutil` as DLLs, but no
> callable `ffmpeg.exe`. An existing Blender installation is therefore no substitute.

## Configuration

`%APPDATA%\FrameFlip\config.json`:

```json
{
  "Hotkey": "Ctrl+Alt+Space",
  "Language": "de",
  "Fps": 24,
  "Loop": true,
  "LockToDisplay": true,
  "ShowMetadata": true,
  "CloseOnFocusLoss": true,
  "MemoryBudgetMb": 1024,
  "PrefetchAhead": 60,
  "PrefetchBehind": 15,
  "AdaptiveResources": true,
  "LoadIntervalSeconds": 10,
  "MaxDecoderThreads": 8,
  "WarmupFrames": 0,
  "DraftStep": 0,
  "RawCacheEnabled": true,
  "RawCacheMaxGb": 16,
  "PanelOpen": false,
  "BridgeEnabled": true,
  "BridgePort": 47823,
  "Adjustments": null,
  "AdjustmentPresets": [],
  "ExportApplyAdjustments": null,
  "FfmpegPath": "",
  "ExportPreset": "H.264 / MP4",
  "ExportHoldLastFrame": true
}
```

`WarmupFrames: 0` means "derive it from the frame rate", `FfmpegPath: ""` means "look
it up again on every export", `Language` is `de` or `en`. Fps, looping, the image
details toggle and the export selection are saved the moment they are switched.
Changes to budget, buffer sizes and load detection take effect the next time the
preview is opened.

New keys get their default value when read; an older file is extended, not
overwritten. The settings dialog starts from the existing state and overwrites only
its own fields — otherwise it would reset every setting it does not itself display.

## Bridge to the Blender add-on

While a preview is open, FrameFlip can report a running render: overall progress,
sample counter, time per frame, a graph of how expensive each frame was, and a
thumbnail of the last frame written. The add-on that feeds it lives in its own
repository under GPL —
[FrameFlipBridge](https://github.com/steggi-bernd/FrameFlipBridge); the design and
the API research are in [Blender-Bridge.md](Blender-Bridge.md).

The receiving end is a TCP listener bound **exclusively to 127.0.0.1**, speaking one
JSON object per line. Not HTTP: the add-on is to manage without third-party packages,
and Python's standard library can do sockets and JSON. The first line has to carry a
token from a file in the user profile — so no other program on the machine can fake a
render. Switched off through `BridgeEnabled`.
