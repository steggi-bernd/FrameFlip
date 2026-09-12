// Prüfstand für die Zuschauerseite.
//
// Es wird NICHTS nachgebaut. Krypto, Verbindungsablauf UND die Platzsuche werden aus
// watch.html herausgeschnitten und ausgeführt - ein Nachbau prüfte am Ende sich
// selbst und könnte denselben Fehler enthalten wie die Seite, ohne dass es auffiele.
//
// Dass auch run() mitläuft, ist nicht Gründlichkeit um ihrer selbst willen. Eine
// frühere Fassung prüfte nur trySeat einzeln und war grün, während die Seite im
// Betrieb nach dem Verbinden munter weitersuchte, ihren eigenen Platz als besetzt
// vorfand, den nächsten nahm - und nach ein paar Runden alle sechs Plätze selbst
// belegt hatte. Kein Zuschauer kam mehr herein, auch kein Handy. Ein Fehler im
// Zusammenspiel ist nicht zu sehen, wenn man nur die Teile prüft.

import fs from "node:fs";

const [, , PAGE, RELAY, SECRET, CODE] = process.argv;

const html = fs.readFileSync(PAGE, "utf8");

function slice(from, to, what) {
  const a = html.indexOf(from), b = html.indexOf(to);
  if (a < 0 || b <= a) { console.error(`${what} ließ sich nicht herausschneiden.`); process.exit(2); }
  return html.slice(a, b);
}

const core   = slice("const SEATS", "/* --------------------------------------------------------------- Das Geheimnis */", "Der Krypto-Teil");
const flow   = slice("const Result = {", "/* --------------------------------------------------------------- Platz suchen */", "Der Verbindungsablauf");
const search = slice("const ask = el(", "run();", "Die Platzsuche");

// Was die Seite anzeigen würde - hier wird es nur gezählt.
const seen = { states: 0, previews: 0, lastState: null, jpegBytes: 0, said: [], hinweis: "" };
const beimVerlassen = [];

const fakeEl = () => ({
  textContent: "", value: "", className: "", style: {},
  classList: { add() { }, remove() { }, contains: () => false },
  focus() { }, set onsubmit(_) { }
});

const page = new Function("location", "say", "show", "showPicture", "WebSocket", "el", "empty",
  "secretFromAddress", "rememberedCode", "rememberCode", "addEventListener",
  core + "\n" + flow + "\n" + search + "\n" +
  "return { trySeat, Result, run, hkdf, toHex, fromBase64Url, ROOM_INFO, SEAT_INFO, SEATS, FREE_SEATS };")(
    { host: RELAY, protocol: "http:" },
    text => { seen.said.push(text); },
    state => { seen.states++; seen.lastState = state; },
    jpeg => { seen.previews++; seen.jpegBytes = jpeg.length; },
    WebSocket,
    fakeEl,
    new Proxy({}, { set(o, k, v) { if (k === "textContent") seen.hinweis = v; return true; }, get: () => "" }),
    () => SECRET,
    () => "",
    () => { },
    (art, fn) => { if (art === "pagehide") beimVerlassen.push(fn); });

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

const wait = ms => new Promise(done => setTimeout(done, ms));

// Anklopfen, ohne einen Platz zu belegen: sobald die Antwort da ist, wieder gehen.
function belegt(room) {
  return new Promise(resolve => {
    const s = new WebSocket(`ws://${RELAY}/r/${room}?role=client`);
    let fertig = false;
    const raus = v => { if (fertig) return; fertig = true; resolve(v); try { s.close(); } catch { } };

    s.onerror = () => raus(null);
    s.onclose = () => raus(null);
    s.onmessage = e => {
      if (typeof e.data !== "string") return;
      const c = JSON.parse(e.data);
      if (c.t === "error") raus(true);
      if (c.t === "waiting" || (c.t === "peer" && c.up)) raus(false);
    };
    setTimeout(() => raus(null), 6000);
  });
}

async function wieVieleBelegt() {
  let n = 0;
  for (let seat = 0; seat < page.SEATS; seat++) {
    if (await belegt(await roomFor(seat))) n++;
    await wait(150);
  }
  return n;
}

function alleGehen() {
  for (const fn of beimVerlassen) fn();
}

// ------------------------------------------------------------ 1. Die Einzelteile

console.log("\nEin zweiter auf einen besetzten Platz");
console.log("------------------------------------");

const free = await seatKey(null);
const erster = await page.trySeat(await roomFor(0), free);
check(erster.outcome === page.Result.Paired, `der erste kommt herein (${erster.outcome})`);

const zweiter = await page.trySeat(await roomFor(0), free);
check(zweiter.outcome === page.Result.Taken, `der zweite wird abgewiesen (${zweiter.outcome})`);

check(await belegt(await roomFor(1)) === false,
  "und der abgewiesene Versuch hat keinen anderen Platz blockiert");

console.log("\nDer Platz hinter dem Kennwort");
console.log("-----------------------------");

const richtig = await page.trySeat(await roomFor(2), await seatKey(CODE));
check(richtig.outcome === page.Result.Paired, `mit richtigem Kennwort auf (${richtig.outcome})`);

const falsch = await page.trySeat(await roomFor(3), await seatKey("falsch999"));
check(falsch.outcome === page.Result.Wrong, `mit falschem zu (${falsch.outcome})`);

const ohne = await page.trySeat(await roomFor(4), free);
check(ohne.outcome === page.Result.Wrong, `und mit dem Link allein zu (${ohne.outcome})`);

check(await belegt(await roomFor(3)) === false && await belegt(await roomFor(4)) === false,
  "auch gescheiterte Versuche geben ihren Platz sofort zurück");

console.log("\nBeim Verlassen der Seite werden die Plätze frei");
console.log("----------------------------------------------");

check(beimVerlassen.length > 0, "die Seite meldet sich fürs Verlassen an");

alleGehen();
await wait(1500);

const nachAbschied = await wieVieleBelegt();
check(nachAbschied === 0, `danach ist kein Platz mehr belegt (${nachAbschied})`);

// ------------------------------------------------------------ 2. Die ganze Seite
//
// Der wichtigste Abschnitt. run() läuft hier wie im Browser - für immer - und darf
// dabei genau einen Platz belegen, nicht mehr.

console.log("\nEine ganze Seite belegt genau einen Platz");
console.log("----------------------------------------");

page.run();
await wait(6000);

check(seen.states > 0, `sie verbindet sich und bekommt Zahlen (${seen.states})`);
check(seen.previews > 0, `und ein Bild (${seen.previews})`);

const nachSechs = await wieVieleBelegt();
check(nachSechs === 1, `nach sechs Sekunden ist genau ein Platz belegt (${nachSechs})`);

const bisher = seen.states;
await wait(9000);

const nachFuenfzehn = await wieVieleBelegt();
check(nachFuenfzehn === 1, `nach fünfzehn immer noch genau einer (${nachFuenfzehn})`);
check(seen.states > bisher, `und es kommen weiter Zahlen (${seen.states})`);

alleGehen();

console.log(failures === 0
  ? "\nAlle Zusicherungen erfüllt.\n"
  : `\n${failures} Zusicherung(en) NICHT erfüllt.\n`);

process.exit(failures === 0 ? 0 : 1);
