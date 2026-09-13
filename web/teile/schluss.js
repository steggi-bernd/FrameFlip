/* --------------------------------------------------------------- Start */

/* Ohne WebCrypto geht hier gar nichts - und das soll man sehen.

   crypto.subtle gibt es nur in einem sicheren Zusammenhang: https, oder der eigene
   Rechner. Wird die Seite je ueber http von einer fremden Adresse ausgeliefert,
   fehlt sie, und jeder Aufruf darauf endet in einem Fehler, den niemand deuten kann.
   Eine klare Zeile ist die bessere Auskunft als eine Seite, die stumm nichts tut. */
if (!globalThis.crypto || !crypto.subtle) {
  say("not a secure context", "gone");
  empty.textContent = "This page needs HTTPS. Without it the browser withholds the "
                    + "cryptography, and nothing here can work.";
} else {
  run();
}

/* ------------------------------------------------------------ Installierbar

   Der Dienstarbeiter ist nicht dafuer da, Daten vorzuhalten - es gibt hier nichts
   vorzuhalten, was laenger als ein paar Sekunden gilt. Er haelt die Huelle bereit,
   damit sich die Seite als Programm ablegen laesst. Alles Lebendige kommt
   weiterhin ueber die verschluesselte Leitung.

   Scheitert die Anmeldung, steht der Grund in der Fusszeile. Das ist nicht
   Zierde: Ohne diese Zeile bleibt "das Installieren wird nicht angeboten" eine
   Beobachtung ohne Ursache, und auf einem Handy laesst sich keine Entwicklerkonsole
   oeffnen, um nachzusehen. */
const appnote = document.createElement("span");
appnote.style.cssText = "flex-basis:100%;color:var(--faint)";
document.querySelector(".note").appendChild(appnote);

function vermerk(text) { appnote.textContent = text; }

// Auf einem iPhone gibt es kein beforeinstallprompt - dort fuehrt der Weg ueber
// "Teilen" und "Zum Home-Bildschirm". Ein Knopf, der nichts bewirkt, waere eine
// Enttaeuschung; der Hinweis ist die ehrlichere Auskunft.
const apfel = /iPad|iPhone|iPod/.test(navigator.userAgent)
  || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);

if (apfel && !navigator.standalone) vermerk("To install: Share → Add to Home Screen");

if (!("serviceWorker" in navigator)) {
  if (!apfel) vermerk("App install unavailable: this browser has no service workers.");
} else {
  addEventListener("load", () => {
    navigator.serviceWorker.register("sw.js")
      .then(() => { if (!apfel) vermerk(""); })
      .catch(error => vermerk("App install unavailable: " + (error && error.message ? error.message : error)));
  });
}
</script>
</body>
</html>
