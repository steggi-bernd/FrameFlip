const SEATS       = 6;                        // muss zu WatchKey.Seats passen
const FREE_SEATS  = 2;                        // ... und zu WatchKey.FreeSeats
const ROOM_INFO   = "frameflip/v1/watchroom";
const SEAT_INFO   = "frameflip/v1/watchseat";
const HOST_INFO   = "frameflip/v1/host";
const CLIENT_INFO = "frameflip/v1/client";
const CONFIRM_INFO = "frameflip/v2/confirm";

const VERSION = 2;
const SALT_BYTES = 16, TAG_BYTES = 16, COUNTER_BYTES = 8, NONCE_BYTES = 12;
const OVERHEAD = COUNTER_BYTES + TAG_BYTES;

const KIND_JSON = 0x01, KIND_PREVIEW = 0x02;

const enc = new TextEncoder();

/* ------------------------------------------------------------------ Kleinkram */

function fromBase64Url(text) {
  let s = String(text).replace(/-/g, "+").replace(/_/g, "/");
  while (s.length % 4) s += "=";

  const raw = atob(s);
  const out = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
  return out;
}

const toHex = bytes => Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");

async function hkdf(secret, info, salt, bytes) {
  const key = await crypto.subtle.importKey("raw", secret, "HKDF", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "HKDF", hash: "SHA-256", salt: salt, info: enc.encode(info) }, key, bytes * 8);

  return new Uint8Array(bits);
}

/* Der Nachweis, dass die Gegenseite denselben Schluessel hat. Bindet beide Salze
   und die Rolle, damit ein Platzhalter im Raum nicht als echtes Geraet durchgeht. */
async function confirmation(seatKey, senderIsHost, hostSalt, clientSalt) {
  const info = enc.encode(CONFIRM_INFO);
  const transcript = new Uint8Array(info.length + 1 + SALT_BYTES * 2);

  transcript.set(info, 0);
  transcript[info.length] = senderIsHost ? 1 : 2;
  transcript.set(hostSalt, info.length + 1);
  transcript.set(clientSalt, info.length + 1 + SALT_BYTES);

  const key = await crypto.subtle.importKey(
    "raw", seatKey, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);

  return new Uint8Array(await crypto.subtle.sign("HMAC", key, transcript));
}

function sameBytes(a, b) {
  if (a.length !== b.length) return false;

  // Ohne fruehen Ausstieg. Hier haengt zwar nichts Geheimes an der Laufzeit, aber
  // ein Vergleich von Pruefsummen, der bei der ersten Abweichung aufhoert, ist eine
  // Angewohnheit, die man sich an der falschen Stelle teuer bezahlt.
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}

/* ------------------------------------------------------- Der verschluesselte Kanal */

class Channel {
  constructor(sendKey, receiveKey) {
    this.sendKey = sendKey;
    this.receiveKey = receiveKey;
    this.next = 0n;
    this.lastSeen = 0n;
    this.sawAny = false;
  }

  static async establish(seatKey, hostSalt, clientSalt) {
    const salt = new Uint8Array(SALT_BYTES * 2);
    salt.set(hostSalt, 0);
    salt.set(clientSalt, SALT_BYTES);

    // Wir sind der Client: Wir senden mit dem Client-Schluessel und lesen den des Hosts.
    const mine  = await hkdf(seatKey, CLIENT_INFO, salt, 32);
    const yours = await hkdf(seatKey, HOST_INFO, salt, 32);

    const usable = raw => crypto.subtle.importKey(
      "raw", raw, { name: "AES-GCM" }, false, ["encrypt", "decrypt"]);

    return new Channel(await usable(mine), await usable(yours));
  }

  async seal(payload) {
    const frame = new Uint8Array(OVERHEAD + payload.length);
    new DataView(frame.buffer).setBigUint64(0, this.next++, false);

    const nonce = new Uint8Array(NONCE_BYTES);
    nonce.set(frame.subarray(0, COUNTER_BYTES), NONCE_BYTES - COUNTER_BYTES);

    const sealed = new Uint8Array(await crypto.subtle.encrypt(
      { name: "AES-GCM", iv: nonce, additionalData: frame.subarray(0, COUNTER_BYTES), tagLength: 128 },
      this.sendKey, payload));

    // WebCrypto haengt die Pruefsumme hinten an, auf der Leitung steht sie vorn.
    frame.set(sealed.subarray(sealed.length - TAG_BYTES), COUNTER_BYTES);
    frame.set(sealed.subarray(0, sealed.length - TAG_BYTES), OVERHEAD);

    return frame;
  }

  async open(frame) {
    if (frame.length < OVERHEAD) return null;

    const counter = new DataView(frame.buffer, frame.byteOffset, frame.byteLength).getBigUint64(0, false);
    if (this.sawAny && counter <= this.lastSeen) return null;

    const nonce = new Uint8Array(NONCE_BYTES);
    nonce.set(frame.subarray(0, COUNTER_BYTES), NONCE_BYTES - COUNTER_BYTES);

    const body = new Uint8Array(frame.length - OVERHEAD + TAG_BYTES);
    body.set(frame.subarray(OVERHEAD), 0);
    body.set(frame.subarray(COUNTER_BYTES, OVERHEAD), frame.length - OVERHEAD);

    let plain;
    try {
      plain = await crypto.subtle.decrypt(
        { name: "AES-GCM", iv: nonce, additionalData: frame.subarray(0, COUNTER_BYTES), tagLength: 128 },
        this.receiveKey, body);
    } catch (e) {
      return null;   // falscher Schluessel oder verfaelscht - beides derselbe Fall
    }

    // Erst jetzt weiterzaehlen. Andernfalls koennte ein Fremder mit einer erfundenen
    // hohen Zahl die echte Gegenseite fuer den Rest der Sitzung aussperren.
    this.lastSeen = counter;
    this.sawAny = true;

    return new Uint8Array(plain);
  }
}

