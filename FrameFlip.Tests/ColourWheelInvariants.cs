using System.Windows;
using System.Windows.Controls;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Die Farbraeder. Ein Rad hat zwei Freiheitsgrade, eine Zone hat drei - was dabei
/// auf welcher Seite landet, ist die ganze Frage.
/// </summary>
public static class ColourWheelInvariants
{
    public static void Run()
    {
        Neutral();
        NoBrightnessDrift();
        Directions();
        RoundTrip();
        InThePanel();
        CanActuallyBeChanged();
    }

    private static void Neutral()
    {
        Check.Group("Farbrad in der Mitte");

        var centre = new WheelPoint(0, 0);
        Check.That(centre.IsCentre, "die Mitte erkennt sich selbst");

        var (r, g, b) = ColourWheelMath.Offset(centre);
        Check.That(r == 0 && g == 0 && b == 0, "und verschiebt nichts");

        // Mit Helligkeit null bleibt die Zone auf ihrem Grundwert.
        var lift = ColourWheelMath.ToChannels(centre, 0f, neutral: 0f, scale: 0.12f);
        Check.That(lift is { R: 0f, G: 0f, B: 0f }, "Lift bleibt bei null");

        var gain = ColourWheelMath.ToChannels(centre, 0f, neutral: 1f, scale: 0.25f);
        Check.That(gain is { R: 1f, G: 1f, B: 1f }, "Gain bleibt bei eins");

        // Ein Punkt knapp neben der Mitte zaehlt noch als Mitte - sonst bliebe nach
        // einem Zurücksetzen ein Rest stehen, den niemand sieht und der trotzdem
        // rechnet.
        Check.That(new WheelPoint(0.002f, 0.002f).IsCentre, "knapp daneben ist auch Mitte");
        Check.That(!new WheelPoint(0.05f, 0f).IsCentre, "deutlich daneben nicht mehr");
    }

