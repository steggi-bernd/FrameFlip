# FrameFlip Bridge — Recherche und Entwurf

[← zurück zur Startseite](../README.md)

Vorarbeit für das Blender-Addon, das laufende Renders an FrameFlip meldet, und für
die Fernsteuerung vom Handy. Alle API-Aussagen hier sind am Blender-Quelltext geprüft,
nicht aus der Erinnerung — die Dokumentation unter `docs.blender.org` verweigert
automatisierte Abrufe, deshalb stehen die Fundstellen im Code dabei.

**Stand:** 5. September 2026, geprüft gegen `blender/blender`, Branch `main`.

---

## 1. Die Anknüpfpunkte in Blender

`bpy.app.handlers`, definiert in `source/blender/blenkernel/BKE_callbacks.hh` und an
Python gebunden in `source/blender/python/intern/bpy_app_handlers.cc`:

| Handler | Argument | Bedeutung für uns |
|---|---|---|
| `render_init` | Scene | Job beginnt — Auflösung, Frame-Bereich, Ausgabepfad einmalig melden |
| `render_pre` | Scene | Ein Frame beginnt |
| `render_post` | Scene | Ein Frame ist fertig gerechnet |
| **`render_write`** | Scene | **Datei geschrieben** — der wichtigste Handler |
| **`render_stats`** | **String** | Fortschrittstext, siehe Abschnitt 3 |
| `render_complete` | Scene | Job regulär beendet |
| `render_cancel` | Scene | Job abgebrochen |
| `load_post` | Pfad | Anderes .blend geladen |

Der Quelltext beschreibt `render_write` als „on writing a render frame (directly after
the frame is written)". Genau das braucht FrameFlip: **Der Addon überträgt keine
Pixel.** Er meldet einen Pfad, FrameFlip liest die Datei selbst — das kann es bereits,
inklusive Puffer, Rohcache und Bildkorrektur.

`BKE_callbacks.hh` hält außerdem fest, wie die Ereignisse zusammenspielen:

> `PRE/POST` handlers may be used along side modal task handlers as is the case for
> rendering, where rendering an animation uses modal task handlers, rendering a single
> frame has `PRE/POST` handlers.

Für eine Animation gibt es also `INIT` → n × (`PRE`/`POST`/`WRITE`) → `COMPLETE`
oder `CANCEL`.

### Dauerhafte Registrierung

Handler ohne `@persistent` werden beim Laden einer neuen Datei entfernt. Mit dem
Dekorator überleben sie jeden Dateiwechsel. Das ist die ganze Technik hinter
„dauerhaft gekoppelt": Der Addon wird einmal installiert, registriert seine Handler
beim Laden und meldet danach jeden Render in dieser Blender-Instanz — ohne dass
irgendwo etwas eingeschaltet werden muss.

---

## 2. Was Blender **nicht** hergibt

Zwei Grenzen, die den Zuschnitt des ganzen Projekts bestimmen.

### 2.1 Ein laufender Render lässt sich nicht abbrechen

In `source/blender/editors/render/render_internal.cc` existiert genau ein
Render-Operator: `RENDER_OT_render`. Starten geht, Abbrechen nicht.

Die Animationsschleife in `source/blender/render/intern/pipeline.cc` zeigt, warum:

```c
for (nfra = sfra, scene->r.cfra = sfra; scene->r.cfra <= efra; scene->r.cfra++) {
  ...
  if (G.is_break == true) break;
```

Abgebrochen wird über das globale Flag `G.is_break`, das der Window-Manager beim
Druck auf Escape setzt. Python hat darauf keinen Zugriff.

Auch der naheliegende Umweg trägt nicht: `efra` ist ein **Parameter**, der beim Start
der Schleife festgehalten wird. `scene.frame_end` nachträglich kleiner zu setzen
beendet den Lauf nicht.

### 2.2 Der Fortschrittstext ist in der Oberfläche arm

Cycles baut den Text in `intern/cycles/blender/session.cpp` zusammen:

```cpp
if (background) {
    timestatus = "Remaining: " + time_human_readable_from_seconds(remaining_time) + " | ";
    timestatus += string_printf("Mem: %dM | ", (int)ceilf(mem_used));
}
RE_engine_update_stats(&b_engine, "", (timestatus + status).c_str());
```

