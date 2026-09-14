using System.Windows;
using System.Windows.Controls;
using FrameFlip.Imaging;
using FrameFlip.Views;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Drei Dinge an den Reglern, die falsch waren und es beim Ausprobieren nicht
/// gewesen sein muessen - ein Regler, der sich seltsam anfuehlt, wird selten
/// gemeldet und noch seltener gefunden.
/// </summary>
public static class PanelPolishInvariants
{
    public static void Run()
    {
        CoupledPoints();
        TemperatureIsEven();
        ChannelsAreDistinguishable();
    }

    /// <summary>
    /// Schwarz- und Weisspunkt duerfen sich nicht ueberkreuzen.
    ///
    /// Vorher wurden beide einzeln begrenzt, aber nicht gegeneinander. Ein
    /// Schwarzpunkt oberhalb des Weisspunkts macht die Spanne negativ, und damit
    /// dreht sich die Tonwertkurve um: Das Bild kippt ins Negative, und niemand
    /// bringt das mit dem Regler in Verbindung, den er zuletzt bewegt hat.
    /// </summary>
    private static void CoupledPoints()
    {
        Check.Group("Schwarz- und Weisspunkt halten Abstand");

        var crossed = new ImageAdjustments { BlackPoint = 0.9, WhitePoint = 0.2 }.Clamped();

        Check.That(crossed.BlackPoint < crossed.WhitePoint, "der Schwarzpunkt bleibt unter dem Weissen",
                   $"{crossed.BlackPoint:0.###} gegen {crossed.WhitePoint:0.###}");

        Check.Near(crossed.WhitePoint, 0.2, 0.001, "der Weisspunkt bleibt, wo er war");
        Check.Near(crossed.BlackPoint, 0.2 - ImageAdjustments.MinimumSpan, 0.001,
                   "und der schwarze weicht bis auf den Mindestabstand");

        // Die Spanne darf nie null oder negativ werden - daran haengt eine Division.
        foreach (var (black, white) in new[] { (0.9, 0.05), (0.5, 0.5), (2.0, 0.1), (0.0, 0.05) })
        {
            var held = new ImageAdjustments { BlackPoint = black, WhitePoint = white }.Clamped();
            double span = held.WhitePoint - held.BlackPoint;

            Check.That(span >= ImageAdjustments.MinimumSpan - 0.001,
                       $"Spanne bleibt positiv bei {black:0.##}/{white:0.##}", $"{span:0.###}");
        }

        // Der uebliche Fall bleibt unberuehrt.
        var normal = new ImageAdjustments { BlackPoint = 0.05, WhitePoint = 0.95 }.Clamped();
        Check.Near(normal.BlackPoint, 0.05, 0.001, "eine gewoehnliche Einstellung bleibt stehen");
        Check.Near(normal.WhitePoint, 0.95, 0.001, "in beiden Werten");
    }

