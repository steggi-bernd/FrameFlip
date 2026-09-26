# Atelier – Werkzeugkatalog

Stand: 26. September 2026. Eine Ideensammlung, noch kein Plan. Sie beantwortet die Frage,
welche Grafikwerkzeuge und Filter im Atelier sinnvoll wären, und ordnet jedes nach Nutzen
und Aufwand ein. Was davon gebaut wird, entscheidet sich einzeln. Gebautes wandert dann
in einen eigenen Plan wie [Projekte und Masken](Projekte-und-Masken.md).

## Wie die Einträge gelesen werden

**Aufwand:** S = ein Nachmittag, M = ein bis drei Tage, L = eine Woche, XL = mehr als
eine Woche oder mit fremder Abhängigkeit.

**Art im Knotensystem.** Sie entscheidet, wie schnell ein Werkzeug beim Malen und
Ziehen bleibt:

- **punktweise:** Jeder Bildpunkt rechnet allein aus demselben Punkt der Eingänge. Solche
  Werkzeuge laufen im groben Vorschauraster und in der Bereichsrechnung beim Malen mit.
  Das ist der billigste Fall.
- **örtlich:** Das Werkzeug liest Nachbarn in einem Radius, wie Schärfe oder Klarheit. Die
  Vorschau läuft weiter grob, beim Malen rechnet das Atelier voll.
- **Datenwerkzeug:** Das Werkzeug braucht einen Renderpass (Tiefe, Normale, Bewegung) oder
  holt Punkte von anderswo. Fehlt der Pass, ruht es und sagt, warum.
- **ganzes Bild** (`IFramePass`): Das Werkzeug läuft der Reihe nach über das fertige Bild,
  wie die Fehlerstreuung. Es kommt erst beim Loslassen dazu.
- **Maske:** Das Werkzeug liefert einen Wert je Punkt statt eines Bildes.

**Was FrameFlip von einem Bildprogramm unterscheidet.** Zwei Dinge sollen in jedem
Werkzeug mitgedacht werden:

- Die Bilder kommen meist **aus Blender, mit Renderpässen**: Tiefe, Normale, Bewegung,
  Kryptomatte, Albedo. Was sich aus einem Pass ergibt, ist bei uns exakt und billig. Ein
  gewöhnliches Bildprogramm muss es schätzen.
- Es sind **Folgen, keine Einzelbilder**. Ein Effekt, der von Bild zu Bild springt, wo er
  stehen sollte, ist ein Fehler. Ein Effekt, der stehen bleibt, wo er leben sollte, wirkt
  wie ein Aufkleber. Jedes zufällige Werkzeug braucht deshalb einen festen Zufall und
  eine Geschwindigkeit, wie Verschiebung und Pixel Sort sie schon haben.

## Was es schon gibt

Die Basisregler sind Belichtung, Schwarz- und Weißpunkt, Gamma, Kontrast und Sättigung.
Die Werkzeuge nach Art:

- **Farbe:** Kurven, Weißabgleich, Zonen (Lift/Gamma/Gain), Farbbänder (HSL), Lebendigkeit,
  LUT.
- **Details:** Dunst entfernen, Klarheit, Textur, Schärfe, Rauschen, Korn.
- **Optik:** Bloom, Halation, Vignette, chromatische Aberration, Verzeichnung.
- **Aus Pässen:** Tiefenschärfe, Bewegungsunschärfe, Verschiebung (Welle, Scheiben,
  Blöcke).
- **Glitch:** Dither (geordnet, zufällig, Linienraster, sechs Fehlerstreuungen, Duoton),
  Pixel Sort.

Dazu kommen das Histogramm mit Clipping-Anzeige und im Knotenmodus die Knoten für
Masken, Licht, Ton, Überlagerung, Maskenrechnung, Wertebereich, Farbverlauf und Formen.

---

