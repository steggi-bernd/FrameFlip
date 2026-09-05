# FrameFlip

**Watch rendered image sequences without turning them into a video first.**

Select a file in Explorer, press the hotkey — the whole sequence plays back
smoothly. Click elsewhere and the window is gone. No import, no export, no project.

FrameFlip sits in the tray and is built to be used **while** a render is running:
it measures the machine's load and gets out of the way when Blender is working.

C# · .NET 8 · WPF · Windows 10/11 · no third-party packages

![The preview window with the side panel open: on the left the running image of a
rendered sequence, on the right a histogram and sliders for exposure, gamma,
contrast, saturation, black and white point, and the timeline along the
bottom](docs/bildschirmfoto.png)

<sub>A sequence at 4096 × 2304, with the collapsible panel for quick image
adjustment on the right. The correction affects the display only.</sub>

---

## Why

Judging a rendered sequence is awkward. Explorer shows single frames — you cannot
tell from those whether the motion works. A video export takes time and is stale
after two looks. Blender's Video Sequence Editor can do it, but costs a context
switch in the middle of your work.

FrameFlip turns that into one keystroke.

## What it does

**Finds the sequence from one file.** `render_0042.png` is enough — prefix, digit
width and suffix are derived from the folder, not guessed from that one file.
Missing numbers are detected and marked red on the timeline: an incomplete render
is visible at a glance.

**Buffers in two stages.** Decoded frames live in a ring buffer in memory, backed
by raw pixel blocks on disk. Reading such a block costs about 6 ms, decoding the
same PNG again about 31 ms. For a sequence that does not fit in memory, that is
the difference between smooth looping and re-buffering on every pass.

**Holds back.** Thread count and priority follow the measured system load. The
buffer, in contrast, is only cut when memory genuinely runs short — under CPU load
it is the one reserve playback still has.

**Stays smooth when there is no headroom.** Playback is time-based. When the
target rate sits exactly on the display refresh, there is no headroom at all: at
60 fps on 60 Hz every single composition step has to carry a frame. Measured, 40
of 60 frames arrived. Locked to the display it is 59, without a single skip. Can
be turned off.

**Zoom and pan**, decoupled from the decode size: the image does not jump when a
sharper version arrives.

**A quick look at whether the exposure holds.** A collapsible side panel with
exposure, black point, contrast, saturation and gamma, a histogram that warns
about blown highlights, an A/B comparison against a kept frame, and presets you
can save. The correction affects the display only; the files stay untouched. The
export asks whether it should apply it.

**Export**, when a video is needed after all — through ffmpeg, with in/out points,
scaling and the usual target formats.

## Building

```bash
dotnet publish FrameFlip/FrameFlip.csproj -c Release
```

Result: `bin/Release/net8.0-windows/win-x64/publish/FrameFlip.exe` — a single file,
self-contained, no installer and no installed .NET required. Copy it somewhere,
run it, done; for permanent use put a shortcut in the startup folder.

The file is around 160 MB because compression is deliberately **off**: with
compression the host unpacks the assemblies into private memory and needs more of
it at runtime (~130 MB instead of ~60 MB). If file size matters more to you, set
`EnableCompressionInSingleFile` to `true` in the `.csproj` — that brings it to
~72 MB.

## Getting started

1. Run `FrameFlip.exe` — the tray icon appears, nothing else happens.
2. Select one image of the sequence in Explorer.
3. **Ctrl + Alt + Space**.

The window opens borderless at media size on the monitor Explorer is on. `Esc` or
a click elsewhere closes it again.

## Controls

| Input | Effect |
|---|---|
| Hotkey (default **Ctrl+Alt+Space**) | open or close the preview |
| `Space` / click on the image | play / pause |
| `→` / `←` | one frame forward / back |
| Mouse wheel | zoom (paused) or scrub (during playback), `Ctrl` swaps the two |
| Double-click / `Ctrl+0` | switch between fit and 100 % |
| `L` | toggle looping |
| `I` / `O` / `Del` | set or clear the in and out point |
| `1` / `2` / `3` | decode size 100 % / 50 % / 25 % — lengthens the buffer ahead |
| `Tab` | show or hide image adjustment |
| `M` | show or hide render metrics |
| `A` / `C` | keep a frame / switch against the kept one |
| `E` | export dialog |
| `Ctrl+,` | settings |

The full table is in [docs/Technik.md](docs/Technik.md#controls).

## Languages

The interface comes in German and English, switchable in the settings and applied
immediately — the texts live in two resource dictionaries that are swapped at
runtime.

Everything written for readers — this page, the technical notes, the add-on — is
English. The comments in the source stay German: that is where the reasoning behind
each individual decision lives, at a density where translation would cost more than
it gains.

## Formats

PNG, JPEG, TIFF and BMP through the Windows Imaging Component. WebP as well, as
long as Microsoft's *WebP Image Extension* is installed — without it, that one
format simply drops out cleanly.

**Not EXR.** The place for it is kept free: another `IFrameDecoder` implementation,
registered in `FrameDecoderRegistry.CreateDefault()`. Cache, playback and interface
stay as they are.

## ffmpeg

For the video export, and **not bundled**: common ffmpeg builds are GPL, and that
would extend to FrameFlip. FrameFlip looks for ffmpeg on the PATH and in the usual
places; the path can also be set by hand.

```bash
winget install Gyan.FFmpeg
```

Without ffmpeg everything works except the export.

## How it works

The reasoning sits next to each decision rather than in a summary —
[docs/Technik.md](docs/Technik.md) explains every one of them along with the
measurement behind it:

* [Buffer first, then play](docs/Technik.md#buffer-first-then-play)
* [Second stage: raw cache on disk](docs/Technik.md#second-stage-raw-cache-on-disk)
* [Locking to the display refresh](docs/Technik.md#exception-locking-to-the-display-refresh)
* [Adaptive resources](docs/Technik.md#adaptive-resources)
* [Zoom and resolution](docs/Technik.md#zoom-and-resolution)
* [Locking discipline](docs/Technik.md#locking-discipline)

## Tests

```bash
dotnet run --project FrameFlip.Tests
```

474 assertions, no third-party packages — a plain console project instead of a test
framework with three NuGet dependencies. A non-zero exit code means failure.
Everything runs without a visible window and in under a minute; the tests create
their own image material.

What gets checked is what can be checked this way and has been wrong before: zoom
mathematics, buffer limits, the in/out range, sequence detection including gaps,
ffmpeg arguments, window placement across multiple monitors, the load profile,
image correction, the raw cache, locking to the display refresh, and that both
language dictionaries carry the same keys.

The sequences under `FrameFlip-Testsequenzen/` are meant for trying things out by
hand, not for the automated tests — one of them has deliberate gaps.

## Where this is going

Working already: FrameFlip receives a running render from the Blender add-on and
shows progress, sample counter, time per frame, a graph of how expensive each frame
was, and a thumbnail of the last one written. The add-on lives in its own repository under GPL —
[FrameFlipBridge](https://github.com/steggi-bernd/FrameFlipBridge); the design and
the API research are in [docs/Blender-Bridge.md](docs/Blender-Bridge.md).

Still missing:

* Live mode: play the frames of a running render as they are written
* Cancelling and a render queue — both require FrameFlip to start renders as
  processes of its own, because Blender offers no way to cancel one from the inside
* Remote control from a phone, end-to-end encrypted

## Licence

MIT — see [LICENSE](LICENSE).

ffmpeg is neither bundled nor linked, but invoked as a separate program. Its
licence remains its own business.
