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
6. **Werkzeugleiste: Entwurf B.** Die Kategorien stehen als Band neben „Bild öffnen“.
   Darunter steht eine Zeile mit den Werkzeugen der gewählten Kategorie (Abschnitt 3.1).
7. **Kein Schalter Folge/Einzelbild im Band.** Ob eine Folge oder ein Einzelbild offen
   ist, erkennt das Atelier selbst. Nur bei einer Folge steht am Ende der Werkzeugzeile
   ein unauffälliges Symbol: Filmstreifen für die ganze Folge, ein Bild für „nur dieses
   Bild“. Ein Ebenen-Symbol wird bewusst nicht verwendet, es wäre mit den Ebenen zu
   verwechseln. Auf „nur dieses Bild“ umgestellt, wirken Änderungen nur auf das aktuelle
   Bild der Folge (Abschnitt 3.2).

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

Die Kategorien stehen als Band (Entscheidung 6). Darunter zeigt eine zweite Zeile die
Werkzeuge der gewählten Kategorie, jedes mit Zeichen und Namen. Der erklärende Satz steht
im Tooltip. Die Einstellungen des gewählten Werkzeugs stehen rechts, wo heute der
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

Ob eine Folge oder ein Einzelbild offen ist, erkennt das Atelier selbst. Einen Schalter
dafür gibt es nicht (Entscheidung 7).

Ist eine **Folge** offen, zeigt die Leiste zuerst, was sich anpasst. Werkzeuge der Art
„je Bild von Hand“ stehen gestrichelt am Ende der Zeile, mit dem Hinweis, dass sie nur
dieses Bild ändern. Sie bleiben erreichbar, weil ein Fehler in einem einzelnen Bild der
Folge genau so ein Werkzeug braucht.

Ganz rechts in der Werkzeugzeile steht dann ein kleines Symbol:

- **Filmstreifen** (Grundstellung): Änderungen wirken auf die ganze Folge.
- **Bild**: Änderungen wirken nur auf das aktuelle Bild. Die Werkzeuge „je Bild von Hand“
  rücken nach vorn und sind nicht mehr gedämpft.

Ist ein **Einzelbild** offen, fehlt das Symbol, denn es gibt nichts zu unterscheiden.

„Nur dieses Bild“ heißt im Datenmodell: Die Änderung wird eine **Ausnahme für dieses
Bild** im Projekt der Folge.

- Für gemalte Masken gibt es das schon: Eine entsperrte Maske hält ihre Deckung je Bild
  (`LayerMask.PaintFrames`).
- Für alle anderen Einstellungen ist es neu. Es liegt nahe an den Schlüsselbildern aus
  W9: Eine Ausnahme ist ein Schlüsselbild, das nur auf seinem eigenen Bild gilt, ohne
  Übergang zu den Nachbarn.
- Beides sollte deshalb **ein Modell** teilen. Die Ausnahme kommt mit Phase U, die
  Übergänge mit W9.
- Eine Ausnahme muss sichtbar sein: Ein Bild mit Ausnahmen trägt in der Folgeleiste eine
  Marke, und die betroffenen Regler zeigen, dass sie hier vom Rest der Folge abweichen.

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
- **Nachspielbar:** Form, Winkel, Seitenverhältnis, Winkelmodus, Begrenzung und
  Druck stehen im `PaintStroke`. Alte Striche lesen sich als rund und ungebunden. Die
  Probe „Nachspielen ist Malen“ muss für jede Form und jeden Modus Byte für Byte halten.

### 4.1 Stand

