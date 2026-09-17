using System.Windows;
using System.Windows.Controls;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Kommt an, was in der Rasterliste gewaehlt wurde?
///
/// Die Rechnung stimmt, und der Weg durch den Prozessor traegt alle neun Verfahren -
/// beides steht nebenan. Bleibt das Stueck dazwischen: die Liste im Bedienfeld. Sie
/// bedient ZWEI Werkzeuge, weil die ersten drei Eintraege Ortswerkzeuge sind und die
/// uebrigen sechs Durchgaenge ueber das ganze Bild, und genau dort kann ein Eintrag
/// ins Leere laufen, ohne dass irgendeine Rechnung falsch wird.
///
/// Gemeldet wurde "die meisten haben keinen Effekt". Das ist die Art Fehler, die
/// kein Rechentest findet.
/// </summary>
public static class DitherPanelInvariants
{
    public static void Run()
    {
        EveryEntryLandsInTheStack();
        RasterBelongsToThePicture();
    }

    /// <summary>
    /// Rastern gilt dem BILD und nicht einer Ebene - und der Abschnitt muss das auch
    /// zeigen.
    ///
    /// Genau das war der gemeldete Fehler: Der Abschnitt blieb bedienbar, waehrend
    /// eine Ebene gewaehlt war. Er schrieb dann in deren Stapel, und dort wird er nie
    /// gerechnet. Ein Regler, der sich bewegt, waehrend das Bild stehenbleibt, ist
    /// schlimmer als einer, der ausgegraut ist: Der eine sagt "geht nicht", der andere
    /// sagt gar nichts.
    /// </summary>
    private static void RasterBelongsToThePicture()
    {
        Check.Group("Rastern gehoert dem Bild, nicht der Ebene");

        var panel = new GradingPanel();

        var window = new Window
        {
            Content = panel,
            Width = 420,
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

            var dither = (FrameworkElement)panel.FindName("DitherBody");
            var grain = (FrameworkElement)panel.FindName("GrainBody");

            panel.Target = null;
            panel.UpdateLayout();

            Check.That(dither.IsEnabled, "beim ganzen Bild ist der Abschnitt bedienbar");

            panel.Target = "Figur";
            panel.UpdateLayout();

            Check.That(!dither.IsEnabled,
                       "bei einer gewaehlten Ebene nicht - dort wird er nie gerechnet");

            Check.That(dither.IsEnabled == grain.IsEnabled,
                       "und er verhaelt sich wie das Korn, das dieselbe Frage hat");
        }
        finally
        {
            window.Close();
        }
    }

    private static void EveryEntryLandsInTheStack()
    {
        Check.Group("Jeder Eintrag der Rasterliste landet im Stapel");

        var panel = new GradingPanel();

        var window = new Window
        {
            Content = panel,
            Width = 420,
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

            var box = (ComboBox)panel.FindName("DitherPatternBox");
            var strength = (Slider)panel.FindName("DitherSlider");

            Check.That(box is not null && strength is not null, "die Liste ist da");
            if (box is null || strength is null) return;

            Check.That(box.Items.Count == 9, "sie fuehrt neun Verfahren", $"{box.Items.Count}");

            // Ohne Staerke tut kein Verfahren etwas - das ist richtig so, aber hier
            // wuerde es jede Probe wertlos machen.
            strength.Value = 1.0;
            panel.UpdateLayout();

            for (int i = 0; i < box.Items.Count; i++)
            {
                box.SelectedIndex = i;
                panel.UpdateLayout();

                bool diffuse = i >= 3;

                var dither = panel.Stack.Optics.OfType<DitherTool>().FirstOrDefault();
                var diffusion = panel.Stack.Frame.OfType<DiffusionTool>().FirstOrDefault();

                if (dither is null || diffusion is null)
                {
                    Check.That(false, $"Eintrag {i}: beide Werkzeuge stehen im Stapel");
                    return;
                }

                // Genau eines darf rechnen.
                Check.That(diffuse ? diffusion.Amount > 0.5f : dither.Amount > 0.5f,
                           $"Eintrag {i} stellt das richtige Werkzeug an",
                           $"Raster {dither.Amount:0.00}, Diffusion {diffusion.Amount:0.00}");

                Check.That(diffuse ? dither.Amount < 0.005f : diffusion.Amount < 0.005f,
                           $"Eintrag {i} stellt das andere ab",
                           $"Raster {dither.Amount:0.00}, Diffusion {diffusion.Amount:0.00}");

                if (diffuse)
                {
                    Check.That((int)diffusion.Kernel == i - 3,
                               $"Eintrag {i} waehlt das richtige Streuschema",
                               $"{diffusion.Kernel}");
                }

                // Und der vorbereitete Stapel muss es weiterreichen - dort entscheidet
                // sich, ob der Prozessor es ueberhaupt zu sehen bekommt.
                var ready = panel.Prepared;

                Check.That(diffuse
                               ? ready.Frame.Length == 1
                               : ready.Optics.OfType<DitherTool>().Any(),
                           $"Eintrag {i} steht im vorbereiteten Stapel",
                           $"Frame {ready.Frame.Length}, Optik {ready.Optics.Length}");
            }
        }
        finally
        {
            window.Close();
        }
    }
}
