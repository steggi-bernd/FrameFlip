# Security policy

## Supported versions

Only the current `main` branch is supported with security fixes.

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability. Use GitHub's
Private Vulnerability Reporting feature for this repository and include a clear
description, reproduction steps, affected version or commit, and any proof of
impact. We will acknowledge the report and coordinate a fix before disclosure.

## What to expect

| | |
|---|---|
| Acknowledgement | within 72 hours |
| First assessment | within 7 days |
| Fix | as fast as I can — serious findings first |
| Credit | gladly, if you want it |

This is a spare-time project. There is no bounty, but there is an honest answer and a
fix.

## Scope

Reports involving remote pairing, relay transport, local file access, uploads,
or Blender process execution are especially welcome.

**You do not need my server to test.** The relay is MIT licensed and starts in a
minute — see [EIGENER-LEUCHTTURM.md](../EIGENER-LEUCHTTURM.md). Please do not disrupt
other people's rooms, touch data that is not yours, or run availability attacks against
`relay.steggi-matrix.work`.

## Where to look first

The relay cannot read what passes through it — the content is end-to-end encrypted and
the key is created on the PC and never crosses the network. A finding on the server is
therefore rarely a finding on the data. The more interesting surface is the
cryptography on both ends: `FrameFlip/Remote/` and `web/teile/krypto.js`, with the wire
format written down in the relay's `PROTOCOL.md`.
