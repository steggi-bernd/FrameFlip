namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Womit ein Effekt beginnt, wenn man ihn hinzufuegt (docs/Atelier-Arbeitsablauf.md,
/// Entscheidung 2): Effekte bekommen einen sichtbaren Startwert, Korrekturen bleiben neutral.
///
/// Der Unterschied ist die Erwartung. Eine Korrektur fuegt man hinzu, um an ihr zu drehen -
/// dass sie auf null steht, ist richtig, und jeder Wert daneben waere ein ungefragter Eingriff.
/// Einen Effekt fuegt man hinzu, um ihn zu SEHEN: Pixel Sort mit geschlossenem Fenster,
/// Filmkorn ohne Korn - das sah aus wie ein Werkzeug, das nicht funktioniert.
///
/// Die Grundstellung der Werkzeugklassen bleibt dabei neutral. Der Stapel legt jedes Werkzeug
/// an, auch ungenutzt; Startwerte in den Klassen selbst schalteten in jedem Rezept jeden Effekt
/// ein. Gesetzt wird nur hier, im Augenblick des Hinzufuegens, und nur, solange das Werkzeug
/// noch neutral ist - ein kopierter oder schon eingestellter Effekt behaelt seine Werte.
/// </summary>
public static class EffectStart
{
    /// <summary>
    /// Setzt den Startwert, wenn das Werkzeug ein Effekt ist und noch in Grundstellung steht.
    /// True, wenn etwas gesetzt wurde.
    /// </summary>
    public static bool Apply(object? tool)
    {
        switch (tool)
        {
            // Das Fenster offen: Laeufe zwischen den dunklen Tiefen und den Lichtern.
            case SortTool { IsNeutral: true } sort:
                sort.Low = 0.25f;
                sort.High = 0.8f;
                return true;

            case DiffusionTool { IsNeutral: true } diffusion:
                diffusion.Amount = 1f;
                return true;

            case DitherTool { IsNeutral: true } dither:
                dither.Amount = 1f;
                return true;

            case GrainTool { IsNeutral: true } grain:
                grain.Amount = 0.25f;
                return true;

            case VignetteTool { IsNeutral: true } vignette:
                vignette.Amount = -0.35f;
                return true;

            // Glanz und Halation greifen erst ueber ihrer Schwelle - bei einem Bild ohne Lichter
            // ueber Weiss bleibt es trotz Startwert ruhig. Das ist die Natur des Effekts.
            case BloomTool { IsNeutral: true } bloom:
                bloom.Amount = 0.4f;
                return true;

            case HalationTool { IsNeutral: true } halation:
                halation.Amount = 0.4f;
                return true;

            case ChromaticTool { IsNeutral: true } chromatic:
                chromatic.Amount = 0.3f;
                return true;

            case DistortionTool { IsNeutral: true } distortion:
                distortion.Amount = 0.15f;
                return true;

            // Die drei mit Renderdaten wirken nur, wenn die Datei sie fuehrt - dann aber gleich.
            case MotionBlurTool { IsNeutral: true } motion:
                motion.Shutter = 0.5f;
                return true;

            case DisplaceTool { IsNeutral: true } displace:
                displace.Amount = 20f;
                return true;

            case DepthFieldTool { IsNeutral: true } depth:
                depth.Aperture = 0.3f;
                return true;

            default:
                return false;
        }
    }
}
