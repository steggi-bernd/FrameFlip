namespace FrameFlip.Tests;

/// <summary>Minimaler Testlaeufer. Sammelt Fehlschlaege, statt beim ersten abzubrechen.</summary>
public static class Check
{
    private static readonly List<string> Failures = new();
    private static readonly List<string> Slow = new();
    private static int _passed;
    private static string _group = "";

    /// <summary>
    /// Mit FRAMEFLIP_TIMING=report werden Zeitgrenzen nur gemeldet. Die CI setzt das:
    /// Auf einem geteilten Rechner misst eine feste Millisekundengrenze die Maschine,
    /// nicht den Code. Lokal bleibt jede Zeitpruefung eine echte Zusicherung.
    /// </summary>
    private static readonly bool ReportTimingOnly = string.Equals(
        Environment.GetEnvironmentVariable("FRAMEFLIP_TIMING"), "report", StringComparison.OrdinalIgnoreCase);

    public static void Group(string name)
    {
        _group = name;
        Console.WriteLine();
        Console.WriteLine(name);
        Console.WriteLine(new string('-', name.Length));
    }

    public static void That(bool condition, string what, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [ok]   {what}");
            return;
        }

        Failures.Add($"{_group}: {what}" + (detail is null ? "" : $"  ({detail})"));
        Console.WriteLine($"  [FEHL] {what}" + (detail is null ? "" : $"  {detail}"));
    }

    /// <summary>Eine Zusicherung ueber gemessene Zeit. Siehe <see cref="ReportTimingOnly"/>.</summary>
    public static void Timing(bool condition, string what, string? detail = null)
    {
        if (condition || !ReportTimingOnly)
        {
            That(condition, what, detail);
            return;
        }

        Slow.Add($"{_group}: {what}" + (detail is null ? "" : $"  ({detail})"));
        Console.WriteLine($"  [ZEIT] {what}" + (detail is null ? "" : $"  {detail}"));
    }

    public static void Near(double actual, double expected, double tolerance, string what)
        => That(Math.Abs(actual - expected) <= tolerance, what,
                $"erwartet {expected:0.####}, ist {actual:0.####}, Toleranz {tolerance:0.####}");

    public static void Throws<T>(Action action, string what) where T : Exception
    {
        try
        {
            action();
            That(false, what, $"{typeof(T).Name} wurde nicht geworfen");
        }
        catch (T)
        {
            That(true, what);
        }
        catch (Exception ex)
        {
            That(false, what, $"stattdessen {ex.GetType().Name}");
        }
    }

    public static int Report()
    {
        Console.WriteLine();
        if (Slow.Count > 0)
        {
            Console.WriteLine($"{Slow.Count} Zeitpruefungen ueber der Grenze, nur gemeldet (FRAMEFLIP_TIMING=report):");
            foreach (var slow in Slow) Console.WriteLine($"  - {slow}");
            Console.WriteLine();
        }

        if (Failures.Count == 0)
        {
            Console.WriteLine($"Alle {_passed} Zusicherungen erfuellt.");
            return 0;
        }

        Console.WriteLine($"{Failures.Count} von {_passed + Failures.Count} Zusicherungen fehlgeschlagen:");
        foreach (var failure in Failures) Console.WriteLine($"  - {failure}");
        return 1;
    }
}
