// Der Gast für den Flutmodus: koppelt sich auf Platz 0 und zählt, was ankommt.
import fs from "node:fs";
const [, , PAGE, RELAY, SECRET] = process.argv;
const html = fs.readFileSync(PAGE, "utf8");
const s = t => html.slice(html.indexOf(t[0]), html.indexOf(t[1]));

const page = new Function("location", "say", "show", "showPicture", "WebSocket", "addEventListener",
  s(["const SEATS", "/* --------------------------------------------------------------- Das Geheimnis */"]) + "\n" +
  s(["const Result = {", "/* --------------------------------------------------------------- Platz suchen */"]) + "\n" +
  "return { trySeat, Result, hkdf, toHex, fromBase64Url, ROOM_INFO, SEAT_INFO };")(
    { host: RELAY, protocol: "http:" }, () => { }, () => { }, () => { }, WebSocket, () => { });

const secret = page.fromBase64Url(SECRET);
const raum = page.toHex(await page.hkdf(secret, page.ROOM_INFO, new Uint8Array([0]), 16));
const schluessel = await page.hkdf(secret, page.SEAT_INFO, new Uint8Array(0), 32);

const { outcome, closed } = await page.trySeat(raum, schluessel);
console.log("  Gast: " + outcome);

closed.then(() => console.log("  Gast: Verbindung beendet"));
await new Promise(r => setTimeout(r, 32000));
process.exit(0);
