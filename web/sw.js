// Der Dienstarbeiter haelt die Huelle bereit - mehr nicht.
//
// Er hat NICHTS mit den Daten zu tun, die durch die verschluesselte Leitung kommen.
// Sie werden nicht abgelegt, nicht zwischengespeichert und nicht angefasst: Ein Bild
// aus einem Render soll auf dem Geraet nicht laenger liegen, als es zu sehen ist.
// Abgelegt wird ausschliesslich, was ohnehin oeffentlich vom Server kommt.

const ABLAGE = "frameflip-huelle-v2";

/* Eine LISTE, keine Regel.
 *
 * Vorher wurde jede eigene Antwort abgelegt, die durch den Dienstarbeiter lief.
 * Heute holt die Seite nichts anderes - aber "heute nichts anderes" ist eine
 * Beobachtung, keine Zusicherung. Eine Liste ist eine: Was nicht darin steht, wird
 * durchgereicht und nie behalten, ganz gleich was spaeter einmal dazukommt.
 */
const HUELLE = ["/w/", "/w/manifest.webmanifest", "/w/icon-192.png", "/w/icon-512.png", "/w/icon-maskable.png"];

const gehoertZurHuelle = url =>
  url.origin === self.location.origin && HUELLE.includes(url.pathname);

self.addEventListener("install", event => {
  event.waitUntil(caches.open(ABLAGE).then(c => c.addAll(HUELLE)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", event => {
  // Alte Staende wegraeumen, sonst bleibt eine ueberholte Seite fuer immer liegen.
  event.waitUntil(
    caches.keys()
      .then(namen => Promise.all(namen.filter(n => n !== ABLAGE).map(n => caches.delete(n))))
      .then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
  const anfrage = event.request;

  if (anfrage.method !== "GET") return;

  // Alles, was nicht ausdruecklich zur Huelle gehoert, geht am Dienstarbeiter vorbei.
  // Er sieht es dann gar nicht erst - und kann es folglich auch nicht ablegen.
  if (!gehoertZurHuelle(new URL(anfrage.url))) return;

  // Erst das Netz, dann die Ablage. Andersherum saehe man nach einem Neubau der
  // Seite womoeglich tagelang die alte Fassung, ohne zu verstehen warum.
  event.respondWith(
    fetch(anfrage)
      .then(antwort => {
        if (antwort && antwort.ok && antwort.type === "basic") {
          const kopie = antwort.clone();
          caches.open(ABLAGE).then(c => c.put(anfrage, kopie)).catch(() => { });
        }

        return antwort;
      })
      .catch(() => caches.match(anfrage).then(treffer => treffer || caches.match("/w/"))));
});