## 1. Pinsel und Masken malen

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Quadratischer und rechteckiger Pinsel** | Kante statt Rundung. Die Größe ist die Kantenlänge, dazu ein Seitenverhältnis. Die harte oder weiche Kante gilt wie beim runden Pinsel, nur entlang der Seiten. | `PaintedMask.Stroke` bekommt eine Form. Der Abstand zur Kante wird im gedrehten Rechteck statt im Kreis gemessen. `PaintStroke` trägt Form, Winkel und Seitenverhältnis. So bleibt das Nachspielen für den Maskenverlauf exakt, alte Striche lesen sich als rund. | S | hoch |
| **Drehbarer Pinsel** | Ein fester Winkel, gedreht mit Umschalt und Mausrad, angezeigt am Pinselring. Wahlweise **folgt der Winkel der Strichrichtung**: Ein flacher Pinsel legt sich dann wie eine Breitfeder in jede Kurve, und man kommt sauber in Ecken und an Kanten. | Derselbe Eingriff wie oben. Beim Folgen misst `PaintStroke` den Winkel je Tupfer aus dem Weg, dadurch bleibt er nachspielbar. | S | hoch |
| **Stempelpinsel** | Eine Graustufen-PNG als Pinselspitze, für Laub, Kratzer oder Spritzer. Drehung und Streuung sind je Tupfer zufällig, mit festem Zufall. | Die Spitze wird im Projekt abgelegt und je Tupfer gedreht abgetastet. | M | mittel |
| **Objektgebundener Pinsel** | Der Pinsel malt nur auf das Objekt, auf dem der Strich begann, gelesen aus der Kryptomatte. So kommt man an Kanten, ohne ins Nachbarobjekt zu malen. Das ist die Kryptomatte-Antwort auf „besser in Ecken und Kanten“. | Beim Ansetzen wird die Kennung unter dem Pinsel gelesen, jeder Tupfer wird mit der Deckung dieses Objekts multipliziert. Der Strich merkt sich die Kennung, das Nachspielen bleibt exakt. | S–M | sehr hoch |
| **Kantengebundener Pinsel** | Wie oben, aber ohne Kryptomatte: Der Pinsel stoppt an Sprüngen in Tiefe, Normale oder Helligkeit. | Je Tupfer ein Flutfüllen im Radius mit Schwelle auf dem gewählten Pass. | M | hoch |
| **Druckstärke** | Bei einem Stift steuert der Druck Größe oder Stärke. | WPF liefert den Druck in `StylusPoint`. `PaintStroke` speichert ihn je Punkt. | S | mittel (mit Stift hoch) |
| **Gerade Linien, Formen, Lasso** | Umschalt+Klick zieht eine gerade Linie vom letzten Punkt. Rechteck, Ellipse und Lasso füllen eine Maske. | Linien sind nur Pfade in `PaintStroke`. Die Formen sind Füllungen, im Verlauf ein Stand ohne Strich. | S–M | hoch |
| **Maske bearbeiten** | Füllen, leeren, umkehren, weiche Kante, ausweiten und schrumpfen. | Operationen auf dem Raster. Der Maskenverlauf behandelt sie schon als „fremde Änderung“ mit Schnappschuss. | S | hoch |
| **Kanten verfeinern** | Eine grobe Maske schmiegt sich an die Kanten des Bildes, etwa an Haare oder Laub. | Geführter Filter (Guided Filter) mit dem Bild oder der Normale als Führung. Örtlich, als Knoten hinter jeder Maske. | M | hoch |

