// Prüfstand für die Zuschauerseite.
//
// Es wird NICHTS nachgebaut. Krypto, Verbindungsablauf und Platzsuche werden aus
// index.html herausgeschnitten und ausgeführt; auf der anderen Seite läuft FrameFlips
// echter WatchService. Ein Nachbau prüfte am Ende sich selbst.
//
// Dass run() mitläuft, ist nicht Gründlichkeit um ihrer selbst willen. Eine frühere
// Fassung prüfte nur trySeat einzeln und war grün, während die Seite im Betrieb nach
// dem Verbinden weitersuchte, ihren eigenen Platz besetzt vorfand, den nächsten nahm -
// und nach ein paar Runden alle Plätze selbst belegt hatte.

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
const search = slice("const ask = el(", "/* --------------------------------------------------------------- Start */", "Die Platzsuche");

const seen = { states: 0, previews: 0, lastState: null, jpegBytes: 0, said: [], hinweis: "", geleert: 0 };
const beimVerlassen = [];

const fakeEl = () => ({
  textContent: "", value: "", className: "", style: {},
  classList: { add() { }, remove() { }, contains: () => false },
  focus() { }, set onsubmit(_) { }
});

const page = new Function("location", "say", "show", "showPicture", "blank", "WebSocket", "el", "empty",
  "secretFromAddress", "rememberedCode", "rememberCode", "addEventListener",
  core + "\n" + flow + "\n" + search + "\n" +
  "return { trySeat, Result, run, hkdf, toHex, fromBase64Url, ROOM_INFO, SEAT_INFO, SEATS, FREE_SEATS };")(
    { host: RELAY, protocol: "http:" },
    text => { seen.said.push(text); },
    state => { seen.states++; seen.lastState = state; },
    jpeg => { seen.previews++; seen.jpegBytes = jpeg.length; },
    () => { seen.geleert++; },
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

/* Anklopfen und sofort wieder gehen.
 *
 * Dreiwertig, weil ein Platz drei Zustände hat: FrameFlip sitzt drin und wartet
 * (frei), jemand sitzt schon auf dem Gastplatz (besetzt), oder FrameFlip ist gar
 * nicht da (zu). Die Sonde führt den Handschlag NICHT zu Ende - für WatchService
 * zählt sie deshalb nicht als Zuschauer und verändert den Bedarf nicht. */
function zustand(room) {
  return new Promise(resolve => {
    const s = new WebSocket(`ws://${RELAY}/r/${room}?role=client`);
    let fertig = false;
    const raus = v => { if (fertig) return; fertig = true; resolve(v); try { s.close(); } catch { } };

    s.onerror = () => raus("?");
    s.onclose = () => raus("?");
    s.onmessage = e => {
      if (typeof e.data !== "string") return;
      const c = JSON.parse(e.data);
      if (c.t === "error") raus("besetzt");
      if (c.t === "waiting") raus("zu");
      if (c.t === "peer" && c.up) raus("frei");
    };
    setTimeout(() => raus("?"), 6000);
  });
}

async function plaetze() {
  const bild = [];
  for (let seat = 0; seat < page.SEATS; seat++) {
    bild.push(await zustand(await roomFor(seat)));
    await wait(120);
  }
  return {
    bild: bild.join(" "),
    offen: bild.filter(z => z === "frei" || z === "besetzt").length,
    besetzt: bild.filter(z => z === "besetzt").length
  };
}

const alleGehen = () => { for (const fn of beimVerlassen) fn(); };

// --------------------------------------- 1. Plätze bei Bedarf, nicht auf Vorrat

console.log("\nIm Leerlauf stehen nur die freien Plätze offen");
console.log("---------------------------------------------");

const leer = await plaetze();
check(leer.offen === page.FREE_SEATS, `${leer.offen} von ${page.SEATS} Plätzen offen  [${leer.bild}]`);
check(leer.besetzt === 0, "und keiner davon besetzt");

console.log("\nEin Zuschauer belegt einen, der Vorrat bleibt einer");
console.log("--------------------------------------------------");

const free = await seatKey(null);
const erster = await page.trySeat(await roomFor(0), free);
check(erster.outcome === page.Result.Paired, `der erste kommt herein (${erster.outcome})`);

await wait(1200);
const nachEinem = await plaetze();
check(nachEinem.offen === 2 && nachEinem.besetzt === 1,
  `weiterhin ${nachEinem.offen} offen, davon ${nachEinem.besetzt} besetzt  [${nachEinem.bild}]`);

console.log("\nSind beide belegt, geht der dritte Platz auf");
console.log("-------------------------------------------");

const zweiter = await page.trySeat(await roomFor(1), free);
check(zweiter.outcome === page.Result.Paired, `der zweite kommt herein (${zweiter.outcome})`);

await wait(1500);
const nachZweien = await plaetze();
check(nachZweien.offen === 3 && nachZweien.besetzt === 2,
  `jetzt ${nachZweien.offen} offen, davon ${nachZweien.besetzt} besetzt  [${nachZweien.bild}]`);

console.log("\nDer dritte Platz will das Kennwort");
console.log("----------------------------------");

const ohne = await page.trySeat(await roomFor(2), free);
check(ohne.outcome === page.Result.Wrong, `mit dem Link allein bleibt er zu (${ohne.outcome})`);

const falsch = await page.trySeat(await roomFor(2), await seatKey("falsch999"));
check(falsch.outcome === page.Result.Wrong, `mit falschem Kennwort auch (${falsch.outcome})`);

const dritter = await page.trySeat(await roomFor(2), await seatKey(CODE));
check(dritter.outcome === page.Result.Paired, `mit richtigem geht er auf (${dritter.outcome})`);

await wait(1500);
const nachDreien = await plaetze();
check(nachDreien.offen === 4 && nachDreien.besetzt === 3,
  `und der vierte steht bereit: ${nachDreien.offen} offen, ${nachDreien.besetzt} besetzt  [${nachDreien.bild}]`);

console.log("\nGehen alle, schrumpft es zurück");
console.log("-------------------------------");

alleGehen();
await wait(1500);

const gleichDanach = await plaetze();
check(gleichDanach.besetzt === 0, `sofort kein Zuschauer mehr  [${gleichDanach.bild}]`);

// Die Wartezeit vor dem Schließen steht im Prüfstand auf drei Sekunden, und je Takt
// geht höchstens einer zu - zwei überzählige brauchen also zwei Takte.
await wait(9000);

const geschrumpft = await plaetze();
check(geschrumpft.offen === page.FREE_SEATS,
  `nach der Wartezeit wieder ${geschrumpft.offen} offen  [${geschrumpft.bild}]`);

// ------------------------------------------------------------ 2. Die ganze Seite

console.log("\nEine ganze Seite belegt genau einen Platz");
console.log("----------------------------------------");

page.run();
await wait(6000);

check(seen.states > 0, `sie verbindet sich und bekommt Zahlen (${seen.states})`);
check(seen.previews > 0, `und ein Bild (${seen.previews})`);
check(seen.jpegBytes > 1000, `das ein echtes JPEG ist (${seen.jpegBytes} Bytes)`);
check(seen.lastState !== null && typeof seen.lastState === "object", "der Zustand ist lesbar");

const mitSeite = await plaetze();
check(mitSeite.besetzt === 1, `genau ein Platz belegt  [${mitSeite.bild}]`);

const bisher = seen.states;
await wait(9000);

const spaeter = await plaetze();
check(spaeter.besetzt === 1, `nach fünfzehn Sekunden immer noch genau einer (${spaeter.besetzt})`);
check(seen.states > bisher, `und es kommen weiter Zahlen (${seen.states})`);

alleGehen();

console.log(failures === 0
  ? "\nAlle Zusicherungen erfüllt.\n"
  : `\n${failures} Zusicherung(en) NICHT erfüllt.\n`);

process.exit(failures === 0 ? 0 : 1);
