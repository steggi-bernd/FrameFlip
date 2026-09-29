# Atelier: Masken – Bestand und Vorschlag

Stand: 29. September 2026 · `feature/atelier` nach #89. Ein Vorschlag zur Entscheidung vor W3
(Auswahl), noch nichts davon ist gebaut.

## 1. Worum es geht

Aus dem Test: Die Maskenlogik wirkt komplex und verwirrend. Etwas durchsichtig zu machen ist
nicht gelungen. Der Wunsch: einen intuitiven, zerstörungsfreien Weg, Dinge auszumaskieren –
etwa über eine umgekehrte Maske.

Die Anordnung des Ateliers bleibt, wie sie ist (der Versuch mit neu geordneter Bedienung wurde
nicht übernommen). Der Vorschlag ändert deshalb Begriffe, Vorgaben und ein paar Aktionen, keine
Felder.

## 2. Bestand

**Was eine Maske ist.** Jede Ebene hat genau eine Maske (`LayerMask`):

- **Art:** keine, Helligkeit der Ebene, Helligkeit darunter, ein Pass, Verlauf, Farbbereich,
  Objekte aus der Kryptomatte, gemalt.
- **Umkehren**, dazu je nach Art ein Bereich mit weichen Kanten (C7), ein Farbfenster, die
  gewählten Objekte oder der Verlauf.
- **Wirkung**, ein Schalter mit zwei Bedeutungen:
  - „Farbe“: Die Maske sagt, wo die Korrektur der Ebene gilt; die Ebene selbst bleibt überall.
  - „Sichtbarkeit“: Die Maske sagt, wo die Ebene zu sehen ist.
- **Bei gemalten Masken** die Sperre „für alle Bilder“: ein Anstrich für die ganze Folge oder
  je Bild ein eigener.

**Wie sie rechnet.** Die Maske multipliziert die Deckung der Ebene (`LayerComposer`: Deckkraft ×
Maske). Die Deckung des fertigen Bildes ist das Maximum der Deckungen aller Ebenen.

**Ebenenarten:** Pass, Bild, Einstellungsebene (eine Korrektur mit Maske, ohne eigenes Bild),
Gruppe (auch isoliert, C2b).

**Knotenmodus:** Masken sind Knoten, sie lassen sich rechnen (Maskenrechnung: addieren,
abziehen, umkehren), und „Ausschneiden“ setzt die Deckung eines Bildes aus einer Maske. Der
Knotenmodus bleibt, wie er ist.

**Wege zu einer Maske im Stapel – sechs:**

1. Die Maskenart im Abschnitt „Maske“ der Ebenenliste.
2. Der Knopf „Objektmaske“ (R4): Ebene wählen, Objekt anklicken – die Maske dieser Ebene.
3. Auswählen, Objekt anklicken, „+ Korrektur“ (C3b): eine neue Einstellungsebene mit der Auswahl
   als Maske.
4. Der Pinsel: Hat die gewählte Ebene keine gemalte Maske, legt der erste Strich eine neue
   Einstellungsebene „Maske“ an (`MakeMaskLayer`).
5. „+ Einstellung“, danach eine Maskenart wählen.
6. Im Knotenmodus: Menü und Schnellfeld („Objekt als Maske“).

## 3. Was verwirrt

1. **Eine Maske, zwei Bedeutungen.** Ob sie die Korrektur begrenzt oder die Ebene selbst, steht
   an einem Schalter weiter unten. Wer nur die Maske sieht, weiß nicht, was sie tut.
2. **Drei Wege zu „nur hier korrigieren“.** Maske der Ebene mit Wirkung „Farbe“,
   Einstellungsebene mit Maske, Auswahl → „+ Korrektur“. Das Ergebnis sieht gleich aus, steht
   aber verschieden in der Liste.
3. **Der Pinsel wechselt still sein Ziel.** Wer auf einer Ebene mit Objektmaske malt, um sie zu
   verfeinern, bekommt eine neue Ebene „Maske“ dazu. Die Objektmaske bleibt, wie sie war.
4. **Eine Maske, eine Zutat.** Objekte und Pinsel lassen sich im Stapel nicht in einer Maske
   verbinden – nur der objektgebundene Pinsel begrenzt einen Strich auf ein Objekt.
5. **„Freistellung“ stellt nicht frei.** So heißt der Regler, der den grauen Deckungsschleier
   einer Datei wegschneidet. Wer „freistellen“ sucht, landet dort.
6. **Durchsichtig gibt es im Stapel nicht.** Die Deckung des Bildes ist das Maximum aller
   Ebenen. Eine Maske auf der untersten Ebene macht dort nur durchsichtig, wenn keine andere
   Ebene deckt – bei Pässen deckt fast immer eine. Und die Vorschau zeigt Durchsichtiges vor
   fast schwarzem Grund, man sieht es kaum.
7. **Umkehren nur an der Ebene.** Eine Auswahl lässt sich nicht umkehren, bevor aus ihr etwas
   wird.

## 4. Vorschlag

**Leitsatz:** Eine Maske sagt, wo etwas gilt – und sie gehört genau einem Ding.

### M1 Ausblenden als eigene Ebenenart

Eine neue Art neben der Einstellungsebene: **„Ausblenden“**. Ihre Maske sagt, was vom
Darunterliegenden verschwindet; umgekehrt, was bleibt.

