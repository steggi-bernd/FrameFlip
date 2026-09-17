# -*- coding: utf-8 -*-
"""
Baut die Testbilder fuer das Atelier.

Warum eigene statt echter: Die Fehler, um die es geht, haengen an dem, was in den
DURCHSICHTIGEN Stellen einer PNG steht - und das sieht man einer Datei nicht an.
Ein Bild, das aussieht wie freigestellt, kann unter der Deckung beliebigen Muell
tragen; viele Programme schreiben dort, was zufaellig im Puffer stand. Genau das ist
hier absichtlich eingebaut, in mehreren Spielarten, damit sich nachweisen laesst,
welche davon FrameFlip stolpern laesst.

Jede Datei traegt ihren Fall im Namen. Wer eine davon oeffnet und etwas Falsches
sieht, weiss damit schon, welcher Fall es ist.
"""

import os
import struct
import zlib

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "png_ebenen")

W, H = 640, 360


def ziel(name):
    os.makedirs(OUT, exist_ok=True)
    return os.path.join(OUT, name)


def kreis(w=W, h=H, radius=0.32):
    """Eine weiche runde Deckung - die uebliche Form einer freigestellten Ebene."""
    y, x = np.mgrid[0:h, 0:w]

    dx = (x - w / 2) / (w / 2)
    dy = (y - h / 2) / (w / 2)

    d = np.sqrt(dx * dx + dy * dy)

    # Weicher Rand ueber zwei Hundertstel - schmal genug, dass ein harter Schnitt
    # auffiele, breit genug, dass Zwischenwerte vorkommen.
    return np.clip((radius - d) / 0.02, 0.0, 1.0).astype(np.float32)