`background` ist nur bei `blender -b` wahr. In der Oberfläche fehlen also **Restzeit,
Speicherverbrauch und der Unterstatus mit dem Sample-Zähler** — die stehen dort nur im
Statusbalken, nicht in dem String, den `render_stats` erhält.

### 2.3 Was daraus folgt

Beide Grenzen zeigen in dieselbe Richtung: **Renders sollten als eigener
Hintergrundprozess laufen** (`blender -b datei.blend -a`). Das löst drei Dinge auf
einmal:

* Abbrechen wird zum Beenden eines Prozesses — sauber und sofort.
* Der Fortschrittstext wird vollständig.
* Blenders Oberfläche bleibt bedienbar, statt für Stunden zu blockieren.

Renders, die von Hand in der Oberfläche gestartet werden, meldet der Addon trotzdem —
nur eben mit weniger Zahlen und ohne Abbruchmöglichkeit. Das ist keine Einschränkung
unserer Umsetzung, sondern eine von Blender.

---

## 3. Woher jede Zahl kommt

| Metrik | Quelle | Verlässlichkeit |
|---|---|---|
| Gesamtfortschritt | `render_write` zählt Frames gegen `frame_start`/`frame_end` | exakt, kein Parsen |
| Sample-Fortschritt | `render_stats`-String | Format ist engine- und versionsabhängig |
| Restzeit, Cycles-Speicher | `render_stats`-String, nur im Hintergrundmodus | dito |
| Zeit je Frame | Differenz zwischen zwei `render_write` | exakt |
| CPU, RAM des Rechners | **FrameFlip** | exakt |
| GPU-Last, VRAM, Temperatur | `nvidia-smi`, aufgerufen von FrameFlip | exakt, nur NVIDIA |
| Vorschaubild | FrameFlip liest die geschriebene Datei | exakt |

Der Grundsatz dahinter: **Der Addon meldet, was nur Blender weiß. Alles über den
Rechner misst FrameFlip.** Blender bringt kein `psutil` mit; ein Addon, das
Systemwerte selbst erhebt, bräuchte eine Fremdabhängigkeit oder würde Blenders
Hauptthread belasten. FrameFlip misst CPU und GPU ohnehin schon.

Der `render_stats`-String wird **defensiv** ausgewertet: Was sich nicht parsen lässt,
fehlt eben — es darf nie dazu führen, dass eine Meldung ausbleibt oder der Addon
eine Ausnahme wirft.

---

## 4. Vergleich mit Render Control