- **Zerstörungsfrei:** eine Zeile im Stapel, mit Auge, verschiebbar, jederzeit änderbar. Sie
  wirkt auf alles unter ihr; in einer Gruppe nur auf die Gruppe.
- **Rechnung:** Die Deckung unter ihr wird mit „1 − Maske“ multipliziert. Was darüber liegt,
  bleibt sichtbar. Das ist dieselbe Rechnung wie „Ausschneiden“ im Knotenmodus; beim Umwandeln
  wird aus der Zeile dieser Knoten.
- **Wege dahin:**
  - Auswählen, Objekt anklicken: In der Zielzeile stehen neben „+ Korrektur“ zwei neue Knöpfe,
    **„Ausblenden“** und **„Nur das behalten“** (dasselbe umgekehrt), dazu **„Umkehren“** für
    die Auswahl selbst (Punkt 7).
  - „+ Einstellung“ bekommt die Wahl „Korrektur“ oder „Ausblenden“.
- **Sehen:** Durchsichtiges steht in der Vorschau vor einem Schachbrett – nur in der Anzeige.
- **Ausgabe:** PNG, TIFF und EXR tragen die Durchsichtigkeit mit. JPEG und Video kennen keine;
  dann steht an der Exportleiste ein Hinweis wie beim 8-Bit-Hinweis.

Die Alternative wäre eine dritte Wirkung am Schalter jeder Maske („Farbe“, „Sichtbarkeit“,
„Ausblenden“). Das machte Punkt 1 schlimmer, deshalb nicht empfohlen.

### M2 Der Pinsel verfeinert die Maske, die da ist

Eine Maske darf mehrere Zutaten haben: ihre Art (Objekte, Bereich, Farbe, Verlauf) und darüber
einen Anstrich, der hinzufügt oder wegnimmt (Alt, rechte Maustaste – wie heute beim Malen).

- Wer eine Ebene mit Objektmaske wählt und malt, verfeinert genau diese Maske.
- Eine neue Ebene „Maske“ entsteht nur, wenn gar keine Ebene gewählt ist.
- Das ist eine Änderung am Datenmodell (`LayerMask` bekommt einen Anstrich neben ihrer Art). Alte
  Projekte bleiben gültig: ohne Anstrich rechnet die Maske wie bisher.

### M3 Die Maske sagt, was sie tut

- In der Ebenenzeile steht es neben ◐, etwa „Objekte: Lampe · wo korrigiert wird“ oder
  „… wo sie zu sehen ist“.
- **Vorgaben je Ebenenart:**
  - Einstellungsebene: immer „wo korrigiert wird“ – dort gibt es den Schalter nicht.
  - Bild und Pass: „wo sie zu sehen ist“; der Schalter bleibt für den Sonderfall, eine
    Korrektur nur an einer Stelle der Ebene.
- Bei einer Ebene, deren Korrektur heute schon über die Maske begrenzt wird, ändert sich nichts
  – die Vorgabe gilt nur für neue.

### M4 Namen

| heute | vorgeschlagen |
|---|---|
| Freistellung (Deckungsschleier einer Datei) | Deckungsschleier |
| Wirkung: Farbe / Sichtbarkeit | wirkt auf: Korrektur / Sichtbarkeit |
| für alle Bilder | gilt für: alle Bilder / dieses Bild |

## 5. So sähen die Abläufe aus

- **Ein Objekt durchsichtig machen:** Auswählen → Lampe anklicken → „Ausblenden“. Oben im
  Stapel steht „Ausblenden: Lampe“, in der Vorschau das Schachbrett, wo die Lampe war. Kante
  weicher über den Bereichsregler, Export als PNG.
- **Nur das Objekt behalten:** dasselbe mit „Nur das behalten“ – oder „Umkehren“ an der Zeile.
- **Eine Objektmaske nachbessern:** die Ebene wählen, Pinsel, mit Alt dort wegnehmen, wo die
  Kryptomatte zu viel erwischt hat. Keine neue Ebene.
- **Nur den Himmel korrigieren:** Farbbereich oder Auswahl → „+ Korrektur“ → Einstellungsebene,
  ihre Maske sagt „wo korrigiert wird“.

## 6. Schnitte

1. **W3a Ausblenden:** Ebenenart, Rechnung im Stapel und beim Umwandeln, Schachbrett,
   Export-Hinweis, die drei Knöpfe an der Auswahl.
2. **W3b Die Maske sagt, was sie tut:** Beschriftung in der Zeile, Vorgaben je Ebenenart, Namen
   (M3, M4).
3. **W3c Pinsel verfeinert jede Maske:** Anstrich über jeder Maskenart (M2).
4. Danach der Rest von W3: Auswahl aus Tiefe, Normale und Bewegung, Zauberstab, Maske über die
   Folge tragen.

## 7. Entscheidungen

- **M1:** Ausblenden als eigene Ebenenart (empfohlen) – oder als dritte Wirkung am Schalter?
- **M2:** Der Pinsel verfeinert die vorhandene Maske der gewählten Ebene, statt still eine neue
  Ebene anzulegen (empfohlen)?
- **M3:** Wirkung vorgegeben je Ebenenart, Schalter nur noch bei Bild und Pass?
- **M4:** Die Namen so?
