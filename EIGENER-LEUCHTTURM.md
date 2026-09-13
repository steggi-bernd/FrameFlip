# Einen eigenen Leuchtturm betreiben

FrameFlip kommt mit einem vorbelegten Relay. **Er ist ein Angebot, keine
Voraussetzung.** Wer ihm nicht vertrauen will — und das ist eine vernünftige Haltung
gegenüber fremder Infrastruktur —, betreibt in ein paar Minuten seinen eigenen.

Das ist kein Trostpreis: Der Relay ist derselbe Code, unter derselben MIT-Lizenz, und
FrameFlip merkt keinen Unterschied.

## Was der Relay tut

Er bringt zwei Geräte zusammen und reicht Bytes zwischen ihnen weiter. Er entschlüsselt
nichts, speichert nichts, deutet nichts. Sein gesamter Zustand ist „welche zwei
Verbindungen gehören zusammen", und der stirbt mit den Verbindungen.

Deshalb lohnt sich das Selbstbetreiben auch ohne Misstrauen: Ein Dienst, der nichts
weiß, ist auch einer, der nichts kaputtmachen kann. Zu betreiben ist er entsprechend
anspruchslos — unter 128 MB Arbeitsspeicher, ein einzelnes statisches Binary in einem
leeren Container.

## Was du brauchst

- einen Rechner, der aus dem Internet erreichbar ist (ein kleiner VPS genügt)
- einen Namen, der darauf zeigt
- Docker und einen Reverse-Proxy mit TLS

Ein Raspberry Pi zu Hause tut es auch, solange er erreichbar ist. Wichtig ist nur, dass
**beide** Geräte hinkommen — genau das ist ja der Zweck.

## Aufsetzen

```bash
git clone <relay-repo> frameflip-relay
cd frameflip-relay
docker compose up -d --build
```

Der Dienst **veröffentlicht bewusst keinen Port**. Erreichbar wird er allein über den
Reverse-Proxy, der auch TLS beisteuert. Für Caddy genügt:

```caddy
relay.example.org {
    reverse_proxy frameflip-relay:8080
}
```

WebSocket-Aufstiege braucht Caddy 2 nicht eigens behandelt — es reicht sie durch.

Prüfen:

```bash
curl -s https://relay.example.org/health
```

Antwortet `{"ok":true,"rooms":0}`, steht er.

## FrameFlip darauf zeigen lassen

**Einstellungen → Relay-Adresse** → `relay.example.org` eintragen. Nur der Wirtsname,
wahlweise mit Port — ohne Schema, ohne Pfad. FrameFlip baut daraus immer eine
`wss`-Adresse; ein Herunterstufen auf Klartext ist nicht vorgesehen.

Danach die Kopplung mit dem Handy **neu durchführen**: Der Raum leitet sich aus dem
Kopplungsschlüssel ab, und das Handy sucht ihn beim alten Wirt.

## Stellschrauben

| Variable | Voreinstellung | Bedeutung |
|---|---|---|
| `RELAY_ADDR` | `:8080` | Lauschadresse |
| `RELAY_MAX_MESSAGE` | `1048576` | größte einzelne Nachricht, in Bytes |
| `RELAY_SEND_QUEUE` | `32` | Nachrichten, die für eine langsame Gegenseite gehalten werden |
| `RELAY_IDLE_SECONDS` | `90` | Verbindung ohne Lebenszeichen wird getrennt |
| `RELAY_PING_SECONDS` | `25` | Takt der Lebenszeichen |

Die Grenze von einem Mebibyte ist kein Geiz: Vorschaubilder in Handygröße liegen bei
100–300 KB, und ein größerer Wert machte den Raum als Ablage brauchbar. Genau das soll
er nicht sein.

## Die Zuschauerseite dazu

Willst du auch die Browserseite selbst ausliefern, liegt sie in `web/`. Die Anleitung
steht in [web/LIESMICH.md](web/LIESMICH.md). Ein Punkt ist dabei nicht verhandelbar:
Sie **muss vom selben Herkunftsgebiet kommen wie der Relay**, sonst lässt die
Inhaltsregel die Verbindung nicht zu.

## Was du dir damit einhandelst

Ehrlichkeitshalber: Wer einen Dienst öffentlich betreibt, wird zum Diensteanbieter.
Betreibst du ihn nur für dich und dein eigenes Handy, ist das folgenlos. Gibst du
anderen Zugang, gelten dieselben Pflichten wie für jeden anderen auch — Impressum,
Datenschutzerklärung, Erreichbarkeit. Die Vorlagen dafür liegen in `web/recht/`.
