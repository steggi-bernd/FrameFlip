/* --------------------------------------------------------------- Das Geheimnis */

/* Aus der Adresse holen und sofort daraus entfernen.

   Der Sinn ist handfest: Danach steht in der Adresszeile nur noch der Pfad. Ein
   Screenshot vom Handy verraet nichts, der Verlauf behaelt nichts, und wer kurz
   ueber die Schulter sieht, sieht nichts.

   Abgelegt wird im localStorage, nicht nur fuer die Sitzung. Das ist noetig, damit
   die Seite sich installieren laesst: Ein installiertes Programm startet unter
   seiner eigenen Adresse, ohne Raute - ohne bleibende Ablage waere es bei jedem
   Start leer. Wer das nicht will, hat unten "Forget access". */
const KEY_SECRET = "frameflip.watch.key";
const KEY_CODE = "frameflip.watch.code";

function keep(name, value) {
  try {
    if (value) localStorage.setItem(name, value);
    else localStorage.removeItem(name);
  } catch (e) { /* privates Fenster: dann eben nur fuer diesen Besuch */ }
}

function kept(name) {
  try { return localStorage.getItem(name) || ""; } catch (e) { return ""; }
}

function secretFromAddress() {
  const raw = location.hash.replace(/^#/, "").trim();

  if (raw) {
    keep(KEY_SECRET, raw);

    // Ein neuer Link heisst auch: das alte Kennwort passt womoeglich nicht mehr.
    keep(KEY_CODE, "");

    history.replaceState(null, "", location.pathname);
    return raw;
  }

  return kept(KEY_SECRET);
}

const rememberedCode = () => kept(KEY_CODE);
const rememberCode = code => keep(KEY_CODE, code);

/* ------------------------------------------------------------------- Anzeige */

const el = id => document.getElementById(id);
const dot = el("dot"), standing = el("standing"), stage = el("stage"), empty = el("empty");
const tools = el("tools"), viewer = el("viewer"), vstage = el("vstage");

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

let pictureUrl = null;   // die derzeitige blob-Adresse des Bildes
let lastJpeg = null;     // die Bytes, fuer das Sichern
let lastState = null;

function say(text, mood) {
  standing.textContent = text;
  dot.className = "dot" + (mood ? " " + mood : "");
}

function clock(seconds) {
  if (seconds === undefined || seconds === null) return "–";

  const s = Math.max(0, Math.round(seconds));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;

  return h > 0
    ? h + ":" + String(m).padStart(2, "0") + ":" + String(r).padStart(2, "0")
    : m + ":" + String(r).padStart(2, "0");
}

const put = (id, text) => { el(id).textContent = text; };

/* Was von der Gegenseite kommt, wird NICHT geglaubt.

   Der Kanal ist verschluesselt und beglaubigt - wer hier hereinredet, hat den
   Schluessel. Das ist aber kein Grund, seinen Zahlen zu trauen: Der Link fuer die
   freien Plaetze ist ein Ausweis zum Weitergeben, und laeuft FrameFlip gerade
   nicht, kann jemand mit diesem Link den leeren Host-Platz einnehmen und dem
   naechsten Zuschauer liefern, was er will. Aus einer erfundenen Bildnummer soll
   dann trotzdem nur eine Zeichenkette werden und niemals Markup oder ein Befehl.

   Deshalb zwei Schleusen: zahl() laesst ausschliesslich endliche Zahlen durch,
   text() schneidet Zeichenketten auf eine Laenge, die eine Zeile nicht sprengt. */
function zahl(wert) {
  return typeof wert === "number" && Number.isFinite(wert) ? wert : null;
}

function ganz(wert) {
  const n = zahl(wert);
  return n === null ? null : Math.trunc(n);
}

function text(wert, grenze) {
  return typeof wert === "string" ? wert.slice(0, grenze || 60) : "";
}

function show(state) {
  // JSON.parse liefert auch null, Zahlen oder Listen. Nur ein Objekt ist ein Zustand.
  if (!state || typeof state !== "object" || Array.isArray(state)) return;

  lastState = state;

  const busy = state.rendering === true;
  const prozent = zahl(state.percent);
  const bild = ganz(state.frame);

  el("chip").className = "chip " + (busy ? "live" : "idle");
  el("chip").textContent = busy ? (text(state.activity, 24) || "rendering") : "ready";

  el("scene").textContent = text(state.scene, 60);

  put("percent", prozent === null ? "–" : prozent.toFixed(1));
  el("barFill").style.width = clamp(prozent === null ? 0 : prozent, 0, 100) + "%";

  put("frameNow", bild === null ? "–" : String(bild));
  put("frameOf", bild !== null && ganz(state.last) !== null ? " / " + ganz(state.last) : "");

  put("elapsed", clock(zahl(state.elapsed)));
  put("remaining", busy ? clock(zahl(state.remaining)) : "–");

  const jeBild = zahl(state.secondsPerFrame);
  put("spf", jeBild === null ? "–" : jeBild.toFixed(2) + " s");

  const geschrieben = ganz(state.written);
  put("written", geschrieben === null ? "–" : String(geschrieben));

  const probe = ganz(state.sample), proben = ganz(state.sampleTotal);
  put("sample", probe === null ? "–" : probe + (proben === null ? "" : " / " + proben));

  const breite = ganz(state.width), höhe = ganz(state.height);
  put("size", breite === null || höhe === null ? "–" : breite + " × " + höhe);

  const speicher = zahl(state.memoryMb);
  put("memory", speicher === null ? "–" : Math.round(speicher) + " MB");

  const cpu = zahl(state.cpu), gpu = zahl(state.gpu);
  put("cpu", cpu === null ? "–" : cpu.toFixed(0) + " %");
  put("gpu", gpu === null ? "–" : gpu.toFixed(0) + " %");

  const frei = zahl(state.freeMb), gesamt = zahl(state.totalMb);
  put("ram", frei === null ? "–"
    : (frei / 1024).toFixed(1) + (gesamt === null ? "" : " / " + (gesamt / 1024).toFixed(0)) + " GB");

  put("saveName", dateiname());

  say(busy ? (text(state.activity, 24) || "rendering") : "ready", busy ? "live" : "idle");
}

/* ----------------------------------------------------------------- Ansichten

   EINE Ansicht, zweimal eingehaengt: auf der Buehne und in der isolierten
   Vorschau. Zwei Umsetzungen desselben Gedankens waeren zwei Orte, an denen
   dieselbe Rechnung schiefgehen kann - und die Rechnung ist hier der ganze Punkt.

   Gerechnet wird durchweg gegen die MITTE der Flaeche, nie gegen den Kasten des
   Bildes. Der wandert ja mit der Verschiebung mit; wer gegen ihn rechnet, jagt
   seinem eigenen Ergebnis hinterher. Aus demselben Grund steht im Stil KEIN
   transform-origin - die Voreinstellung ist die Mitte. */
function macheAnsicht(flaeche, anzeige, { drehbar = false, beiKlick = null } = {}) {
  let bild = null;
  let maß = 1, tx = 0, ty = 0, dreh = 0;

  /* Wie weit sich das Bild schieben laesst.

     Der Betrag der Differenz, nicht nur der Ueberstand. Das ist der Unterschied,
     an dem es haengt: Ist das Bild kleiner als die Flaeche, war es frueher starr
     in der Mitte festgenagelt - und der Punkt unter dem Zeiger konnte gar nicht
     dort bleiben. Es zog erst zur Mitte und dann langsam zum Zeiger, genau wie es
     sich angefuehlt hat. Jetzt darf es sich auch im freien Raum bewegen, solange
     es die Flaeche nicht verlaesst. */
  function grenzen() {
    if (!bild) return;

    const f = flaeche.getBoundingClientRect();
    const quer = dreh % 180 !== 0;

    const breite = (quer ? bild.clientHeight : bild.clientWidth) * maß;
    const höhe = (quer ? bild.clientWidth : bild.clientHeight) * maß;

    /* Der Betrag allein ergibt ein V: Ist das Bild viel kleiner oder viel groesser
       als die Flaeche, gibt es reichlich Raum - aber genau dort, wo beide ungefaehr
       gleich gross sind, geht er gegen null. Und das ist der Zoomschritt, in dem
       der Punkt unter dem Zeiger am weitesten wegrutscht: Er braucht dort umso mehr
       Verschiebung, je weiter er von der Mitte weg liegt.

       Ein Viertel der Flaeche als Untergrenze fuellt diese Senke. Mehr als ein
       Viertel kann das Bild dadurch nie aus der Mitte wandern - es bleibt immer weit
       ueberwiegend zu sehen, und der Zeiger trifft trotzdem. */
    const x = Math.max(Math.abs(breite - f.width) / 2, f.width * 0.25);
    const y = Math.max(Math.abs(höhe - f.height) / 2, f.height * 0.25);

    tx = clamp(tx, -x, x);
    ty = clamp(ty, -y, y);
  }

  function anwenden() {
    if (!bild) return;

    grenzen();

    // Die Reihenfolge zaehlt: erst skalieren, dann drehen, dann verschieben.
    // Dadurch bleibt die Verschiebung in Bildschirmrichtung, auch wenn das Bild
    // hochkant steht - sonst zoege ein Zug nach rechts es nach unten.
    bild.style.transform = `translate(${tx}px, ${ty}px) rotate(${dreh}deg) scale(${maß})`;
    bild.className = maß > passend() * 1.01 ? "grab" : "";

    if (anzeige) anzeige.textContent = Math.round(maß * 100) + " %";
  }

  /* Der kleinste sinnvolle Maßstab: so, dass das Bild ganz hineinpasst.

     Nach einer Vierteldrehung tauschen Breite und Höhe - ein liegendes Bild
     braucht hochkant deutlich weniger Maßstab, um noch ganz zu sein. */
  function passend() {
    if (!bild || !bild.clientWidth) return 1;
    if (dreh % 180 === 0) return 1;   // ungedreht passt es durch das Layout schon

    const f = flaeche.getBoundingClientRect();

    return Math.min(1, Math.min(f.width / bild.clientHeight, f.height / bild.clientWidth));
  }

  /* Zoomen dorthin, wo der Zeiger ist.

     Der Punkt unter dem Zeiger soll unter dem Zeiger BLEIBEN - vom ersten Schritt
     an, nicht erst, wenn genug Ueberstand da ist. Mit p als Abstand des Zeigers
     von der Mitte und k als Aenderung des Maßstabs ist die neue Verschiebung
     t' = p - k * (p - t). */
  function zoom(ziel, cx, cy) {
    if (!bild) return;

    const klein = passend();
    const next = clamp(ziel, klein, klein * 12);
    const k = next / maß;

    if (cx === undefined) {
      tx *= k;
      ty *= k;
    } else {
      const f = flaeche.getBoundingClientRect();
      const px = cx - (f.left + f.width / 2);
      const py = cy - (f.top + f.height / 2);

      tx = px - k * (px - tx);
      ty = py - k * (py - ty);
    }

    maß = next;
    anwenden();
  }

  function einpassen() {
    maß = passend();
    tx = 0;
    ty = 0;
    anwenden();
  }

  function drehen(schritt) {
    if (!drehbar) return;

    dreh = (dreh + schritt + 360) % 360;
    einpassen();
  }

  function setzeBild(url, behalten) {
    if (!bild) {
      bild = document.createElement("img");
      bild.alt = "";
      bild.addEventListener("dblclick", event => {
        const klein = passend();
        zoom(maß > klein * 1.01 ? klein : klein * 2.5, event.clientX, event.clientY);
      });
      flaeche.appendChild(bild);
    }

    bild.src = url;

    // Beim Bildwechsel wird Zoom und Drehung NICHT zurueckgesetzt: Wer sich gerade
    // eine Ecke ansieht, will beim naechsten Frame dieselbe Ecke sehen.
    if (!behalten) {
      dreh = 0;
      if (bild.complete && bild.clientWidth) einpassen();
      else bild.onload = einpassen;
    }
  }

  function weg() {
    if (bild) { bild.remove(); bild = null; }
    maß = 1; tx = 0; ty = 0; dreh = 0;
    if (anzeige) anzeige.textContent = "100 %";
  }

  // ---------------------------------------------------------- Rad und Finger

  flaeche.addEventListener("wheel", event => {
    if (!bild) return;

    event.preventDefault();
    zoom(maß * (event.deltaY < 0 ? 1.18 : 1 / 1.18), event.clientX, event.clientY);
  }, { passive: false });

  const zeiger = new Map();
  let greifpunkt = null, kneifAbstand = 0, kneifStart = 1, gezogen = 0;

  flaeche.addEventListener("pointerdown", event => {
    if (!bild) return;

    zeiger.set(event.pointerId, { x: event.clientX, y: event.clientY });
    flaeche.setPointerCapture(event.pointerId);

    if (zeiger.size === 1) {
      greifpunkt = { x: event.clientX - tx, y: event.clientY - ty };
      gezogen = 0;
      bild.className = "grabbing";
    }

    if (zeiger.size === 2) {
      const [a, b] = Array.from(zeiger.values());
      kneifAbstand = Math.hypot(a.x - b.x, a.y - b.y);
      kneifStart = maß;
      greifpunkt = null;
    }
  });

  flaeche.addEventListener("pointermove", event => {
    if (!zeiger.has(event.pointerId)) return;

    const vorher = zeiger.get(event.pointerId);
    zeiger.set(event.pointerId, { x: event.clientX, y: event.clientY });

    if (zeiger.size === 2) {
      const [a, b] = Array.from(zeiger.values());
      const jetzt = Math.hypot(a.x - b.x, a.y - b.y);

      if (kneifAbstand > 0) zoom(kneifStart * (jetzt / kneifAbstand), (a.x + b.x) / 2, (a.y + b.y) / 2);

      return;
    }

    if (greifpunkt) {
      gezogen += Math.hypot(event.clientX - vorher.x, event.clientY - vorher.y);
      tx = event.clientX - greifpunkt.x;
      ty = event.clientY - greifpunkt.y;
      anwenden();
    }
  });

  function loslassen(event) {
    const war = zeiger.size;
    zeiger.delete(event.pointerId);

    if (zeiger.size < 2) kneifAbstand = 0;

    if (zeiger.size === 0) {
      greifpunkt = null;
      if (bild) anwenden();

      // Ein Zug ist kein Klick. Nur wer das Bild nicht bewegt hat, wollte es
      // oeffnen - sonst kaeme die Vorschau nach jedem Verschieben hoch.
      if (war === 1 && gezogen < 5 && beiKlick) beiKlick();
    }
  }

  flaeche.addEventListener("pointerup", loslassen);
  flaeche.addEventListener("pointercancel", loslassen);

  return { setzeBild, weg, zoom, einpassen, drehen, anwenden, maß: () => maß };
}

const bühne = macheAnsicht(stage, el("level"), { beiKlick: openViewer });
const groß = macheAnsicht(vstage, el("vLevel"), { drehbar: true });

function showPicture(jpeg) {
  lastJpeg = jpeg;

  const next = URL.createObjectURL(new Blob([jpeg], { type: "image/jpeg" }));

  bühne.setzeBild(next, true);
  empty.style.display = "none";
  tools.classList.add("on");
  el("save").classList.add("on");
  put("saveName", dateiname());

  if (viewer.classList.contains("on")) groß.setzeBild(next, true);

  // Die alte Adresse erst freigeben, wenn die neue steht - sonst blitzt es weiss auf.
  const old = pictureUrl;
  pictureUrl = next;
  if (old) setTimeout(() => URL.revokeObjectURL(old), 1000);
}

/* Alles vom Bildschirm nehmen.

   Wird gerufen, bevor nach einem Kennwort gefragt wird. Ohne das staende hinter der
   Abfrage noch, was eine fruehere Verbindung geliefert hat - und ein Kennwort, das
   man umgehen kann, indem man dahinterschaut, ist keines. */
function blank() {
  closeViewer();

  bühne.weg();
  groß.weg();

  if (pictureUrl) { URL.revokeObjectURL(pictureUrl); pictureUrl = null; }

  lastJpeg = null;
  lastState = null;

  tools.classList.remove("on");
  el("save").classList.remove("on");
  empty.style.display = "";
  empty.textContent = "No frame yet.";

  el("chip").className = "chip";
  el("chip").textContent = "waiting";
  el("scene").textContent = "";
  put("percent", "–");
  put("frameNow", "–");
  put("frameOf", "");
  el("barFill").style.width = "0%";

  for (const id of ["elapsed", "remaining", "spf", "written", "sample", "size", "memory", "cpu", "gpu", "ram"]) {
    put(id, "–");
  }
}

/* ------------------------------------------------------- Die isolierte Vorschau */

function openViewer() {
  if (!pictureUrl) return;

  viewer.classList.add("on");
  viewer.setAttribute("aria-hidden", "false");

  groß.setzeBild(pictureUrl, false);
}

function closeViewer() {
  viewer.classList.remove("on");
  viewer.setAttribute("aria-hidden", "true");
}

el("zoomIn").onclick = () => bühne.zoom(bühne.maß() * 1.5);
el("zoomOut").onclick = () => bühne.zoom(bühne.maß() / 1.5);
el("zoomFit").onclick = () => bühne.einpassen();

el("vClose").onclick = closeViewer;
el("vIn").onclick = () => groß.zoom(groß.maß() * 1.5);
el("vOut").onclick = () => groß.zoom(groß.maß() / 1.5);
el("vFit").onclick = () => groß.einpassen();
el("vLeft").onclick = () => groß.drehen(-90);
el("vRight").onclick = () => groß.drehen(90);

// Ein Klick neben das Bild schließt - auf das Bild selbst nicht.
vstage.addEventListener("click", event => { if (event.target === vstage) closeViewer(); });

addEventListener("keydown", event => {
  if (!viewer.classList.contains("on")) return;

  if (event.key === "Escape") closeViewer();
  else if (event.key === "+" || event.key === "=") groß.zoom(groß.maß() * 1.5);
  else if (event.key === "-") groß.zoom(groß.maß() / 1.5);
  else if (event.key === "0") groß.einpassen();
});

addEventListener("resize", () => { bühne.anwenden(); groß.anwenden(); });

/* ------------------------------------------------------------------- Sichern */

/* Der Dateiname entsteht aus einer geprueften Zahl, nicht aus dem, was ankam.

   Eine Zeichenkette von der Gegenseite haette hier als Dateiname gelandet. Browser
   raeumen so etwas zwar auf, aber ein Name, der aus fremder Hand stammt, hat in
   einem Download nichts zu suchen. */
function dateiname() {
  const nummer = lastState ? ganz(lastState.frame) : null;

  return "frameflip_" + (nummer === null ? "current" : String(nummer).padStart(4, "0")) + ".jpg";
}

function sichern() {
  if (!lastJpeg) return;

  const url = URL.createObjectURL(new Blob([lastJpeg], { type: "image/jpeg" }));
  const a = document.createElement("a");

  a.href = url;
  a.download = dateiname();
  document.body.appendChild(a);
  a.click();
  a.remove();

  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

el("save").onclick = sichern;
el("vSave").onclick = sichern;

el("forget").onclick = () => {
  keep(KEY_SECRET, "");
  keep(KEY_CODE, "");
  location.reload();
};

/* ---------------------------------------------------------------- Ablegen

   Der Knopf erscheint nur, wenn der Browser das Ablegen auch anbietet. Ihn immer
   zu zeigen und dann nichts zu tun, waere die schlechtere Auskunft.

   Auf einem iPhone gibt es dieses Ereignis nicht - dort fuehrt der Weg ueber
   "Teilen" und "Zum Home-Bildschirm". Das steht dann als Hinweis in der Fusszeile,
   statt einen Knopf anzubieten, der nichts bewirkt. */
let ablegen = null;

addEventListener("beforeinstallprompt", event => {
  event.preventDefault();
  ablegen = event;
  el("install").classList.add("on");
});

el("install").onclick = async () => {
  if (!ablegen) return;

  ablegen.prompt();
  await ablegen.userChoice;

  ablegen = null;
  el("install").classList.remove("on");
};

addEventListener("appinstalled", () => el("install").classList.remove("on"));

