using System.Windows;
using System.Windows.Controls.Primitives;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Farbspalte in Gruppen.
///
/// Einundzwanzig Abschnitte in einer Liste sind keine Liste mehr, sondern ein
/// Schacht: Wer die Vignette sucht, rollt an siebzehn Dingen vorbei, die er nicht
/// gesucht hat. Fuenf Gruppen bilden den Rechenweg ab, und das ist dieselbe
/// Reihenfolge, in der auch gerechnet wird.
///
/// Geprueft wird die eine Eigenschaft, an der so etwas scheitert: Eine Gruppe muss
/// den Stand ihrer Abschnitte ZURUECKGEBEN, wenn sie wieder aufgeht. Wer eine Gruppe
/// zumacht und wieder auf, will den Stand von vorher und nicht einen Streifen aus
/// zwanzig offenen Abschnitten.
/// </summary>
public static class GradingGroupInvariants
{
    public static void Run() => AGroupHidesAndRemembers();

    private static void AGroupHidesAndRemembers()
    {
        Check.Group("Die Farbspalte klappt in Gruppen");

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

            foreach (string name in new[]
                     { "GroupBasic", "GroupDehaze", "GroupMotion", "GroupDither", "GroupLut" })
            {
                Check.That(panel.FindName(name) is ToggleButton, $"{name} ist da");
            }

            var group = (ToggleButton)panel.FindName("GroupMotion");
            var head = (FrameworkElement)panel.FindName("VignetteHead");
            var body = (FrameworkElement)panel.FindName("VignetteBody");

            if (group is null || head is null || body is null)
            {
                Check.That(false, "die Optikgruppe kennt die Vignette");
                return;
            }

            // Der Benutzer klappt einen Abschnitt auf.
            body.Visibility = Visibility.Visible;

            group.IsChecked = false;
            panel.UpdateLayout();

            Check.That(head.Visibility != Visibility.Visible,
                       "zugeklappt ist die Ueberschrift des Abschnitts weg");

            Check.That(body.Visibility != Visibility.Visible, "und sein Inhalt auch");

            group.IsChecked = true;
            panel.UpdateLayout();

            Check.That(head.Visibility == Visibility.Visible, "aufgeklappt steht sie wieder da");

            Check.That(body.Visibility == Visibility.Visible,
                       "und der Abschnitt ist so offen wie vorher");

            // Und die Gegenprobe: Ein Abschnitt, der zu war, bleibt zu.
            var closed = (FrameworkElement)panel.FindName("DistortionBody");

            closed.Visibility = Visibility.Collapsed;

            group.IsChecked = false;
            panel.UpdateLayout();
            group.IsChecked = true;
            panel.UpdateLayout();

            Check.That(closed.Visibility != Visibility.Visible,
                       "ein zugeklappter Abschnitt geht davon nicht auf");

            // Eine andere Gruppe darf davon nichts mitbekommen.
            var other = (FrameworkElement)panel.FindName("GrainHead");

            Check.That(other.Visibility == Visibility.Visible,
                       "und eine andere Gruppe bleibt unberuehrt");
        }
        finally
        {
            window.Close();
        }
    }
}
