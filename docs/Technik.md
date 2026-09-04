# FrameFlip – Technik

[← zurück zur Startseite](../README.md)

Vorschau für gerenderte Bildsequenzen. Läuft im Tray, öffnet auf Tastendruck die
Sequenz der im Explorer markierten Datei als Video – und passt sich dabei laufend
an die Auslastung der Maschine an, damit ein parallel laufender Blender-Render
nichts davon merkt.

C# / .NET 8 / WPF. Keine externen Abhängigkeiten, kein ffmpeg, kein OpenCV.

## Bauen

```bash
dotnet publish FrameFlip/FrameFlip.csproj -c Release
```

Ergebnis: `bin/Release/net8.0-windows/win-x64/publish/FrameFlip.exe` – self-contained,
eine einzelne Datei, kein Installer, kein installiertes .NET nötig. Einfach hinkopieren
und starten (z. B. eine Verknüpfung in den Autostart legen).

Die Datei ist ~160 MB, weil die Kompression bewusst **aus** ist: mit Kompression
entpackt der Host die Assemblies in privaten Speicher (~130 MB committed statt ~60 MB).
Wem die Dateigröße wichtiger ist als der Speicher, setzt in der `.csproj`
`EnableCompressionInSingleFile` auf `true` – dann sind es ~72 MB.

## Bedienung

| Eingabe | Wirkung |
|---|---|
| Hotkey (Standard **Strg+Alt+Leertaste**) | Vorschau öffnen bzw. schließen |
| `Esc` / Klick außerhalb | schließen |
| `Leertaste` / Klick ins Bild | Play / Pause |
| `→` / `←` | ein Bild vor / zurück (pausiert die laufende Wiedergabe) |
| Mausrad **pausiert** | zoomen, der Punkt unter dem Zeiger bleibt stehen |
| Mausrad **während der Wiedergabe** | scrubben |
| **Strg + Mausrad** | jeweils das andere von beiden |
| Ziehen im Bild / mittlere Maustaste | verschieben, sobald hineingezoomt ist |
| Doppelklick / Rechtsklick / `Strg+0` | zwischen Einpassung und 100 % wechseln |
| `Strg` + `+` / `-` | zoomen ohne Maus |
| Ziehen an der Kopfleiste | Fenster verschieben |
| Fensterrand ziehen | Größe ändern |
| `Pos1` / `Ende` | erster / letzter Frame |
| `L` | Loop umschalten |
| `I` / `O` | In- / Out-Punkt setzen (Loop und Export übernehmen ihn) |
| `Entf` | In/Out wieder aufheben |
| `D` | Bilddaten in der Kopfleiste ein- / ausblenden |
| `E` | Exportdialog öffnen |
| `1` / `2` / `3` | Dekodiergröße 100 % / 50 % / 25 % — verlängert den Puffervorlauf |
| `Tab` | Bildanpassung ein- / ausblenden |
| `A` | aktuellen Frame als Vergleich merken |
| `C` | zwischen gemerktem und aktuellem Frame umschalten |

Das Fenster öffnet randlos in der Größe des Mediums (höchstens 90 % der
Arbeitsfläche, mindestens 400 × 300), mittig auf dem Monitor des Explorers, und
blendet über 120 ms ein. Die Kopfleiste zeigt Dateiname, Systemlast, Auflösung,
Farbtiefe, Dateigröße und Zoomstufe; die Bedienleiste unten blendet nach 2 s
Inaktivität aus und bleibt stehen, solange die Maus darüber liegt, der Scrubber
gezogen wird oder das FPS-Menü offen ist.

Ist die Vorschau bereits offen und der Explorer zeigt inzwischen auf eine **andere**
Sequenz, tauscht der Hotkey den Inhalt, statt ein zweites Fenster zu öffnen. Zeigt
er auf dieselbe, schließt er wie gewohnt.

Der Klick außerhalb schließt die Vorschau (QuickLook-Verhalten). Wer nebenher in
Blender arbeiten und die Sequenz stehen lassen will, schaltet das in den
Einstellungen ab.

Statt über den Explorer geht es auch direkt:

```bash
FrameFlip.exe --preview "D:\renders\shot_010\render_0001.png"
```

Für automatisierte Tests lässt sich über die Umgebungsvariable `FRAMEFLIP_CONFIG`
ein alternativer Konfigurationspfad angeben; eine so gestartete Instanz läuft auch
neben einer normalen und lässt `%APPDATA%\FrameFlip\config.json` unberührt.

> **Auf diesem Rechner ist Strg+Alt+Leertaste bereits belegt** (`RegisterHotKey`
> meldet Fehler 1409) – ebenso Alt+Leertaste; das sieht nach einem installierten
> Launcher aus. FrameFlip meldet das beim Start per Ballon-Hinweis. Frei getestet
> sind u. a. Strg+Umschalt+Leertaste, Strg+Alt+V und Strg+Alt+Q. Umstellen im
> Tray-Menü unter *Einstellungen*.

## Was wo passiert

