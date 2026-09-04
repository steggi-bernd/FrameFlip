namespace FrameFlip.Tests;

/// <summary>Minimaler Testlaeufer. Sammelt Fehlschlaege, statt beim ersten abzubrechen.</summary>
public static class Check
{
    private static readonly List<string> Failures = new();
    private static int _passed;
    private static string _group = "";

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