def motiv(w=W, h=H):
    """Ein Motiv mit Struktur: Verlauf, Kanten, Farbe."""
    y, x = np.mgrid[0:h, 0:w]

    r = (x / w * 255).astype(np.uint8)
    g = (y / h * 255).astype(np.uint8)
    b = (((x // 40 + y // 40) % 2) * 180 + 40).astype(np.uint8)

    return np.dstack([r, g, b])


def schreibe(name, rgb, alpha=None, mode="RGBA", extra=None):
    if alpha is None:
        bild = Image.fromarray(rgb.astype(np.uint8), "RGB")
    else:
        rgba = np.dstack([rgb.astype(np.uint8), (alpha * 255).astype(np.uint8)])
        bild = Image.fromarray(rgba, "RGBA")

        if mode != "RGBA":
            bild = bild.convert(mode)

    bild.save(ziel(name), **(extra or {}))
    print("  ", name)


def sechzehn(pfad, rgba):
    """
    Schreibt eine PNG mit sechzehn Bit je Kanal - Farbtyp 6, ohne Umwege.

    Von Hand, weil die Bildbibliothek RGBA mit sechzehn Bit nicht ausgibt. Der Fall
    ist trotzdem einer, den Blender und Photoshop beide erzeugen.
    """
    h, w, _ = rgba.shape

    # Jede Zeile faengt mit ihrem Filterbyte an; null heisst "unveraendert".
    keiner = bytes([0])
    roh = b"".join(keiner + rgba[y].astype(">u2").tobytes() for y in range(h))

    def brocken(art, daten):
        return (struct.pack(">I", len(daten)) + art + daten +
                struct.pack(">I", zlib.crc32(art + daten) & 0xFFFFFFFF))

    with open(pfad, "wb") as datei:
        datei.write(bytes([137, 80, 78, 71, 13, 10, 26, 10]))
        datei.write(brocken(b"IHDR", struct.pack(">IIBBBBB", w, h, 16, 6, 0, 0, 0)))
        datei.write(brocken(b"IDAT", zlib.compress(roh, 6)))
        datei.write(brocken(b"IEND", b""))


def main():
    rgb = motiv()
    a = kreis()

    rauschen = np.random.default_rng(7).integers(0, 256, rgb.shape, dtype=np.uint8)

    # 1. Der saubere Fall: unter der Deckung steht dieselbe Farbe wie daneben.
    #    So sollte eine freigestellte Ebene aussehen, und so sieht sie selten aus.
    schreibe("01_freigestellt_sauber.png", rgb, a)

    # 2. Der haeufige Fall: unter der Deckung steht Rauschen. Genau das waere der
    #    "pixelige Nebel", wenn ein Programm die Deckung nicht beachtet.
    laut = rgb.copy()
    laut[a < 0.5] = rauschen[a < 0.5]
    schreibe("02_freigestellt_muell_unter_deckung.png", laut, a)

    # 3. Derselbe Fall in Schwarz - unauffaelliger und deshalb heimtueckischer.
    schwarz = rgb.copy()
    schwarz[a < 0.5] = 0
    schreibe("03_freigestellt_schwarz_unter_deckung.png", schwarz, a)

    # 4. Vormultipliziert: Die Farbe ist bereits mit der Deckung verrechnet. Wer das
    #    ein zweites Mal tut, bekommt einen zu dunklen Rand.
    vor = (rgb * a[:, :, None]).astype(np.uint8)
    schreibe("04_freigestellt_vormultipliziert.png", vor, a)

    # 5. Harte Kante statt weicher - zeigt Treppen, wenn falsch abgetastet wird.
    hart = (kreis() > 0.5).astype(np.float32)
    schreibe("05_freigestellt_harte_kante.png", laut, hart)

    # 6. Ganz undurchsichtig, aber MIT Alphakanal. Ein Leser, der den Kanal falsch
    #    behandelt, zeigt hier nichts oder alles.
    schreibe("06_deckend_mit_alphakanal.png", rgb, np.ones((H, W), np.float32))

    # 7. Ohne Alphakanal.
    schreibe("07_deckend_ohne_alphakanal.png", rgb)

    # 8. Graustufen mit Deckung - ein anderer Farbtyp im Dateikopf.
    grau = np.dstack([rgb[:, :, 1]] * 3)
    schreibe("08_graustufen_mit_deckung.png", grau, a, mode="LA")

    # 9. Palette mit einer durchsichtigen Farbe - wieder ein anderer Farbtyp.
    palette = Image.fromarray(rgb.astype(np.uint8), "RGB").convert(
        "P", palette=Image.ADAPTIVE, colors=255)
    palette.save(ziel("09_palette_mit_transparenz.png"), transparency=255)
    print("   09_palette_mit_transparenz.png")

    # 10. Sechzehn Bit je Kanal mit Deckung.
    tief = np.dstack([
        rgb[:, :, 0].astype(np.uint16) * 257,
        rgb[:, :, 1].astype(np.uint16) * 257,
        rgb[:, :, 2].astype(np.uint16) * 257,
        (a * 65535).astype(np.uint16),
    ])
    sechzehn(ziel("10_sechzehn_bit_mit_deckung.png"), tief)
    print("   10_sechzehn_bit_mit_deckung.png")

    # 11. Verschraenkt gespeichert (Adam7) - manche Leser koennen das nicht.
    Image.fromarray(np.dstack([laut, (a * 255).astype(np.uint8)]), "RGBA").save(
        ziel("11_verschraenkt.png"), interlace=1)
    print("   11_verschraenkt.png")

    # 12. Andere Groesse als das Bild: eine Ebene, die platziert werden muss.
    Image.fromarray(np.dstack([laut, (a * 255).astype(np.uint8)]), "RGBA") \
        .resize((220, 124)).save(ziel("12_kleiner_als_das_bild.png"))
    print("   12_kleiner_als_das_bild.png")

    # 13. Ein Wasserzeichen: schmal, fast leer, mit weicher Kante. Der Fall, in dem
    #     ein Fehler unter der Deckung am staerksten auffaellt.
    zeichen = np.zeros((H, W, 3), np.uint8)
    zeichen[:, :] = (255, 230, 120)

    marke = np.zeros((H, W), np.float32)
    marke[H - 90:H - 40, W - 260:W - 40] = 1.0

    krach = np.random.default_rng(3).integers(0, 256, zeichen.shape, dtype=np.uint8)
    zeichen[marke < 0.5] = krach[marke < 0.5]

    schreibe("13_wasserzeichen_muell_unter_deckung.png", zeichen, marke)

    # 14. Ein Grund-Bild ohne Deckung, auf das sich alles legen laesst.
    grund = np.dstack([
        np.full((H, W), 40, np.uint8),
        np.full((H, W), 60, np.uint8),
        np.full((H, W), 90, np.uint8),
    ])
    y, x = np.mgrid[0:H, 0:W]
    grund[(x // 80 + y // 80) % 2 == 0] = (70, 95, 130)

    schreibe("00_grundbild.png", grund)


if __name__ == "__main__":
    print("Testbilder:")
    main()
    print("in", os.path.normpath(OUT))