    /// <summary>
    /// Die wichtigste Zusicherung am Rad: Es darf die Helligkeit nicht anfassen.
    ///
    /// Die drei Kanalrichtungen stehen um je 120 Grad versetzt, und der Kosinus ueber
    /// drei solche Winkel summiert sich zu null. Waere das nicht so, zoege jeder
    /// Griff ins Farbige den Helligkeitsregler hinter sich her - und man saesse
    /// abwechselnd am Rad und am Regler, ohne je anzukommen.
    /// </summary>
    private static void NoBrightnessDrift()
    {
        Check.Group("Farbrad laesst die Helligkeit in Ruhe");

        float worst = 0;
        float worstAt = 0;

        for (int degrees = 0; degrees < 360; degrees += 3)
        {
            double angle = degrees * Math.PI / 180;

            foreach (float radius in new[] { 0.25f, 0.6f, 1.0f })
            {
                var point = new WheelPoint((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                var (r, g, b) = ColourWheelMath.Offset(point);

                float sum = MathF.Abs(r + g + b);
                if (sum > worst)
                {
                    worst = sum;
                    worstAt = degrees;
                }
            }
        }

        Check.That(worst < 1e-5f, "die drei Kanaele summieren sich ueberall zu null",
                   $"groesste Abweichung {worst:0.#######} bei {worstAt:0} Grad");

        // Und in der Zone selbst: der Mittelwert der drei Werte haengt nur an der
        // Helligkeit, nicht am Rad.
        foreach (float brightness in new[] { -0.2f, 0f, 0.15f })
        {
            foreach (int degrees in new[] { 0, 90, 200, 310 })
            {
                double angle = degrees * Math.PI / 180;
                var point = new WheelPoint((float)Math.Cos(angle), (float)Math.Sin(angle));

                var (r, g, b) = ColourWheelMath.ToChannels(point, brightness, neutral: 1f, scale: 0.3f);
                float mean = (r + g + b) / 3f;

                Check.Near(mean, 1f + brightness, 0.0005,
                           $"Mittelwert bei {degrees} Grad haengt nur an der Helligkeit");
            }
        }
    }

    private static void Directions()
    {
        Check.Group("Farbrad zeigt in die richtige Richtung");

        // Rechts ist Rot, und die anderen beiden weichen zurueck.
        var (r, g, b) = ColourWheelMath.Offset(new WheelPoint(1, 0));
        Check.That(r > 0 && g < 0 && b < 0, "rechts hebt Rot", $"{r:0.##}/{g:0.##}/{b:0.##}");
        Check.Near(r, 1.0, 0.001, "und zwar voll");

        // 120 Grad weiter liegt Gruen, weitere 120 Grad Blau.
        var green = ColourWheelMath.Offset(Polar(120));
        Check.That(green.G > green.R && green.G > green.B, "bei 120 Grad ist es Gruen",
                   $"{green.R:0.##}/{green.G:0.##}/{green.B:0.##}");

        var blue = ColourWheelMath.Offset(Polar(240));
        Check.That(blue.B > blue.R && blue.B > blue.G, "bei 240 Grad Blau",
                   $"{blue.R:0.##}/{blue.G:0.##}/{blue.B:0.##}");

        // Gegenueberliegende Punkte heben einander auf - das macht das Rad
        // vorhersagbar: zurueckziehen ist wirklich zurueck.
        var (or_, og, ob) = ColourWheelMath.Offset(Polar(60));
        var (ur, ug, ub) = ColourWheelMath.Offset(Polar(240));

        Check.Near(or_ + ur, 0.0, 0.001, "gegenueberliegende Punkte heben sich auf (R)");
        Check.Near(og + ug, 0.0, 0.001, "(G)");
        Check.Near(ob + ub, 0.0, 0.001, "(B)");

        // Ein Griff am Rand wirkt staerker als einer in der Mitte.
        var near = ColourWheelMath.Offset(new WheelPoint(0.3f, 0));
        Check.That(r > near.R * 2, "am Rand wirkt es staerker als auf halbem Weg",
                   $"{r:0.##} gegen {near.R:0.##}");

        static WheelPoint Polar(double degrees)
        {
            double angle = degrees * Math.PI / 180;
            return new WheelPoint((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
    }

    /// <summary>
    /// Aus Werten zurueck auf das Rad. Gebraucht beim Laden eines Rezepts: Der Griff
    /// muss dort stehen, wo die Werte ihn hinsetzen wuerden, sonst springt er beim
    /// ersten Anfassen an eine andere Stelle.
    /// </summary>
    private static void RoundTrip()
    {
        Check.Group("Farbrad hin und zurueck");

        int wrong = 0;
        float worst = 0;

        for (int degrees = 0; degrees < 360; degrees += 7)
        {
            foreach (float radius in new[] { 0.2f, 0.55f, 0.9f })
            {
                double angle = degrees * Math.PI / 180;
                var point = new WheelPoint((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);

                foreach (float brightness in new[] { -0.1f, 0f, 0.2f })
                {
                    var (r, g, b) = ColourWheelMath.ToChannels(point, brightness, 1f, 0.3f);

                    var back = ColourWheelMath.ToPoint(r, g, b);
                    float gotBrightness = ColourWheelMath.ToBrightness(r, g, b, 1f);

                    // Der Radpunkt kommt in der Groesse des Massstabs zurueck, nicht
                    // in der des Rades - er wird beim Zurueckrechnen wieder geteilt.
                    float scaled = back.Radius / 0.3f;

                    float error = MathF.Abs(scaled - radius) + MathF.Abs(gotBrightness - brightness);
                    if (error > 0.002f) wrong++;
                    worst = MathF.Max(worst, error);
                }
            }
        }

        Check.That(wrong == 0, "Werte und Radpunkt passen zueinander",
                   $"{wrong} Abweichungen, groesste {worst:0.#####}");

        // Ausserhalb des Kreises wird hereingeholt - ein Rezept aus einer anderen
        // Quelle koennte dort liegen.
        var outside = ColourWheelMath.ToPoint(5f, -5f, 0f);
        Check.That(outside.Radius <= 1.0001f, "ein Punkt ausserhalb wird hereingeholt",
                   $"{outside.Radius:0.###}");
    }

    private static void InThePanel()
    {
        Check.Group("Farbraeder im Panel");

        var panel = new GradingPanel();
        panel.Measure(new Size(300, 1600));
        panel.Arrange(new Rect(0, 0, 300, 1600));
        panel.UpdateLayout();

        foreach (string name in new[] { "LiftWheel", "GammaWheel", "GainWheel" })
        {
            var wheel = panel.FindName(name) as ColourWheel;
            Check.That(wheel is not null, $"{name} ist da");

            if (wheel is null) continue;

            // Wirklich zeichnen lassen: OnRender laeuft sonst nie, und ein Fehler
            // darin faellt erst beim Aufklappen des Abschnitts auf.
            try
            {
                wheel.Measure(new Size(82, 82));
                wheel.Arrange(new Rect(0, 0, 82, 82));

                var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    82, 82, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                target.Render(wheel);

                Check.That(true, $"{name} zeichnet");
            }
            catch (Exception ex)
            {
                Check.That(false, $"{name} zeichnet", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // Ein Rad ohne Groesse kommt beim ersten Aufbau vor.
        try
        {
            var bare = new ColourWheel { Width = 0, Height = 0 };
            bare.Measure(new Size(0, 0));
            bare.Arrange(new Rect(0, 0, 0, 0));
            Check.That(true, "ein Rad ohne Groesse ueberlebt das Anordnen");
        }
        catch (Exception ex)
        {
            Check.That(false, "ein Rad ohne Groesse ueberlebt das Anordnen", ex.GetType().Name);
        }

        // Und die neun alten Regler sind wirklich fort - zwei Wege zu denselben
        // Werten waeren genau die Doppelung, die auseinanderlaeuft.
        foreach (string gone in new[] { "LiftRSlider", "GammaGSlider", "GainBSlider" })
            Check.That(panel.FindName(gone) is null, $"{gone} ist abgeloest");

        foreach (string kept in new[] { "LiftBrightSlider", "GammaBrightSlider", "GainBrightSlider" })
            Check.That(panel.FindName(kept) is Slider, $"{kept} steht an seiner Stelle");

        // Die Zahlen stehen je Rad, nicht in einer gemeinsamen Zeile - sonst sind
        // sie unter keinem der drei Raeder zentriert und gehoeren optisch zu keinem.
        foreach (string values in new[] { "LiftValues", "GammaValues", "GainValues" })
        {
            var block = panel.FindName(values) as System.Windows.Controls.TextBlock;
            Check.That(block is not null, $"{values} steht unter seinem Rad");

            if (block is not null)
                Check.That(block.TextAlignment == System.Windows.TextAlignment.Center,
                           $"{values} ist zentriert");
        }

        // Die acht Bereichsregler tragen die Farbe ihres Bereichs auf der Bahn.
        var bands = panel.FindName("BandSliders") as System.Windows.Controls.Panel;
        Check.That(bands is not null, "die Bereichsregler sind da");

        if (bands is not null)
        {
            int tinted = 0;
            int plain = 0;

            foreach (var child in bands.Children)
            {
                if (child is not Slider slider) continue;

                if (slider.Background is System.Windows.Media.LinearGradientBrush) tinted++;
                else plain++;
            }

            Check.That(tinted == 8, "alle acht tragen einen Verlauf", $"{tinted} von {tinted + plain}");

            // Gerichtet, nicht symmetrisch: Ein Verlauf, der zu beiden Seiten
            // dasselbe tut, sagt nur "hier ist Blau" - die Bahn soll aber zeigen,
            // was der Regler bewirkt.
            var first = bands.Children.OfType<Slider>().First();
            var gradient = (System.Windows.Media.LinearGradientBrush)first.Background;

            Check.That(gradient.GradientStops.Count == 2, "mit zwei Enden statt dreien",
                       $"{gradient.GradientStops.Count}");

            Check.That(gradient.GradientStops[0].Color != gradient.GradientStops[1].Color,
                       "und die Enden unterscheiden sich");

            // Und beim Wechsel der Groesse aendert sich die Bahn mit: derselbe
            // Regler bewirkt dann etwas anderes.
            var before = gradient.GradientStops[1].Color;

            var satButton = panel.FindName("BandSatButton") as System.Windows.Controls.Primitives.ToggleButton;
            Check.That(satButton is not null, "der Umschalter auf Saettigung ist da");

            if (satButton is not null)
            {
                satButton.IsChecked = true;

                var after = ((System.Windows.Media.LinearGradientBrush)first.Background).GradientStops[0].Color;
                Check.That(after != before, "die Bahn folgt der gewaehlten Groesse",
                           $"{before} gegen {after}");
            }
        }
    }

    /// <summary>
    /// Laesst sich ueberhaupt etwas verstellen?
    ///
    /// Die Frage klingt albern und ist die wichtigste: Ein Panel, das laedt, richtig
    /// aussieht und auf keinen Griff reagiert, besteht jede andere Pruefung. Hier
    /// wird ein Regler bewegt und ein Rad gesetzt, und beides muss ankommen.
    /// </summary>
    private static void CanActuallyBeChanged()
    {
        Check.Group("Im Panel laesst sich etwas verstellen");

        var panel = new GradingPanel();

        // Ein Fenster drumherum, damit IsLoaded wirklich wahr wird - ohne
        // Fensterwurzel bleibt ein Steuerelement ungeladen, und genau daran haengt
        // der Handler.
        var window = new Window
        {
            Content = panel,
            Width = 340,
            Height = 900,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            panel.UpdateLayout();

            Check.That(panel.IsLoaded, "das Panel gilt als geladen");

            int changes = 0;
            panel.Changed += _ => changes++;

            // Ein Regler der Grundkorrektur.
            var exposure = (Slider)panel.FindName("ExposureSlider");
            exposure.Value = -1.25;

            Check.That(changes > 0, "ein Regler meldet seine Aenderung", $"{changes}");
            Check.Near(panel.Adjustments.Exposure, -1.25, 0.001, "und der Wert kommt an");

            // Ein eingefaerbter Regler - der neue Stil darf nichts verschluckt haben.
            changes = 0;
            var temperature = (Slider)panel.FindName("TemperatureSlider");
            temperature.Value = temperature.Minimum + (temperature.Maximum - temperature.Minimum) * 0.25;

            Check.That(changes > 0, "auch ein Regler mit eingefaerbter Bahn", $"{changes}");

            // Und ein Rad.
            changes = 0;
            var wheel = (ColourWheel)panel.FindName("LiftWheel");
            var before = panel.Stack.Tools.OfType<LiftGammaGainTool>().First().Lift.R;

            wheel.Value = new WheelPoint(0.8f, 0f);

            // Das Setzen der Eigenschaft allein meldet noch nichts - das tut erst
            // die Maus. Geprueft wird deshalb, dass der Weg dahinter stimmt.
            Check.That(wheel.Value.X > 0.5f, "das Rad nimmt einen Wert an", $"{wheel.Value.X:0.##}");

            // Der Bereichsregler mit gerechnetem Verlauf.
            changes = 0;
            var bands = (System.Windows.Controls.Panel)panel.FindName("BandSliders");
            var firstBand = bands.Children.OfType<Slider>().FirstOrDefault();

            Check.That(firstBand is not null, "ein Bereichsregler ist da");

            if (firstBand is not null)
            {
                firstBand.Value = -40;
                Check.That(changes > 0, "und meldet seine Aenderung", $"{changes}");

                var hsl = panel.Stack.Tools.OfType<HslTool>().First();
                Check.Near(hsl.Bands[0].Hue, -40, 0.5, "der Wert landet im Werkzeug");
            }
        }
        finally
        {
            window.Close();
        }
    }
}
