"""Setzt index.html aus ihren Teilen zusammen - samt Pruefsummen fuer die Inhaltsregel.

Die Seite ist EINE Datei, damit sie sich durch Lesen pruefen laesst. Geschrieben wird
sie in Stuecken, weil die drei geprueften Abschnitte - Krypto, Verbindungsablauf,
Platzsuche - unveraendert uebernommen und nicht bei jeder Gestaltungsaenderung neu
getippt werden sollen.

Der eigentliche Grund fuer dieses Skript sind aber die Pruefsummen. Die Inhaltsregel
der Seite erlaubt kein 'unsafe-inline' mehr, sondern genau den einen Skriptblock und
den einen Stilblock, die hier drinstehen - benannt ueber ihre SHA-256. Von Hand ginge
das nicht: Jede Aenderung an Stil oder Code aendert die Summe, und eine falsche Summe
heisst leere Seite. Also rechnet sie, wer die Datei zusammensetzt.

    python bauen.py            # baut web/index.html neu
    node pruefstand/regel.mjs index.html sw.js    # und prueft, dass sie stimmt
"""

import base64
import hashlib
import io
import os
import re
import sys

HIER = os.path.dirname(os.path.abspath(__file__))
TEILE = os.environ.get("FRAMEFLIP_TEILE", os.path.join(HIER, "teile"))

REIHE = [
    "kopf.html",      # Kopfzeilen, Gestaltung, Markup
    "krypto.js",      # geprueft - unveraendert uebernehmen
    "anzeige.js",     # Geheimnis, Anzeige, Ansichten, Sichern
    "ablauf.js",      # geprueft - unveraendert uebernehmen
    "suche.js",       # geprueft - unveraendert uebernehmen
    "schluss.js",     # Start und Dienstarbeiter
]

TRENNER = "/* --------------------------------------------------------------- Platz suchen */\n\n"


def lies(pfad):
    """Immer mit \\n, nie mit \\r\\n.

    Ohne das uebersetzt Python unter Windows beim Schreiben jedes \\n in \\r\\n. Die
    Pruefsumme waere dann ueber einen anderen Text gebildet als den, der auf der
    Platte landet, der Browser wiese das Skript ab, und die Seite bliebe leer - man
    suchte den Fehler im Code statt in den Zeilenenden.
    """
    return io.open(pfad, encoding="utf-8", newline="").read().replace("\r\n", "\n")


def summe(inhalt):
    """Die Form, die eine Inhaltsregel erwartet: 'sha256-<base64>'."""
    roh = hashlib.sha256(inhalt.encode("utf-8")).digest()
    return "'sha256-" + base64.b64encode(roh).decode("ascii") + "'"


def block(text, tag):
    """Der Inhalt zwischen <tag ...> und </tag> - genau das, was gehasht wird."""
    treffer = re.search(r"<" + tag + r"[^>]*>(.*?)</" + tag + r">", text, re.S)

    if not treffer:
        sys.exit("kein <%s>-Block gefunden" % tag)

    return treffer.group(1)


def main():
    stuecke = []

    for name in REIHE:
        pfad = os.path.join(TEILE, name)

        if not os.path.exists(pfad):
            sys.exit("fehlt: " + pfad)

        stuecke.append(lies(pfad))

        # Die Platzsuche traegt ihre Ueberschrift nicht selbst - sie ist die Marke,
        # an der der Pruefstand schneidet.
        if name == "ablauf.js":
            stuecke.append(TRENNER)

    ganz = "".join(stuecke)

    ganz = ganz.replace("__STYLE_HASH__", summe(block(ganz, "style")))
    ganz = ganz.replace("__SCRIPT_HASH__", summe(block(ganz, "script")))

    # Genau die Marken, an denen der Pruefstand die Seite zerschneidet. Kommt eine
    # doppelt oder gar nicht vor, schneidet er falsch - und prueft dann etwas
    # anderes als das, was ausgeliefert wird.
    marken = ["const SEATS", "const Result = {", "const ask = el(",
              "/* --------------------------------------------------------------- Start */"]

    for marke in marken:
        if ganz.count(marke) != 1:
            sys.exit("Marke %r kommt %dx vor - der Pruefstand schneidet danach"
                     % (marke, ganz.count(marke)))

    if "__STYLE_HASH__" in ganz or "__SCRIPT_HASH__" in ganz:
        sys.exit("eine Pruefsumme blieb unersetzt")

    ziel = os.path.join(HIER, "index.html")
    io.open(ziel, "w", encoding="utf-8", newline="").write(ganz)

    print("index.html: %d Zeichen" % len(ganz))


if __name__ == "__main__":
    main()
