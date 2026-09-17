# -*- coding: utf-8 -*-
"""
Befragt eine PNG, ohne sie zu zeigen - und baut auf Wunsch einen unverfaenglichen
Zwilling davon.

Wozu: Ein Fehler, der nur an einer bestimmten Datei auftritt, laesst sich ohne diese
Datei nicht finden. Wenn die Datei nicht aus dem Haus darf, muss statt ihres INHALTS
ihre BAUART hinaus - und die besteht aus Zahlen, nicht aus Bildern.

  befragen   Sagt, was in der Datei steckt: Farbtyp, Bittiefe, wie die Deckung
             verteilt ist, was unter der Deckung steht, wie stark es rauscht. Alles
             Zahlen. Wer sie liest, sieht kein Bild.

  zwilling   Baut eine Datei mit derselben Groesse, derselben Deckung und demselben
             Inhalt UNTER der Deckung - aber mit einer flaechigen Farbe da, wo etwas
             zu erkennen waere. Der Zwilling zeigt eine Silhouette und sonst nichts,
             reproduziert aber genau die Eigenschaften, an denen die vermuteten
             Fehler haengen.

Aufruf:

    python png_befragen.py befragen  DATEI [DATEI ...]
    python png_befragen.py zwilling  DATEI [ZIEL]
"""

import os
import sys
import struct
import zlib

import numpy as np
from PIL import Image


# ----------------------------------------------------------------------- Lesen

def kopf(path):
    """Farbtyp und Bittiefe aus dem Dateikopf - ohne die Bildbibliothek."""
    with open(path, "rb") as datei:
        datei.read(8)
        laenge = struct.unpack(">I", datei.read(4))[0]
        art = datei.read(4)

        if art != b"IHDR":
            return None

        w, h, tiefe, farbe, _, _, verschraenkt = struct.unpack(">IIBBBBB", datei.read(13))

    arten = {
        0: "Graustufen", 2: "RGB", 3: "Palette",
        4: "Graustufen + Deckung", 6: "RGB + Deckung",
    }

    return {
        "breite": w, "hoehe": h, "bittiefe": tiefe,
        "farbtyp": arten.get(farbe, str(farbe)),
        "verschraenkt": bool(verschraenkt),
    }


def rauschen(werte):
    """
    Wie stark benachbarte Punkte auseinanderliegen - das Mass fuer Korn.

    Waagerecht gemessen und nicht als Streuung ueber die Flaeche: Ein Verlauf hat
    eine grosse Streuung und kein Rauschen. Nachbarn dagegen liegen in einem Verlauf
    dicht beieinander und in einem Rauschen nicht.
    """
    if werte.size < 4:
        return 0.0

    return float(np.mean(np.abs(np.diff(werte.astype(np.int16), axis=1))))


def nachbarn_unter(rgb, a):
    """
    Der mittlere Abstand zwischen waagerechten Nachbarn - aber nur dort, wo BEIDE
    ganz durchsichtig sind.

    Nur dort sagt er etwas ueber das, was unter der Deckung liegt. Ein Paar, von dem
    einer zum Motiv gehoert, misst die Kante und nicht den Inhalt.
    """
    leer = a == 0
    beide = leer[:, :-1] & leer[:, 1:]

    if not beide.any():
        return 0.0

    links = rgb[:, :-1, :].astype(np.int16)
    rechts = rgb[:, 1:, :].astype(np.int16)

    abstand = np.abs(rechts - links).mean(axis=2)

    return float(abstand[beide].mean())


