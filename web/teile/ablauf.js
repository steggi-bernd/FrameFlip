const Result = {
  Paired: "paired",    // drin, Kanal steht
  Taken: "taken",      // hier sitzt schon jemand
  Alone: "alone",      // Raum da, aber kein FrameFlip
  Blocked: "blocked",  // die Verbindung kam gar nicht erst zustande
  Broken: "broken",    // sie stand und ist abgerissen
  Wrong: "wrong"       // falscher Schluessel - Kennwort oder alter Link
};

/* Jede offene Verbindung dieser Seite.

   Sie werden mitgezaehlt, damit beim Verlassen der Seite wirklich alle zugehen.
   Ein Platz, den niemand mehr benutzt, aber auch niemand freigibt, ist aus Sicht
   des Leuchtturms besetzt - und der naechste Zuschauer steht davor. Auf einem Handy
   ist das der Normalfall und nicht die Ausnahme: Der Reiter wandert in den
   Hintergrund, die Verbindung bleibt und beantwortet brav jeden Ping. */
const live = new Set();

function drop(socket) {
  live.delete(socket);
  try { socket.close(); } catch (e) { /* war schon zu */ }
}

addEventListener("pagehide", () => {
  for (const socket of Array.from(live)) drop(socket);
});

/* Ein Versuch auf einem Platz.

   Aufgeloest wird mit ZWEI Dingen: dem Ausgang - und einem Versprechen, das erst
   faellt, wenn die Verbindung endet. Das zweite ist nicht Beiwerk. Ohne es weiss
   der aeussere Lauf nicht, wann er wieder anfangen soll; er suchte nach kurzer Pause
   munter weiter, waehrend er laengst verbunden war, faende seinen eigenen Platz als
   besetzt vor, naehme den naechsten - und haette nach ein paar Runden alle sechs
   Plaetze selbst belegt. Genau das ist passiert. */
