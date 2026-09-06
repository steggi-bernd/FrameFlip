using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace FrameFlip.Diagnostics;

/// <summary>Was nvidia-smi ueber die Karte sagt. Alles einzeln optional.</summary>
public readonly record struct GpuReading(
    int? UtilizationPercent,
    long? MemoryUsedMb,
    long? MemoryTotalMb,
    int? TemperatureCelsius,
    string? Name = null)
{
    public bool IsEmpty => UtilizationPercent is null && MemoryUsedMb is null && TemperatureCelsius is null;
}

/// <summary>
/// Fragt nvidia-smi nach VRAM und Temperatur.
///
/// Die Auslastung selbst liefert schon <see cref="GpuLoadCounter"/> ueber die
/// PDH-Zaehler, herstellerunabhaengig. Was dort fehlt, ist VRAM und Temperatur -
/// und dafuer gibt es unter Windows keine allgemeine Quelle, nur die Werkzeuge der
/// Hersteller.
///
/// Deshalb ist das hier ausdruecklich eine Zusatzquelle und keine Voraussetzung:
/// Auf einer AMD- oder Intel-Karte, ohne Treiber, ohne nvidia-smi im Pfad bleibt es
/// still, und der Rest funktioniert weiter. Genau ein Fehlversuch reicht, um es
/// dauerhaft abzuschalten - ein Prozessstart alle paar Sekunden, der jedes Mal
/// scheitert, waere Lastverursachung ohne Gegenwert.
///
/// Der Aufruf ist kurzlebig, nicht dauerhaft. nvidia-smi kann mit -l dauerhaft
/// laufen und Zeilen ausgeben, aber ein Kindprozess, den jemand beenden muss,
/// ueberlebt einen Absturz der Tray-Anwendung als Waise. Ein Start alle fuenf
/// Sekunden kostet ein paar Millisekunden und hinterlaesst nichts.
/// </summary>
public sealed class NvidiaProbe : IDisposable
{
    /// <summary>Temperatur und VRAM aendern sich langsam; oefter zu fragen brachte nichts.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private const string Arguments =
        "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu,name " +
        "--format=csv,noheader,nounits";

    private readonly object _gate = new();

    private GpuReading _last;
    private DateTime _lastAttempt = DateTime.MinValue;
    private bool _unavailable;
    private bool _disposed;

    /// <summary>False, sobald ein Versuch gescheitert ist - dann wird nicht mehr gefragt.</summary>
    public bool IsAvailable => !_unavailable;

    /// <summary>
    /// Der letzte bekannte Stand. Fragt hoechstens alle fuenf Sekunden nach und
    /// blockiert dabei kurz - der Aufrufer ist der Metriktakt, kein Zeichenpfad.
    /// </summary>
    public GpuReading Read()
    {
        if (_disposed || _unavailable) return _last;

        lock (_gate)
        {
            if (DateTime.UtcNow - _lastAttempt < Interval) return _last;

            _lastAttempt = DateTime.UtcNow;
            _last = Query();

            return _last;
        }
    }

    private GpuReading Query()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi", Arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                _unavailable = true;
                return default;
            }

            string output = process.StandardOutput.ReadLine() ?? string.Empty;

            // Zwei Sekunden sind grosszuegig: nvidia-smi antwortet ueblicherweise in
            // unter hundert Millisekunden. Haengt es laenger, stimmt etwas mit dem
            // Treiber nicht, und dann soll es nicht den Metriktakt aufhalten.
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }

                _unavailable = true;
                return default;
            }

            if (process.ExitCode != 0)
            {
                _unavailable = true;
                return default;
            }

            return Parse(output);
        }
        catch (Exception)
        {
            // Nicht installiert, nicht im Pfad, keine NVIDIA-Karte. Alles derselbe Fall.
            _unavailable = true;
            return default;
        }
    }

    /// <summary>
    /// Erwartet "97, 18234, 24576, 71, NVIDIA GeForce RTX 4070 Ti" - Auslastung,
    /// belegt, gesamt, Grad, Name.
    ///
    /// Fehlende Werte gibt nvidia-smi als "[N/A]" aus, etwa die Temperatur auf
    /// manchen Notebook-Karten. Was sich nicht als Zahl lesen laesst, fehlt eben.
    /// </summary>
    public static GpuReading Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return default;

        string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 4) return default;

        // Der Name steht bewusst hinten und wird wieder zusammengesetzt: Er ist das
        // einzige Feld, in dem ein Komma vorkommen koennte, und wuerde sonst alle
        // Zahlen dahinter verschieben.
        string? name = parts.Length > 4
            ? string.Join(", ", parts.Skip(4)).Trim()
            : null;

        return new GpuReading(
            ReadInt(parts[0]),
            ReadLong(parts[1]),
            ReadLong(parts[2]),
            ReadInt(parts[3]),
            string.IsNullOrWhiteSpace(name) ? null : name);
    }

    private static int? ReadInt(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static long? ReadLong(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    public void Dispose() => _disposed = true;
}
