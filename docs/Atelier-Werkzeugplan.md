# Atelier – Werkzeugplan

Stand: 26. September 2026. Aus dem [Werkzeugkatalog](Atelier-Werkzeugkatalog.md) wird hier
ein Plan: was gebaut wird, in welcher Reihenfolge, und wie die Oberfläche das alles
aufnimmt.

## 1. Entscheidungen

Getroffen am 26. September 2026:

1. **Gebaut werden die Gruppen des Katalogs vollständig:**
   - Pinsel und Masken,
   - Auswahl ohne KI,
   - Tonwert und Histogramm,
   - Farbe,
   - Glitch,
   - Dither,
   - Weichzeichnen, Schärfe und Retusche,
   - Licht aus Renderpässen,
   - Folge und Zeit,
   - Arbeitsablauf.
2. **Hochskalieren** kommt später.
3. **KI-Werkzeuge** (Segmentierung per Klick, Hintergrund entfernen, Tiefe schätzen) nur,
   wenn Last, Größe und Lizenz vertretbar sind. Die Einschätzung steht in Abschnitt 5.
4. **Die Reihenfolge** folgt der Empfehlung aus dem Katalog.
5. **Die Oberfläche wird umgebaut**, damit viele Werkzeuge auffindbar bleiben: eine
   Werkzeugleiste und ein Kontext, der ausblendet, was für eine Folge nicht taugt
   (Abschnitt 3).

## 2. Reihenfolge

Jede Phase ist ein eigener Zweig mit eigener PR. Zuerst kommen die Pinsel, weil sie im
Pinselbereich leben und vom Umbau nicht abhängen. Dann folgt der Umbau, damit alle
weiteren Werkzeuge gleich an ihren Platz kommen.

| Phase | Inhalt | Warum hier |
|---|---|---|
| **W1 Pinsel** | quadratisch und rechteckig, drehbar, Winkel folgt dem Strich, objektgebunden (Kryptomatte), kantengebunden (Pässe), Druckstärke, gerade Linien, Rechteck, Ellipse und Lasso, Maske bearbeiten (füllen, leeren, umkehren, weiche Kante, ausweiten, schrumpfen), Kanten verfeinern, Stempelpinsel | Wunsch des Nutzers, klein, vom Umbau unabhängig. Der Strich muss für den Maskenverlauf exakt nachspielbar bleiben. |
| **U Oberfläche** | Werkzeugleiste, Kontext Folge/Einzelbild, Suche, Werkzeugbeschreibung als Daten | Die Grundlage für alle weiteren Werkzeuge. |
| **W2 Tonwert** | Tonwert am Histogramm, Auto-Tonwert, Pipetten, Kurvenpunkt aus dem Bild, Clipping und Falschfarben, Messgeräte (Waveform, Parade, Vektorskop), Ausgleich und CLAHE, Farbe angleichen, Deflicker, Tonwerttrennung nach Quantilen | Größter Nutzen beim Graden. |
| **W3 Auswahl** | Kryptomatte ausbauen, Auswahl aus Tiefe, Normale und Bewegung, Zauberstab, Maske über die Folge tragen | Baut auf W1 und den Pässen auf. |
| **W4 Retusche** | Glühwürmchen entfernen, Weichzeichner, kantenerhaltendes Glätten, Entrauschen, Stempel und Reparatur, inhaltsbasiertes Füllen | Glühwürmchen zuerst, klein und ständig gebraucht. |
| **W5 Licht** | Nebel aus Tiefe, Konturen, mehr Licht, Umgebungsverdeckung, Lichtsaum, Strahlen und Streifen, Himmel tauschen | Die Stärke der Pässe. |
| **W6 Dither** | blaues Rauschen, Vorformung, mehr Matrizen, weitere Fehlerstreuungen, Streustärke, Ruhe über die Folge | Viel Look für wenig Arbeit. |
| **W7 Farbe** | Paletten (auch für Dither), Verlaufsumsetzung, Kanalmixer, getrennte Tönung, selektive Farbe, LUT-Export | Paletten sind ein neues Datenmodell, deshalb nach dem schnellen Dither-Teil. |
| **W8 Glitch** | Pixel Sort mit Pässen, Schmieren, Kanaltrennung, Bitcrush, JPEG, Röhre und VHS, Mosaik, Kristall und Halbton, Zeichenraster, Glitch an Objektkanten, Datamosh | Datamosh braucht das Rechnen in Reihenfolge aus W9. |
| **W9 Zeit** | Schlüsselbilder für Regler, Echo, Slit-Scan, Zwischenbilder | Das große Stück, braucht eine Zeitleiste. |
| **W10 Ablauf** | Vergleichsansicht, Looks und Vorlagen, Schnappschüsse | Klein, kann zwischen die anderen rutschen. |
| **K KI** | Segmentierung, Hintergrund, Tiefe, als optionaler Download | Nur, wenn Abschnitt 5 hält. |
| später | Hochskalieren, Maskenraster auslagern | Entscheidung 2 und Projekte-und-Masken, Entscheidung 7. |

