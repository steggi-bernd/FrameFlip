# Atelier: Knoten in Gruppen (Versuch)

Stand: 6. Oktober 2026, Zweig `experiment/knoten-gruppen`. Ein Versuch zum Ausprobieren,
noch nicht übernommen.

## Wunsch

Aus der Rückmeldung zum Masken-Konzept (`docs/Atelier-Masken-Konzept.md`): Der Graph soll
aufgebaut sein wie in Blender. Ganz links stehen alle Quellbilder mit kleiner Vorschau, nach
rechts findet der Aufbau statt. Jeder Pass bekommt eine eigene kleine Gruppe, und die Gruppen
verbinden sich miteinander. Ziel ist mehr Übersicht: Man soll sehen, welche Einstellungen an
welcher Ebene geändert wurden, und Ebene und Masken als Einheit wahrnehmen, ohne die heutigen
Möglichkeiten im Graphen zu verlieren.

## Was der Versuch zeigt

- **Links die Quellen.** Jeder Pass ist ein eigener Knoten mit Vorschau, benannt wie der Pass.
  Liest ein Pass mehrere Ebenen oder eine Passmaske, gibt es ihn trotzdem nur einmal. Darüber
  steht die Datei selbst mit Bild und Renderdaten (Tiefe, Vektor, Normale), Bilddateien und
  die schwarze Leinwand stehen in derselben Spalte. Jede Quelle steht auf der Höhe dessen,
  was sie liest.
- **Je Ebene eine Gruppe.** Um alles, was nur einer Ebene zuarbeitet (Platzieren, Belichtung,
  Korrektur, Begrenzen, Maske, Mischen), liegt ein Rahmen. Im Kopf stehen Name, Mischart und
  Deckkraft wie in der Ebenenliste. Die Gruppen stehen untereinander, oben die oberste Ebene.
- **Zwei Bahnen in der Gruppe.** Oben läuft der Bildweg von links auf das Mischen zu, darunter
  liegen die Masken.
- **Die Mischen in einer Spalte.** Alle Gruppen enden an derselben Stelle. Dort reicht jedes
  Mischen sein Bild an das nächste weiter, das ist die Verbindung der Gruppen.
- **Gruppen und Schnittmasken verschachtelt.** Der Rahmen einer Gruppe umschließt die Rahmen
  ihrer Ebenen, der Rahmen eines Trägers den des Angeschnittenen.
- **Rechts das Gesamtbild.** Ein eigener Rahmen in Gelb: alles hinter den Ebenen bis zur
  Ausgabe. Nach sechs Knoten wird umbrochen.
- **Rahmen am Kopf packen.** Ein Klick auf den Kopf wählt die Ebene, Ziehen verschiebt die
  ganze Gruppe samt der Ebenen darin. Rückgängig nimmt den Zug zurück. Die Rahmen ergeben
  sich aus den Kabeln und wandern mit, wenn man einzelne Knoten verschiebt.
- **Masken knapp.** Eine Maske zeigt nur die Eingänge, die sie liest: Helligkeit die Ebene,
  Untergrund und Farbbereich den Untergrund, eine Passmaske ihren Pass. Verlauf, Gemalt und
  Objekte zeigen keinen. Die Kabel an verborgenen Eingängen stecken weiter, tun aber nichts
  und liefen vorher quer durch die Gruppen. Die Vorschau einer Maske ist kleiner, der Knoten
  rund ein Drittel niedriger.
- **Oben oben.** Am Mischen steht „Oben“ zuoberst, darunter „Unten“, dann „Faktor“. Vorher
  stand „Unten“ oben. Gespeichert wird nach Namen, alte Graphen lesen sich unverändert; der
  Bildweg und das, was ein stummes Mischen durchreicht, bleibt „Unten“.
- **Eine neue Maske kommt an die gewählte Ebene.** Gewählt sein kann ihr Mischen, ein Knoten
  ihrer Gruppe oder ihre Quelle links. Der Pinsel und „Objekt hier als Maske“ legen die Maske
  dann in den Faktor dieser Ebene, sie begrenzt, wo die Ebene zu sehen ist. Vorher entstand
  immer eine neue Maskenebene oben auf dem Stapel, die auf alles darunter wirkte. Hat die
  Ebene schon eine Maske, kommt die neue über eine Maskenrechnung dazu (das Größere: sichtbar,
  wo eine der beiden es sagt). Der Pinsel findet die gemalte Maske auch hinter dieser Rechnung.
  Ohne gewählte Ebene bleibt es die Maskenebene, ebenso für eine Auswahl, an der man Farbe
  dreht – dort sagt die Maske, wo korrigiert wird.

## Alte Graphen

Ein Graph aus der Zeit davor wird beim Öffnen einmal umgestellt: Jeder Pass, der aus dem
einen Dateiknoten kam, bekommt seine eigene Quelle, und alles wird in Gruppen angeordnet.
Das Bild bleibt Byte für Byte dasselbe, und jeder Pass wird weiter einmal gelesen. Danach
bleibt die Lage, wie man sie schiebt. „Anordnen“ (⊞ unten rechts) ordnet jederzeit neu.