function trySeat(room, seatKey) {
  return new Promise(resolve => {
    // Dasselbe Schema wie die Seite selbst. Steht sie auf https - und im Betrieb
    // tut sie das -, ist es wss. Auf dem eigenen Rechner zum Pruefen ist es ws.
    const scheme = location.protocol === "https:" ? "wss://" : "ws://";

    let socket;

    try {
      socket = new WebSocket(scheme + location.host + "/r/" + room + "?role=client");
    } catch (e) {
      resolve({ outcome: Result.Blocked, closed: Promise.resolve() });
      return;
    }

    socket.binaryType = "arraybuffer";
    live.add(socket);

    let channel = null, mySalt = null, theirSalt = null, confirmed = false;
    let settled = false;

    // Ob die Verbindung ueberhaupt zustande kam. Der Unterschied ist wichtig: Ein
    // Aufbau, der nie gelingt, ist etwas ganz anderes als ein belegter Platz - und
    // er darf niemals nach einem Kennwort fragen lassen.
    let opened = false;

    /* Wie lange der Platz gehalten wird, wenn FrameFlip aus dem Raum verschwindet.

       Kurz sitzenbleiben lohnt sich: Wer neu laedt, kurz das Netz wechselt oder
       FrameFlip neu startet, ist in Sekunden zurueck, und der Zuschauer ist ohne
       Suchen wieder dabei.

       Ewig sitzenbleiben lohnt sich nicht. Ein Raum gilt beim Leuchtturm als belegt,
       solange irgendjemand darin sitzt - auch ein vergessener Reiter auf einem Handy,
       dessen PC seit Tagen aus ist. Nach dieser Frist wird der Platz also
       zurueckgegeben und ganz normal weitergesucht; kommt FrameFlip wieder, ist die
       Seite binnen Sekunden zurueck. */
    const HOLD_WITHOUT_HOST = 20000;

    let waiting = null;

    let ended;
    const closed = new Promise(fall => { ended = fall; });

    const done = outcome => {
      if (settled) return;
      settled = true;

      // Alles ausser dem Platz, den wir behalten wollen, wird SOFORT freigegeben.
      // Ein aufgegebener Versuch, dessen Verbindung offen bleibt, belegt einen Platz
      // fuer niemanden.
      if (outcome !== Result.Paired) drop(socket);

      resolve({ outcome, closed });
    };

    socket.onopen = () => { opened = true; };

    socket.onerror = () => done(opened ? Result.Broken : Result.Blocked);

    /* Nachrichten NACHEINANDER, nicht durcheinander.

       onmessage darf async sein - aber dann ruft der Browser es erneut auf, sobald
       der laufende Aufruf auf das erste await trifft. Und genau das passiert hier:
       Begruessung und Schluesselnachweis kommen dicht hintereinander, der Aufbau des
       Kanals braucht vier await. Trifft der Nachweis in dieses Fenster, gibt es den
       Kanal noch nicht; er faellt durch die Pruefung auf eine Begruessung, wird
       verworfen, und die Verbindung haengt fuer immer - ohne Fehler, ohne Meldung. */
    let pending = Promise.resolve();

    socket.onmessage = event => {
      pending = pending.then(() => handle(event)).catch(() => { /* eine kaputte Nachricht beendet nicht die Leitung */ });
    };

    socket.onclose = () => {
      live.delete(socket);

      if (waiting) { clearTimeout(waiting); waiting = null; }

      // Hinten anstellen, nicht vordraengeln: Der Leuchtturm schickt seine
      // Abweisung als Text und schliesst unmittelbar danach. Wer hier sofort
      // entscheidet, kommt der noch wartenden Meldung zuvor und macht aus
      // "Platz belegt" ein "Schluessel falsch".
      pending = pending.then(() => {
        if (!opened) done(Result.Blocked);
        else {
          done(confirmed ? Result.Broken : Result.Wrong);
          if (confirmed) say("connection lost", "gone");
        }

        ended();
      });
    };

    async function handle(event) {
      // Text gehoert dem Leuchtturm, Binaer der Gegenseite. Der Rahmentyp
      // entscheidet, nie der Inhalt.
      if (typeof event.data === "string") {
        let control;
        try { control = JSON.parse(event.data); } catch (e) { return; }

        if (control.t === "error") {
          // "role already taken" heisst: hier sitzt schon jemand. Alles andere ist
          // eine Abweisung, die ein anderer Platz auch nicht heilt.
          done(/taken|full/i.test(control.why || "") ? Result.Taken : Result.Broken);
          return;
        }

        if (control.t === "waiting") {
          // Wir sind drin, aber FrameFlip nicht. Auf einem der freien Plaetze heisst
          // das: Das Programm laeuft nicht. Auf einem hinteren: Es ist kein Kennwort
          // gesetzt, also gibt es diesen Platz gar nicht.
          done(Result.Alone);
          return;
        }

        if (control.t === "peer" && control.up) {
          // Er ist zurueck - der Platz wird nicht mehr zurueckgegeben.
          if (waiting) { clearTimeout(waiting); waiting = null; }

          mySalt = crypto.getRandomValues(new Uint8Array(SALT_BYTES));

          const hello = new Uint8Array(1 + SALT_BYTES);
          hello[0] = VERSION;
          hello.set(mySalt, 1);

          socket.send(hello);
          return;
        }

        if (control.t === "peer" && !control.up) {
          say("FrameFlip is gone", "gone");
          channel = null;
          confirmed = false;

          /* Den Platz nach einer Frist zurueckgeben.

             Geschlossen wird, nicht aufgegeben: Das Schliessen loest "closed" aus,
             der aeussere Lauf faengt von vorn an, findet den Raum leer und sagt
             genau das - "FrameFlip is unreachable" - waehrend er weiter nachfragt.
             Die Auskunft bleibt also stehen; nur der Raum ist wieder frei. */
          if (!waiting) waiting = setTimeout(() => { waiting = null; drop(socket); }, HOLD_WITHOUT_HOST);
        }

        return;
      }

      const data = new Uint8Array(event.data);

      /* --- 1. Die Begruessung der Gegenseite: offen, Fassung und Salz --- */
      if (!channel) {
        if (data.length !== 1 + SALT_BYTES || data[0] !== VERSION) return;

        theirSalt = data.subarray(1);
        channel = await Channel.establish(seatKey, theirSalt, mySalt);

        socket.send(await channel.seal(await confirmation(seatKey, false, theirSalt, mySalt)));
        return;
      }

      /* --- 2. Ihr Schluesselnachweis. Stimmt er nicht, ist es der falsche Schluessel --- */
      if (!confirmed) {
        const proof = await channel.open(data);
        const expected = await confirmation(seatKey, true, theirSalt, mySalt);

        if (!proof || !sameBytes(proof, expected)) {
          channel = null;
          done(Result.Wrong);
          return;
        }

        confirmed = true;
        done(Result.Paired);
        say("connected", "idle");
        return;
      }

      /* --- 3. Von hier an nur noch Nutzlast --- */
      const payload = await channel.open(data);
      if (!payload || payload.length < 1) return;

      const kind = payload[0];

      if (kind === KIND_JSON) {
        try { show(JSON.parse(new TextDecoder().decode(payload.subarray(1)))); }
        catch (e) { /* eine unleserliche Meldung ist kein Grund aufzuhoeren */ }
        return;
      }

      if (kind === KIND_PREVIEW && payload.length > 5) {
        showPicture(payload.subarray(5));   // 1 Byte Art, 4 Byte Bildnummer
      }
    }
  });
}

