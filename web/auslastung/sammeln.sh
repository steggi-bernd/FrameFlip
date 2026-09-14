#!/bin/sh
# Haelt fest, wieviele Raeume der Relay gerade belegt hat.
#
# Eine ZAHL, sonst nichts. /health nennt weder Raumkennungen noch Adressen - es gibt
# hier nichts, was auf eine Person zurueckfuehrte, und deshalb auch nichts, das
# geschuetzt werden muesste. Wer hier je mehr aufschreiben wollte als diese Zahl,
# sollte vorher die Datenschutzerklaerung lesen.
#
# Aufruf alle fuenf Minuten ueber cron:
#   */5 * * * * /home/steggi/relay-stats/sammeln.sh

ZIEL="${FRAMEFLIP_STATS_DIR:-/home/steggi/caddy/last}"
QUELLE="${FRAMEFLIP_HEALTH:-https://relay.steggi-matrix.work/health}"

mkdir -p "$ZIEL"

ANTWORT=$(curl -sf --max-time 10 "$QUELLE") || exit 0

# Nur die Zahl hinter "rooms". Kommt etwas anderes zurueck, wird nichts geschrieben -
# eine Luecke in der Reihe ist eine ehrlichere Auskunft als eine erfundene Null.
RAEUME=$(printf '%s' "$ANTWORT" | sed -n 's/.*"rooms":\([0-9]\{1,\}\).*/\1/p')

[ -n "$RAEUME" ] || exit 0

MONAT=$(date -u +%Y-%m)
DATEI="$ZIEL/$MONAT.csv"

[ -f "$DATEI" ] || echo "zeit,raeume" > "$DATEI"

echo "$(date -u +%Y-%m-%dT%H:%M:%SZ),$RAEUME" >> "$DATEI"

# Eine Liste der vorhandenen Monate, damit die Seite weiss, was es gibt.
( cd "$ZIEL" && ls -1 [0-9][0-9][0-9][0-9]-[0-9][0-9].csv 2>/dev/null | sed 's/\.csv$//' | sort ) > "$ZIEL/monate.txt"