**Erster Schnitt (PR #37, gemergt):** Form, Winkel, „folgt dem Strich“ und objektgebunden.

- Rund und gestreckt ergibt eine Ellipse, eckig und gestreckt ein Rechteck (bis 1:8).
  Eine schlichte runde Spitze nimmt weiter den alten Rechenweg.
- Der Winkel dreht mit Umschalt + Mausrad in Schritten von 15 Grad, der Regler zieht nach.
- Mit „folgt dem Strich“ wartet der erste Tupfer auf die Richtung des Weges. Ein Klick
  ohne Bewegung setzt ihn beim Loslassen.
- Abweichung vom Entwurf oben: Der Strich speichert nicht die Kryptomatte-Kennung,
  sondern die **gepackte Deckung** des Objekts in Maskengröße. Nur so spielt er im
  Maskenverlauf genau nach, auch ohne die Datei, und ein Bild später mit anderen
  Objekten an derselben Stelle ändert nichts an einem alten Strich.
- Proben: `BrushShapeInvariants`.
  - Alte Striche bleiben unverändert.
  - Jede Form deckt, was sie soll.
  - „Folgt dem Strich“ legt die Spitze in die Richtung des Weges.
  - Die Begrenzung hält den Strich draußen.
  - 40 zufällige Striche spielen nach, auch aus Text gelesen und im Maskenverlauf.
  - Die Seite bindet an ein Objekt der Kryptomatte-Probedatei.

**Nach dem ersten Test (PR #39, gemergt):**

- **Ruhige Richtung:** Bei „folgt dem Strich“ kam die Richtung jedes Tupfers aus dem
  letzten Mausschritt. Bei langsamem Malen ist der 1–2 Bildpunkte lang und auf 0, 45 oder
  90 Grad gerastert, ein zittriger Strich sprang zwischen schräg, hochkant und quer.
  - Jetzt hängt ein Anker an einer Schnur von etwa einem Radius hinter dem Stift. Die
    Richtung ist die vom Anker zum Stift.
  - Die Richtungen der einzelnen Schritte zu mitteln reicht nicht: Ein Zickzack aus 45 und
    135 Grad mittelt sich zu nichts, obwohl der Weg senkrecht geht.
  - Eine Umkehr dreht die Spitze nicht. Der erste Tupfer wartet auf eine halbe Schnur Weg.
  - Striche tragen eine **Fassung** (`PaintStroke.Version`). Alte Striche spielen im
    Maskenverlauf mit der Rechnung nach, mit der sie gemalt wurden.
- **Ring und Vorschau:** Der Ring zeigt beim Malen den Winkel, in dem gerade gemalt wird.
  Die Vorschau beim Ziehen von Größe, Härte und Abstand zeigt die Form der Spitze statt
  immer eines Kreises.
- **Winkel beim Folgen:** Er gilt zur Strichrichtung. Bei 0° liegt die Spitze längs und
  malt schmal, bei 90° quer und malt breit. So steht es auch im Tooltip.
- **Karo** (Wunsch aus dem Test): Zwei gegenüberliegende Ecken der eckigen Spitze gehen
  auseinander wie an einem Gelenkrahmen, die Seiten bleiben gleich lang. Voll gezogen ist
  das Karo sieben Mal so lang wie breit, für schmale Spalten.

**Druck und Kante (PR #40, gemergt):**

- **Druckstärke:** Der Druck eines Grafiktabletts wirkt wahlweise auf Größe, Stärke oder
  beides. Die Grundstellung ist Größe.
  - Der Strich zeichnet einen Druckwert je Wegpunkt auf, dazwischen wird gemittelt.
  - Der Abstand der Tupfer folgt dem kleineren Radius, damit ein leichter Strich keine
    Lücken bekommt.
  - Druck wirkt nur, wenn er aufgezeichnet wird. Maus, Finger, Stift ohne Sensor oder
    Druck „aus“ rechnen Schritt für Schritt wie vorher.
- **Kantengebunden:** Aus Tiefen- und Normalpass wird eine Begrenzung in Maskengröße
  gebaut, wie beim objektgebundenen Pinsel, und mit dem Strich gespeichert.
  - Verglichen wird mit dem Ansatz, nicht mit dem Nachbarn.
  - Die Tiefe zählt als Anteil der Entfernung, die Richtung als Winkel.
  - Die Toleranz (Grundstellung 30 %) sagt, wie weit eine Rundung vom Ansatz abweichen darf.
  - Wer auf dem Hintergrund ansetzt, malt nur auf ihm.
  - Objekt und Fläche schließen sich aus.
- Proben in `BrushShapeInvariants`:
  - Voller Druck malt wie ohne Druck.
  - Druck ohne Aufzeichnung wirkt nicht.
  - Größe und Stärke folgen dem Druck.
  - Druckwerte spielen aus Text genau nach.
  - Sprung, Hintergrund, Knick, Wölbung und Toleranz an künstlichen Pässen.
  - Der Tiefenpass der Blender-Probedatei auf der Seite.

**Linien und Flächen (`feature/pinsel-formen`):**

- **Gerade Linie:** Umschalt + Klick zieht eine Linie vom Ende des letzten Zugs auf
  derselben Maske. Solange Umschalt gehalten wird, zeigt eine gestrichelte Linie, wohin
  sie ginge.
- **Rechteck, Ellipse, Lasso:** vier Knöpfe am Anfang der Pinselleiste (✎ ▭ ◯ ➰), genau
  einer ist an.
  - Rechteck und Ellipse werden aufgezogen. Umschalt macht Quadrat und Kreis.
  - Das Lasso umfährt eine Form frei und schließt sich beim Loslassen. Kreuzt es sich
    selbst, entsteht ein Loch (gerade-ungerade).
  - Gefüllt wird mit geglätteter Kante (4 × 4 Abtastungen je Maskenpunkt) bis zur
    Deckkraft. Rechte Taste oder Alt nimmt weg. Objekt- und Flächenbindung gelten auch hier.
- **Im Modell** ist eine Fläche ein `PaintStroke` mit `Area` und den Ecken als `Path`. Der
  Maskenverlauf spielt sie ohne Umbau nach.
- Beim Bauen gefunden: Ein Knopf mit `IsChecked="True"` im XAML löst `Checked` aus, bevor
  die anderen Knöpfe zugewiesen sind. Die Seite wäre beim Öffnen abgestürzt. Die
  Seitenprobe hat das gefangen.
- Die Flächen stehen vorerst in der Pinselleiste. Mit Phase U wandern sie als eigene
  Werkzeuge in die Kategorie „Malen“ der Werkzeugzeile.

**Offen in W1:**

- Maske bearbeiten.
- Kanten verfeinern.
- Stempelpinsel.

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