| Datei | Aufgabe |
|---|---|
| `AppHost.cs` | Tray-Icon, Menü, Toggle-Logik, Fenstergeometrie |
| `Interop/HotKeyService.cs` | `RegisterHotKey` auf unsichtbarem Fenster, `WM_HOTKEY` |
| `Interop/ExplorerSelectionProvider.cs` | `Shell.Application` → `ShellWindows` → `Document.SelectedItems` |
| `Diagnostics/SystemLoadMonitor.cs` | CPU/GPU/RAM messen, Ressourcenprofil ableiten |
| `Diagnostics/GpuLoadCounter.cs` | PDH-Zähler `GPU Engine(*)` über `pdh.dll` |
| `Sequencing/SequenceScanner.cs` | Dateiname → Präfix/Zifferngruppe/Endung, Ordner scannen |
| `Decoding/` | `IFrameDecoder` + WIC-Implementierung |
| `Caching/FrameCache.cs` | Ringpuffer, Decoder-Threads, RAM-Budget |
| `Playback/PlaybackClock.cs` | zeitbasierte Wiedergabeposition |
| `Views/ViewerWindow.xaml` | Anzeige, Zoom, Overlays, Eingaben |

### Erst puffern, dann abspielen

Die Wiedergabe startet nicht mit dem ersten Frame, sondern erst, wenn genug im
Ring liegt: standardmäßig anderthalb Sekunden Material (`WarmupFrames` in der
Konfiguration überschreibt das), oder wenn die ganze Sequenz gepuffert ist.
Solange steht ein dezentes *Puffern …* oben rechts. Notausstieg nach 8 s, damit
eine langsame Platte nicht endlos blockiert.

Läuft der Ring während der Wiedergabe leer, wird angehalten und nachgeladen
statt weiterzuruckeln. Einzelne fehlende Frames werden dagegen verworfen – erst
wenn **gar nichts** mehr vorausliegt, wird gepuffert.

In der Kopfleiste steht neben der tatsächlich angezeigten Bildrate, **wie weit der
Puffer vorausreicht**. Wird die Zahl gelb oder rot, ist das nächste Stocken
absehbar. Und der Hinweis *Puffern …* nennt den Anlass – *Ring leer*, *Sprung*,
*neue Auflösung*, *neue Sequenz* –, damit unterscheidbar bleibt, ob der Vorrat
aufgebraucht war oder etwas anderes den Ring verworfen hat.

### Der Vorlauf folgt der Bildrate

`PrefetchAhead` ist eine **Frameanzahl**, und die bedeutet je nach Bildrate etwas
völlig anderes: 60 Frames sind bei 24 fps zweieinhalb Sekunden Reserve, bei 60 fps
nur eine. Deshalb gilt der eingestellte Wert als Untergrenze, und darüber hinaus
werden mindestens **zwei Sekunden** Material vorgehalten.

### Der Ring kennt den In/Out-Bereich

Ist ein Bereich gesetzt, spielt die Wiedergabe nur darin – der Ringpuffer rechnete
aber lange über die **ganze** Sequenz. Beim Loop-Sprung vom Out- zurück zum In-Punkt
wrappte das Vorausladen deshalb über das Sequenzende statt über das Bereichsende:

* Der Frame am In-Punkt war weder vorgeladen noch gehalten – genau der, auf den der
  nächste Schritt geht. Ergebnis: Nachpuffern bei **jeder** Runde.
* Stattdessen lud der Ring die Frames *hinter* dem Out-Punkt, die nie gezeigt werden.
  Bei einer Sequenz aus 2077 Bildern zu je 8,3 MB sind das bis zu 120 Dekodierungen
  je Runde, jede zusätzlich in den Rohcache geschrieben – knapp ein Gigabyte
  Schreiblast für Bilder, die niemand sieht.

`SequenceMath.OffsetInRange` gab es dafür bereits, es war nur nicht angeschlossen.
Jetzt beziehen sich Ladereihenfolge, Bewertung (`Score`), Rückfall auf ein älteres
Bild und die Zählung des Vorrats auf den Bereich. Frames außerhalb fliegen sofort
raus: Der Platz gehört dem Bereich.

Nebeneffekt, der den eigentlichen Gewinn ausmacht: Ein kurzer Ausschnitt passt
dadurch oft **vollständig** in den Ring, auch wenn die Sequenz es nie täte. Dann gibt
es überhaupt kein Nachpuffern mehr. Der Regressionstest hält das fest – ohne den Fix
liegt bei einem Bereich aus zehn Frames genau einer im Ring, mit Fix alle zehn.

### Der Ring benutzt das Budget

Die Ringgröße folgte früher **allein** aus Vor- plus Rücklauf – bei 2 GB Budget und
1080p blieb er deshalb bei 151 Frames stehen, obwohl 259 hineingepasst hätten. Alles
darüber hinaus fiel heraus und musste bei jedem Loop-Durchlauf neu dekodiert werden,
obwohl der Speicher längst zugesagt war.

Zwei Regeln gelten jetzt:

* **Passt die Sequenz vollständig ins Budget, wird sie ganz gehalten.** Nach dem
  ersten Durchlauf wird dann nie wieder nachgeladen – der Loop läuft ohne jedes
  Puffern. Nachgemessen mit 150 Frames über drei Runden: 0 Nachladevorgänge.
* **Passt sie nicht, wird trotzdem der ganze Platz benutzt.** Bei 600 Frames und
  2 GB sind das 258 statt 151 im Ring und 227 statt 120 Frames Vorlauf – bei 60 fps
  also 3,8 statt 2,0 Sekunden Reserve.

Wie viele Frames hineinpassen, hängt an der Bildgröße:

| Budget | 1080p (7,9 MB) | 1080p schwer (8,5 MB) | 4K (33 MB) |
|---|---|---|---|
| 1 GB | 129 | 120 | 31 |
| 2 GB | 259 | 240 | 62 |
| 4 GB | 518 | 481 | 124 |

### Zweite Stufe: Rohcache auf der Platte

Passt die Sequenz nicht in den Arbeitsspeicher, fällt bei jedem Loop-Durchlauf ein
Teil aus dem Ring und muss neu beschafft werden. Statt das PNG erneut zu entpacken,
legt FrameFlip die dekodierten Frames als **rohe Bgra32-Blöcke** unter
`%TEMP%\FrameFlip
awcache` ab. Gemessen an 1080p:

| Weg | Zeit je Frame |
|---|---|
| PNG entpacken | 31 ms |
| **rohen Block lesen** | **6 ms** |
| rohen Block schreiben | 3 ms |
| aus dem Arbeitsspeicher | 0,02 ms |

Am Loop gemessen, 300 Frames bei einem Ring für 100: die zweite Runde dauert
**0,59 s statt 1,74 s**. Der erste Durchlauf kostet dafür 15 % mehr, weil nebenher
geschrieben wird.

Bewusst **ohne Kompression** – sie brächte genau die Rechenzeit zurück, die hier
gespart werden soll. Und bewusst **sitzungsgebunden**: Der Ordner hängt an
Sequenz *und* Dekodiergröße und wird beim Schließen gelöscht; Reste früherer
Sitzungen räumt der nächste Start weg. Jeder Eintrag trägt Änderungszeit und Länge
der Quelldatei im Kopf – wird während eines laufenden Renders ein Frame
überschrieben, gilt der alte Block sofort als ungültig. Ohne diese Prüfung zeigte
die Vorschau hartnäckig das Bild von vorhin.

Abschaltbar über `RawCacheEnabled`; die Obergrenze steht in `RawCacheMaxGb`
(Standard 16). Auf einer mechanischen Platte lohnt es sich nicht – dort ist Lesen
kaum schneller als Entpacken.

Soll eine Sequenz komplett im Speicher liegen, muss das Budget also mindestens
`Frames × Bildgröße` betragen. Die Pufferstufe (`2` / `3`) senkt die Bildgröße auf
ein Viertel bzw. ein Sechzehntel und bringt eine lange Sequenz damit oft doch noch
vollständig unter.

Wird die Bildrate im Betrieb umgeschaltet, zieht der Ring sein Fenster nach, ohne
die bereits dekodierten Frames zu verwerfen.

### Dekodiergröße als Pufferstufe

`1` / `2` / `3` oder der Knopf in der Bedienleiste schalten die Dekodierung auf
100 %, 50 % oder 25 %. Das wirkt **nicht** auf den Zoom – hineinzoomen geht weiter,
das Bild ist nur gröber.

Der Gewinn liegt nicht dort, wo man ihn vermutet. Gemessen an 1080p-Material
(5,9 MB je PNG) kostet ein Frame **35,7 ms bei voller und 31,1 ms bei viertel
Größe** – das Verkleinern spart beim Dekodieren also kaum etwas, weil WIC das PNG
ohnehin vollständig entpacken muss, bevor es skalieren kann. Der Hebel ist der
**Speicher**:

| Stufe | Speicher je Frame | Passen in 1 GB | Vorlauf bei 24 fps |
|---|---|---|---|
| 100 % | 7,91 MB | 129 | 5,4 s |
| 50 % | 1,98 MB | 517 | 21,5 s |
| 25 % | 0,49 MB | 2070 | 86 s |

Deshalb heißt es Pufferstufe und nicht Qualitätsstufe. In einer verkleinerten Stufe
wird bilinear hochskaliert statt mit `NearestNeighbor` – harte Klötzchen würden
genau das verdecken, was man beurteilen will.

Zum Vergleich, was der Decoder liefert: bei diesem Material schafft **ein** Thread
rund 30 Frames/s, vier Threads 116, sechs Threads 161. Ein einzelner Thread liegt
also nur knapp über den 24 fps der Wiedergabe – unter Systemlast reicht das nicht,
und dann trägt allein der Vorrat.

### Wiedergabe ist zeitbasiert

`PlaybackClock` zählt keine Ticks, sondern rechnet `Anker + Sekunden × FPS`.
Getaktet wird über `CompositionTarget.Rendering` – kein zusätzlicher Thread, und
bei Pause wird das Ereignis abgemeldet (0 % CPU im Pausenzustand).

