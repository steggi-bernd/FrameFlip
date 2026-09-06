using System.Text.Json;
using FrameFlip.Bridge;
using FrameFlip.Diagnostics;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Die Maschinenwerte, die zum Handy gehen.
///
/// Der Kern hier ist eine Regel, die sich leicht verletzen laesst und deren
/// Verletzung niemandem auffaellt: Was nicht gemessen werden konnte, wird
/// weggelassen - nicht als Null geschickt. Eine Null im Feld "gpu" sieht auf dem
/// Handy aus wie eine schlafende Maschine; ein fehlendes Feld wird als
/// Gedankenstrich gezeigt und sagt die Wahrheit.
/// </summary>
public static class MachineInvariants
{
    public static void Run()
    {
        Check.Group("nvidia-smi - Ausgabe lesen");

        // So sieht die echte Zeile aus, gemessen auf dem Entwicklungsrechner.
        var full = NvidiaProbe.Parse("33, 2988, 12282, 49, NVIDIA GeForce RTX 4070 Ti");

        Check.That(full.UtilizationPercent == 33, "Auslastung", full.UtilizationPercent?.ToString());
        Check.That(full.MemoryUsedMb == 2988, "belegter Speicher", full.MemoryUsedMb?.ToString());
        Check.That(full.MemoryTotalMb == 12282, "Gesamtspeicher", full.MemoryTotalMb?.ToString());
        Check.That(full.TemperatureCelsius == 49, "Temperatur", full.TemperatureCelsius?.ToString());
        Check.That(full.Name == "NVIDIA GeForce RTX 4070 Ti", "Kartenname", full.Name);
        Check.That(!full.IsEmpty, "die Zeile gilt als brauchbar");

        // Ein Komma im Namen darf die Zahlen davor nicht verschieben - deshalb steht
        // er hinten und wird wieder zusammengesetzt.
        var comma = NvidiaProbe.Parse("5, 1, 2, 3, Karte, Sonderausgabe");
        Check.That(comma.Name == "Karte, Sonderausgabe", "Komma im Namen", comma.Name);
        Check.That(comma.UtilizationPercent == 5, "die Zahlen bleiben stehen");

        // Einzelne Werte fehlen auf manchen Karten - nvidia-smi schreibt dann [N/A].
        var partial = NvidiaProbe.Parse("12, 1024, 8192, [N/A]");

        Check.That(partial.TemperatureCelsius is null, "fehlende Temperatur bleibt leer");
        Check.That(partial.UtilizationPercent == 12, "der Rest wird trotzdem gelesen");

        foreach (string junk in new[] { "", "   ", "kaputt", "1, 2", "a, b, c, d" })
        {
            var parsed = NvidiaProbe.Parse(junk);
            Check.That(parsed.UtilizationPercent is null, $"Unsinn \"{junk}\" ergibt nichts");
        }

        Check.That(NvidiaProbe.Parse(null).IsEmpty, "nichts ergibt nichts");

        Check.Group("Nutzlast - was fehlt, wird weggelassen");

        // Ohne Messung darf kein einziges Maschinenfeld auftauchen.
        var bare = Read(RemoteLink.Describe(null));

        Check.That(bare.GetProperty("t").GetString() == "idle", "Leerlauf wird gemeldet");

        foreach (string field in new[] { "cpu", "gpu", "ramUsedMb", "ramTotalMb", "vramUsedMb", "vramTotalMb", "gpuTemp" })
            Check.That(!bare.TryGetProperty(field, out _), $"ohne Messung kein Feld \"{field}\"");

        Check.That(!bare.TryGetProperty("gpuName", out _), "ohne Messung kein Kartenname");

        // Mit Messung stehen sie da - und zwar mit den echten Gesamtgroessen, nicht
        // mit den Zahlen aus dem Entwurf. Der Rechner hat 12 GB, nicht 24.
        var load = new LoadSnapshot(41.5, null, AvailableMb: 20000, LoadLevel.Moderate) { TotalMb = 65536 };
        var gpu = new GpuReading(97, 11000, 12282, 71, "NVIDIA GeForce RTX 4070 Ti");

        var filled = Read(RemoteLink.Describe(null, load, gpu));

        Check.Near(filled.GetProperty("cpu").GetDouble(), 41.5, 0.01, "CPU geht mit");
        Check.That(filled.GetProperty("ramUsedMb").GetInt64() == 45536, "belegter RAM ist gesamt minus frei",
                   filled.GetProperty("ramUsedMb").GetInt64().ToString());
        Check.That(filled.GetProperty("ramTotalMb").GetInt64() == 65536, "Gesamt-RAM geht mit");
        Check.That(filled.GetProperty("vramTotalMb").GetInt64() == 12282, "die echte VRAM-Groesse geht mit");
        Check.That(filled.GetProperty("gpuTemp").GetInt32() == 71, "Temperatur geht mit");
        Check.That(filled.GetProperty("gpuName").GetString() == "NVIDIA GeForce RTX 4070 Ti", "der Kartenname geht mit");
        Check.That(filled.GetProperty("gpu").GetDouble() == 97, "GPU-Last kommt von nvidia-smi, wenn der Zaehler schweigt");

        // Der PDH-Zaehler ist herstellerunabhaengig und hat deshalb Vorrang.
        var counted = new LoadSnapshot(10, 88, AvailableMb: 1000, LoadLevel.Moderate) { TotalMb = 2000 };
        var preferred = Read(RemoteLink.Describe(null, counted, gpu));

        Check.That(preferred.GetProperty("gpu").GetDouble() == 88,
                   "der Zaehler hat Vorrang vor nvidia-smi",
                   preferred.GetProperty("gpu").GetDouble().ToString());

        Check.Group("Nutzlast - auch im Leerlauf");

        // Gerade ohne Render ist die Frage, ob der Rechner ueberhaupt wach ist.
        Check.That(filled.GetProperty("t").GetString() == "idle" && filled.TryGetProperty("cpu", out _),
                   "die Maschinenwerte stehen auch ohne Render da");
    }

    private static JsonElement Read(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
