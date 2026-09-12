// Der Dienstarbeiter haelt die Huelle bereit - mehr nicht.
//
// Er hat NICHTS mit den Daten zu tun, die durch die verschluesselte Leitung kommen.
// Sie werden nicht abgelegt, nicht zwischengespeichert und nicht angefasst: Ein Bild
// aus einem Render soll auf dem Geraet nicht laenger liegen, als es zu sehen ist.
// Abgelegt wird ausschliesslich, was ohnehin oeffentlich vom Server kommt.

const ABLAGE = "frameflip-huelle-v1";

const HUELLE = ["./", "./manifest.webmanifest", "./icon-192.png", "./icon-512.png", "./icon-maskable.png"];

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

  // Nur eigene GETs. Alles andere geht den Dienstarbeiter nichts an - und die
  // WebSocket-Verbindung laeuft ohnehin an ihm vorbei.
  if (anfrage.method !== "GET" || new URL(anfrage.url).origin !== self.location.origin) return;

  // Erst das Netz, dann die Ablage. Andersherum saehe man nach einem Neubau der
  // Seite womoeglich tagelang die alte Fassung, ohne zu verstehen warum.
  event.respondWith(
    fetch(anfrage)
      .then(antwort => {
        if (antwort && antwort.ok) {
          const kopie = antwort.clone();
          caches.open(ABLAGE).then(c => c.put(anfrage, kopie)).catch(() => { });
        }

        return antwort;
      })
      .catch(() => caches.match(anfrage).then(treffer => treffer || caches.match("./"))));
});
