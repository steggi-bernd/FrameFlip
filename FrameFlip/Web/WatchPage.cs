namespace FrameFlip.Web;

/// <summary>
/// Die Seite, die der Browser bekommt - eine Datei, ohne Nachladen.
///
/// Kein Skript von aussen, keine Schrift von aussen, kein Bild von aussen. Das ist
/// nicht Sparsamkeit, sondern dieselbe Haltung wie beim Rest: Was die Seite nicht
/// nachlaedt, kann auch niemand unterwegs austauschen. Die Sicherheitsregel im
/// Kopf der Antwort sagt dasselbe noch einmal - "default-src 'none'".
///
/// DREI FORMEN, EIN AUFBAU. Auf dem Schreibtisch steht der Frame links und die
/// Zahlen rechts daneben. Auf dem hochkant gehaltenen Handy steht der Frame oben und
/// die Zahlen darunter. Quer gehalten wird daraus wieder das Nebeneinander, nur
/// enger. Dafuer braucht es keine drei Entwuerfe, sondern ein Raster, das an zwei
/// Stellen umbricht - und ein Bild, das sich nach der verfuegbaren Hoehe richtet,
/// statt nach der Breite. Genau daran scheitern die meisten Seiten im Querformat:
/// Sie rechnen mit der Breite, und das Bild schiebt alles andere aus dem Schirm.
/// </summary>
public static class WatchPage
{
    public const string Html = """
<!DOCTYPE html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="dark">
<title>FrameFlip</title>
<style>
  :root {
    --bg: #07070d; --panel: #0b0b14; --surface: #11111b; --stage: #05050a;
    --line: #191926; --ink: #f4f4f8; --muted: #9b9bb0; --faint: #6b6b80;
    --accent: #a855f7; --blue: #60a5fa; --cyan: #22d3ee; --warn: #f472b6;
  }
  * { box-sizing: border-box; }
  html, body { margin: 0; height: 100%; }
  body {
    background: var(--bg); color: var(--ink);
    font: 14px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif;
    -webkit-text-size-adjust: 100%;
    padding: env(safe-area-inset-top) env(safe-area-inset-right)
             env(safe-area-inset-bottom) env(safe-area-inset-left);
  }

  .shell { display: flex; flex-direction: column; height: 100%; }

  header {
    display: flex; align-items: center; gap: 10px;
    padding: 10px 14px; background: var(--panel);
    border-bottom: 1px solid var(--line); flex: none;
  }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--faint); flex: none; }
  .dot.live { background: var(--cyan); animation: pulse 1.6s ease-in-out infinite; }
  @keyframes pulse { 50% { opacity: .35; } }
  .mark { font-weight: 800; font-size: 11px; letter-spacing: .16em; }
  .scene {
    margin-left: auto; font-family: ui-monospace, Consolas, monospace;
    font-size: 11px; color: var(--muted);
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  }

  main { flex: 1; min-height: 0; display: grid; gap: 12px; padding: 12px; }

  /* Handy hochkant: Bild oben, Zahlen darunter. */
  main { grid-template-rows: minmax(0, 1fr) auto; }

  .stage {
    background: var(--stage); border: 1px solid var(--line); border-radius: 12px;
    display: flex; align-items: center; justify-content: center;
    overflow: hidden; min-height: 0; position: relative;
  }
  .stage img { max-width: 100%; max-height: 100%; display: block; }
  .stage .empty { color: var(--faint); font-size: 13px; text-align: center; padding: 20px; }

  .badge {
    position: absolute; top: 10px; left: 10px;
    background: #cc07070d; background: rgba(7,7,13,.8);
    border-radius: 7px; padding: 5px 9px;
    font-family: ui-monospace, Consolas, monospace; font-size: 10px; color: var(--ink);
  }

  .side { display: flex; flex-direction: column; gap: 10px; min-height: 0; }

  .big { background: var(--surface); border: 1px solid var(--line); border-radius: 12px; padding: 14px; }
  .big .row { display: flex; align-items: baseline; gap: 8px; }
  .big .pct { font-size: 40px; font-weight: 800; line-height: 1; font-variant-numeric: tabular-nums; }
  .big .unit { font-size: 16px; color: var(--accent); font-weight: 700; }
  .big .rest { margin-left: auto; text-align: right; }
  .label { font-size: 9px; letter-spacing: .14em; color: var(--muted); font-weight: 700; }
  .value { font-family: ui-monospace, Consolas, monospace; font-size: 13px; font-variant-numeric: tabular-nums; }

  .bar { height: 4px; border-radius: 2px; background: #ffffff14; margin-top: 12px; overflow: hidden; }
  .bar > i { display: block; height: 100%; width: 0; background: var(--accent); transition: width .4s ease; }

  .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(88px, 1fr)); gap: 8px; }
  .tile { background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 10px; }
  .tile .value { font-size: 17px; margin-top: 5px; display: block; }

  footer {
    flex: none; padding: 8px 14px; background: var(--panel);
    border-top: 1px solid var(--line);
    font-family: ui-monospace, Consolas, monospace; font-size: 10px; color: var(--faint);
    display: flex; gap: 14px; flex-wrap: wrap;
  }

  /* Schreibtisch und Handy quer: nebeneinander. Die Umbruchstelle richtet sich nach
     der Breite, die Bildhoehe nach der Hoehe - sonst draengt das Bild im Querformat
     alles andere hinaus. */
  @media (min-width: 700px) {
    main { grid-template-rows: minmax(0, 1fr); grid-template-columns: minmax(0, 1fr) 300px; }
    .side { overflow-y: auto; }
  }
  @media (min-width: 1100px) {
    main { grid-template-columns: minmax(0, 1fr) 360px; gap: 16px; padding: 16px; }
    .big .pct { font-size: 52px; }
  }
  @media (max-height: 520px) and (orientation: landscape) {
    header { padding: 6px 12px; }
    main { gap: 8px; padding: 8px; }
    .big { padding: 10px; }
    .big .pct { font-size: 30px; }
    .tile { padding: 8px; }
    footer { padding: 5px 12px; }
  }
</style>
</head>
<body>
<div class="shell">
  <header>
    <span class="dot" id="dot"></span>
    <span class="mark">FRAMEFLIP</span>
    <span class="scene" id="scene">verbinde …</span>
  </header>

  <main>
    <div class="stage">
      <img id="frame" alt="" hidden>
      <div class="empty" id="empty">Noch kein Bild geschrieben.</div>
      <div class="badge" id="badge" hidden></div>
    </div>

    <div class="side">
      <div class="big">
        <div class="row">
          <span class="pct" id="pct">—</span><span class="unit">%</span>
          <span class="rest">
            <span class="label">RESTZEIT</span><br>
            <span class="value" id="remaining">—</span>
          </span>
        </div>
        <div class="bar"><i id="barFill"></i></div>
      </div>

      <div class="tiles">
        <div class="tile"><span class="label">BILD</span><span class="value" id="frameNo">—</span></div>
        <div class="tile"><span class="label">SAMPLE</span><span class="value" id="sample">—</span></div>
        <div class="tile"><span class="label">SEK/BILD</span><span class="value" id="spf">—</span></div>
        <div class="tile"><span class="label">GPU</span><span class="value" id="gpu">—</span></div>
        <div class="tile"><span class="label">CPU</span><span class="value" id="cpu">—</span></div>
        <div class="tile"><span class="label">RAM FREI</span><span class="value" id="ram">—</span></div>
      </div>
    </div>
  </main>

  <footer>
    <span id="activity">—</span>
    <span id="size"></span>
    <span id="elapsed"></span>
  </footer>
</div>

<script>
(function () {
  // Die Adresse enthaelt das Zeichen; alles wird relativ dazu geholt. Damit steht
  // es an genau einer Stelle und nirgends sonst.
  var base = location.pathname.replace(/\/?$/, '/');
  var lastFrame = null;
  var failures = 0;

  function text(id, value) { document.getElementById(id).textContent = value; }

  function clock(seconds) {
    if (seconds == null) return '—';
    var s = Math.max(0, Math.round(seconds));
    var h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;
    return h > 0 ? h + ':' + String(m).padStart(2, '0') + ':' + String(r).padStart(2, '0')
                 : m + ':' + String(r).padStart(2, '0');
  }

  function pad(n) { return String(n).padStart(4, '0'); }

  function show(state) {
    failures = 0;

    var live = state.rendering === true;
    document.getElementById('dot').className = 'dot' + (live ? ' live' : '');

    text('scene', (state.scene || 'FrameFlip') + (live ? ' · rendert' : ' · bereit'));

    var pct = state.percent;
    text('pct', pct == null ? '—' : Math.round(pct));
    document.getElementById('barFill').style.width = (pct == null ? 0 : pct) + '%';

    text('remaining', clock(state.remaining));
    text('frameNo', state.frame == null ? '—' : pad(state.frame) + ' / ' + pad(state.last));
    text('sample', state.sample == null ? '—' : state.sample + (state.sampleTotal ? ' / ' + state.sampleTotal : ''));
    text('spf', state.secondsPerFrame == null ? '—' : state.secondsPerFrame.toFixed(1) + ' s');
    text('gpu', state.gpu == null ? '—' : Math.round(state.gpu) + ' %');
    text('cpu', state.cpu == null ? '—' : Math.round(state.cpu) + ' %');
    text('ram', state.freeMb == null ? '—' : (state.freeMb / 1024).toFixed(1) + ' GB');

    text('activity', state.activity || (live ? 'rendert' : 'keine Aktivität'));
    text('size', state.width ? state.width + ' × ' + state.height : '');
    text('elapsed', state.elapsed == null ? '' : clock(state.elapsed) + ' vergangen');

    var badge = document.getElementById('badge');
    if (state.frame != null && live) {
      badge.textContent = pad(state.frame);
      badge.hidden = false;
    } else {
      badge.hidden = true;
    }

    // Das Bild nur holen, wenn es ein anderes ist. Sonst laedt ein Handy im Mobilnetz
    // alle zwei Sekunden dasselbe Bild.
    if (state.frameId && state.frameId !== lastFrame) {
      lastFrame = state.frameId;

      var img = document.getElementById('frame');
      var next = new Image();

      next.onload = function () {
        img.src = next.src;
        img.hidden = false;
        document.getElementById('empty').hidden = true;
      };
      next.src = base + 'frame?v=' + encodeURIComponent(state.frameId);
    }
  }

  function poll() {
    fetch(base + 'state', { cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
      .then(show)
      .catch(function () {
        failures++;
        if (failures > 1) {
          document.getElementById('dot').className = 'dot';
          text('scene', 'keine Verbindung');
        }
      });
  }

  poll();

  // Zwei Sekunden, solange die Seite zu sehen ist. Im Hintergrund gar nicht: Ein
  // Handy in der Tasche soll nicht im Takt Daten ziehen.
  var timer = setInterval(function () {
    if (!document.hidden) poll();
  }, 2000);

  document.addEventListener('visibilitychange', function () {
    if (!document.hidden) poll();
  });
})();
</script>
</body>
</html>
""";
}
