# Atelier – Werkzeugplan

Stand: 27. September 2026. Aus dem [Werkzeugkatalog](Atelier-Werkzeugkatalog.md) wird hier
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
   verwechseln.
8. **„Nur dieses Bild“ löst das Bild heraus** (entschieden am 27. September 2026, statt
   einer Ausnahme im Projekt der Folge). Im Quellordner entsteht ein Ordner mit dem Namen
   des Bildes. Darin liegen eine Kopie des Originals, die FrameFlip-Daten (ein eigenes
   Projekt, beginnend mit dem Stand der Folge) und später die Ausgabe. Die Folge bleibt,
   wie sie ist.
9. **Ein neuer Rahmen im Stil von FrameFlip** (entschieden am 27. September 2026, nach
   dem Entwurf mit runden Feldern und Chips aus Zeichen und Namen):
   - „Bild öffnen“ steht vorn im Kategorienband, eine eigene Kopfzeile gibt es nicht mehr.
   - Die Einstellungen des gewählten Werkzeugs stehen als Leiste unter der Werkzeugzeile,
     aufgeräumter als bisher.
   - Unten eine niedrige Statuszeile: Folge und Bild, Datei, Größe, Pipette, Vergleich,
     Zoom und der Speicherstand („Gespeichert 00:14“).
   - Format, Ziel und Export stehen unten im rechten Seitenbereich.
   - Das heutige Knotensymbol bleibt.
   - Die Ebenen behalten ihre Zeilen (ein- und ausgeblendet, Größe, Vorschau). Die Vorschau
     wird etwas kleiner, damit rechts Platz für neue Knöpfe ist. Dazu kommen unten
     „+ Ebene“, „Einstellung“ und „Objektmaske“.

## 2. Reihenfolge

Jede Phase ist ein eigener Zweig mit eigener PR. Zuerst kommen die Pinsel, weil sie im
Pinselbereich leben und vom Umbau nicht abhängen. Dann folgt der Umbau, damit alle
weiteren Werkzeuge gleich an ihren Platz kommen.

| Phase | Inhalt | Warum hier |
|---|---|---|
| **W1 Pinsel** | quadratisch und rechteckig, drehbar, Winkel folgt dem Strich, objektgebunden (Kryptomatte), kantengebunden (Pässe), Druckstärke, gerade Linien, Rechteck, Ellipse und Lasso, Maske bearbeiten (füllen, leeren, umkehren, weiche Kante, ausweiten, schrumpfen), Kanten verfeinern, Stempelpinsel | Wunsch des Nutzers, klein, vom Umbau unabhängig. Der Strich muss für den Maskenverlauf exakt nachspielbar bleiben. |
| **U Oberfläche** | Werkzeugleiste, Kontext Folge/Einzelbild, Suche, Werkzeugbeschreibung als Daten | Die Grundlage für alle weiteren Werkzeuge. |
| **R Rahmen** | Rahmen der Seite, aufgeräumte Einstellungsleiste mit Zeichen, Ebenen mit Knöpfen und Wirkung nur auf die gewählte Ebene, Objektmaske per Klick | Wunsch des Nutzers nach dem Umbau (Entscheidung 9). Die Werkzeuge ab W2 bekommen so gleich ihren Platz. |
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
im Tooltip. Die Einstellungen des gewählten Werkzeugs stehen als Leiste darunter, über die
ganze Breite (Entscheidung 9). Eine Suche mit Strg+K findet jedes Werkzeug beim Namen, so
wie der Hub im Knotenmodus.

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

„Nur dieses Bild“ löst das Bild heraus (Entscheidung 8): ein eigener Ordner im
Quellordner mit Kopie, Projekt und Ausgabe. Eine Ausnahme im Projekt der Folge gibt es
nicht. Die Schlüsselbilder aus W9 bekommen ihr eigenes Modell.

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