Neue Ebenen, Einstellungsebenen und Maskenebenen ordnen den Graphen neu an, damit die neue
Ebene ihre Gruppe an der richtigen Stelle bekommt. Verschieben, Löschen und Verdoppeln
taten das schon vorher. Eine von Hand geschobene Lage geht dabei verloren.

## Modell

- `RenderNode.Only`: ein Dateiknoten, der nur einen Ausgang zeigt, also die Quelle eines
  Passes. Null ist die ganze Datei. Beim Speichern fehlt das Feld, wenn es leer ist.
- `NodeGraph.Layout`: wie zuletzt angeordnet wurde (0 in Spalten, 1 in Gruppen). Nur ein
  Graph mit 0 wird beim Öffnen umgestellt.
- `NodeGroups`: teilt den Graphen in Quellen und Gruppen. Die Gruppen werden nicht
  gespeichert, sondern aus `LayerEdits.Chains` und `LayerEdits.Branch` abgeleitet: Ein Knoten
  gehört zur innersten Ebene, deren Zweig ihn enthält.
- `NodeLayout.Arrange`: die Anordnung oben, statt der bisherigen in umbrochenen Spalten.
- `NodeLayout.ShownInputs`, `MaskNode.Reads`: welche Eingänge eine Maske zeigt.
- `AtelierPage.LayerAt`, `AttachMask`: welche Ebene gemeint ist, und die Maske an ihr.
- Eine Ebene zu löschen nimmt jetzt auch die Quelle ihres Passes mit, wenn ihn sonst
  niemand liest. Die Datei bleibt.

Die normale App ignoriert `Only` und `Layout`. Ein Graph aus dem Versuch rechnet dort
dasselbe, zeigt aber mehrere Dateiknoten mit je allen Ausgängen.

## Versuchsbetrieb

`Versuch starten.cmd` im Worktree startet den Debug-Build mit eigener Konfiguration unter
`%APPDATA%\FrameFlip-Versuch-Knoten` und setzt `FRAMEFLIP_VERSUCH`. Damit liest das Atelier
die Projekte am Quellordner, schreibt aber nur unter seinen eigenen Einstellungen
(`projects\…`). Die Projektdateien der normalen App bleiben, wie sie sind, auch wenn der
Versuch einen Graphen umstellt. Bilder öffnet man über „Bild öffnen“ oder per Ziehen.

Am besten nach der normalen App starten, dann behält diese die Blender-Brücke.

## Rückmeldung vom 7. Oktober

- Übersichtlicher als vorher.
- „Oben“ stand unten und „Unten“ oben – getauscht.
- Masken kamen immer an die Bildebene, nicht an den gewählten Pass, und landeten als neue
  Gruppen ganz oben – jetzt an die gewählte Ebene.
- Der Maskenblock war zu groß – knapper, ohne die Eingänge, die er nicht liest.
- „Original“ zeigte das Bild der Datei ohne Pässe und mit falscher Farbe. Das betraf auch die
  normale App und ist dort behoben (`fix/original-mit-ebenen`): Das Original zeigt dieselben
  Ebenen ohne eigene Korrekturen. Der Versuch hat den Stand mit übernommen.

## Noch nicht drin

- Rahmen einklappen (zugeklappt eine Zeile mit dem, was geändert ist).
- Die benannten Einfügestellen aus dem Mockup („vor der Ebene“, „an der Maske“, „nach der
  Maske“).
- Den Pass einer Quelle umschalten, ohne neu zu verkabeln.
- Die Kabel zwischen den Mischen laufen als Bogen von einem Mischen zum darüber, weil beide
  in derselben Spalte stehen.

## Prüfungen

- `NodeGroupInvariants`: eine Gruppe je Ebene in der Folge der Ebenenliste, Quellen in
  keiner Gruppe, Verschachtelung, Anordnung (Quellen links, Mischen in einer Spalte,
  nichts überdeckt etwas), Rahmen umgeben ihre Knoten, nebeneinander ohne Überlappung,
  Ziehen am Kopf verschiebt genau die Gruppe. Dazu „Oben“ oben, knappe Masken, und über die
  echte Seite: Mit gewählter Quelle oder gewähltem Platzieren kommt die Maske in den Faktor
  der Ebene, eine zweite kommt dazu, der Pinsel findet die gemalte wieder, ohne Wahl entsteht
  eine Maskenebene.
- `NodeParityInvariants`, „Quellen am linken Rand“: eigene Quellen und ein umgestellter
  alter Graph rechnen byte-gleich mit dem Stapel und lesen dasselbe.
- `NodeModeInvariants`, „ein alter Graph wird beim Öffnen in Gruppen angeordnet“: über die
  echte Seite, samt Speichern.
- `AtelierProjectInvariants`, „der Versuch liest das Projekt und lässt es stehen“.
- Oberflächenreihe: `Knoten-Gruppen.png` und `Knoten-Gruppen-ganz.png` aus einem
  synthetischen Graphen.