Bei aktivem Loop ist die Position `raw mod Frameanzahl` – der Sprung vom letzten
auf den ersten Frame ist damit kein Sonderfall, weder in der Wiedergabe noch im
Vorausladen des Puffers. Deshalb ist er nahtlos.

### Ausnahme: Kopplung an den Bildschirmtakt

Eine bewusste Abweichung von der zeitbasierten Wiedergabe, abschaltbar unter
*Wiedergabe → an den Bildschirmtakt koppeln* (Vorgabe: an).

Liegt die Sollrate auf dem Bildschirmtakt, hat die Zeitachse **keinerlei Reserve**:
Bei 60 fps auf 60 Hz muss jeder einzelne Kompositionsschritt ein neues Bild
tragen, und jeder ausgelassene Schritt verschluckt sofort eines. Gemessen auf
einem 60-Hz-Schirm mit echter Zeichenlast:

| Sollrate | Verfahren | gezeigt | Standbilder | Sprünge |
|---|---|---|---:|---:|
| 60 fps | zeitbasiert | 40,3/s | 9,3/s | **15,0/s** |
| 60 fps | gekoppelt | 59,0/s | 0 | **0** |
| 30 fps | zeitbasiert | 30,0/s | 13,5/s | 0 |
| 30 fps | gekoppelt | 18,3/s | – | 0 |

Die letzte Zeile ist der Grund für die enge Bedingung: Bei 30 fps auf 60 Hz kostet
jeder ausgelassene Schritt einen halben Frame, und die Kopplung bricht ein. Dort
ist die Uhr richtig – sie zeigt ohnehin null Sprünge, weil die doppelte Reserve
dazwischenliegt.

Gekoppelt wird deshalb nur, wenn **alle** Bedingungen gelten:

* die Sollrate weicht um höchstens 5 % vom gemessenen Takt ab,
* der Schirm liefert mindestens 80 % seines eigenen Takts (sonst Zeitlupe),
* die Einstellung ist aktiv.

Der Takt wird nicht erfragt, sondern gemessen (`RefreshEstimator`): Die
Anzeigeeinstellung nennt 60 Hz, viele Anschlüsse laufen mit 59,94 – und genau
dieser Unterschied entscheidet. Der Median der letzten 64 Abstände ergibt den
Takt des Schirms, die Zahl der Schritte je Sekunde das, was er tatsächlich
liefert; fallen beide auseinander, lässt die Komposition Schritte aus.

Bezahlt wird die Kopplung mit dem Unterschied zwischen Sollrate und echtem
Schirmtakt, also rund einem Promille. Wer die Zeitachse exakt braucht, schaltet
sie ab – dann gilt ausschließlich `PlaybackClock`.

### Dynamische Ressourcen

Solange eine Vorschau offen ist, misst FrameFlip alle 10 Sekunden (einstellbar):

* **CPU** über `GetSystemTimes` – der eigene Verbrauch wird abgezogen, sonst sieht
  FrameFlip die Last, die es selbst erzeugt, und drosselt sich grundlos.
* **RAM** über `GlobalMemoryStatusEx` (freier physischer Speicher).
* **GPU** über die PDH-Zähler `GPU Engine(*)\Utilization Percentage`, also dieselbe
  Quelle wie der Task-Manager. Instanzen derselben Engine-Art werden summiert, über
  die Engine-Arten hinweg zählt das Maximum.

Daraus folgt eine von vier Stufen mit Totband gegen Pendeln:

| Stufe | Auslastung | Decoder-Threads | Threadpriorität | Prozess |
|---|---|---|---|---|
| Leerlauf | < 20 % | bis `MaxDecoderThreads` | Normal | Normal |
| Mäßig | < 45 % | zwei Drittel davon | BelowNormal | BelowNormal |
| Beschäftigt | < 80 % | 1 | Lowest | BelowNormal |
| Kritisch | ≥ 80 % | 1 | Lowest | BelowNormal |

Weniger als 2 GB frei stuft auf *Beschäftigt* herab, weniger als 1 GB auf
*Kritisch* – unabhängig von der CPU. Die Threadobergrenze ist zusätzlich auf die
Kernzahl minus zwei gedeckelt (auf diesem Rechner: 10 von 12). Ohne offene Vorschau
misst nichts, und der Prozess steht wieder auf `BelowNormal`.

**Die Threadzahl entscheidet, welche Bildrate überhaupt erreichbar ist** – das ist
der Punkt, an dem eine zu vorsichtige Einstellung als Programmfehler erscheint. Ein
1080p-PNG mit 8,5 MB braucht rund 46 ms zum Entpacken, weil `zlib` das ganze Bild
dekomprimieren muss:

| Threads | erreichbare Bildrate | reicht für |
|---|---|---|
| 1 | ~18 fps | nicht einmal 24 fps |
| 2 | ~37 fps | 24 fps, nicht 30 |
| 4 | ~73 fps | 60 fps, knapp |
| 6 | ~110 fps | 60 fps mit Reserve |

