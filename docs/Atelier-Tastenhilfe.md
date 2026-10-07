# Atelier: Tastenhilfe in der Statuszeile

Stand: 29. September 2026 · Zweig `feature/tastenhilfe`, auf `fix/umkehren-vorher`.

## Worum es geht

Vieles im Atelier geht nur mit Zusatztaste oder einer bestimmten Maustaste: Alt+Klick auf das
Auge zeigt eine Ebene allein, Umschalt+Klick mit dem Pinsel zieht eine gerade Linie, die rechte
Maustaste radiert. Nichts davon war zu sehen, und so blieb es ungenutzt. Beim „Umkehren“ der
Maske war genau das der Grund, warum es nicht gefunden wurde.

Die Idee des Nutzers: unten in der Statuszeile, dort, wo das Kürzel für die Vorschau steht,
je nach Werkzeug zeigen, was Tasten und Maus gerade tun.

## Wie es aussieht

- **Wo:** in der Statuszeile des Hauptfensters, links. Im Atelier ersetzt die Tastenhilfe dort
  die Angaben des Players (Format, Dekodierung, fps, Bereich), die im Atelier nichts sagen.
  Rechts bleiben das globale Kürzel und der Stand des Relays.
- **Was:** höchstens neun Hinweise, je „Taste Handlung“, die Taste in der Akzentfarbe.
- **Wonach es sich richtet:**
  - das Mauswerkzeug, beim Pinsel auch die Fläche (Strich, Rechteck, Ellipse, Lasso);
  - wo die Maus steht: über der Ebenenliste die Kürzel der Liste, über dem Graphen die des
    Knoteneditors;
  - ob eine Pipette oder die Objektmaske auf einen Klick ins Bild wartet: dann nur „Klick“
    und „Esc“;
  - ob die Kurven zu sehen sind (Strg+Klick setzt einen Punkt) und ob sich etwas mit Esc
    beenden lässt (Isolieren, Betrachter);
  - gehaltene Zusatztasten: Wer Alt, Strg oder Umschalt hält, sieht nur noch, was mit dieser
    Taste geht – wie in Blender.
- **Ohne Bild:** nur Strg+K für die Suche.

## Woher die Hinweise stammen

Jeder Hinweis ist aus dem Code abgelesen, nicht ausgedacht. Die Tabelle steht an einer
Stelle, `AtelierHints`.

| Lage | Hinweise | Im Code |
|---|---|---|
| Verschieben | Ziehen, Griff (Größe), Drehgriff, Rad zoomt, Mitteltaste+Ziehen verschiebt die Ansicht | `PlacementDrag` (Griffe, `DragHandle.Rotate`), `OnViewportWheel`, `OnViewportDown` |
| Auswählen | Klick wählt, Umschalt+Klick fügt hinzu, Alt+Klick nimmt weg | `AtelierPage.Crypto` (`PickMode.Add`/`Remove`) |
| Zuschneiden | Griff passt den Ausschnitt an | `AdornerMode.Crop` |
| Hand | Ziehen verschiebt die Ansicht | `OnViewportDown` |
| Pinsel, Strich | Ziehen malt, rechts oder Alt radiert, Umschalt+Klick gerade Linie, Umschalt+Rad dreht die Spitze, Strg+Rad Abstand, Rad zoomt | `PlacementAdorner.Paint` (`_erasing`, `_lineFrom`), `OnViewportWheel` |
| Rechteck, Ellipse | Ziehen zieht auf, Umschalt gleichmäßig (Quadrat, Kreis), Alt nimmt weg | `AreaPath(even)`, `_erasing` |
| Lasso | Ziehen zieht den Umriss, Alt nimmt weg | wie oben |
| Pipette | Klick liest; bei einer Farbbereichsmaske erweitert Umschalt+Klick | `AtelierPage.Pipette.ReadAt` |
| Ebenenliste | Klick, Ziehen, Alt+Klick ● (allein), Alt+Klick ◐ (Maske), Alt+Umschalt+Klick ◐ (Schleier), Alt+↑↓, Strg/Umschalt+Klick, Strg+J, Strg+G, Strg+Umschalt+G, Entf | `LayerPanel` (`OnListKeyDown`, Isolieren C6b, Mehrfachwahl C2b) |
| Graph | Doppelklick (Schnellfeld), Ziehen, Entf, Strg+D, Strg+Z, Strg+Y | `NodeEditor`, `HandleUndoKey` |
| immer | O Original, J Sichthilfe, V W C H B I N Werkzeuge, Strg+K Suche, Strg+Klick Kurvenpunkt (bei sichtbaren Kurven), Esc | `HandleToolKey` |

## Wie die Seite es meldet

Die Seite liest ihre Lage alle 200 ms ab, solange sie zu sehen ist, und meldet nur, wenn
sich die Hinweise ändern (`AtelierPage.Hints`). Ein Dutzend Zustände mit je eigenem Weg, sich
zu ändern, hätten ein Dutzend Ereignisse gebraucht; der regelmäßige Blick ist billiger und
verpasst keinen. Das Hauptfenster zeigt die Meldung, solange das Atelier offen ist.

## Proben

- `AtelierHintInvariants` (16 Zusicherungen):
  - je Lage die richtigen Hinweise (Auswählen, Pinsel, Rechteck, wartende Pipette,
    Ebenenliste, ohne Bild, Kurvenpunkt nur bei sichtbaren Kurven);
  - Alt gehalten: nur noch, was mit Alt geht;
  - über alle Werkzeuge, Flächen, Zusatztasten und Lagen: jede Lage hat Hinweise, höchstens
    neun, und jeder Hinweis und jede Taste hat einen Text;
  - auf der Seite: Jede Werkzeugtaste der Hilfe wählt ihr Werkzeug, J und O wirken, ein
    Werkzeugwechsel wird gemeldet, ohne Änderung kommt keine Meldung.
- Nachbargruppen: Seite, Werkzeugleiste, Texte, Einstellungen und die Gruppen, die das
  Hauptfenster aufbauen (Dashboard, Auswahl, Live, Ecken, Zuschauerkarte) – 245
  Zusicherungen grün.
- Echte Instanz: Im Atelier steht unten „Ziehen Ebene verschieben · Griff Größe · Drehgriff
  drehen · Rad zoomen · Mitteltaste+Ziehen Ansicht verschieben · O Original · J Sichthilfe ·
  V W C H B I N Werkzeuge · Strg+K Suche“; die Angaben des Players sind dort ausgeblendet.
