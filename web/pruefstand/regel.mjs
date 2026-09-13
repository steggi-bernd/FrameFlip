// Prüft die Seite selbst, ohne sie zu starten.
//
// Alles hier sind Zusicherungen, die man an der Datei ablesen kann - und deren
// Bruch sich sonst erst im Betrieb zeigt, im schlechtesten Fall gar nicht.
//
//   node regel.mjs ../index.html ../sw.js

import fs from "node:fs";
import crypto from "node:crypto";

const [, , SEITE, WORKER] = process.argv;

const html = fs.readFileSync(SEITE, "utf8");
const sw = fs.readFileSync(WORKER, "utf8");

let failures = 0;

function check(ok, what, detail) {
  console.log((ok ? "  [ok]   " : "  [NEIN] ") + what + (detail && !ok ? "  → " + detail : ""));
  if (!ok) failures++;
}

function block(text, tag) {
  const treffer = text.match(new RegExp("<" + tag + "[^>]*>([\\s\\S]*?)</" + tag + ">"));
  return treffer ? treffer[1] : null;
}

const summe = inhalt =>
  "'sha256-" + crypto.createHash("sha256").update(inhalt, "utf8").digest("base64") + "'";

const regel = (html.match(/content="(default-src[^"]*)"/) || [])[1] || "";

// ------------------------------------------------------------- Die Inhaltsregel

console.log("\nDie Inhaltsregel");
console.log("----------------");

check(regel.length > 0, "es gibt eine");

for (const tag of ["style", "script"]) {
  const inhalt = block(html, tag);
  check(inhalt !== null, `es gibt genau einen <${tag}>-Block`);

  if (inhalt !== null) {
    check(regel.includes(summe(inhalt)),
      `die Prüfsumme des <${tag}>-Blocks steht in der Regel`,
      "die Regel ist veraltet - web/bauen.py neu laufen lassen, sonst bleibt die Seite leer");
  }
}

// Der eigentliche Gewinn: Eingeschleuster Code laeuft nicht, weil nur die eine
// bekannte Pruefsumme erlaubt ist.
const scriptTeil = (regel.match(/script-src ([^;]*)/) || [])[1] || "";
check(!scriptTeil.includes("unsafe-inline"),
  "script-src erlaubt kein 'unsafe-inline'", scriptTeil);
check(scriptTeil.includes("sha256-"), "sondern eine Prüfsumme");
check(!regel.includes("unsafe-eval"), "und kein 'unsafe-eval'");

check(/default-src 'none'/.test(regel), "alles ist voreingestellt verboten");
check(/connect-src [^;]*wss:/.test(regel),
  "connect-src nennt das Schema ausdrücklich (WebKit rechnet 'self' nicht darauf an)");
check(/base-uri 'none'/.test(regel), "base-uri ist zu");
check(/form-action 'none'/.test(regel), "form-action ist zu");

// ------------------------------------------------------ Fremde Daten und das DOM

console.log("\nFremde Daten und das DOM");
console.log("------------------------");

for (const [muster, was] of [
  [/\.innerHTML\s*=/, "innerHTML"],
  [/\.outerHTML\s*=/, "outerHTML"],
  [/insertAdjacentHTML/, "insertAdjacentHTML"],
  [/document\.write/, "document.write"],
  [/\beval\s*\(/, "eval"],
  [/new Function\s*\(/, "new Function"]
]) {
  check(!muster.test(html), `die Seite benutzt kein ${was}`);
}

check(/function zahl\(/.test(html) && /Number\.isFinite/.test(html),
  "Zahlen von der Gegenseite gehen durch eine Schleuse");
check(/function text\(/.test(html) && /\.slice\(0,/.test(html),
  "Zeichenketten von der Gegenseite werden beschnitten");
check(/typeof state !== "object" \|\| Array\.isArray\(state\)/.test(html),
  "ein Zustand, der kein Objekt ist, wird verworfen");

// ------------------------------------------------------------------ Das Geheimnis

console.log("\nDas Geheimnis");
console.log("-------------");

check(/history\.replaceState\(null, "", location\.pathname\)/.test(html),
  "es verschwindet sofort aus der Adresszeile");
check(/keep\(KEY_CODE, ""\)/.test(html),
  "ein neuer Link wirft das alte Kennwort weg");
check(!/fetch\(/.test(html.replace(/\/\*[\s\S]*?\*\//g, "")),
  "die Seite holt von sich aus nichts nach");

// ------------------------------------------------------------- Der Dienstarbeiter

console.log("\nDer Dienstarbeiter");
console.log("------------------");

check(/const HUELLE = \[/.test(sw), "er hat eine Liste dessen, was er ablegen darf");
check(/HUELLE\.includes\(url\.pathname\)/.test(sw),
  "und legt nur ab, was darin steht - keine Regel, eine Liste");
check(!/\/r\//.test(sw), "die Räume des Leuchtturms kennt er nicht");
check(/antwort\.type === "basic"/.test(sw), "und nur eigene Antworten");

console.log(failures === 0
  ? "\nAlle Zusicherungen erfüllt.\n"
  : `\n${failures} Zusicherung(en) NICHT erfüllt.\n`);

process.exit(failures === 0 ? 0 : 1);