Dabei sehen CPU und GPU im Task-Manager **unbelastet** aus: zwei arbeitende Threads
von zwölf Kernen sind 17 % Gesamtlast. Bleibt die Wiedergabe hinter der Bildrate
zurück und ist zugleich der Puffer leer, sagt FrameFlip das jetzt als Hinweis –
samt der Zahl der gerade erlaubten Threads.

Frühere Standardwerte waren zu knapp: Deckel bei der halben Kernzahl,
`MaxDecoderThreads` von 4, und bei mittlerer Last nochmals halbiert. Für 24 fps
reichte das, für 60 fps nicht.

**Die Puffergröße folgt ausdrücklich *nicht* der CPU-Last, sondern nur dem freien
Arbeitsspeicher.** Eine frühere Fassung kürzte beides gemeinsam — bei *Beschäftigt*
auf 70 %, bei *Kritisch* auf 40 %. Das war ein Denkfehler: gerade wenn der Decoder
nur noch einen Thread hat, ist ein **großer** Vorrat die einzige Reserve, aus der
die Wiedergabe flüssig laufen kann. Nachgemessen mit 1080p-Material (7,91 MB je
Frame): bei 512 MB Budget fassen 64 Frames 2,7 Sekunden — auf 70 % gekürzt bleiben
1,8 Sekunden, und der Ring läuft beim ersten Stocken leer. Gekürzt wird jetzt nur
bei echtem Speichermangel (unter 2 GB auf 70 %, unter 1 GB auf 40 %), denn dort
träfe Auslagern die Wiedergabe härter als ein kurzer Puffer.

Zur Einordnung: **GPU-Last ist messbar, aber kaum steuerbar.** FrameFlip benutzt die
GPU nur zum Compositing des Fensters; wenn Cycles die Karte auslastet, bringt ein
Drosseln des Decoders der GPU wenig. Der Wert dient als Indikator „die Maschine
arbeitet", die wirksamen Stellschrauben sind Threads, Priorität und Puffer.

Abschaltbar über *Einstellungen → Dynamische Last*. Dann bleibt es bei genau einem
Decoder-Thread mit `Lowest`, wie ursprünglich spezifiziert.

### Speicher

Frames liegen als `Bgra32`-Pixelpuffer aus einem eigenen Pool im Ring. Im
eingeschwungenen Zustand rotieren immer dieselben Arrays – keine Allokation pro Frame,
keine GC-Pausen, und das RAM-Budget ist exakt statt geschätzt
(`Kapazität = Budget / (Breite × Höhe × 4)`). Passt das konfigurierte Fenster nicht ins
Budget, schrumpft das Fenster; es wird nie darüber hinaus allokiert. Angezeigt wird
über **eine** wiederverwendete `WriteableBitmap`.

Das Budget gilt für den Ringpuffer, nicht für den Prozess: dazu kommen etwa
60 MB Grundlast (WPF, WIC, Runtime).

### Fensterplatzierung und DPI

Das Fenster wird nicht über eine DIP-Umrechnung positioniert, sondern über einen
kleinen Regelkreis: Größe setzen, Lage messen (`GetWindowRect`), gegen den Monitor
(`MonitorFromWindow` + `GetMonitorInfo`) zentrieren, nachziehen, bis die Abweichung
unter zwei Pixel liegt. Der Grund ist unangenehm konkret: auf gemischt skalierten
Systemen liefern Fenstergröße, Monitorabfrage und WPF-Koordinaten Werte aus
unterschiedlichen Räumen. Jede feste Umrechnung wendet den Skalierungsfaktor
irgendwo doppelt an und schiebt das Fenster aus dem Bildschirm; der Regelkreis
kommt ohne Annahme über den Faktor aus.

> **Bekannte Einschränkung:** Auf der Testmaschine (zwei Monitore, beide 175 %)
> greift die DPI-Deklaration aus dem Manifest im Single-File-Build nicht — der
> Prozess läuft für Windows DPI-unaware, `GetMonitorInfo` liefert virtualisierte
> Werte, und Windows skaliert das fertige Fenster nachträglich hoch. Lage und Größe
> stimmen dadurch, die Darstellung ist aber nicht pixelgenau, sondern leicht
> weichgezeichnet. `SetProcessDpiAwarenessContext` lässt sich zur Laufzeit nicht
> mehr setzen (`ERROR_ACCESS_DENIED`), `ApplicationHighDpiMode` ändert nichts.
> Auf Systemen mit 100 % Skalierung tritt der Effekt nicht auf.

### Zoom und Auflösung

Dekodiert wird auf die tatsächliche Anzeigegröße in **Geräte**pixeln, nie darüber
und nie über die Quellauflösung hinaus. Beim Hineinzoomen reicht das nicht mehr:
der Zoom reagiert sofort (hochskaliert), und 150 ms nach dem letzten Rad-Ereignis
wird der Ring im pausierten Zustand in der passenden Auflösung neu aufgebaut. Weil
größere Frames mehr Platz brauchen, schrumpft dabei automatisch die Zahl der
gepufferten Frames – das Budget bleibt eingehalten. Der alte Ring wird vor dem Aufbau
des neuen geleert, sonst läge das Budget kurzzeitig doppelt im Speicher.

