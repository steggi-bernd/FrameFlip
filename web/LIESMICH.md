# Die Zuschauerseite

`watch.html` ist die ganze Seite. Eine Datei, kein Beiwerk, nichts nachzuladen — sie
lässt sich prüfen, indem man sie liest.

Sie zeigt **nur** Zahlen und das jeweils neueste Bild. Es gibt keinen Weg, von ihr aus
etwas auszulösen: FrameFlip öffnet eingehende Nutzlast in diesen Räumen gar nicht
erst, und der Schlüssel, mit dem ein Befehl verstanden würde, ist ein anderer als
der, den der Link enthält.

## Warum der Leuchtturm und nicht das Heimnetz

Ein eigener Server auf dem PC bräuchte eine Öffnung in der Firewall — einen Weg von
außen nach innen. Hier baut FrameFlip die Verbindung selbst auf, nach draußen, wie
ein Browser auch. Es gibt keinen Eingang, der offen stehen könnte, und es ist
gleichgültig, in welchem Netz der Zuschauer sitzt.

Der Leuchtturm bleibt dabei ein **blinder Durchgang**. Er sieht Zufallsbytes und eine
Raumkennung; die Schlüssel entstehen aus einem Geheimnis, das hinter der Raute der
Adresse steht, und diesen Teil schickt ein Browser grundsätzlich nicht zum Server.

## Plätze

Ein Raum des Relays fasst zwei Verbindungen — FrameFlip und einen Zuschauer. Mehrere
Zuschauer brauchen deshalb mehrere Räume; sie entstehen, indem die Platznummer in die
Ableitung eingeht. FrameFlip sitzt in allen, ein Browser sucht sich einen freien.

| Platz | Wer hereinkommt |
|---|---|
| 0, 1 | der Link allein genügt |
| 2 – 5 | zusätzlich das Kennwort, das am PC steht |

**Am Relay ist dafür nichts zu ändern.** Er sieht sechs ganz gewöhnliche Räume und
muss von Zuschauern nichts wissen. Jeder Zuschauer hat außerdem seinen eigenen Kanal
mit eigenen Salzen — keiner kann lesen, was ein anderer bekommt, selbst wenn beide
denselben Link haben.

## Aufstellen

Die Seite muss über **dasselbe Herkunftsgebiet** ausgeliefert werden wie der Relay
(`relay.steggi-matrix.work`), sonst greift `connect-src 'self'` in der Inhaltsregel
nicht. Zwei Handgriffe auf dem Leuchtturm:

**1. Die Datei ablegen** — in ein Verzeichnis, das Caddy sehen kann:

```bash
mkdir -p ~/caddy/watch
# watch.html dorthin kopieren
```

Und in `~/caddy/docker-compose.yml` bei `volumes` ergänzen:

```yaml
      - ./watch:/srv/watch:ro
```

**2. Den Block in `~/caddy/Caddyfile`** — der bestehende Eintrag für den Relay
bekommt einen zweiten Pfad davor:

```caddy
relay.steggi-matrix.work {
    redir /w /w/

    handle /w/* {
        root * /srv/watch
        rewrite * /watch.html
        file_server

        header {
            Cache-Control       "no-store"
            X-Content-Type-Options "nosniff"
            Referrer-Policy     "no-referrer"
        }
    }

    # Alles Übrige bleibt, wie es war: der Relay selbst.
    handle {
        reverse_proxy frameflip-relay:8080
    }
}
```

Die Reihenfolge ist wichtig — `handle /w/*` muss **vor** dem allgemeinen `handle`
stehen, sonst geht der Aufruf an den Relay, und der kennt den Pfad nicht.

Dann neu laden:

```bash
cd ~/caddy && docker compose up -d --force-recreate
```

## Nachsehen, ob es steht

```bash
curl -sI https://relay.steggi-matrix.work/w/ | head -3
curl -s  https://relay.steggi-matrix.work/health
```

Das erste soll `200` und `text/html` melden, das zweite `{"ok":true,...}`.

## Prüfen ohne Server

Der ganze Weg lässt sich auf dem eigenen Rechner durchspielen — echter Relay, echtes
FrameFlip, und die Krypta der Seite wird aus dieser Datei herausgeschnitten und
ausgeführt statt nachgebaut:

```bash
go build -o relay.exe .        # im Ordner frameflip-relay
node client.mjs watch.html 127.0.0.1:8080 <geheimnis> <kennwort>
```

Zwei Dinge sind dabei schon aufgefallen und behoben, die in keiner Durchsicht
auftauchen würden:

* `onmessage` darf `async` sein, wird dann aber erneut aufgerufen, sobald der
  laufende Aufruf auf das erste `await` trifft. Begrüßung und Schlüsselnachweis
  kommen dicht hintereinander — traf der Nachweis in dieses Fenster, gab es den Kanal
  noch nicht, und die Verbindung hing für immer. Die Seite reiht Nachrichten deshalb
  auf und behandelt sie nacheinander.

* Wer an einen leeren Platz sendet, staut Nachrichten im Ausgangspuffer auf. Sobald
  jemand hereinkommt, ergießt sich der Stau auf ihn, und der Relay trennt ihn als
  „zu langsam", bevor er ein Bild gesehen hat. **Nichts senden, solange niemand
  zusieht** ist deshalb keine Sparsamkeit, sondern Bedingung.

* `connect-src 'self'` genügt für WebSockets **nicht**. WebKit — also jedes Safari
  und alles auf einem iPhone — rechnet `'self'` dort nicht auf `wss://` an. Die Seite
  lädt dann ganz normal und bleibt stumm leer, ohne dass irgendwo etwas steht. Das
  Schema muss ausdrücklich genannt werden; die enge Fassung mit dem genauen
  Wirtsnamen setzt Caddy als Kopfzeile dazu.

* Ein gescheiterter Verbindungsaufbau darf nicht wie ein belegter Platz aussehen.
  Er tat es: Der Ausgang fiel durch alle Prüfungen bis zur Kennwortfrage, und man
  tippte ein Kennwort ein, das nichts besser machen konnte. `Result.Blocked` ist
  deshalb ein eigener Ausgang, und nach dem Kennwort wird nur gefragt, wenn beide
  freien Plätze wirklich besetzt waren.