## 2. Auswahl und Objekterkennung

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Kryptomatte ausbauen** | Mehrere Objekte, Material- und Asset-Ebene, Auswahl mit Umschalt erweitern. | Die Daten sind da, es fehlt nur die Bedienung. | S | hoch |
| **Auswahl aus Tiefe, Normale oder Bewegung** | Ein Klick wählt den Tiefenbereich um einen Punkt, alle Flächen gleicher Ausrichtung (etwa alles, was nach oben zeigt) oder alles, was sich bewegt. | Maske aus Pass plus Wertebereich gibt es schon. Neu ist eine Pipette, die den Bereich setzt. | S | hoch |
| **Zauberstab** | Farbe oder Helligkeit mit Toleranz, zusammenhängend oder überall. | Flutfüllen, Ergebnis als gemalte Maske. | M | mittel |
| **KI-Segmentierung per Klick** | Wie „Segment Anything“: Ein Klick auf ein Objekt ergibt eine Maske, auch ohne Kryptomatte, etwa bei fremden Bildern oder PNG-Folgen. | ONNX Runtime mit DirectML. Das Modell (MobileSAM etwa 40 MB, SAM bis 2,5 GB) wird bei Bedarf geladen. Auf den Folgen läuft es asynchron, die Maske wird je Bild zwischengespeichert. Lizenz prüfen, SAM steht unter Apache 2.0. | XL | mittel: bei Blender-Renders hilft die Kryptomatte besser und billiger |
| **Hintergrund entfernen** | Ein Klick stellt das Motiv frei. | Wie oben mit einem kleineren Modell (RMBG, U²-Net). | L | niedrig |
| **Tiefe schätzen** | Eine Tiefe für Bilder ohne Tiefenpass, damit Tiefenschärfe und Nebel auch auf fremden Bildern gehen. | Modell wie Depth Anything, über ONNX. | L | niedrig |
| **Maske über die Folge tragen** | Eine gemalte Maske wandert mit dem Bewegungspass von Bild zu Bild, statt je Bild neu gemalt zu werden. | Die Maske wird entlang des Vektorpasses verschoben, mit einem Verfeinern gegen das Auseinanderlaufen. | L | hoch bei entsperrten Masken |

**Zur Objekterkennung:** Für Renders aus Blender ist die Kryptomatte die bessere
„Objekterkennung“. Sie ist exakt, sofort da und kostet nichts. KI-Segmentierung lohnt
sich erst für Bilder ohne Pässe und wäre ein optionaler Zusatz mit eigenem Download.

## 3. Tonwert und Histogramm

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Tonwertkorrektur am Histogramm** | Schwarz-, Grau- und Weißpunkt als Anfasser direkt unter dem Histogramm, je Kanal oder gemeinsam, mit Ausgabebereich. | Die Regler gibt es als Basisregler, neu ist die Anzeige am Histogramm. Punktweise. | M | sehr hoch |
| **Auto-Tonwert, Auto-Kontrast, Auto-Farbe** | Setzt die Punkte so, dass ein wählbarer Anteil (etwa 0,1 %) oben und unten beschnitten wird. Auto-Farbe gleicht die Kanäle einzeln an. | Das Histogramm ist schon berechnet, daraus die Perzentile lesen. | S | hoch |
| **Pipetten für Schwarz, Weiß und Grau** | Ein Klick ins Bild setzt den jeweiligen Punkt. Grau entfernt einen Farbstich. | Das Pipetten-Werkzeug gibt es, es braucht drei Ziele. | S | hoch |
| **Kurvenpunkt aus dem Bild** | Mit Strg+Klick ins Bild bekommt die Kurve einen Punkt beim Ton dieser Stelle. | Das Histogramm liegt in der Kurvenansicht schon im Hintergrund. | S | mittel |
| **Clipping im Bild und Falschfarben** | Was aus- oder abläuft, erscheint farbig im Bild, auf Wunsch auch eine Belichtungskarte in Falschfarben. | Eine Ansichtsumwandlung, punktweise, nicht im Export. | S | hoch |
| **Messgeräte** | Waveform, RGB-Parade, Vektorskop, Histogramm logarithmisch und je Kanal. | Bisher gibt es nur das Histogramm. Die Waveform ist in Kommentaren vorgesehen, aber nirgends gebaut. | M | hoch beim Graden |
| **Histogramm ausgleichen, CLAHE** | Streckt die Töne gleichmäßig. CLAHE macht das lokal in Kacheln, mit Obergrenze gegen verstärktes Rauschen. Holt Zeichnung aus flachen Renders. | Global punktweise nach einer Tabelle. CLAHE ist örtlich (Kacheln, bilinear gemischt). | M | mittel |
| **Farbe angleichen** | Passt Tonverteilung und Farben an ein Referenzbild an, etwa ein anderes Bild der Folge oder einen Filmstill. | Histogramm-Abgleich je Kanal oder Farbtransfer im Lab-Raum (Reinhard). Das Ergebnis kann eine LUT werden. | M | hoch |
| **Deflicker** | Gleicht die Helligkeit von Bild zu Bild aus, wenn eine Folge flackert, etwa durch Rauschunterdrückung, Adaptive Sampling oder Lichtwechsel. | Mittelwert und Perzentile je Bild. Der Ausgleich richtet sich nach einem gleitenden Mittel der Nachbarbilder. Braucht die Statistik der ganzen Folge, also einen Hintergrundlauf. | M | hoch für Folgen |
| **Tonwerttrennung nach Histogramm** | Stufen nicht in gleichen Abständen, sondern nach Quantilen, jede Stufe gleich groß im Bild. Gibt Plakaten und Dither eine bessere Grundlage. | Eine Tabelle aus dem Histogramm, punktweise. | S | mittel |

