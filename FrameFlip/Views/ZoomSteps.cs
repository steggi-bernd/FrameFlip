namespace FrameFlip.Views;

/// <summary>
/// Die Stufen des Mausrad-Zooms - fuer das Vorschaufenster und das Atelier.
///
/// Beide zeigen ein Bild, beide zoomen mit dem Rad, und wer in einem die Stufen
/// kennt, soll sie im anderen wiederfinden. Die Anzeige selbst bleibt getrennt: Das
/// Vorschaufenster rechnet mit einer Matrix, das Atelier mit der Groesse des
/// Bildelements, weil Greifrahmen, Pinsel und Knoten dort in Punkten auf dem Element
/// rechnen.
/// </summary>
public static class ZoomSteps
{
    /// <summary>Obergrenze: 800 %.</summary>
    public const double Max = 8.0;

    /// <summary>Eine Raste des Rades - spuerbar, gleichmaessig, wie in QuickLook.</summary>
    public const double Factor = 1.2;

    /// <summary>Wie nahe ein Massstab an 100 % oder der Einpassung liegen muss, um einzurasten.</summary>
    public const double Tolerance = 0.08;

    /// <summary>
    /// Rastet nahe 100 % und nahe der Einpassung ein. Ohne das trifft man die beiden
    /// wichtigen Stufen mit dem Mausrad nie genau.
    /// </summary>
    public static double Snap(double zoom, double fit)
    {
        if (Math.Abs(zoom - 1.0) < Tolerance) return 1.0;
        if (Math.Abs(zoom - fit) < Tolerance * fit) return fit;

        return zoom;
    }

    /// <summary>
    /// Der Massstab nach einer Raddrehung - nie unter der Einpassung, nie ueber
    /// <see cref="Max"/>.
    ///
    /// Anders als <see cref="Snap"/> allein ueberspringt ein Schritt 100 % und die
    /// Einpassung nicht: Laege 100 % zwischen dem alten und dem neuen Massstab, haelt
    /// das Rad dort an. Sonst fuehren 90 % und ein Schritt auf 108 %, und die Stufe,
    /// auf der ein Bildpunkt ein Bildpunkt ist, waere mit dem Rad nicht zu treffen.
    /// </summary>
    /// <param name="notches">Rasten, vorwaerts positiv. Ein Touchpad liefert Bruchteile.</param>
    public static double Next(double current, double notches, double fit)
    {
        if (current <= 0 || fit <= 0 || notches == 0) return current;

        double target = current * Math.Pow(Factor, notches);

        // Die naechstgelegene Stufe zuerst: Liegen beide dazwischen, haelt das Rad an
        // der, die es zuerst erreicht.
        foreach (double stop in Math.Abs(1.0 - current) <= Math.Abs(fit - current) ? new[] { 1.0, fit } : new[] { fit, 1.0 })
        {
            if (Math.Abs(current - stop) > 1e-9 && (current - stop) * (target - stop) < 0)
                return Math.Clamp(stop, fit, Math.Max(fit, Max));
        }

        return Math.Clamp(Snap(target, fit), fit, Math.Max(fit, Max));
    }
}
