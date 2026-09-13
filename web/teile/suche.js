const ask = el("ask"), askForm = el("askForm"), askInput = el("askInput"), askWrong = el("askWrong");

function askForCode(message) {
  return new Promise(resolve => {
    // Erst leeren, dann fragen - sonst steht hinter der Abfrage noch, was eine
    // fruehere Verbindung geliefert hat.
    blank();

    askWrong.textContent = message || "";
    askInput.value = "";
    ask.classList.add("on");
    setTimeout(() => askInput.focus(), 50);

    askForm.onsubmit = event => {
      event.preventDefault();

      const typed = askInput.value.trim();
      if (!typed) return;

      ask.classList.remove("on");
      resolve(typed);
    };
  });
}

async function run() {
  const text = secretFromAddress();

  if (!text) {
    say("link incomplete", "gone");
    empty.textContent = "This link carries no secret. Create a new one on the PC and copy all of it.";
    return;
  }

  let secret;
  try {
    secret = fromBase64Url(text);
    if (secret.length !== 32) throw new Error("Länge");
  } catch (e) {
    say("link unusable", "gone");
    empty.textContent = "There is nothing to do with this link.";
    return;
  }

  const rooms = [];
  for (let seat = 0; seat < SEATS; seat++) {
    rooms.push(toHex(await hkdf(secret, ROOM_INFO, new Uint8Array([seat]), 16)));
  }

  const freeKey = await hkdf(secret, SEAT_INFO, new Uint8Array(0), 32);

  let code = rememberedCode();
  let codeKey = code ? await hkdf(secret, SEAT_INFO, enc.encode(code), 32) : null;
  let wait = 2000;

  for (;;) {
    let landed = false;

    // Wieviele Plaetze besetzt waren - getrennt nach frei und kennwortgeschuetzt.
    // Nur volle FREIE Plaetze rechtfertigen die Frage nach dem Kennwort. Frueher
    // fiel jeder andere Ausgang stillschweigend bis dorthin durch, und ein Browser,
    // der die Verbindung gar nicht zulaesst, sah damit aus wie ein volles Haus.
    let besetztFrei = 0, besetztGesamt = 0, versucht = 0;

    for (let seat = 0; seat < SEATS; seat++) {
      const protectedSeat = seat >= FREE_SEATS;

      if (protectedSeat && !codeKey) break;

      say(seat === 0 ? "connecting …" : "looking for a free seat …");

      versucht++;

      const { outcome, closed } = await trySeat(rooms[seat], protectedSeat ? codeKey : freeKey);

      if (outcome === Result.Paired) {
        landed = true;
        wait = 2000;

        // HIER BLEIBEN, bis die Leitung endet.
        //
        // Ohne dieses Warten lief der aeussere Lauf sofort weiter, suchte nach
        // kurzer Pause erneut ab Platz 0, fand seinen eigenen Platz besetzt, nahm
        // den naechsten - und hatte nach ein paar Runden alle sechs Plaetze selbst
        // belegt. Fuer jeden anderen war dann kein Platz mehr frei.
        await closed;
        break;
      }

      if (outcome === Result.Taken) {
        besetztGesamt++;
        if (!protectedSeat) besetztFrei++;
        continue;
      }

      if (outcome === Result.Blocked) {
        say("cannot connect", "gone");
        empty.textContent = "The browser will not allow the connection to the relay, "
                          + "or the relay is unreachable right now. Still trying.";
        landed = true;
        break;
      }

      if (outcome === Result.Alone) {
        if (!protectedSeat) {
          say("FrameFlip is unreachable", "gone");
          empty.textContent = "FrameFlip is not running, or watching is switched off there.";
        } else {
          // Ein hinterer Platz ohne FrameFlip heisst: Es ist kein Kennwort gesetzt.
          say("no seat free", "gone");
          empty.textContent = "Two people are already watching. More is only possible "
                            + "once a password is set on the PC.";
        }

        landed = true;   // Warten statt weitersuchen - die anderen Plaetze sind auch leer.
        break;
      }

      if (outcome === Result.Wrong && protectedSeat) {
        rememberCode("");
        code = await askForCode("That was not the right password.");
        rememberCode(code);
        codeKey = await hkdf(secret, SEAT_INFO, enc.encode(code), 32);
        seat = FREE_SEATS - 1;   // die geschuetzten Plaetze noch einmal durchgehen
        continue;
      }

      if (outcome === Result.Wrong) {
        say("link no longer valid", "gone");
        empty.textContent = "This link has been renewed. The current one is shown on the PC.";
        landed = true;
        break;
      }

      // Bleibt Result.Broken: Die Verbindung stand und riss ab. Nicht den naechsten
      // Platz belegen - abwarten und denselben noch einmal versuchen.
      say("connection lost", "gone");
      landed = true;
      break;
    }

    if (!landed) {
      // Alle freien Plaetze besetzt und noch kein Kennwort: dann eben danach fragen.
      if (!codeKey && besetztFrei >= FREE_SEATS) {
        code = await askForCode("");
        rememberCode(code);
        codeKey = await hkdf(secret, SEAT_INFO, enc.encode(code), 32);
        continue;
      }

      // Wirklich alles voll. Das laut sagen, statt still weiterzusuchen - sonst
      // steht man vor einer Seite, die ohne Grund nichts anzeigt.
      if (besetztGesamt >= versucht && versucht > 0) {
        say("no seat free", "gone");
        empty.textContent = "All " + versucht + " seats are taken right now. "
                          + "This page continues on its own as soon as one frees up.";
      }
    }

    say("retrying …", "gone");
    await new Promise(done => setTimeout(done, wait));
    wait = Math.min(wait * 2, 30000);
  }
}