def befragen(path):
    print("")
    print(os.path.basename(path))
    print("-" * len(os.path.basename(path)))

    info = kopf(path)

    if info is None:
        print("  keine lesbare PNG")
        return

    print("  {breite} x {hoehe}, {bittiefe} Bit, {farbtyp}{v}".format(
        v=", verschraenkt" if info["verschraenkt"] else "", **info))

    bild = Image.open(path).convert("RGBA")
    feld = np.array(bild)

    rgb = feld[:, :, :3]
    a = feld[:, :, 3]

    punkte = a.size
    leer = int(np.count_nonzero(a == 0))
    voll = int(np.count_nonzero(a == 255))
    rand = punkte - leer - voll

    print("  Deckung:  {:5.1f} % ganz durchsichtig, {:5.1f} % ganz deckend, "
          "{:5.1f} % dazwischen".format(100 * leer / punkte, 100 * voll / punkte,
                                        100 * rand / punkte))

    if leer == 0:
        print("  -> Diese Datei ist NICHT freigestellt. Sie deckt ueberall.")
    if leer > 0:
        unter = rgb[a == 0]
        mittel = unter.mean(axis=0)

        # Nachbarn statt Streuung: Ein Verlauf, der unter der Deckung weiterlaeuft,
        # hat eine grosse Streuung und ist harmlos. Rauschen erkennt man daran, dass
        # NEBENEINANDERLIEGENDE Punkte weit auseinanderliegen.
        koerner = nachbarn_unter(rgb, a)

        print("  unter der Deckung:  Mittel {:.0f}/{:.0f}/{:.0f}, "
              "Nachbarabstand {:.1f}".format(mittel[0], mittel[1], mittel[2], koerner))

        if koerner > 8:
            print("  -> dort steht MUELL. Ein Programm, das die Deckung uebergeht,")
            print("     zeigt genau diesen Muell als pixeligen Nebel.")
        elif mittel.max() < 2:
            print("  -> sauber: dort steht Schwarz.")
        else:
            print("  -> sauber: dort laeuft das Motiv glatt weiter.")

    # Das Dunkle: Wo ein Glanz- oder Schleierbild nahe null liegt, entscheidet sich,
    # ob beim Aufhellen Rauschen hochkommt.
    hell = rgb.max(axis=2)
    dunkel = (hell > 0) & (hell < 24) & (a > 128)

    anteil = np.count_nonzero(dunkel) / punkte

    print("  dunkle Flaeche (unter 24 von 255, aber nicht null): {:.1f} %".format(100 * anteil))

    if anteil > 0.02:
        maske = np.zeros(rgb.shape[:2], bool)
        maske[dunkel] = True

        zeilen = np.where(maske.any(axis=1))[0]

        if zeilen.size > 0:
            aus = rgb[zeilen[0]:zeilen[-1] + 1, :, 0]
            print("  Rauschen in der dunklen Flaeche: {:.2f} Stufen zwischen Nachbarn".format(
                rauschen(aus)))
            print("  -> Wird so eine Ebene auf Negativ multiplizieren (Screen) oder")
            print("     Addieren gestellt, kommt dieses Rauschen ungedaempft ins Bild.")
            print("     Auf einem dunklen Untergrund ist es das Einzige, was man dort sieht.")

    print("  Gesamtrauschen im Rotkanal: {:.2f} Stufen zwischen Nachbarn".format(
        rauschen(rgb[:, :, 0])))


# --------------------------------------------------------------------- Zwilling

def zwilling(path, ziel=None):
    """
    Dieselbe Bauart, nichts zu erkennen.

    Behalten wird die Deckung Punkt fuer Punkt und die Farbe DORT, WO NICHTS ZU SEHEN
    IST - also unter der Deckung und in den sehr dunklen Stellen. Das ist genau das,
    woran die vermuteten Fehler haengen. Ersetzt wird, was ein Motiv ergaebe.
    """
    bild = Image.open(path).convert("RGBA")
    feld = np.array(bild).astype(np.int16)

    rgb = feld[:, :, :3]
    a = feld[:, :, 3]

    h, w = a.shape

    # Eine flaechige Ersatzfarbe mit einem leichten Verlauf, damit sich Kanten und
    # Platzierung noch beurteilen lassen.
    y, x = np.mgrid[0:h, 0:w]

    ersatz = np.dstack([
        (60 + 120 * x / max(1, w - 1)),
        (90 + 60 * y / max(1, h - 1)),
        np.full((h, w), 150.0),
    ])

    sichtbar = (a > 8) & (rgb.max(axis=2) >= 24)

    neu = rgb.copy()
    neu[sichtbar] = ersatz[sichtbar]

    aus = np.dstack([neu.astype(np.uint8), a.astype(np.uint8)])

    ziel = ziel or os.path.splitext(path)[0] + "_zwilling.png"
    Image.fromarray(aus, "RGBA").save(ziel)

    print("Zwilling geschrieben:", ziel)
    print("Er hat dieselbe Groesse, dieselbe Deckung und dasselbe, was unter der")
    print("Deckung und in den dunklen Stellen steht. Zu sehen ist eine Silhouette.")


# ------------------------------------------------------------------------ Start

def main(argv):
    if len(argv) < 3 or argv[1] not in ("befragen", "zwilling"):
        print(__doc__)
        return 1

    if argv[1] == "befragen":
        for path in argv[2:]:
            befragen(path)

        return 0

    zwilling(argv[2], argv[3] if len(argv) > 3 else None)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
