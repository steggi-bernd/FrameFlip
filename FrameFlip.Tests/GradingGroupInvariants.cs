using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Farbspalte als Reiter mit Kacheln.
///
/// Einundzwanzig aufklappbare Faecher untereinander waren ein Schacht: Wer die
/// Vignette suchte, rollte an siebzehn Dingen vorbei, die er nicht gesucht hatte -
/// und sah dabei von keinem einzigen, ob es gerade etwas tut. Ein zugeklapptes Fach
/// sieht aus wie ein zugeklapptes Fach, ob die Vignette nun steht oder nicht.
///
/// Geprueft werden deshalb genau die drei Zusagen, die Kacheln machen: Ein Reiter
/// zeigt seine und nur seine Werkzeuge. Ausgeschrieben steht immer GENAU EINES. Und
/// eine Kachel, deren Werkzeug etwas tut, sieht anders aus als eine, deren Werkzeug
/// nichts tut - das ist der eigentliche Gewinn, und er laesst sich messen.
/// </summary>
public static class GradingGroupInvariants
{
    public static void Run() => TilesShowWhatIsThereAndWhatIsOn();

    private static void TilesShowWhatIsThereAndWhatIsOn()
    {
        Check.Group("Die Farbspalte: Reiter und Kacheln");

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

            var tabs = (WrapPanel)panel.FindName("Tabs");
            var tiles = (WrapPanel)panel.FindName("Tiles");

            Check.That(tabs is not null && tiles is not null, "Reiterleiste und Kachelfeld sind da");
            if (tabs is null || tiles is null) return;

            Check.That(tabs.Children.Count == 5, "fuenf Reiter", $"{tabs.Children.Count}");

            // Der erste Reiter steht offen und zeigt seine Werkzeuge.
            //
            // Geprueft wird, WELCHE dort stehen, nicht wieviele: Die Zahl aendert
            // sich mit jedem neuen Werkzeug, und sie soll es auch. Eine feste Zahl
            // hier bricht die Probe beim naechsten Werkzeug, ohne dass etwas kaputt
            // waere - und das ist die Art Probe, die man irgendwann nur noch
            // nachzieht, statt sie zu lesen.
            Check.That(tiles.Children.Count > 0 &&
                       tiles.Children.OfType<ToggleButton>().Any(b => (string)b.Tag == "Curve"),
                       "die Grundkorrektur zeigt ihre Kacheln",
                       string.Join(", ", tiles.Children.OfType<ToggleButton>()
                                               .Select(b => (string)b.Tag)));

            // Genau ein Abschnitt ist ausgeschrieben.
            int open = 0;

            foreach (string name in new[]
                     {
                         "BasicBody", "CurveBody", "WhiteBalanceBody", "ZonesBody", "BandsBody",
                         "DehazeBody", "BloomBody", "VignetteBody", "DitherBody", "GrainBody",
                     })
            {
                if (panel.FindName(name) is FrameworkElement shown &&
                    shown.Visibility == Visibility.Visible)
                {
                    open++;
                }
            }

            Check.That(open == 1, "und genau ein Abschnitt steht ausgeschrieben", $"{open}");

            // Ein anderer Reiter zeigt andere Kacheln - und den ersten seiner eigenen.
            var optics = tabs.Children.OfType<ToggleButton>()
                             .First(b => (string)b.Tag == "S_GroupOptics");

            optics.IsChecked = true;
            panel.UpdateLayout();

            // Gezaehlt und nicht aufgezaehlt: Die Zahl aendert sich mit jedem neuen
            // Werkzeug, und sie soll es auch - die Aussage ist "der Reiter zeigt
            // ANDERE Kacheln als der erste", nicht "genau diese fuenf". Wer hier eine
            // feste Zahl hinterlegt, bricht die Probe beim naechsten Werkzeug, ohne
            // dass etwas kaputt waere.
            Check.That(tiles.Children.Count > 0 &&
                       tiles.Children.OfType<ToggleButton>().Any(b => (string)b.Tag == "Vignette") &&
                       tiles.Children.OfType<ToggleButton>().All(b => (string)b.Tag != "Curve"),
                       "die Optik zeigt ihre eigenen Kacheln und keine der ersten",
                       string.Join(", ", tiles.Children.OfType<ToggleButton>()
                                               .Select(b => (string)b.Tag)));

            var basic = (FrameworkElement)panel.FindName("BasicBody");

            Check.That(basic.Visibility != Visibility.Visible,
                       "und die Grundkorrektur ist damit nicht mehr ausgeschrieben");

            // Eine Kachel waehlen schreibt ihren Abschnitt aus - und nur ihren.
            var vignette = tiles.Children.OfType<ToggleButton>()
                                .First(b => (string)b.Tag == "Vignette");

            vignette.IsChecked = true;
            panel.UpdateLayout();

            var body = (FrameworkElement)panel.FindName("VignetteBody");
            var motion = (FrameworkElement)panel.FindName("MotionBody");

            Check.That(body.Visibility == Visibility.Visible, "die gewaehlte Kachel schreibt aus");
            Check.That(motion.Visibility != Visibility.Visible, "und die daneben nicht mehr");

            // Und der Gewinn, um den es ging: Eine Kachel, deren Werkzeug etwas tut,
            // sieht anders aus als eine, deren Werkzeug nichts tut.
            double quiet = vignette.Opacity;

            panel.Stack.Optics.OfType<VignetteTool>().First().Amount = 0.5f;

            panel.Load(panel.Adjustments, panel.Stack);
            panel.UpdateLayout();

            var again = ((WrapPanel)panel.FindName("Tiles")).Children.OfType<ToggleButton>()
                        .First(b => (string)b.Tag == "Vignette");

            Check.That(again.Opacity > quiet,
                       "eine Kachel, deren Werkzeug etwas tut, tritt hervor",
                       $"{again.Opacity:0.00} gegen {quiet:0.00}");
        }
        finally
        {
            window.Close();
        }
    }
}