**Linien und Flächen (PR #41, gemergt):**

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

**Maske bearbeiten (PR #43, gemergt):**

- Umkehren, Füllen, Leeren, Weiche Kante, Ausweiten, Schrumpfen, für die ganze gemalte
  Maske.
  - Zu finden im Maskenmenü („Maske bearbeiten …“) und am Knopf „Maske ▾“ in der
    Pinselleiste, sobald der Pinsel auf einer gemalten Maske liegt.
  - Weiche Kante, Ausweiten und Schrumpfen fragen nach der Weite: 2, 5, 10 oder
    25 Bildpunkte.
- Ausweiten und Schrumpfen nehmen den hellsten oder dunkelsten Wert in einem Kreis, Ecken
  werden beim Ausweiten rund. Der Bildrand zählt weder als voll noch als leer.
- Die weiche Kante sind drei Kastenfilter in ganzen Zahlen, zusammen nahe an einer Glocke.
- Jede Bearbeitung ist ein Strich (`PaintStroke.Edit`). Der Maskenverlauf spielt sie Byte
  für Byte nach, und Strg+Z nimmt sie zurück. Was nichts ändern würde, ist kein Schritt.
- In Phase U werden die Bearbeitungen eigene Befehle in der Kategorie „Malen“.

**Kanten verfeinern (PR #45, gemergt):**

- Im Menü „Maske bearbeiten“, mit 4, 8, 16 oder 32 Bildpunkten Weite.
  - Ragt die Maske über eine Kante im Bild hinaus, zieht sie sich zurück.
  - Hört sie davor auf, wächst sie bis an die Kante.
  - Wo das Bild keine Kante hat, bleibt sie.
- Verfahren: lokales Matting mit zwei Klassen auf dem Raster der Maske, geführt von der
  gestauchten Helligkeit (l / (1 + l), Wurzel), damit Glanzlichter die Schatten nicht
  erschlagen.
  - Um den Rand liegt ein unsicherer Streifen so breit wie die Weite.
  - Jeder Punkt darin wird mit dem sicher Inneren und dem sicher Äußeren in seiner Nähe
    verglichen.
- Erst versucht war der geführte Filter nach He et al. Die Probe zeigte, dass er Kanten
  weich macht, aber nicht einrastet: Eine Maske, die in eine gleichmäßig helle Fläche
  ragt, behielt dort Zwischenwerte.
- Das Ergebnis hängt am Bild. Der Strich trägt es gepackt mit (`PaintStroke.Result`),
  der Maskenverlauf setzt es ein, statt neu zu rechnen.

**Stempel (PR #46, gemergt):**

- Der Knopf ✿ schaltet den Stempel ein, „Spitze …“ lädt ein Bild als Pinselspitze.
  - Weiß malt, Schwarz nicht. Hat die Datei Transparenz, zählt die Deckkraft.
  - Die Spitze wird auf höchstens 128 Punkte verkleinert und reist gepackt in jedem Strich
    mit (`PaintStroke.Stamp`). Der Maskenverlauf braucht die Datei danach nicht mehr.
- Je Tupfer dreht sich die Spitze zufällig („Zufall“) und landet neben dem Weg
  („Streuung“).
  - Der Zufall hängt nur an einer Saat je Strich und der Nummer des Tupfers. Gerechnet wird
    mit SplitMix64, kein `Random`-Objekt, dessen Folge nicht zugesichert ist.
- Winkel, Streckung, Druck und Begrenzung gelten wie bei den anderen Spitzen. Ohne Spitze
  malt der Stempel rund.

**W1 ist damit vollständig.** Als Nächstes folgt Phase U, die Oberfläche mit Werkzeugleiste
(Entscheidungen 6 und 7). Die Pinselleiste ist inzwischen voll: Form, Winkel, Karo, Druck,
Bindung, Flächen, Stempel und Maske bearbeiten. U verteilt das auf die Werkzeugzeile
der Kategorie „Malen“.

## 4a. Phase U im Einzelnen

In drei Schnitten:

1. **U1 – Leiste und Katalog** (`feature/werkzeugleiste`): Kategorienband, Werkzeugzeile, Suche.
2. **U2 – „nur dieses Bild“**: das Symbol am Ende der Werkzeugzeile; das Bild wird ein eigenes
   Einzelbild in einem Ordner im Quellordner (Entscheidungen 7 und 8).
3. **U3 – Pinselleiste verschlanken**: Form, Winkel, Karo, Druck, Bindung, Flächen und Stempel
   stehen heute alle in einer Zeile. Was ein eigenes Werkzeug ist, wandert in die
   Werkzeugzeile; die Pinselleiste behält die Regler.

**U1 (PR #48, gemergt):**

- Unter der Kopfzeile stehen die Kategorien als Band: Auswahl, Malen, Tonwert, Farbe,
  Details, Optik, Licht, Glitch. Darunter die Werkzeuge der gewählten, rechts die Suche
  (Strg+K). Zeit und Retusche erscheinen, sobald sie etwas enthalten (W9, W4).
- **Katalog** (`ToolCatalog`): ordnet die Arten des Knotenkatalogs den Kategorien zu und gibt
  jedem sein Zeitverhalten mit. Es ist dieselbe Liste, aus der der Hub baut. Dazu kommen die
  Pinselarten (Pinsel, Rechteck, Ellipse, Lasso, Stempel) und „Maske bearbeiten“. Die
  Bausteine des Graphen (Mischen, Platzieren, Schwarz …) bleiben im Hub, sie tun dem Bild
  nichts.
- **Wirkung:**
  - Im Stapel öffnet ein Werkzeug seine Karte im Farbstreifen.
  - Im Knotenmodus entsteht ein Knoten, hinter dem gewählten oder in der Mitte der Ansicht.
  - Beim Pinsel wird seine Art gewählt.
  - Was nur im Knotenmodus Platz hat (Masken als Knoten, Effekte ohne Karte), steht im
    Stapel gedämpft da, mit dem Hinweis, wie man umschaltet.
- Die Leiste beginnt bei „Malen“, weil dort in jedem Modus etwas zu tun ist.
- Beim Bauen gefunden: Ein doppelter Schlüssel im Wörterbuch ließ es im Testlauf still nicht
  laden, alle Texte blieben Schlüssel. Die Probe prüft jetzt, dass jeder Name übersetzt
  ankommt.

**U2 (PR #50, gemergt):**

- Am Ende der Werkzeugzeile steht bei einer Folge der Umschalter: Filmstreifen oder Bild,
  als gezeichnete Zeichen. Bei einem gewöhnlichen Einzelbild erscheint er nicht.
- **„Nur dieses Bild“** (`FrameDetach`):
  - Im Quellordner entsteht ein Ordner mit dem Namen des Bildes (`render_0047/`). Darin
    liegen die Kopie des Originals und ein eigenes Projekt, beginnend mit dem Stand der
    Folge, samt Maskenverlauf.
  - Projekt und Schnell-Exporte liegen wie immer im Ordner `FrameFlip` daneben.
  - Gibt es das Einzelbild schon, wird es geöffnet, nicht überschrieben.
  - Kopiert wird im Hintergrund und erst unter anderem Namen, damit eine halbe Kopie nie
    als fertiges Bild dasteht.
- **Zurück:** Das Einzelbild merkt sich seine Herkunft im Projekt (`Origin`). Der
  Filmstreifen führt zurück auf das Bild der Folge, aus dem es kam.

**U3 (`feature/pinselleiste-schlank`):**

- Die Pinselleiste zeigt nur, was zum gewählten Werkzeug gehört:
  - Bei Rechteck, Ellipse und Lasso nur Deckkraft und Bindung. Spitze, Abstand und Druck
    haben beim Füllen nichts zu sagen.
  - Das Karo nur bei eckiger Spitze, Spitze/Zufall/Streuung nur beim Stempel, die Toleranz
    nur mit der Kante.
- Die Knöpfe der Arten und des Stempels treten aus der Pinselleiste zurück. Sie sind
  Werkzeuge der Zeile „Malen“.
- Die Werkzeugzeile zeigt gedrückt, als was der Pinsel gerade malt, auch wenn er über die
  Spalte links oder mit B gewählt wurde. Tut die Maus etwas anderes, ist nichts gedrückt.

**Phase U ist damit abgeschlossen.**

## 4b. Phase R im Einzelnen

Der Rahmen nach Entscheidung 9, in vier Schnitten:

1. **R1 – Rahmen** (`feature/atelier-rahmen`): wo was steht.
2. **R2 – Einstellungsleiste und Zeichen:** die Einstellungen in beschrifteten Gruppen;
   Zeichen aus Tabler Icons (MIT) für Chips und Werkzeugleiste. Das Knotensymbol bleibt.
3. **R3 – Ebenen:**
   - Zeilen wie heute, die Vorschau etwas kleiner.
   - Rechts in der Zeile Chips für Effekte und Maske, unten „+ Ebene“, „Einstellung“ und
     „Objektmaske“.
   - Ein Effekt wirkt nur auf die gewählte Ebene: Im Knotenmodus wird er in den Zweig der
     Ebene eingesetzt, vor ihrem Mischen. Heute wählt ein Klick auf die Ebene ihr Mischen,
     und alles darunter ist mit betroffen.
4. **R4 – Objektmaske:** ein Objekt im Bild anklicken, und aus der Kryptomatte entsteht eine
   Maske auf der gewählten Ebene.

**R1 (`feature/atelier-rahmen`):**

- **Oben:** „Bild öffnen“ vorn im Kategorienband. Die alte Kopfzeile fällt weg.
- **Darunter** die Einstellungen des Werkzeugs als Leiste über die ganze Breite, nicht mehr
  über dem Bild.
- **Unten eine niedrige Statuszeile:**
  - links: Bild i von n mit Schritten davor und danach, Datei, Größe, Ansicht, Pipette;
  - rechts: Original zeigen, Zoom, Speicherstand und Speichern.
- **Ausgabe:** ein Feld der Andockfläche, in der Grundanordnung unten rechts. Format und
  Ziel stehen in einer Zeile, darunter Schnell-Export und „Sequenz ausgeben“.
  - Eine Anordnung von früher bekommt das Feld beim Laden dazu (`Normalise`).
  - Das Feld ist so hoch wie sein Inhalt (`DockHost.FitsContent`). Ein Anteil der Spalte
    schnitt die Knöpfe in einem kleinen Fenster ab. Liegt es als Reiter bei anderen
    Feldern, teilt es sich die Höhe wie gewohnt.
- Die Proben kennen das neue Feld: Andocken, Einklappen, Grundanordnung, dazu die volle
  Höhe der Ausgabe, unabhängig vom Gewicht.
- Beim Bauen gefunden, Fehler aus U3: Die Gruppen der Pinselleiste waren falsch
  geschachtelt. Größe, Härte und Stärke lagen in der unsichtbaren Gruppe der Arten und
  waren nie zu sehen. Die Probe prüfte nur den Schalter der Gruppe, jetzt prüft sie, ob
  die Regler wirklich zu sehen sind.

Danach R2 bis R4, dann laut Reihenfolge W2 Tonwert.

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