Das Nachschärfen ist sprungfrei, weil der Maßstab absolut geführt wird (Bildpixel je
Gerätepixel) und beim Puffertausch das Produkt aus Inhaltsbreite und Matrixfaktor
konstant bleibt. Der Zoom selbst liegt ausschließlich in einer `MatrixTransform`:
kein Codepfad verändert dabei eine Puffergröße, eine Bitmapgröße oder eine
Layoutgröße.

Der Bildbereich liegt in einem **`Canvas`**, nicht in einem `Grid`. Das ist keine
Kosmetik: ein Grid arrangiert sein Kind in der Zellgröße und setzt, sobald das Kind
größer ist, einen Layout-Clip. Der greift in den Koordinaten des Kindes, also **vor**
der `RenderTransform` – das anschließend verschobene Bild wurde dadurch rechts und
unten um genau den Betrag des Versatzes abgeschnitten, und der Versatz wächst mit dem
Zoom. Ein Canvas misst seine Kinder unbegrenzt; beschnitten wird nur außen am
Viewport. `FrameFlip.Tests` hält das mit einem Test fest, der wirklich rendert und
Pixel zählt – auf Matrixwerte allein wäre der Fehler nicht aufgefallen, die Matrix
war die ganze Zeit richtig.

### Sperrdisziplin

Unter dem Cache-Lock laufen nur Dictionary-Operationen. Dekodieren und Kopieren finden
außerhalb statt – sonst könnte der UI-Thread auf einen `Lowest`-Priority-Decoder warten,
und unter Renderlast wäre das eine Prioritätsinversion mit zweistelligen Millisekunden.
Möglich macht das ein Refcount pro Puffer: die Präsentation hält ihn während des
Kopierens, die Eviction darf ihn parallel aus dem Fenster nehmen, zurück in den Pool
geht er erst, wenn beide fertig sind. Bei mehreren Decoder-Threads verhindert eine
`_inFlight`-Menge, dass zwei denselben Frame dekodieren.

### Nach dem Schließen

Decoder-Threads werden signalisiert und **außerhalb** des UI-Threads gejoint, Dictionary
und Pool geleert, dann LOH-Kompaktierung mit `GCCollectionMode.Aggressive` und
`SetProcessWorkingSetSize(-1,-1)`. Die Puffer liegen auf dem Large Object Heap – ohne
Kompaktierung bliebe der Speicher stehen, auch wenn managed nichts mehr darauf zeigt.

## Formate

PNG, JPG/JPEG, TIFF, BMP über WIC. WebP funktioniert, wenn die *WebP Image Extension*
von Microsoft installiert ist – fehlt sie, fällt genau dieses Format sauber aus.

EXR ist **nicht** implementiert, die Architektur hält den Platz frei: eine weitere
`IFrameDecoder`-Implementierung, registriert in `FrameDecoderRegistry.CreateDefault()`.
An Cache, Wiedergabe und UI ändert sich dadurch nichts.

## Sequenzerkennung

`render_0042.png` → Präfix `render_`, 4 Stellen, Endung `.png`. Erkannt wird die
**letzte** Zifferngruppe im Namen, also auch bei Ziffern im Präfix (`shot2_0001.png`)
und bei leerem Präfix (`0001.png`, Ausgabepfad `//render/`). View-Suffixe nach der
Nummer (`f_0001_L.png`) trennen linke und rechte Ansicht in eigene Sequenzen.

Das **Padding** wird aus dem Bestand abgeleitet, nicht aus der angeklickten Datei.
Blender füllt auf N Stellen auf und lässt die Zahl darüber hinauswachsen – nach
`f_99` kommt `f_100`, nicht `f_00100`. Eine führende Null beweist das Padding,
sonst zählt die kürzeste vorkommende Nummer. Dadurch findet FrameFlip dieselbe
Sequenz, egal ob `f_99` oder `f_100` markiert war.

Die Zeitleiste spannt den **Nummernbereich** auf, nicht die Listenposition – nur so
sind Lücken darstellbar; über Positionen sähen 250 gerenderte von 500 Frames aus wie
eine vollständige Sequenz. Fehlende Frames erscheinen als rote Markierung, darüber
steht ihre Zahl und Lage (`2 Lücken: 7–9, 13`). Ein Klick auf *Blender-Befehl
kopieren* legt einen vollständigen Aufruf zum Nachrendern in die Zwischenablage:

```
blender -b "PFAD/ZUM/PROJEKT.blend" -o "D:/renders/shot_010/render_####" -F PNG -x 1 -f 7..9,13
```

Der Frame-Zähler zeigt die **echte** Framenummer – `0042 / 0250` ist aktuelle Nummer /
höchste Nummer. Beim Abspielen werden Lücken übersprungen, ohne die Zeitbasis zu
verschieben, weil die Wiedergabe über Listenpositionen läuft. Ein Sprung in eine Lücke
landet auf dem nächstgelegenen vorhandenen Frame.

