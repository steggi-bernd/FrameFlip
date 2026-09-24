using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Dashboard;
using FrameFlip.Localization;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Wiedergabe am echten, unsichtbaren Dashboard mit eigenen Testbildern. Der Takt wird
/// nicht abgewartet: Die Pruefungen rufen denselben Schritt auf, den der Zeitgeber ausloest.
/// </summary>
public static class DashboardPlaybackInvariants
{
    public static void Run()
    {
        Transport();
        Rates();
        PreloadAndSelection();
    }

    private static void Transport()
    {
        Check.Group("Dashboard-Wiedergabe - Abspielen, Luecken, Schleife und Bereich");
        using var h = new DashboardFrameInvariants.Harness();
        File.Copy(h.PathFor(1), h.PathFor(5));
        File.Copy(h.PathFor(1), h.PathFor(6));
        h.Open(settings: new AppSettings { Prebuffer = false, PrepareVideo = false, MemoryBudgetMb = 128, Fps = 12 });
        h.Until(() => h.Image.Source is not null);
        var timer = (DispatcherTimer)h.Read("_player")!;
        Check.That(Head(h) == 1 && In(h) == 1 && Out(h) == 6 && !Playing(h) && h.Text("StageZoom") == "1 / 5",
            "eine neue Auswahl beginnt am ersten Frame mit der ganzen Folge als Bereich");

        Toggle(h);
        Check.That(Playing(h) && timer.IsEnabled && timer.Interval == TimeSpan.FromSeconds(1.0 / 12)
                   && Glyph(h) == Strings.T("D_Pause"),
            "ohne Vorladen startet Abspielen sofort mit der eingestellten Bildrate");
        h.Call("Advance");
        h.Call("Advance");
        h.Call("Advance");
        Check.That(Head(h) == 5 && h.Text("StageZoom") == "4 / 5",
            "der Takt springt ueber eine Luecke zum naechsten vorhandenen Frame");
        h.Call("Advance");
        h.Call("Advance");
        Check.That(Head(h) == 1 && Playing(h) && timer.IsEnabled,
            "mit Schleife beginnt die Wiedergabe am Bereichsende wieder beim Startpunkt");

        Loop(h).IsChecked = false;
        for (int i = 0; i < 4; i++) h.Call("Advance");
        Check.That(Head(h) == 6 && Playing(h), "bis zum Bereichsende laeuft die Wiedergabe auch ohne Schleife");
        h.Call("Advance");
        Check.That(Head(h) == 6 && !Playing(h) && !timer.IsEnabled && Glyph(h) == Strings.T("D_Play"),
            "ohne Schleife haelt die Wiedergabe am Bereichsende an und bleibt dort stehen");
        Toggle(h);
        h.Call("Advance");
        Check.That(Head(h) == 6 && !Playing(h) && !timer.IsEnabled,
            "ohne Schleife haelt ein Start am Bereichsende beim ersten Takt wieder an");

        h.Call("ShowFrame", 2);
        Chip(h, 1).IsChecked = true;
        h.Call("ShowFrame", 5);
        Chip(h, 2).IsChecked = true;
        Check.That(In(h) == 2 && Out(h) == 5 && Chip(h, 1).IsChecked == false && Chip(h, 2).IsChecked == false
                   && h.Text("RangeInfo") == Strings.T("D_InOut", "0002", "0005", 3),
            "Start und Ende setzen den Kopf als Bereichsgrenze und zaehlen die vorhandenen Bilder");
        h.Call("ShowFrame", 6);
        Chip(h, 1).IsChecked = true;
        Check.That(In(h) == 5 && Out(h) == 5, "ein Start hinter dem Ende wird auf das Ende begrenzt");
        h.Call("ShowFrame", 1);
        Chip(h, 2).IsChecked = true;
        Check.That(In(h) == 5 && Out(h) == 5, "ein Ende vor dem Start wird auf den Start begrenzt");
        h.Call("ShowFrame", 2);
        Chip(h, 1).IsChecked = true;
        h.Call("ShowFrame", 3);
        Chip(h, 2).IsChecked = true;

        Loop(h).IsChecked = true;
        Toggle(h);
        h.Call("Advance");
        Check.That(Head(h) == 2 && Playing(h), "der Takt nach dem gesetzten Ende kehrt zum gesetzten Start zurueck");
        h.Call("Pause");
        h.Call("ShowFrame", 1);
        Toggle(h);
        h.Call("Advance");
        Check.That(Head(h) == 2 && Playing(h), "ein Kopf vor dem Bereich laeuft von seiner Stelle aus in den Bereich");

        h.Call("Step", 1);
        Check.That(Head(h) == 3 && !Playing(h) && !timer.IsEnabled && Glyph(h) == Strings.T("D_Play"),
            "ein Einzelschritt haelt die Wiedergabe an");
        h.Call("Step", 1);
        Check.That(Head(h) == 5, "Einzelschritte folgen den vorhandenen Frames und ueberschreiten das gesetzte Ende");
        h.Call("Step", 1);
        h.Call("Step", 1);
        Check.That(Head(h) == 6, "Einzelschritte bleiben am letzten Frame der Folge stehen");
        for (int i = 0; i < 6; i++) h.Call("Step", -1);
        Check.That(Head(h) == 1, "Einzelschritte zurueck bleiben am ersten Frame der Folge stehen");

        Toggle(h);
        h.Call("OnJumpOut", h.Window, new RoutedEventArgs());
        Check.That(Head(h) == 3 && !Playing(h), "der Sprung ans Ende haelt an und zeigt das gesetzte Ende");
        h.Call("OnJumpIn", h.Window, new RoutedEventArgs());
        Check.That(Head(h) == 2 && !Playing(h), "der Sprung zum Start zeigt den gesetzten Start");
    }

