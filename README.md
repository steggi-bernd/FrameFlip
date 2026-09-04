# FrameFlip

**Gerenderte Bildsequenzen ansehen, ohne sie vorher zu einem Video zu machen.**

Datei im Explorer markieren, Tastenkürzel drücken – die ganze Sequenz läuft als
flüssige Vorschau. Fenster schließt sich beim Klick daneben. Kein Import, kein
Export, kein Projekt.

FrameFlip liegt im Tray und ist darauf ausgelegt, **während** eines laufenden
Renders benutzt zu werden: Es misst die Auslastung der Maschine und nimmt sich
zurück, wenn Blender rechnet.

C# · .NET 8 · WPF · Windows 10/11 · keine Fremdpakete

---

> **Bildschirmfoto folgt.** Am aussagekräftigsten wäre eines mit echtem
> Render-Material statt der synthetischen Testbilder aus diesem Repo.

---

## Warum

Eine gerenderte Sequenz zu beurteilen ist unangenehm umständlich. Der Explorer
zeigt Einzelbilder – ob die Bewegung stimmt, sieht man daran nicht. Ein
Videoexport dauert und ist nach zwei Blicken wieder veraltet. Der Video Sequence
Editor in Blender kann es, kostet aber einen Kontextwechsel mitten in der Arbeit.

FrameFlip macht daraus einen Tastendruck.

## Was es kann

**Sequenz aus einer Datei erkennen.** `render_0042.png` genügt – Präfix, Zahlenbreite
und Suffix werden aus dem Ordner abgeleitet, nicht aus der einen Datei geraten.
Fehlende Nummern werden erkannt und in der Zeitleiste rot markiert: Ein
unvollständiger Render ist auf einen Blick zu sehen.

**Puffern in zwei Stufen.** Dekodierte Bilder liegen in einem Ringpuffer im
Arbeitsspeicher, darunter als rohe Pixelblöcke auf der Platte. Einen solchen Block
zu lesen kostet rund 6 ms, dasselbe PNG erneut zu entpacken rund 31 ms. Für eine
Sequenz, die nicht vollständig in den Speicher passt, ist das der Unterschied
zwischen flüssigem Loopen und Nachpuffern bei jeder Runde.

**Sich zurücknehmen.** Threadzahl und Priorität folgen der gemessenen Systemlast.
Der Puffer dagegen wird nur bei echtem Speichermangel gekürzt – unter CPU-Last ist
er die einzige Reserve, aus der die Wiedergabe noch flüssig laufen kann.

**Flüssig bleiben, wenn es eng wird.** Die Wiedergabe ist zeitbasiert. Liegt die
Sollrate auf dem Bildschirmtakt, gibt es dabei keinerlei Reserve: Bei 60 fps auf
60 Hz muss jeder einzelne Kompositionsschritt ein Bild tragen. Gemessen kamen so
40 von 60 Bildern an. An den Schirmtakt gekoppelt sind es 59, ohne einen einzigen
Sprung. Abschaltbar.

**Zoomen und schwenken**, entkoppelt von der Dekodiergröße: Beim Nachschärfen in
voller Auflösung springt das Bild nicht.

**Schnell nachsehen, ob die Belichtung trägt.** Ein ausklappbares Seitenpanel mit
Belichtung, Schwarzpunkt, Kontrast, Sättigung und Gamma, Histogramm mit Warnung vor
ausgebrannten Lichtern, A/B-Vergleich gegen einen gemerkten Frame und speicherbaren
Voreinstellungen. Die Korrektur betrifft nur die Anzeige; die Dateien bleiben
unberührt. Der Export fragt, ob er sie übernehmen soll.

**Exportieren**, wenn doch ein Video gebraucht wird – über ffmpeg, mit In-/Out-Punkt,
Skalierung und den üblichen Zielformaten.

## Bauen

```bash
dotnet publish FrameFlip/FrameFlip.csproj -c Release
```

Ergebnis: `bin/Release/net8.0-windows/win-x64/publish/FrameFlip.exe` – eine einzelne
Datei, self-contained, kein Installer und kein installiertes .NET nötig. Hinkopieren,
starten, fertig; für den Dauerbetrieb eine Verknüpfung in den Autostart.

Die Datei ist rund 160 MB, weil die Kompression bewusst **aus** ist: Mit Kompression
entpackt der Host die Assemblies in privaten Speicher und braucht im Betrieb mehr
davon (~130 MB statt ~60 MB). Wem die Dateigröße wichtiger ist, setzt in der
`.csproj` `EnableCompressionInSingleFile` auf `true` – dann sind es ~72 MB.