War im Explorer nichts (oder etwas Unlesbares) markiert, nimmt FrameFlip das erste
darstellbare Bild im aktiven Ordner.

## Bildanpassung

`Tab` klappt rechts ein Panel auf. Das Fenster wächst dabei nach rechts, solange
der Bildschirm es hergibt; sonst gibt der Bildbereich den Platz ab.

Alles darin betrifft **nur die Anzeige** – die Dateien auf der Platte bleiben
unberührt. Damit man das beim Beurteilen nicht vergisst, steht eine aktive
Korrektur als Kurzform in der Kopfleiste (`EV -1,2  γ 1,3  K 1,15  S 1,2`).

**Regler:** Belichtung (in Blendenstufen), Gamma, Kontrast, Sättigung, Schwarz- und
Weißpunkt. Doppelklick auf einen Regler setzt ihn zurück. Die Reihenfolge der
Schritte ist die in der Farbkorrektur übliche: Belichtung, dann Schwarz-/Weißpunkt,
dann Gamma, dann Kontrast, zuletzt Sättigung.

**Verteilung:** Histogramm über RGB oder Helligkeit, gemessen am *korrigierten*
Bild – im Diagramm steht, was man auch sieht. Liegen mehr als 0,5 % der Pixel oben
oder unten an, erscheint ein Balken am Rand und eine Zeile darunter. Die Kurve ist
wurzelskaliert, weil ein einzelner hoher Ausschlag den Rest der Verteilung sonst im
Bodensatz verschwinden ließe.

**A/B-Vergleich:** `A` merkt den aktuellen Frame, `C` schaltet um. Der gemerkte
Frame wird ausdrücklich **kopiert** – eine Referenz auf den Ringpuffer zeigte
später irgendein Bild, weil der Puffer gleich an den nächsten Frame weitergereicht
wird.

**Vorlagen:** Korrektureinstellungen lassen sich benennen und speichern; sie stehen
beim nächsten Mal in der Auswahlliste.

### Wie schnell das ist

Die Korrektur läuft auf der CPU beim Kopieren in die Anzeige-Bitmap, ohne
Zwischenpuffer. Gemessen an 1080p:

| Fall | Zeit je Bild |
|---|---|
| ohne Korrektur | 0,3 ms |
| nur Tonwerte (Belichtung, Gamma, Kontrast, Levels) | 2,4 ms |
| zusätzlich Sättigung und Kanalansicht | 9,6 ms |
| Histogramm getrennt, jedes 4. Pixel | 3,8 ms |

Bei 24 fps liegen 41,7 ms zwischen zwei Bildern – es bleibt also Luft. Zwei Dinge
waren dafür nötig: **Ganzzahl-Arithmetik** statt `double` je Pixel (die erste
Fassung brauchte 72 ms und hätte Bilder gekostet) und **Verteilen der Zeilen über
die Kerne**. Ohne eingestellte Korrektur ist es ein reiner Speicherkopiervorgang,
damit eine ungenutzte Funktion die Wiedergabe nicht einen Takt kostet.

## Videoexport

`E` oder der Knopf *Export …* in der Bedienleiste. Formate: H.264/MP4, H.265/MP4,
ProRes 422 HQ, WebM/VP9 und GIF (zweistufig mit eigener Farbpalette). Bereich ist
wahlweise die ganze Sequenz oder In bis Out, die Bildrate ist aus dem Player
vorbelegt, die Auflösung original oder verkleinert.

Der Export läuft über den **concat-Demuxer** mit einer expliziten Frameliste – nicht
über `-i "render_%04d.png"`. Der naheliegende Weg hat zwei Bruchstellen, die bei
Renderausgaben beide regelmäßig auftreten: er bricht bei der ersten fehlenden Nummer
ab, und er versteht keinen Padding-Überlauf (nach `f_99` sucht er `f_00100`). Bei
Lücken ist wählbar, ob sie übersprungen werden oder der letzte Frame als kurzes
Standbild stehen bleibt – für die Beurteilung von Bewegung meist das bessere Verhalten.

Der Player bleibt währenddessen bedienbar, der Export ist abbrechbar, und eine
unvollständige Datei wird dabei gelöscht. Prozesspriorität und `-threads` folgen dem
Lastprofil, damit der Encoder einen laufenden Render nicht verdrängt.

Zwei Details der Liste sind gegen die verbreitete Anleitung nachgemessen, mit
**ffmpeg 9.0.1**:

- **Kein `-framerate`.** Das ist eine Option des Bilddatei-Demuxers (`image2`) und
  existiert beim concat-Demuxer nicht – ffmpeg bricht mit *„Option framerate not
  found"* ab, bevor überhaupt eine Datei gelesen wird. Die Bildrate der Eingabe steht
  stattdessen in den `duration`-Zeilen der Liste, `-r` am Ausgang erzwingt die
  konstante Ausgabe-Bildrate.