    private static void Rates()
    {
        Check.Group("Dashboard-Wiedergabe - Bildrate");
        using var h = new DashboardFrameInvariants.Harness();
        var settings = new AppSettings { Prebuffer = false, PrepareVideo = false, MemoryBudgetMb = 128, Fps = 12 };
        var persisted = new List<double>();
        h.Open(settings: settings, persist: s => persisted.Add(s.Fps));
        h.Until(() => h.Image.Source is not null);
        var timer = (DispatcherTimer)h.Read("_player")!;
        var box = (ComboBox)h.Window.FindName("RateBox");
        Check.That(Fps(h) == 12 && box.Text == "12" && persisted.Count == 0,
            "die gespeicherte Bildrate gilt ab dem Aufbau, ohne erneut gespeichert zu werden");

        h.Call("TakeRate", "30");
        Check.That(Fps(h) == 30 && box.Text == "30" && settings.Fps == 30 && persisted.SequenceEqual(new[] { 30d })
                   && h.Text("StatusFps") == "30 fps",
            "eine gewaehlte Rate gilt sofort, wird gespeichert und in der Statuszeile gezeigt");
        Toggle(h);
        Check.That(timer.Interval == TimeSpan.FromSeconds(1.0 / 30), "Abspielen verwendet die zuletzt gewaehlte Rate");
        h.Call("TakeRate", "23,976");
        Check.That(Fps(h) == 23.976 && box.Text == "23.976" && timer.IsEnabled
                   && timer.Interval == TimeSpan.FromSeconds(1.0 / 23.976),
            "ein Komma wird als Dezimaltrenner gelesen und aendert den laufenden Takt");
        int saved = persisted.Count;
        h.Call("TakeRate", "abc");
        Check.That(Fps(h) == 23.976 && box.Text == "23.976" && persisted.Count == saved,
            "Unlesbares setzt das Feld auf die geltende Rate zurueck und speichert nichts");
        h.Call("TakeRate", "0");
        Check.That(Fps(h) == 24 && box.Text == "24", "null oder negative Raten fallen auf 24 zurueck");
        h.Call("TakeRate", "500");
        Check.That(Fps(h) == 240, "Raten ueber 240 werden auf 240 begrenzt");
        h.Call("TakeRate", "0.5");
        Check.That(Fps(h) == 1 && box.Text == "1" && timer.Interval == TimeSpan.FromSeconds(1),
            "Raten unter 1 werden auf 1 begrenzt");
        saved = persisted.Count;
        h.Call("TakeRate", "1");
        Check.That(persisted.Count == saved, "eine unveraenderte Rate wird nicht erneut gespeichert");
        h.Call("Pause");
    }

