@echo off
rem Versuch (docs/Atelier-UX-Versuch.md): startet den Versuchszweig neben der normalen App.
rem Eigene Konfiguration unter %APPDATA%\FrameFlip-Versuch - die Einstellungen, Anordnung und
rem Kopplung der normalen App bleiben unberuehrt. Bilder oeffnet man im Atelier mit "Bild oeffnen"
rem oder per Ziehen.
set "FRAMEFLIP_CONFIG=%APPDATA%\FrameFlip-Versuch\config.json"
if not exist "%APPDATA%\FrameFlip-Versuch" mkdir "%APPDATA%\FrameFlip-Versuch"
start "" "%~dp0FrameFlip\bin\Debug\net8.0-windows\win-x64\FrameFlip.exe" --show