- **Der letzte Dateiname steht *nicht* doppelt.** Die übliche Empfehlung, ihn zu
  wiederholen, stammt aus einer Zeit, in der concat die `duration` des letzten
  Eintrags verworfen hat. Heute wird sie ausgewertet: 16 Frames ergaben mit
  Wiederholung 17 Bilder (0,708 s statt 0,667 s bei 24 fps), ohne exakt 16. Die
  Wiederholung erzeugt inzwischen also genau den Fehler, den sie einmal verhindert hat.

Ist im Panel eine Korrektur eingestellt, **fragt der Dialog**, ob sie ins Video
eingerechnet werden soll. Die Antwort wird gemerkt, bleibt aber je Export
änderbar. Übernommen werden Belichtung, Gamma, Kontrast, Sättigung sowie Schwarz-
und Weißpunkt (als `eq`- und `curves`-Filter). **Kanalansichten nicht** – ein Video
nur mit dem Rotkanal ist praktisch nie gewollt, das ist ein Beurteilungswerkzeug.

Der Zielname ist frei editierbar, aber nicht jeder Codec passt in jeden Behälter.
ProRes in MP4 etwa lässt ffmpeg mit *„Could not find tag for codec prores"*
scheitern. Passt die Endung nicht zum Format, korrigiert der Dialog sie beim Start
und sagt es in der Statuszeile.

### ffmpeg wird nicht mitgeliefert

Übliche ffmpeg-Builds enthalten **libx264 und stehen damit unter der GPL**. Wäre
ffmpeg Teil der Auslieferung, müsste FrameFlip ebenfalls unter GPL stehen. Zur
Laufzeit gesucht bleibt die Lizenzfrage beim Benutzer und FrameFlip permissiv
lizenzierbar. Ein automatischer Download findet aus demselben Grund nicht statt.

Gesucht wird in dieser Reihenfolge: eingestellter Pfad, Unterordner `ffmpeg` neben
der Exe, `PATH`, dann die Ablageorte von winget, Chocolatey und Scoop. Der letzte
Schritt ist kein Luxus – nach einer frischen Installation kennt ein bereits laufender
Prozess den erweiterten `PATH` noch nicht, er hat ihn beim Start geerbt.

Wird nichts gefunden, erklärt der Dialog das und bietet eine Dateiauswahl an.
Installation etwa mit:

```bash
winget install Gyan.FFmpeg
```

Der gewählte Pfad wird über `ffmpeg -version` geprüft: eine gleichnamige Datei belegt
noch nicht, dass dahinter ein lauffähiges ffmpeg steckt.

> **Hinweis:** Blender bringt zwar `avcodec`, `avformat` und `avutil` als DLLs mit,
> aber keine aufrufbare `ffmpeg.exe`. Eine vorhandene Blender-Installation ersetzt
> ffmpeg also nicht.

## Konfiguration

`%APPDATA%\FrameFlip\config.json`:

```json
{
  "Hotkey": "Ctrl+Alt+Space",
  "Fps": 24,
  "Loop": true,
  "ShowMetadata": true,
  "CloseOnFocusLoss": true,
  "MemoryBudgetMb": 1024,
  "PrefetchAhead": 60,
  "PrefetchBehind": 15,
  "AdaptiveResources": true,
  "LoadIntervalSeconds": 10,
  "MaxDecoderThreads": 4,
  "WarmupFrames": 0,
  "DraftStep": 0,
  "RawCacheEnabled": true,
  "RawCacheMaxGb": 16,
  "PanelOpen": false,
  "Adjustments": null,
  "AdjustmentPresets": [],
  "ExportApplyAdjustments": null,
  "FfmpegPath": "",
  "ExportPreset": "H.264 / MP4",
  "ExportHoldLastFrame": true
}
```

`WarmupFrames: 0` heißt „aus der Bildrate ableiten", `FfmpegPath: ""` heißt „bei
jedem Export neu suchen". FPS, Loop, Bilddatenanzeige und die Exportauswahl werden
direkt beim Umschalten gespeichert. Änderungen an Budget, Puffergrößen und
Lasterkennung greifen beim nächsten Öffnen der Vorschau.

Neue Schlüssel bekommen beim Einlesen ihren Standardwert; eine ältere Datei wird
ergänzt, nicht überschrieben. Der Einstellungsdialog geht dabei vom vorhandenen
Stand aus und überschreibt nur seine eigenen Felder – sonst würde er jede Einstellung
zurücksetzen, die er selbst nicht anzeigt.

## Gemessen

Auf dieser Maschine (12 Kerne), 58 Frames à 1600×900, Budget 512 MB, im Leerlauf:

| Zustand | Working Set | Private Bytes |
|---|---|---|
| Tray, Leerlauf | 8 MB | 60 MB |
| Vorschau offen, auf 229 % gezoomt | 361 MB | 419 MB |
| nach dem Schließen | 19 MB | 168 MB |

Wiedergabe: 24,9 fps bei 24 fps Sollwert (Messung über einen vollen Loop-Durchlauf,
Zählerstand per UI Automation abgetastet), kein einziger Messpunkt ohne Fortschritt,
kein Puffer- oder Rückstandshinweis. Prozesspriorität folgt der Last, Decoder-Threads
im Leerlauf 3 von maximal 3.