## 4. Farbe

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Paletten** | Eine Farbliste statt Stufen je Kanal. Sie kommt aus dem Bild (Median Cut, k-Means), aus Vorlagen (Game Boy, CGA, PICO-8, Zeitungsdruck) oder von Hand, und lässt sich speichern und teilen. Der Glitch-Plan nennt sie die eigentliche Lücke. | Neues Datenmodell plus Oberfläche. Dither und Tonwerttrennung arbeiten dann auf die Palette hin, der Fehler wird als Farbvektor gestreut. | M–L | hoch |
| **Verlaufsumsetzung** | Helligkeit wird zu einem Farbverlauf, mit Vorlagen. | Den Farbverlauf gibt es als Knoten, als Werkzeug in der Farbspalte fehlt er. | S | mittel |
| **Kanalmixer** | Jeder Ausgabekanal ist eine Mischung der Eingangskanäle, auch für Schwarzweiß mit Filterwirkung. | Eine 3×3-Matrix, punktweise. | S | mittel |
| **Getrennte Tönung** | Lichter und Schatten bekommen je eine eigene Tönung, mit Balance. | Punktweise. | S | mittel |
| **Farbe ersetzen, selektive Farbe** | Ein Farbbereich wird verschoben, oder je Farbfamilie ändern sich Cyan, Magenta, Gelb und Schwarz. | Die Farbbänder decken einen Teil davon ab. Es fehlt eine Pipette mit Toleranz. | S–M | mittel |
| **LUT exportieren** | Das aktuelle Grading wird als `.cube`, etwa für DaVinci oder Nuke. | Ein 33³-Gitter durch die punktweisen Werkzeuge schicken. Örtliche Werkzeuge fallen weg und werden genannt. | M | mittel |