## Loslegen

1. `FrameFlip.exe` starten – das Symbol erscheint im Tray, sonst passiert nichts.
2. Im Explorer ein Bild der Sequenz markieren.
3. **Strg + Alt + Leertaste**.

Das Fenster öffnet randlos in Mediengröße auf dem Monitor des Explorers. `Esc` oder
ein Klick daneben schließt es wieder.

## Bedienung

| Eingabe | Wirkung |
|---|---|
| Hotkey (Standard **Strg+Alt+Leertaste**) | Vorschau öffnen bzw. schließen |
| `Leertaste` / Klick ins Bild | Play / Pause |
| `→` / `←` | ein Bild vor / zurück |
| Mausrad | zoomen (pausiert) bzw. scrubben (während der Wiedergabe), `Strg` kehrt es um |
| Doppelklick / `Strg+0` | zwischen Einpassung und 100 % wechseln |
| `L` | Loop umschalten |
| `I` / `O` / `Entf` | In-/Out-Punkt setzen bzw. aufheben |
| `1` / `2` / `3` | Dekodiergröße 100 % / 50 % / 25 % – verlängert den Puffervorlauf |
| `Tab` | Bildanpassung ein-/ausblenden |
| `A` / `C` | Frame merken / gegen den gemerkten umschalten |
| `E` | Exportdialog |

Die vollständige Tabelle steht in [docs/Technik.md](docs/Technik.md#bedienung).

## Formate

PNG, JPEG, TIFF und BMP über die Windows Imaging Component. WebP zusätzlich, sofern
die *WebP Image Extension* von Microsoft installiert ist – fehlt sie, fällt genau
dieses Format sauber aus.

**EXR nicht.** Der Platz dafür ist freigehalten: eine weitere `IFrameDecoder`-Implementierung,
registriert in `FrameDecoderRegistry.CreateDefault()`. An Cache, Wiedergabe und
Oberfläche ändert sich dadurch nichts.

## ffmpeg

Für den Videoexport, und **nicht mitgeliefert**: Übliche ffmpeg-Builds stehen unter
der GPL, und die würde sich auf FrameFlip erstrecken. FrameFlip sucht ffmpeg im PATH
und an den üblichen Stellen; der Pfad lässt sich auch von Hand setzen.

```bash
winget install Gyan.FFmpeg
```

Ohne ffmpeg funktioniert alles außer dem Export.

## Wie es funktioniert

Die Begründungen stehen bei den Entscheidungen, nicht in einer Zusammenfassung –
[docs/Technik.md](docs/Technik.md) erklärt jede davon mitsamt der Messung dahinter:

* [Erst puffern, dann abspielen](docs/Technik.md#erst-puffern-dann-abspielen)
* [Zweite Stufe: Rohcache auf der Platte](docs/Technik.md#zweite-stufe-rohcache-auf-der-platte)
* [Kopplung an den Bildschirmtakt](docs/Technik.md#ausnahme-kopplung-an-den-bildschirmtakt)
* [Dynamische Ressourcen](docs/Technik.md#dynamische-ressourcen)
* [Zoom und Auflösung](docs/Technik.md#zoom-und-auflösung)
* [Sperrdisziplin](docs/Technik.md#sperrdisziplin)

## Tests

```bash
dotnet run --project FrameFlip.Tests
```

387 Zusicherungen, keine Fremdpakete – ein schlichtes Konsolenprojekt statt eines
Testrahmens mit drei NuGet-Abhängigkeiten. Exitcode ungleich 0 heißt Fehlschlag.

Geprüft wird, was sich ohne sichtbares Fenster prüfen lässt und schon einmal
falsch war: Zoommathematik, Puffergrenzen, Sequenzerkennung samt Lücken,
ffmpeg-Argumente, Fensterplatzierung über mehrere Monitore, das Lastprofil, die
Bildkorrektur und der Rohcache. Die Testsequenzen in
`FrameFlip-Testsequenzen/` gehören dazu – eine davon hat absichtlich Lücken.

## Was noch fehlt

* Blender-Addon, das die Ausgabepfade laufender Renders meldet
* Live-Modus: neu geschriebene Frames während des Renders nachladen
* Renderwarteschlange

## Lizenz

MIT – siehe [LICENSE](LICENSE).

ffmpeg wird nicht mitgeliefert und nicht eingebunden, sondern als eigenständiges
Programm aufgerufen. Seine Lizenz bleibt seine eigene Sache.
