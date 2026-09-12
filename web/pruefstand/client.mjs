// Prüfstand für die Zuschauerseite.
//
// Es wird NICHTS nachgebaut. Krypto und Verbindungsablauf werden aus watch.html
// herausgeschnitten und ausgeführt - ein Nachbau prüfte am Ende sich selbst und
// könnte denselben Fehler enthalten wie die Seite, ohne dass es auffiele.
//
// Ersetzt werden nur die drei Stellen, die einen Bildschirm brauchen: say, show und
// showPicture. Sie merken sich hier, was ankam.

import fs from "node:fs";

const [, , PAGE, RELAY, SECRET, CODE] = process.argv;

const html = fs.readFileSync(PAGE, "utf8");

function slice(from, to, what) {
  const a = html.indexOf(from), b = html.indexOf(to);
  if (a < 0 || b <= a) { console.error(`${what} ließ sich nicht herausschneiden.`); process.exit(2); }
  return html.slice(a, b);
}

const core = slice("const SEATS",
                   "/* --------------------------------------------------------------- Das Geheimnis */",
                   "Der Krypto-Teil");

const flow = slice("const Result = {",
                   "/* --------------------------------------------------------------- Platz suchen */",
                   "Der Verbindungsablauf");

// Was zuletzt ankam - die Seite würde es anzeigen, hier wird es geprüft.
const seen = { states: 0, previews: 0, lastState: null, jpegBytes: 0, said: [] };
const sockets = [];

class Watched extends WebSocket {
  constructor(url) { super(url); sockets.push(this); }
}

const page = new Function("location", "say", "show", "showPicture", "WebSocket",
  core + "\n" + flow + "\n" +
  "return { trySeat, Result, hkdf, toHex, fromBase64Url, ROOM_INFO, SEAT_INFO, SEATS, FREE_SEATS };")(
    { host: RELAY, protocol: "http:" },
    (text, mood) => seen.said.push(text),
    state => { seen.states++; seen.lastState = state; },
    jpeg => { seen.previews++; seen.jpegBytes = jpeg.length; },
    Watched);

const enc = new TextEncoder();
const secret = page.fromBase64Url(SECRET);

let failures = 0;

function check(ok, what) {
  console.log((ok ? "  [ok]   " : "  [NEIN] ") + what);
  if (!ok) failures++;
}

const roomFor = async seat =>
  page.toHex(await page.hkdf(secret, page.ROOM_INFO, new Uint8Array([seat]), 16));

const seatKey = async code =>
  await page.hkdf(secret, page.SEAT_INFO, code ? enc.encode(code) : new Uint8Array(0), 32);

function closeAll() {
  for (const socket of sockets) { try { socket.close(); } catch { } }
  sockets.length = 0;
}

// trySeat löst auf, sobald feststeht, woran man ist - und lässt die Verbindung dann
// offen. Genau so soll es sein; für die Prüfung wird danach noch etwas gelauscht.
async function visit(seat, key, listen = 0) {
  const before = { states: seen.states, previews: seen.previews };
  const outcome = await page.trySeat(await roomFor(seat), key);

  if (listen > 0 && outcome === page.Result.Paired) {
    await new Promise(done => setTimeout(done, listen));
  }

  return {
    outcome,
    states: seen.states - before.states,
    previews: seen.previews - before.previews
  };
}

const wait = ms => new Promise(done => setTimeout(done, ms));

// -------------------------------------------------------------------- Ablauf

console.log("\nEin Zuschauer auf dem ersten freien Platz");
console.log("----------------------------------------");

const free = await seatKey(null);
const first = await visit(0, free, 2500);

check(first.outcome === page.Result.Paired, `der Handschlag geht auf (${first.outcome})`);
check(first.states > 0, `Zahlen kommen an (${first.states})`);
check(first.previews > 0, `ein Bild kommt an (${first.previews})`);
check(seen.jpegBytes > 100, `und es ist ein JPEG (${seen.jpegBytes} Bytes)`);
check(seen.lastState?.scene === "pruefstand", "der Inhalt ist lesbar");
check(seen.lastState?.width === 1920, "und vollständig");
check(seen.lastState?.percent > 0, "die Zahlen sind Zahlen, kein Text");

console.log("\nEin zweiter auf denselben Platz");
console.log("------------------------------");

const second = await visit(0, free);

check(second.outcome === page.Result.Taken, `wird abgewiesen, nicht durchgelassen (${second.outcome})`);

closeAll();
await wait(800);

console.log("\nEin dritter - der Platz hinter dem Kennwort");
console.log("------------------------------------------");

const third = await visit(2, await seatKey(CODE), 1800);

check(third.outcome === page.Result.Paired, `mit richtigem Kennwort geht er auf (${third.outcome})`);
check(third.states > 0, `und die Zahlen kommen an (${third.states})`);

closeAll();
await wait(800);

console.log("\nDerselbe Platz mit falschem Kennwort");
console.log("-----------------------------------");

const wrong = await visit(2, await seatKey("falsch999"), 1200);

check(wrong.outcome === page.Result.Wrong, `der Schlüsselnachweis scheitert (${wrong.outcome})`);
check(wrong.states === 0, "es kommen keine Zahlen an");
check(wrong.previews === 0, "und kein Bild");

closeAll();
await wait(800);

console.log("\nDas Geheimnis allein reicht für die hinteren Plätze nicht");
console.log("--------------------------------------------------------");

const bare = await visit(2, free, 1200);

check(bare.outcome === page.Result.Wrong, `auch mit gültigem Link bleibt zu (${bare.outcome})`);
check(bare.states === 0, "und es kommt nichts durch");

closeAll();

console.log(failures === 0
  ? "\nAlle Zusicherungen erfüllt.\n"
  : `\n${failures} Zusicherung(en) NICHT erfüllt.\n`);

process.exit(failures === 0 ? 0 : 1);