## 5. Glitch

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Pixel Sort: Schlüssel aus einem Pass** | Sortiert nach Tiefe oder Normale statt nach Helligkeit. Die Läufe folgen dann der Geometrie, nicht dem Licht. | `SortTool` bekommt einen Pass als Schlüssel und wird damit zum Datenwerkzeug. | S–M | hoch |
| **Pixel Sort: Richtung aus einem Pass** | Läufe folgen nicht einer Geraden, sondern den Normalen (die Oberfläche fließt ab) oder dem Bewegungspass (Schmieren entlang der Bewegung). | Läufe entlang von Stromlinien. Die Invariante „nichts erfinden, nichts verlieren“ ist dann schwerer zu halten, eine Stromlinie darf keinen Punkt zweimal besuchen. | L | hoch |
| **Pixel Sort: Länge nach Helligkeit oder Maske** | Lange Läufe in den Lichtern, kurze in den Schatten, oder gesteuert durch eine Maske. | Die Grenzen je Lauf aus einer Wertkarte. | S | mittel |
| **Schmieren** | Wie Pixel Sort, aber der Lauf wird zu einem Verlauf vom ersten zum letzten Wert, statt sortiert. | Neue Intervallart. | S | mittel |
| **Datamosh** | Die Bewegung schiebt das vorige Bild weiter, statt das neue zu zeigen. Das ist der Look kaputter Videokompression. | Wir haben den **Bewegungspass**: Das vorige Ergebnis wird entlang der Vektoren weitergetragen, ab einem Bild oder in einer Maske. Braucht das Ergebnis des Vorbilds, also Rechnen in Reihenfolge. | L | sehr hoch, fast einzigartig |
| **Kanaltrennung** | Rot, Grün und Blau je um einen eigenen Versatz verschoben, auch mit Welle, anders als die optische Aberration, die radial wirkt. | Datenwerkzeug, die Wellenlogik der Verschiebung wiederverwenden. | S | hoch |
| **JPEG- und Blockschaden** | Blockweise DCT mit grober Quantisierung, verschobene Makroblöcke, falsche Farbunterabtastung. | Ganzes Bild, blockweise. | M | mittel |
| **Bitcrush** | Weniger Bits je Kanal, auch ungleich je Kanal, mit Bitverschiebung als harter Fehler. | Punktweise. | S | mittel |
| **Scanlinien, Röhre, VHS** | Zeilen, Lochmaske, Krümmung, Nachleuchten, Farbbluten, Spurrauschen und Zeilenzittern. | Mehrere punktweise Teile plus ein örtliches Farbbluten. Zittern je Bild über die Geschwindigkeit. | M | mittel |
| **Slit-Scan** | Jede Zeile oder Spalte kommt aus einem anderen Bild der Folge. Bewegung wird zu verzerrten Formen. | Braucht mehrere Bilder der Folge zugleich, also einen Frame-Cache. | L | hoch für Folgen |
| **Echo und Nachzieher** | Frühere Bilder scheinen mit abnehmender Deckung durch. | Wie oben, mit Mischart. | M | mittel |
| **Mosaik, Kristall, Halbton** | Blöcke oder Sechsecke gemittelt, Voronoi-Zellen, Halbtonpunkte je Kanal mit Rasterwinkel wie im Druck. | Mosaik punktweise über das Raster. Voronoi örtlich. Halbton ist ein naher Verwandter des Linienrasters. | S–M | mittel |
| **Zeichenraster (ASCII)** | Das Bild aus Zeichen einer Schrift, nach Helligkeit gewählt. | Zellen wie beim Dither, je Zelle eine Glyphe aus einer vorberechneten Tabelle. | M | niedrig |
| **Glitch nur an Objektkanten** | Die Verschiebung oder das Sortieren wirkt nur entlang der Kanten der Kryptomatte. | Kanten der Kryptomatte als Maske, ein allgemeiner Kantenknoten. | S | mittel |

