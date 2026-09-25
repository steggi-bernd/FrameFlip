namespace FrameFlip.Tests;

/// <summary>
/// Zeitmessung fuer Pruefungen, die Kosten gegeneinander stellen.
///
/// Nacheinander gemessen traf eine Lastspitze oder eine Speicherbereinigung einen
/// einzelnen Fall - und verschob genau das Verhaeltnis, um das es geht. Hier werden die
/// Faelle abwechselnd gemessen, Runde um Runde, mit einer Bereinigung vor jedem Lauf,
/// und je Fall zaehlt der schnellste: das, was der Code kostet, wenn der Rechner ihn laesst.
/// </summary>
public static class Measure
{
    /// <summary>
    /// Die schnellste Zeit je Fall in Millisekunden, in der Reihenfolge der Faelle.
    /// Liegt das Ergebnis nicht <paramref name="within"/> der Grenze, gibt es einen zweiten
    /// Durchgang, dessen Bestzeiten mitzaehlen: Ein einzelner Ausreisser ist kein Befund,
    /// erst einer, der sich wiederholt.
    /// </summary>
    public static double[] Fastest(int rounds, Func<double[], bool> within, params Action[] cases)
    {
        var best = Round(rounds, cases);

        return within(best) ? best : Round(rounds, cases).Zip(best, Math.Min).ToArray();
    }

    private static double[] Round(int rounds, Action[] cases)
    {
        var best = Enumerable.Repeat(double.MaxValue, cases.Length).ToArray();

        for (int round = 0; round < rounds; round++)
        {
            for (int i = 0; i < cases.Length; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();

                var watch = System.Diagnostics.Stopwatch.StartNew();
                cases[i]();
                watch.Stop();

                best[i] = Math.Min(best[i], watch.Elapsed.TotalMilliseconds);
            }
        }

        return best;
    }
}
