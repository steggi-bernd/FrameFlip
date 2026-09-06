# Watching a render from the phone

A render runs for hours. Sitting next to the machine for them is not the point, and
neither is walking back every ten minutes to see whether frame 900 is done.

So FrameFlip can hand the progress to a phone. The part that matters is what it does
*not* do on the way.

## What the middle of the network sees

Nothing.

The phone cannot reach a PC behind a home router, so something in between has to pass
the bytes along. That something is the [relay](https://github.com/steggi-bernd/frameflip-relay) —
a service whose whole job is "these two connections belong together, forward between
them". It does not decrypt, does not store, does not interpret.

That is not a promise about the operator's good intentions; it is a property of the
construction. The pairing secret is 256 random bits that FrameFlip shows as a QR code
and the phone reads **off the screen**. It never crosses the network. The relay only
ever learns the *room id*, a one-way derivation of that secret: enough to find the
room, useless for reading anything in it.

So running your own relay is possible and documented — but it buys you very little,
because the one you use already cannot see your work.

The exact derivations, the handshake and the frame layout are in the relay's
[PROTOCOL.md](https://github.com/steggi-bernd/frameflip-relay/blob/main/PROTOCOL.md).
Anyone writing a second client should work from that page, not from this one.

## Setting it up

Settings → **Remote**, then scan the QR code with the app. That is the whole thing.

The relay address is filled in with the project's public one. Enter your own to use it
instead; an empty field means "the default", not "no relay". Never `https://` — the
connection is always built as `wss://`, so a tampered QR code cannot downgrade it.

**Showing the code commits it.** The key is written to disk the moment it appears on
screen, and remote is switched on with it. This is not convenience; it is the fix for a
real failure: previously the key was only stored on Save, so scanning the code and then
closing the window left the phone holding a key the PC had forgotten. The phone said
"paired, PC not here" — correct, and impossible to diagnose from either side.

Someone who lets a pairing code be displayed intends to pair. The tick box above it
moves with it and turns the whole thing off again in one click.

Below the code, a line says whether anyone has actually read it: *waiting for the
phone* / *phone connected*. Without it, pairing is blind from this end.

**New key** disconnects every device paired so far. There is no separate "unpair" —
replacing the secret is what unpairing *is*.

The key is stored in `config.json`, encrypted against your Windows account with DPAPI.
A copy of that file taken to another machine is worthless. It does not protect against
someone already logged in as you, and is not meant to.

## What travels

Every encrypted message starts with one byte saying what it is:

| Byte | Contents |
|---|---|
| `0x01` | UTF-8 JSON. Both directions — state from the PC, commands from the phone. |
| `0x02` | A preview image: frame number (4 bytes, big endian), then JPEG. |

The type is **stated, not guessed**. This is the same mistake the relay made once and a
test caught: deciding a frame's type from its first byte sends roughly one payload in
256 down the wrong path. Here it would be the mirror image — a JPEG begins with
`0xFF 0xD8`, but relying on that is still reading the envelope out of the letter.

The byte sits *inside* the encryption, so the relay cannot tell numbers from pictures
either. Size gives it a rough hint anyway; that is the difference between "can be
guessed at" and "is written down".

Unknown types are dropped, not treated as errors — a later version may add some.

### The state object

One JSON object, at most once a second while rendering and once every five seconds
when idle. Names are short because every byte crosses a mobile network:

```json
{
  "t": "job",
  "state": "rendering",
  "scene": "Shot_04",
  "engine": "CYCLES",
  "file": "kitchen.blend",
  "frame": 412,
  "first": 1,
  "last": 2077,
  "written": 411,
  "progress": 0.1978,
  "elapsed": 2841.6,
  "remaining": 11530.2,
  "spf": 6.91,
  "sample": 2304,
  "samples": 4096,
  "memMb": 6543,
  "activity": "Path Tracing Sample 2304/4096",

  "cpu": 41.5,
  "ramUsedMb": 45536,
  "ramTotalMb": 65536,
  "gpu": 97,
  "vramUsedMb": 11000,
  "vramTotalMb": 12282,
  "gpuTemp": 71
}
```

With no render running it is simply `{"t":"idle"}`.

Every field after `engine` may be missing. Blender decides what goes into its status
text, and it is not an interface — Cycles builds it one way, EEVEE another, and it
changes between versions. A reader that requires a field will eventually be wrong; a
reader that treats each as optional will not.

The last block is the machine rather than the render, and it is sent **in the idle
message too** — when nothing is rendering, the question is whether the PC is even
awake, and a screen that then shows nothing does not answer it.

Those fields have their own reasons to be absent. `cpu` and the RAM pair come from
FrameFlip's own load monitor and disappear when adaptive resources are switched off.
`gpu` comes from a Windows performance counter that does not exist before Windows 10,
and falls back to `nvidia-smi`. `vramUsedMb`, `vramTotalMb` and `gpuTemp` come only
from `nvidia-smi` — on an AMD or Intel card there is no vendor-neutral source for
them under Windows, so they are simply not there.

**The totals are sent, not assumed.** `ramTotalMb` and `vramTotalMb` are what that
machine actually has. A client that hardcodes "/ 24 GB" will be wrong on a 12 GB
card, and the bar next to it will be wrong by a factor of two.

Absent means absent — never `0`. A zero in `gpu` looks like a sleeping machine, and a
reader cannot tell it from a real idle GPU. Show a dash instead.

## Preview images

The phone asks; the PC answers. The request is `{"c":"preview"}` as a `0x01` payload,
and the reply is a `0x02` payload holding the last written frame.

It is **never** sent unasked. The render folder holds full-resolution PNGs — at
4096×2304 that is easily 70 MB each — so the frame is downscaled to 1280 pixels wide
and encoded as JPEG at quality 80, which lands around 100–300 KB. Even that is too
much to push automatically: at seven seconds a frame it would be tens of megabytes an
hour, unasked, often over mobile data. So it costs a tap.

Downscaling happens *while decoding*, not after. The decoder never reads the image at
full size, which saves both time and memory — and both matter, because a render is
using the same machine. The file is opened shared for reading, writing and deletion:
Blender may be writing into that folder right now, and an exclusive handle would be a
reason for a render to fail. No preview is worth that.

The reply carries the frame number of the *picture*, not of the frame running now.
While the image was in flight the render moved on, and labelling it with the current
number would simply be wrong.

## Why once a second

Blender reports several times per second. Each report would be an encrypted packet
over a mobile connection, and one second is already finer than anyone stares at a
phone.

State changes do not wait for the tick — a finished frame or a completed render goes
out immediately. Those are what someone is actually waiting for.

## What a network fault costs

Nothing, deliberately. FrameFlip is running during a render, and the render is the
thing that matters.

Sending never blocks: messages go into a fixed buffer and the oldest falls out when it
is full. Progress from twenty seconds ago is of no interest to anybody. A dropped
connection is a state, not an error — it retries at a growing interval until someone
stops it. Routers restart, WLANs change, laptops close; none of that is a reason to
give up on the feature for the rest of the session.
