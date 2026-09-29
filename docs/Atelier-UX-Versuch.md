# Atelier: Versuch mit neu geordneter Bedienung

Stand: 29. September 2026 · Zweig `experiment/inspektor`, abgezweigt von `feature/angleichen`
(W2f). Ein Versuch zum Ausprobieren, kein Schnitt für `feature/atelier`. Was sich bewährt,
kommt später in eigenen Schnitten mit Charakterisierung (U1 bis U4 der Bewertung), was nicht,
fällt weg.

Grundlage: die UX-Analyse vom 28. September, ihre Bewertung und die Mockups „Atelier –
Bedienung neu geordnet“. Umgesetzt sind rechts, unten und der
Streifen über dem Bild aus „Vorschlag: Inspektor und Ebenen zugleich sichtbar“, dazu der
Katalog. Der Knotenmodus bleibt, wie er ist.

## Entscheidungen

- **E1 Palette:** nur noch Favoriten neben „+ Effekt“ und der Katalog.
- **E2 Gesamtbild:** Die Zeile „Gesamtbild“ oben in der Ebenenliste ersetzt den Schalter
  „Ganzes Bild / Ebene“. Gewählt heißt sie: keine Ebene – die Regler gelten dem ganzen Bild,
  wie bisher, wenn nichts markiert ist. Abwählen geht jetzt über die Zeile, Esc in der Liste
  oder einen Klick ins Leere.
- **E3 Anordnung:** Der Versuch testet die neue Anordnung mit eigener Konfiguration.
- **E4 Ebenen links auf breiten Monitoren:** offen.

## Starten

`Versuch starten.cmd` im Wurzelordner des Zweigs startet den Debug-Build mit eigener
Konfiguration unter `%APPDATA%\FrameFlip-Versuch`. Einstellungen, Anordnung, zuletzt
geöffnete Folgen und die Kopplung der normalen App bleiben unberührt. Mit eigener
Konfiguration darf die Instanz neben der normalen laufen (eigene Einzelinstanz-Sperre). Die
Blender-Brücke bekommt, wer zuerst läuft – deshalb den Versuch nach der normalen App starten.

## Was anders ist

- **Rechts:** Verteilung, darunter Einstellungen, darunter Ebenen – drei Gruppen, alle
  sichtbar. Ein Klick auf einen Reiter holt ihn nur nach vorn; eingeklappt wird über den
  Pfeil am Rand jeder Gruppe.
- **Einstellungen:**
  - Der Kopf sagt, worauf sie wirken: „Gesamtbild“ oder „Gesamtbild › Lampe · Ebene“, im
    Knotenmodus „Graph › Knoten“.
  - Die Palette ist weg. Dafür „+ Effekt auf … “ (öffnet den Katalog) und die Favoriten.
  - Ist eine Ebene gewählt, stehen ihre Einstellungen oben: Mischung, Tönung, Deckkraft,
    Belichtung, Platzierung, Maske (aufgeklappt, „Umkehren“ beschriftet statt „±“),
    Bilddatei. Die Ebenenliste ist dadurch nur noch Liste.
- **Ebenen:** die Zeile „Gesamtbild“ oben; nach dem Laden ist sie gewählt.
- **Streifen über dem Bild:**
  - Das Ziel wie bisher.
  - Bei einer Ebene mit Maske: Ergebnis · Maske · Überlagerung (dasselbe wie Alt+Klick und
    Alt+Umschalt+Klick auf ◐, jetzt sichtbar).
  - „Original“: Ein kurzer Klick schaltet auf Vorher und bleibt dort, mit Abzeichen am
    Bild; der nächste Klick, die Taste O oder jede Änderung am Bild schaltet zurück.
    Gedrückt halten ist weiter nur ein Blick.
- **Unten:**
  - Die Folgenleiste: Bild/Folge (aus dem Band hierher), Bildnummer mit ‹ ›, wofür die
    Einstellungen gelten, das Format, „8 Bit!“, wenn der Hinweis gilt, der Fortschritt
    eines Exports und „Ausgeben …“.
  - „Ausgeben …“ klappt die bisherige Ausgabe auf – Format, Zielordner, Schnell-Export,
    Sequenz, Stopp. Sie ist kein Feld der Andockfläche mehr.
  - Darunter die Statuszeile in 11 statt 9 Punkt.
- **Katalog (Strg+K, „+ Effekt“, der Suchknopf oben):**
  - Links Favoriten, zuletzt benutzt, alle, die Kategorien des Bands.
  - Jeder Eintrag sagt vor dem Klick „hinzufügen“, „bearbeiten“ (schon im Stapel) oder
    „wählen“ (Pinsel).
  - Gesperrt mit Grund: nur fürs Gesamtbild (mit „aufs Gesamtbild legen“), fehlender Pass,
    nur im Knotenmodus.
  - Oben das Ziel; ein Klick darauf nimmt das Gesamtbild. Strg+Eingabe legt aufs Gesamtbild.
  - Der Stern macht einen Eintrag zum Favoriten; gespeichert in der Konfiguration
    (`AtelierFavourites`, Vorgabe Tonwert, Kurven, Weißabgleich).
  - Im Knotenmodus entsteht wie bisher ein Knoten hinter dem gewählten.

## Umgesetzt durch Umhängen

Der Bild/Folge-Schalter, der Vergleichsknopf, die Einstellungen der Ebene und die Ausgabe
sind dieselben Bedienelemente wie bisher, nur an anderer Stelle eingehängt
(`AtelierPage.Experiment.cs`). Ihre Logik ist unverändert.

## Proben

- `ExperimentInvariants` (12 Zusicherungen): getrennte Gruppen ohne Ausgabe-Feld; nach dem
  Laden das Gesamtbild; eine Ebene gewählt heißt Ziel mit ihren Einstellungen im Inspektor;
  Katalog an einer Ebene (Klarheit gesperrt mit Ausweg, Kurven „hinzufügen“, danach
  „bearbeiten“); „Gesamtbild“ gibt Klarheit frei; „+ Effekt“ öffnet den Katalog, die Suche
  findet; ein Stern speichert einen Favoriten; O rastet Vorher ein, eine Änderung schaltet
  zurück; die Folgenleiste trägt Format und Bild/Folge.
- Nachbargruppen: 19 von 485 Zusicherungen weichen ab, alle in `DockLayoutInvariants`,
  `DockToolInvariants` und `PropertiesFollowInvariants`. Sie halten die alte Grundanordnung
  fest (Farbe und Ebenen als Reiter einer Gruppe, die Ausgabe als eigenes Feld, der zweite
  Reiterklick klappt zu). Das ändert der Versuch absichtlich; angepasst werden sie erst,
  wenn etwas davon übernommen wird.
- Echte Instanz mit eigener Konfiguration und synthetischer Folge: startet ohne Fehler, die
  Bedienelemente stehen an ihren neuen Plätzen (geprüft über die Textausgabe).

## Nicht im Versuch

- Knotenmodus: bleibt, wie er ist (Ansicht an das Mauswerkzeug gebunden).
- Masken-Miniatur als eigenes Ziel und die geteilte Vorher/Nachher-Ansicht.
- Transparent machen einer Auswahl: Im Stapel gibt es dafür heute keinen Weg, nur im
  Knotenmodus „Ausschneiden“. Das ist eine eigene Funktion und kommt als eigener Schnitt.
