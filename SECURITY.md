# Sicherheitslücken melden

Wenn du eine Schwachstelle in FrameFlip oder im Relay gefunden hast: **danke.** Melde sie
bitte zuerst mir, nicht der Öffentlichkeit — dann kann ich sie beheben, bevor sie jemand
ausnutzt.

## Wohin

<!-- AUSFÜLLEN: eine Adresse, die du liest. Sie wird öffentlich sein. -->
**E-Mail:** `<EINTRAGEN>`

Wenn du verschlüsselt schreiben möchtest, nenne mir das in einer ersten Zeile, dann
schicke ich dir einen Schlüssel.

## Was hilft

- was du gefunden hast, und woran es sichtbar wird
- welche Fassung (Commit oder Versionsnummer)
- der kürzeste Weg, es nachzuvollziehen
- was ein Angreifer damit erreichen könnte

Ein Prüfstand-Fall, der den Fehler zeigt, ist willkommen, aber keine Bedingung.

## Was du erwarten kannst

| | |
|---|---|
| Eingangsbestätigung | innerhalb von 72 Stunden |
| Erste Einschätzung | innerhalb von 7 Tagen |
| Behebung | so schnell es geht — bei ernsten Funden zuerst |
| Nennung | gern, wenn du möchtest |

Ich betreibe das in meiner Freizeit. Es gibt kein Kopfgeld, aber es gibt eine ehrliche
Antwort und eine Behebung.

## Bitte nicht

- fremde Räume stören oder fremde Daten anfassen
- Verfügbarkeitsangriffe auf `relay.steggi-matrix.work`
- veröffentlichen, bevor die Lücke zu ist — sag mir Bescheid, wenn dir 90 Tage zu lang
  werden, dann finden wir etwas

Der Relay ist MIT-lizenziert. **Zum Prüfen brauchst du meinen Server nicht** — starte
deinen eigenen:

```bash
git clone <relay-repo> && cd frameflip-relay && docker compose up -d --build
```

## Was ohnehin gilt

Der Relay kann die Inhalte nicht lesen — sie sind Ende zu Ende verschlüsselt, der
Schlüssel entsteht am PC und geht nie durchs Netz. Ein Fund am Server ist deshalb selten
ein Fund an den Daten. Der interessantere Angriffsweg ist die Krypta auf beiden Seiten:
`FrameFlip/Remote/` und `web/teile/krypto.js`, beschrieben in `PROTOCOL.md` des Relays.