    /// <summary>
    /// Der Temperaturregler muss ueber seine Laenge gleich wirken.
    ///
    /// In Kelvin tut er das nicht: Ein Schritt von 1000 K bewirkt bei 2000 K das
    /// Dreissigfache dessen, was er bei 14000 K bewirkt - die rechte Haelfte des
    /// Reglers taete praktisch nichts. Geprueft wird deshalb, dass gleiche
    /// Reglerwege ungefaehr gleiche Wirkung haben.
    /// </summary>
    private static void TemperatureIsEven()
    {
        Check.Group("Temperaturregler wirkt gleichmaessig");

        var panel = new GradingPanel();
        panel.Measure(new Size(300, 900));
        panel.Arrange(new Rect(0, 0, 300, 900));
        panel.UpdateLayout();

        var slider = (Slider)panel.FindName("TemperatureSlider");
        Check.That(slider is not null, "der Regler ist da");
        if (slider is null) return;

        Check.That(slider.IsDirectionReversed,
                   "er laeuft umgekehrt, damit rechts waermer heisst");

        // Ein Zehntel des Reglerwegs, an drei Stellen gemessen: die Wirkung auf ein
        // graues Bild darf sich nicht um Groessenordnungen unterscheiden.
        double step = (slider.Maximum - slider.Minimum) / 10;
        var effects = new List<double>();

        foreach (double at in new[] { slider.Minimum, (slider.Minimum + slider.Maximum) / 2,
                                      slider.Maximum - step })
        {
            effects.Add(Effect(at, at + step));
        }

        double smallest = effects.Min();
        double largest = effects.Max();
        double ratio = largest / Math.Max(1e-9, smallest);

        // Zehn, nicht zwei. Mired macht die Schritte gleich gross, aber die
        // chromatische Anpassung ist selbst nicht linear: bei 2200 K liegt der
        // Weisspunkt so weit von D65, dass dieselbe Strecke mehr bewirkt. Gemessen
        // sind es rund acht - die Schranke laesst Luft, ohne einen Rueckfall auf
        // Kelvin durchzulassen.
        Check.That(ratio < 10, "gleiche Reglerwege wirken ungefaehr gleich",
                   $"{smallest:0.####} bis {largest:0.####}, Verhaeltnis {ratio:0.#}");

        // Die Gegenprobe ist das eigentliche Argument: derselbe Regler in Kelvin
        // liegt bei rund zweihundert. Ohne sie waere "unter zehn" eine willkuerliche
        // Zahl statt einer gewonnenen.
        var inKelvin = new List<double>();
        double kelvinStep = (15000 - 2000) / 10.0;

        foreach (double at in new[] { 2000.0, 8500.0, 13700.0 })
            inKelvin.Add(Effect(1e6 / at, 1e6 / (at + kelvinStep)));

        double kelvinRatio = inKelvin.Max() / Math.Max(1e-9, inKelvin.Min());

        Check.That(kelvinRatio > ratio * 5, "und sind es in Kelvin deutlich weniger",
                   $"Kelvin {kelvinRatio:0.#} gegen Mired {ratio:0.#}");

        static double Effect(double fromMired, double toMired)
        {
            var a = Colour(1e6 / fromMired);
            var b = Colour(1e6 / toMired);
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        }

        static (float R, float G, float B) Colour(double kelvin)
        {
            var tool = new FrameFlip.Imaging.Grading.WhiteBalanceTool { Kelvin = (float)kelvin };
            tool.Prepare();

            float r = 0.5f, g = 0.5f, b = 0.5f;
            tool.Apply(ref r, ref g, ref b);
            return (r, g, b);
        }
    }

    /// <summary>
    /// Die neun Zonenregler sehen einander gleich. Ohne eine sichtbare Kennzeichnung
    /// weiss niemand, welcher Rot ist - ein Hinweis, der nur beim Verweilen mit der
    /// Maus erscheint, hilft dabei nicht.
    /// </summary>
    private static void ChannelsAreDistinguishable()
    {
        Check.Group("Zonenregler sind unterscheidbar");

        var panel = new GradingPanel();
        panel.Measure(new Size(300, 1400));
        panel.Arrange(new Rect(0, 0, 300, 1400));
        panel.UpdateLayout();

        // Zu jedem der neun Regler muss eine Beschriftung in der Kanalfarbe gehoeren.
        foreach (string zone in new[] { "Lift", "Gamma", "Gain" })
        {
            foreach (string channel in new[] { "R", "G", "B" })
            {
                var slider = panel.FindName($"{zone}{channel}Slider") as Slider;
                if (slider is null)
                {
                    Check.That(false, $"{zone}{channel}Slider ist da");
                    continue;
                }

                var label = Sibling(slider, channel);
                Check.That(label is not null, $"{zone} {channel} traegt seinen Buchstaben");

                if (label is not null)
                {
                    Check.That(!ReferenceEquals(label.Foreground, panel.Foreground),
                               $"{zone} {channel} in eigener Farbe");
                }
            }
        }

        static TextBlock? Sibling(Slider slider, string text)
        {
            if (slider.Parent is not Grid grid) return null;

            foreach (var child in grid.Children)
                if (child is TextBlock block && block.Text == text) return block;

            return null;
        }
    }
}