## 6. Dither

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Blaues Rauschen** | Die beste Schwellenkarte: kein Muster wie Bayer, kein Klumpen wie weißes Rauschen. | Eine Void-and-Cluster-Karte (64×64) als Datei im Programm. Punktweise, also schnell. Je Bild verschoben, damit es lebt, oder fest, damit es steht. | S | sehr hoch |
| **Mehr Matrizen** | Bayer 2, 4 und 16, Halbton-Punkte (gruppiert) mit Winkel, eigene Schwellenkarte aus einem Bild. | Nach demselben Muster wie Bayer. | S | mittel |
| **Weitere Fehlerstreuungen** | Riemersma (entlang einer Hilbert-Kurve, ohne Richtung), Ostromoukhov (Gewichte nach Helligkeit, weniger Artefakte), Stevenson-Arce. | In die bestehende Streuung einhängen. | S–M | mittel |
| **Paletten-Dither** | Siehe Paletten. Fehler als Farbvektor, Vergleich wahlweise in einem wahrnehmungsgleichen Raum (Oklab). | Mit den Paletten zusammen. | M | hoch |
| **Vorformung** | Kontrast, Gamma und Schärfe **vor** dem Raster, eigene Regler im Werkzeug. Das entscheidet über den Look mehr als der Algorithmus. | Punktweise vor dem Rastern. | S | hoch |
| **Streustärke und Richtung** | Wie viel Fehler weitergegeben wird, Schlangenlinie an oder aus, Richtung (für Streifen). | Parameter der bestehenden Streuung. | S | mittel |
| **Ruhe über die Folge** | Fehlerstreuung flackert von Bild zu Bild, weil jede kleine Änderung die Kette verschiebt. Eine Option hält das Muster ruhig. | Blaues Rauschen als Anker, oder Streuung mit Rückkopplung aus dem Vorbild. | M | hoch für Folgen |

## 7. Weichzeichnen, Schärfe, Retusche

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Weichzeichner** | Gauß, Kasten, Objektiv (Bokeh-Form), radial, Zoom, Wirbel. | Örtlich. Bokeh und Zoom sind teurer. | M | hoch |
| **Kantenerhaltendes Glätten** | Glättet Flächen, hält Kanten (bilateral, Oberflächenweichzeichner). | Örtlich. Mit Normale oder Tiefe als Führung ist es bei Renders besonders sauber. | M | mittel |
| **Glühwürmchen entfernen** | Einzelne überhelle Punkte aus dem Rendern (Fireflies) werden durch ihre Nachbarn ersetzt. Das gibt es in keinem Bildprogramm, und es ist bei Renders ständig nötig. | Punkt gegen Median der Nachbarn, mit Schwelle. Örtlich, kleiner Radius. | S | sehr hoch |
| **Entrauschen** | Restrauschen aus dem Render, geführt von Albedo und Normale. | Intel Open Image Denoise (Apache 2.0, C-API) oder ein eigener Non-Local-Means. Blender entrauscht meist selbst, das ist also nur ein Nachgang. | L | mittel |
| **Stempel, Reparatur, inhaltsbasiertes Füllen** | Bereiche übermalen mit Inhalt von anderswo, Kratzer und Artefakte weg. | Stempel ist M. Inhaltsbasiert (PatchMatch) ist L. | M–L | mittel |
| **Hochskalieren** | Lanczos, oder KI, für Vorschau-Renders in kleiner Auflösung. | Lanczos ist S. KI ist L mit Modell. | S–L | mittel |

## 8. Licht und Atmosphäre aus Renderpässen

Diese Gruppe kann FrameFlip besser als jedes Bildprogramm, weil die Pässe da sind.

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Nebel aus Tiefe** | Farbe, Dichte, Anfang und Ende. Mit einem Positionspass auch als Bodennebel mit Höhe. | Punktweise mit Tiefe, ein Datenwerkzeug. | S | sehr hoch |
| **Konturen** | Linien an Sprüngen von Tiefe, Normale oder Kryptomatte, mit Breite und Farbe. Ergibt Toon- und Illustrationslooks, sauber und ohne Kantenflimmern. | Kantenerkennung auf den Pässen, örtlich, kleiner Radius. | M | sehr hoch |
| **Mehr Licht** | Den Lichtknoten gibt es. Dazu kämen mehrere Lichter, ein Randlicht (Rim) aus der Blickrichtung und farbige Lichter. | Ausbau des Lichtknotens. | M | hoch |
| **Umgebungsverdeckung nachträglich** | Dunkelt Ecken und Fugen ab, aus Tiefe und Normale (SSAO). | Örtlich, mittlerer Radius. | L | mittel |
| **Lichtsaum** | Der Hintergrund scheint um die Kanten des Vordergrunds, wie bei echter Optik. Macht Composites glaubwürdig. | Kryptomatte oder Alpha als Kante, Hintergrund weichgezeichnet darübergemischt. | M | hoch für Composites |
| **Strahlen, Streifen, Blendenfleck** | Anamorphe Streifen, Lichtstrahlen von einer Lichtquelle, Linsenflecken. | Radiale Weichzeichnung der Lichter. Streifen ist eine Richtungsvariante von Bloom. | M | mittel |
| **Himmel tauschen** | Den Himmel über Kryptomatte oder Alpha durch ein Bild ersetzen, mit Farbangleich an die Szene. | Überlagerung plus Maske gibt es. Neu wäre ein geführter Ablauf. | S | mittel |

