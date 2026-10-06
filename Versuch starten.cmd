@echo off
rem Versuch (docs/Atelier-Knoten-Gruppen.md): Knoten in Gruppen, neben der normalen App.
rem Eigene Konfiguration unter %APPDATA%\FrameFlip-Versuch-Knoten - Einstellungen, Anordnung
rem und Kopplung der normalen App bleiben unberuehrt. FRAMEFLIP_VERSUCH sorgt dafuer, dass
rem Projekte am Quellordner nur gelesen und nie ueberschrieben werden. Bilder oeffnet man
rem im Atelier mit "Bild oeffnen" oder per Ziehen.
set "FRAMEFLIP_CONFIG=%APPDATA%\FrameFlip-Versuch-Knoten\config.json"
set "FRAMEFLIP_VERSUCH=1"
if not exist "%APPDATA%\FrameFlip-Versuch-Knoten" mkdir "%APPDATA%\FrameFlip-Versuch-Knoten"
start "" "%~dp0FrameFlip\bin\Debug\net8.0-windows\win-x64\FrameFlip.exe" --show