    private static void PreloadAndSelection()
    {
        Check.Group("Dashboard-Wiedergabe - Vorladen, Follow und Auswahlwechsel");
        using (var h = new DashboardFrameInvariants.Harness())
        {
            h.Open();
            h.Until(() => h.Image.Source is not null);
            var timer = (DispatcherTimer)h.Read("_player")!;
            h.Call("ShowFrame", 2);
            Chip(h, 1).IsChecked = true;
            h.Call("ShowFrame", 3);
            Toggle(h);
            var loader = h.Loaders.Single();
            Check.That(!Playing(h) && !timer.IsEnabled && Glyph(h) == Strings.T("D_Pause"),
                "mit Vorladen zeigt der Knopf sofort Pause, die Wiedergabe wartet aber auf den Lader");
            loader.Frames = new BitmapSource?[] { h.Picture, h.Picture, h.Picture };
            loader.Complete(true);
            h.Until(() => Playing(h));
            Check.That(Head(h) == 2 && timer.IsEnabled && Glyph(h) == Strings.T("D_Pause"),
                "nach dem Vorladen beginnt die Wiedergabe beim gesetzten Start");
            h.Call("Pause");
            Toggle(h);
            Check.That(Playing(h) && h.Loaders.Count == 1 && Head(h) == 2,
                "ein vollstaendig geladener Bereich startet ohne neues Laden an der aktuellen Stelle");
            h.Call("Pause");

            var follow = (ToggleButton)h.Window.FindName("FollowToggle");
            h.Call("ShowFrame", 1);
            follow.IsChecked = false;
            Check.That(Head(h) == 1 && !h.Playback.Follow, "Follow ausschalten laesst den Kopf stehen");
            follow.IsChecked = true;
            Check.That(Head(h) == 3 && h.Playback.Follow, "Follow einschalten springt im Stillstand ans Ende");
            h.Call("ShowFrame", 1);
            Toggle(h);
            follow.IsChecked = false;
            follow.IsChecked = true;
            Check.That(Head(h) == 1 && Playing(h), "waehrend der Wiedergabe springt eingeschaltetes Follow nicht");

            string other = Directory.CreateDirectory(Path.Combine(h.Root, "other")).FullName;
            File.Copy(h.PathFor(1), Path.Combine(other, "shot_0011.png"));
            File.Copy(h.PathFor(1), Path.Combine(other, "shot_0012.png"));
            Check.That((bool)h.Call("OpenPath", Path.Combine(other, "shot_0011.png"))!, "eine zweite Testfolge wird geoeffnet");
            Check.That(!Playing(h) && !timer.IsEnabled && Glyph(h) == Strings.T("D_Play")
                       && Head(h) == 11 && In(h) == 11 && Out(h) == 12,
                "ein Auswahlwechsel haelt an und setzt Kopf und Bereich auf die neue Folge");
        }

        using (var h = new DashboardFrameInvariants.Harness())
        {
            File.Delete(h.PathFor(2));
            File.Delete(h.PathFor(3));
            h.Open();
            h.Until(() => h.Image.Source is not null);
            var timer = (DispatcherTimer)h.Read("_player")!;
            Toggle(h);
            Check.That(!Playing(h) && !timer.IsEnabled && h.Loaders.Count == 0 && Glyph(h) != Strings.T("D_Pause"),
                "ein einzelnes Bild wird weder vorgeladen noch abgespielt, und der Knopf wechselt nicht auf Pause");
        }
    }

    private static void Toggle(DashboardFrameInvariants.Harness h) => h.Call("OnTogglePlay", h.Window, new RoutedEventArgs());
    private static int Head(DashboardFrameInvariants.Harness h) => h.Playback.Head;
    private static int In(DashboardFrameInvariants.Harness h) => h.Playback.InPoint;
    private static int Out(DashboardFrameInvariants.Harness h) => h.Playback.OutPoint;
    private static bool Playing(DashboardFrameInvariants.Harness h) => h.Playback.IsPlaying;
    private static double Fps(DashboardFrameInvariants.Harness h) => h.Playback.Fps;
    private static string? Glyph(DashboardFrameInvariants.Harness h) => ((Button)h.Window.FindName("PlayButton")).ToolTip as string;
    private static ToggleButton Loop(DashboardFrameInvariants.Harness h) => (ToggleButton)h.Read("_loopChip")!;
    private static ToggleButton Chip(DashboardFrameInvariants.Harness h, int index)
        => (ToggleButton)((Panel)h.Window.FindName("PlayChips")).Children[index];

    internal static DashboardPlaybackController Playback(MainWindow window)
        => (DashboardPlaybackController)DashboardSelectionInvariants.Read(window, "_playback")!;

    /// <summary>
    /// Nur den Zustand setzen, ohne Zeitgeber, Anzeige oder Begrenzung - so wie die
    /// Pruefungen frueher die Felder des Fensters gesetzt haben.
    /// </summary>
    internal static void Poke(MainWindow window, string property, object value)
        => typeof(DashboardPlaybackController).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Playback(window), value);
}