## 3. Die Oberfläche

### 3.1 Werkzeugleiste

Neben „Bild öffnen“ steht eine waagerechte Leiste mit den Kategorien:

- Auswahl, Malen, Tonwert, Farbe, Details, Optik, Licht, Glitch, Zeit, Retusche.

Ein Klick öffnet ein Fach mit den Werkzeugen dieser Kategorie, jedes mit Zeichen, Namen
und einem Satz. Die Einstellungen des gewählten Werkzeugs stehen rechts, wo heute der
Farbstreifen steht. Eine Suche mit Strg+K findet jedes Werkzeug beim Namen, so wie der
Hub im Knotenmodus.

Die senkrechte Spalte links bleibt für das, was die **Maus** tut: verschieben, wählen,
zuschneiden, Hand, Pinsel, Pipette, Knoten. Die neue Leiste ist für das, was dem **Bild**
geschieht. Diese Trennung gibt es heute schon unausgesprochen, jetzt wird sie sichtbar.

### 3.2 Folge oder Einzelbild

Jedes Werkzeug trägt ein **Zeitverhalten**. Es entscheidet, wo das Werkzeug erscheint:

| Zeitverhalten | Bedeutung | Beispiele |
|---|---|---|
| **passt sich an** | Rechnet je Bild neu aus dem Bild oder seinen Pässen. Auf einer Folge immer richtig. | alle Farb- und Tonwerkzeuge, Kryptomatte, Masken aus Helligkeit oder Pass, Nebel, Konturen, Licht, Dither, Glitch mit Geschwindigkeit |
| **steht still** | Eine feste Lage im Bild, gleich auf jedem Bild der Folge. Auf einer Folge nur sinnvoll, wenn sich im Bild nichts bewegt. | gemalte Maske (gesperrt), Rechteck- und Lassomaske, Himmel tauschen, Überlagerung |
| **je Bild von Hand** | Gilt nur für das Bild, auf dem es entstand. | entsperrte gemalte Maske, Stempel, Reparatur, inhaltsbasiertes Füllen, KI-Segmentierung per Klick |
| **über die Folge getragen** | Entsteht auf einem Bild und wird mit dem Bewegungspass oder Schlüsselbildern weitergegeben. | Maske über die Folge tragen, Schlüsselbilder |

Ist eine **Folge** offen, zeigt die Leiste zuerst, was sich anpasst. Werkzeuge der Art
„je Bild von Hand“ stehen gedämpft in einem eigenen Abschnitt „Einzelbild“, mit dem
Hinweis, dass sie nur dieses Bild ändern. Sie bleiben erreichbar, weil ein Fehler in
einem einzelnen Bild der Folge genau so ein Werkzeug braucht. Ist ein **Einzelbild**
offen, gibt es keine Unterscheidung.

Dafür bekommt jedes Werkzeug eine Beschreibung als Daten: Kategorie, Art im
Knotensystem, Zeitverhalten, Zeichen und Suchwörter. Die Leiste, die Suche, der Hub im
Knotenmodus und die Einstellungen lesen alle aus derselben Liste.

## 4. Pinsel (W1) im Einzelnen

- **Form:** rund, quadratisch, rechteckig (Seitenverhältnis). Der Abstand zur Kante wird
  im gedrehten Rechteck gemessen, Härte wie beim runden Pinsel.
- **Winkel:** fest (Umschalt + Mausrad, sichtbar am Pinselring) oder „folgt dem Strich“:
  Der Winkel jedes Tupfers ergibt sich aus der Richtung des Weges.
- **Objektgebunden:** Beim Ansetzen wird die Kryptomatte-Kennung unter dem Pinsel gelesen.
  Jeder Tupfer wirkt nur, so weit das Objekt deckt.