## 9. Folge und Zeit

| Werkzeug | Was es tut | Umsetzung | Aufwand | Nutzen |
|---|---|---|---|---|
| **Schlüsselbilder für Regler** | Jeder Regler lässt sich über die Folge animieren, etwa eine Belichtung, die über 50 Bilder steigt. Das ist das größte fehlende Stück für eine Werkstatt für Folgen. | Werte werden zu Kurven über die Bildnummer, das Rezept bekommt Spuren. Braucht eine Zeitleiste in der Oberfläche. | L | sehr hoch |
| **Deflicker** | Siehe Tonwert. | | M | hoch |
| **Bewegungsspuren, Echo, Slit-Scan** | Siehe Glitch. | | M–L | mittel |
| **Zeitlupe mit Zwischenbildern** | Zwischenbilder aus dem Bewegungspass. | Die Pixel werden entlang der Vektoren geschoben, Löcher gefüllt. | L | mittel |

## 10. Arbeitsablauf

| Werkzeug | Was es tut | Aufwand | Nutzen |
|---|---|---|---|
| **Vergleichsansicht** | Geteilt oder Wischen zwischen Original und Ergebnis, oder zwischen zwei Ständen des Gradings. | S | hoch |
| **Looks und Vorlagen** | Ein Grading speichern, benennen, auf eine andere Folge legen, teilen. | M | hoch |
| **Schnappschüsse** | Mehrere Varianten eines Gradings nebeneinander halten und vergleichen. | M | mittel |

---

## Empfehlung: die ersten zehn

Nach Nutzen und Aufwand gereiht, mit Blick auf Blender-Renders und Folgen:

1. **Quadratischer und drehbarer Pinsel**, mit „Winkel folgt dem Strich“ (S). Wunsch des
   Nutzers, klein, und er macht den Maskenverlauf nicht kaputt.
2. **Objektgebundener Pinsel** über die Kryptomatte (S–M). Er löst „in Ecken und an
   Kanten malen“ grundsätzlicher als jede Pinselform.
3. **Tonwertkorrektur am Histogramm** mit Auto-Tonwert und Pipetten (M).
4. **Maske bearbeiten und Kanten verfeinern** (S–M).
5. **Glühwürmchen entfernen** (S). Klein, und bei Renders ständig gebraucht.
6. **Nebel aus Tiefe und Konturen** (S + M). Die Stärke der Pässe, sichtbar.
7. **Blaues Rauschen und Vorformung im Dither** (S). Größter Gewinn im Glitch-Bereich für
   wenig Arbeit.
8. **Kanaltrennung und Pixel Sort mit Pass-Schlüssel** (S + S–M).
9. **Clipping im Bild und Messgeräte** (S + M).
10. **Schlüsselbilder für Regler** (L). Das große Stück danach, zusammen mit Deflicker.

**Später, wenn es jemand braucht:** Paletten, Datamosh, Slit-Scan, KI-Segmentierung,
Entrauschen, inhaltsbasiertes Füllen.