[rendercontrol.solutions](https://www.rendercontrol.solutions) ist das nächstliegende
Vorbild. Was es laut eigener Beschreibung kann, und wie es sich hier einordnet:

| Funktion von Render Control | Umsetzbar | Anmerkung |
|---|---|---|
| Prozent, Frames, Samples, Laufzeit, Restzeit | ja | Samples und Restzeit nur im Hintergrundmodus |
| Last, VRAM, Temperatur je Grafikkarte | ja | über `nvidia-smi`; AMD bräuchte einen anderen Weg |
| Verlauf der letzten Minute als Kurve | ja | FrameFlip misst bereits im Takt |
| Mehrere Karten einzeln | ja | `nvidia-smi` listet alle |
| Live-Vorschau des aktuellen Frames | ja | FrameFlip liest die Datei, skaliert auf Handygröße |
| Zurückspulen durch fertige Frames | **besser** | das ist FrameFlips Kerngeschäft |
| Render stoppen | ja | **nur** als Hintergrundprozess, siehe 2.1 |
| Weitere Datei in die Warteschlange | ja | setzt Hintergrundprozesse voraus |
| .blend speichern | ja | `bpy.ops.wm.save_mainfile` aus einem Timer |
| PC schlafen legen / herunterfahren | ja | FrameFlip, nicht der Addon |
| Kopplung per QR-Code oder 6-stelligem Code | ja | siehe Abschnitt 5 |
| Ende-zu-Ende verschlüsselt | ja | siehe Abschnitt 5 |
| Einzelbilder oder Video herunterladen | ja | FrameFlip exportiert bereits über ffmpeg |
| Wecker bei Fertigstellung | ja | Push-Nachricht der App |
| Bis zu drei Render-PCs | ja | der Relay unterscheidet Geräte ohnehin |

**Eine Vermutung, ausdrücklich als solche:** Render Control nennt keine eigene
PC-Anwendung, bietet aber eine Warteschlange, „Stop render" und Sample-Zahlen. Eine
Warteschlange ergibt nur Sinn, wenn etwas außerhalb von Blender die Aufträge startet,
und die anderen beiden Punkte gibt es nach 2.1 und 2.2 nur im Hintergrundmodus. Alles
spricht dafür, dass auch Render Control Renders als eigene Prozesse startet. Beweisen
lässt sich das von außen nicht.

---

## 5. Übertragung und Verschlüsselung

### Aufbau

```
Blender-Addon ──lokal──> FrameFlip ──verschlüsselt──> Relay ──verschlüsselt──> App
   (dünn)                 (die Zentrale)            (sieht nur Chiffrat)
```

Der Addon spricht ausschließlich mit FrameFlip auf dem eigenen Rechner. Er braucht
damit **kein Netzwerk, keine Kryptografie und keine Fremdpakete** — das hält ihn klein
und schnell, und es hält die GPL-Grenze sauber: Der Addon ist ein eigenes Repo unter
GPL, FrameFlip bleibt MIT.

### Kopplung

Ein QR-Code, den FrameFlip anzeigt, enthält einen zufälligen 256-Bit-Schlüssel. Der
geht damit **nie über das Netz**. Wer den Code nicht gesehen hat, kann nichts
entschlüsseln — auch der Relay nicht. Ein sechsstelliger Code als Alternative für den
Fall, dass der Bildschirm nicht abfotografiert werden kann.

### Verschlüsselung

AES-256-GCM, Sitzungsschlüssel über HKDF aus dem gekoppelten Geheimnis. Beides ist
ohne Fremdbibliothek zu haben: .NET 8 bringt `AesGcm`, `HKDF` und
`RandomNumberGenerator` mit, Android `javax.crypto` beziehungsweise die
Jetpack-Security-Bausteine.

Das ist echtes Ende-zu-Ende: Der Relay leitet Bytes weiter, die er nicht lesen kann.
Er braucht dafür weder Zertifikate für die Nutzdaten noch Vertrauen.

### Relay

Ein kleiner Dienst, der Verbindungen einander zuordnet und Pakete weiterreicht — mehr
nicht. Kein Speichern, keine Entschlüsselung, kein Zustand außer der Zuordnung.
Läuft als Container hinter einem Reverse-Proxy, der TLS für die Transportschicht
beisteuert.

Weil er nur weiterleitet, ist er anspruchslos: Ein paar hundert Kilobyte je Sekunde
für Metriken, dazu ein Vorschaubild auf Anfrage. Vorschauen werden **nur auf Abruf**
erzeugt, in Handygröße und als JPEG — nie das 72-MB-PNG aus dem Renderordner.

> Die Betriebsdaten des Relays (Host, Domains, Zugänge) gehören **nicht** in dieses
> öffentliche Repo. Sie stehen in der privaten Infrastruktur-Dokumentation.

---

## 6. Sparsam bleiben

Der eine Grundsatz, aus dem alles andere folgt:

> **Handler dürfen nichts tun außer einen Eintrag in eine Warteschlange legen.**

Sie laufen auf Blenders Hauptthread und blockieren ihn. Ein Netzwerkaufruf an dieser
Stelle hängt den Render an der Netzwerklatenz auf — bei einer schlechten Mobilverbindung
sind das Sekunden je Frame. Ein Hintergrundthread leert die Warteschlange und schreibt
in den Socket; `bpy`-Daten fasst er nie an.

Weiter:

* `render_stats` feuert in der Oberfläche bis zu einmal je Sekunde und im
  Hintergrundmodus öfter — wird gedrosselt.
* Vorschauen nur auf Abruf, nicht bei jedem Frame.
* Der Addon überträgt Pfade und Ereignisse, keine Bilddaten.
* Im Leerlauf, also ohne laufenden Render, kostet der Addon nichts außer der
  Registrierung seiner Handler.

---

## 7. Offene Punkte

* **AMD-Grafikkarten** — `nvidia-smi` deckt nur NVIDIA ab. Für AMD wäre ein anderer
  Weg nötig; bis dahin fehlen dort VRAM und Temperatur.
* **Andere Render-Engines** — der `render_stats`-String von EEVEE sieht anders aus als
  der von Cycles. Der Parser muss beides vertragen oder sauber nichts liefern.
* **Blender-Version** — die Handler gibt es seit 2.8x unverändert, der Aufbau des
  Statustexts ändert sich dagegen zwischen Versionen. Deshalb hängt keine Funktion
  davon ab, ob er sich parsen lässt.