- **Nachspielbar:** Form, Winkel, Seitenverhältnis, Winkelmodus, Objektkennung und
  Druck stehen im `PaintStroke`. Alte Striche lesen sich als rund und ungebunden. Die
  Probe „Nachspielen ist Malen“ muss für jede Form und jeden Modus Byte für Byte halten.

## 5. KI-Werkzeuge: Last, Größe, Lizenz

**Laufzeit.** ONNX Runtime (MIT-Lizenz) mit DirectML auf jeder Grafikkarte unter
Windows, sonst auf dem Prozessor. Das Paket ist etwa 12 MB groß. DirectML ist in Wartung,
neue Entwicklung läuft in Windows ML, das dieselbe ONNX-Runtime-Schnittstelle anbietet.
Ein späterer Wechsel wäre also klein.

**Modelle** kommen nicht mit dem Programm. Sie werden beim ersten Gebrauch geladen, mit
Prüfsumme, in den Ordner der Einstellungen. Wer die Werkzeuge nie benutzt, lädt nichts.

| Aufgabe | Modell | Lizenz | Größe | Last | Bewertung |
|---|---|---|---|---|---|
| Segmentierung per Klick | **MobileSAM** | Apache 2.0 | 9,7 M Parameter, etwa 40 MB | etwa 12 ms je Bild auf einer Grafikkarte laut Autoren (8 ms Bildkodierer, 4 ms Maske), auf dem Prozessor ein Vielfaches | **vertretbar.** Der Kodierer läuft einmal je Bild, jeder weitere Klick kostet nur den Masken-Teil. |
| Segmentierung, genauer | **SAM 2.1 Tiny** | Apache 2.0 | 38,9 M Parameter, etwa 150 MB | spürbar mehr, ohne Grafikkarte mehrere Sekunden | **vertretbar als zweite Stufe**, kann Masken über Bilder verfolgen |
| Hintergrund entfernen | **BiRefNet** (leichte Variante) | MIT | Größe vor dem Einbau messen | mittel | **vertretbar** |
| Hintergrund entfernen | RMBG 1.4 und 2.0 (BRIA) | nicht kommerziell | 44 M Parameter | – | **ausgeschlossen:** Renders sind oft Auftragsarbeit |
| Tiefe schätzen | **Depth Anything V2 Small** | Apache 2.0 | 24,8 M Parameter, etwa 100 MB | mittel | **vertretbar** |
| Tiefe schätzen | Depth Anything V2 Base, Large | nicht kommerziell | 97,5 M bzw. 335 M | – | **ausgeschlossen** |
| Entrauschen (W4) | **Open Image Denoise** (Intel) | Apache 2.0 | Bibliothek mit Gewichten, einige MB | läuft auf dem Prozessor, bei Intel, NVIDIA und AMD auch auf der Grafikkarte | **vertretbar**, keine KI-Laufzeit nötig |

**Zusammengefasst:**
- Last und Größe sind mit MobileSAM, BiRefNet und Depth Anything V2 Small vertretbar,
  wenn die Modelle erst bei Bedarf geladen werden.
- Lizenzrechtlich gehen nur die Apache- und MIT-Modelle. Die Varianten mit
  Nicht-kommerziell-Lizenz fallen weg, auch wenn sie besser wären.
- Bei Blender-Renders bleibt die Kryptomatte das bessere Werkzeug. KI ist für Bilder ohne
  Pässe da.

**Quellen** (abgerufen am 26. September 2026):
- MobileSAM: [github.com/ChaoningZhang/MobileSAM](https://github.com/ChaoningZhang/MobileSAM)
- SAM 2: [github.com/facebookresearch/sam2](https://github.com/facebookresearch/sam2)
- BiRefNet: [github.com/ZhengPeng7/BiRefNet](https://github.com/ZhengPeng7/BiRefNet)
- RMBG: [huggingface.co/briaai/RMBG-1.4](https://huggingface.co/briaai/RMBG-1.4) und
  [RMBG-2.0](https://huggingface.co/briaai/RMBG-2.0)
- Depth Anything V2: [github.com/DepthAnything/Depth-Anything-V2](https://github.com/DepthAnything/Depth-Anything-V2)
- ONNX Runtime DirectML: [nuget.org/packages/Microsoft.ML.OnnxRuntime.DirectML](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.DirectML)
  und [DirectML-Hinweis](https://learn.microsoft.com/en-us/windows/ai/directml/dml)
- Open Image Denoise: [github.com/OpenImageDenoise/oidn](https://github.com/OpenImageDenoise/oidn)
