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

Settings → **Remote**:

1. Enter the relay's host name (no `https://` — the connection is always `wss://`,
   and a tampered QR code cannot downgrade it).
2. Scan the QR code with the app.
3. Tick **Send render progress to the phone**.

Until all three are true, nothing happens: no connection is attempted, no key is
written to disk, nothing runs in the background. A feature you do not use should cost
you nothing.

**New key** disconnects every device paired so far. There is no separate "unpair" —
replacing the secret is what unpairing *is*.

The key is stored in `config.json`, encrypted against your Windows account with DPAPI.
A copy of that file taken to another machine is worthless. It does not protect against
someone already logged in as you, and is not meant to.

## What travels

One JSON object, at most once a second, whenever the render state changes. Names are
short because every byte crosses a mobile network:

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
  "activity": "Path Tracing Sample 2304/4096"
}
```

With no render running it is simply `{"t":"idle"}`.

Every field after `engine` may be missing. Blender decides what goes into its status
text, and it is not an interface — Cycles builds it one way, EEVEE another, and it
changes between versions. A reader that requires a field will eventually be wrong; a
reader that treats each as optional will not.

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
