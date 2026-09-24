# FrameFlip – Hinweise für Claude

FrameFlip ist eine Windows-Tray-Anwendung in C#/WPF (.NET 8, `net8.0-windows`):
Vorschauplayer für Blender-Bildsequenzen, dazu Dashboard, Zuschauerseite (`web/`)
und der Bearbeitungsbereich Atelier. Verwandte Repos: `FrameFlipApp` (Android,
Kotlin), `frameflip-relay` (Go), `FrameFlipBridge` (Blender-Addon).

- `main` ist der veröffentlichte Stand. Entwickelt wird auf `feature/atelier`.
- Dokumentation, Entwürfe und Pläne liegen in `docs/`, geschrieben auf Deutsch.
  Quelltextkommentare verwenden ae/oe/ue statt Umlauten.

## Refactoring

Reihenfolge, Stand und Regeln stehen in `docs/Refactoring-Plan.md`, der Studio-Teil
in `docs/Refactoring-Studio.md`. Vor jedem Schnitt dort nachlesen, danach Stand
und Abnahme nachtragen. Kurzfassung der Regeln:

- Ein Schnitt je Zweig und PR. Zuerst Charakterisierungstests (`test:`), gefundene
  Fehler als eigener Commit (`fix:`), dann die Auslagerung (`refactor:`), zuletzt
  der Plan (`docs:`). Refactoring-Commits auf Englisch in diesem Stil.
- Keine Produktänderung in einem Refactoring-Schnitt. Auffälliges Verhalten
  notieren und nachfragen, nicht nebenbei ändern.
- Öffentliche Fassaden und die Kopplungsprotokolle (`PairingKey`, `SecureChannel`,
  `RelayClient`, `RelayControl`, `Envelope`, `PairingInvite`) bleiben unverändert.

## Tests

Zwei Prüfreihen, beide nur unter Windows lauffähig (WPF):

- `dotnet run --project FrameFlip.Tests -c Release` – Invarianten, lokal gut eine
  Minute. Einzelne Gruppen: `... -- DashboardPlaybackInvariants ExportInvariants.RequestMath`
  oder `... -- --only=Klasse,Klasse.Methode`. Neue Gruppen in `FrameFlip.Tests/Program.cs`
  registrieren.
- `dotnet run --project FrameFlip.UiTests -c Release -- <Ausgabeordner>` – baut echte
  WPF-Elemente auf und schreibt Bilder in den Ausgabeordner (Standard `review/`).

Zeitgrenzen in Tests gehören in `Check.Timing` statt `Check.That`. Mit
`FRAMEFLIP_TIMING=report` (so in der CI gesetzt) werden Überschreitungen nur
gemeldet. Lokal sind sie echte Zusicherungen. Kippt lokal nur eine Zeitprüfung,
zuerst die Rechnerlast prüfen und die Gruppe einzeln wiederholen.

Tests verwenden eigene Konfigurationen (`FRAMEFLIP_CONFIG`) und synthetische
Bilder. Nie die Einstellungen oder Medien des Nutzers lesen oder verändern.

## In Cloud-Sitzungen (Linux)

Die Tests laufen dort nicht. Ablauf: Zweig pushen, PR öffnen. Die CI
(`.github/workflows/ci.yml`, `windows-latest`) baut und startet beide Reihen.
Das Ergebnis über die PR-Checks lesen und rote Läufe beheben. Ein reiner
Kompiliertest unter Linux braucht `-p:EnableWindowsTargeting=true`. Die App selbst
lässt sich in der Cloud nicht starten, Oberflächenfragen gehören in eine lokale Sitzung.

## Lokal (Windows)

- Eine laufende `FrameFlip.exe` sperrt `bin/` und lässt Builds mit MSB3021/MSB3027
  scheitern. Für Testläufe ohne Beenden in einen Nebenordner im Repo bauen:
  `dotnet build FrameFlip.Tests -o FrameFlip.Tests/bin/side`.
- Der Haupt-Checkout wird von einer laufenden Atelier-Sitzung benutzt. Strukturarbeit
  läuft in einem eigenen Worktree unter `.worktrees/`, nicht im gemeinsamen Checkout.
