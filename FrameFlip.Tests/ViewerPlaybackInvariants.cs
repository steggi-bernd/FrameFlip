using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Interop;
using FrameFlip.Playback;
using FrameFlip.Sequencing;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>Charakterisiert die Fensterbedienung vor der Controller-Extraktion, ohne Fenster anzuzeigen.</summary>
public static class ViewerPlaybackInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FrameFlip;component/Views/Theme.xaml", UriKind.Relative)
        });
        Check.Group("Viewer - Wiedergabe, Puffern und Navigation");
        var window = Create(10, 3);
        try
        {
            Check.That(Session(window).Index == 3, "die ausgewaehlte Listenposition wird uebernommen");
            Call(window, "EnterBuffering", true, null);
            Check.That(Session(window).IsBuffering && !Session(window).IsPlaying,
                       "vor dem Start wird gepuffert, ohne die Uhr zu starten");
            Call(window, "Pause");
            Check.That(!Session(window).IsBuffering && !Session(window).ResumeAfterBuffering,
                       "Pause beim Puffern nimmt den automatischen Neustart zurueck");
            Call(window, "Play");
            var clock = Session(window).Clock;
            Check.That(Session(window).IsPlaying && clock.IsRunning
                       && (string)((Button)window.FindName("PlayButton")).Content == "❙❙",
                       "Play startet Uhr und Anzeige gemeinsam");
            Call(window, "SeekTo", 7);
            Check.That(Session(window).Index == 7 && Session(window).Direction == 1,
                       "ein Sprung waehrend der Wiedergabe setzt Position und Richtung");
            Session(window).MarkPresented(5);
            Call(window, "Pause");
            Check.That(Session(window).Index == 5 && !clock.IsRunning,
                       "Pause bleibt am gezeigten Bild, nicht am vorausgelaufenen Ziel");
            Call(window, "Step", -1);
            Check.That(Session(window).Index == 4 && Session(window).Direction == -1,
                       "Rueckwaertsschritt setzt die Vorausladerichtung");
            Call(window, "SetInPoint");
            Call(window, "SeekTo", 6);
            Call(window, "SetOutPoint");
            Check.That(((int, int))Call(window, "ActiveRange")! == (4, 6), "In/Out begrenzt die Listenpositionen");
            Call(window, "Step", 1);
            Check.That(Session(window).Index == 4, "Loop springt innerhalb des Ausschnitts zurueck");
            ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("LoopButton")).IsChecked = false;
            Call(window, "Step", -1);
            Check.That(Session(window).Index == 4, "ohne Loop bleibt der Rueckwaertsschritt am In-Punkt");
            Call(window, "SeekTo", -1);
            Call(window, "SeekTo", 10);
            Check.That(Session(window).Index == 4, "ungueltige Spruenge veraendern die Position nicht");
            Call(window, "ClearInOut");
            Check.That(((int, int))Call(window, "ActiveRange")! == (0, 9), "Loeschen stellt die ganze Sequenz wieder her");
            Check.That((int)Call(window, "WarmupTarget")! == 9, "der Vorlauf ist durch die Sequenzlaenge begrenzt");
            Call(window, "Play");
            Call(window, "OnScrubStarted", window, new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
            Check.That(!Session(window).IsPlaying && !Session(window).IsBuffering,
                       "Ziehen am Regler pausiert die Wiedergabe");
            Call(window, "SeekTo", 6);
            Call(window, "OnScrubCompleted", window, new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false));
            Check.That(Session(window).IsBuffering && Session(window).ResumeAfterBuffering,
                       "Loslassen puffert vor dem Wiederanlaufen");
        }
        finally { Call(window, "Pause"); window.Close(); }
        Call(window, "Play");
        Call(window, "EnterBuffering", true, null);
        Check.That(!Session(window).IsPlaying && !Session(window).IsBuffering,
                   "nach dem Schliessen nimmt der Viewer keine Wiedergabe mehr an");

        var still = Create(1, 99);
        try
        {
            Call(still, "Play");
            Call(still, "EnterBuffering", true, null);
            Check.That(Session(still).Index == 0 && !Session(still).IsPlaying
                       && !Session(still).IsBuffering, "ein Einzelbild startet weder Uhr noch Pufferwartezeit");
        }
        finally { still.Close(); }
    }

    private static ViewerWindow Create(int count, int start)
    {
        var frames = Enumerable.Range(0, count)
            .Select(i => new SequenceFrame(100 + i * 2, "missing-frame.png", $"frame_{i}.png")).ToArray();
        return new ViewerWindow(new ImageSequence(new SequencePattern(".", "frame_", 4, "", ".png"), frames),
            start, new AppSettings(), _ => { }, FrameDecoderRegistry.CreateDefault(),
            new PixelRect(0, 0, 800, 600), 800, 600, 1);
    }

    private static ViewerPlaybackController Session(ViewerWindow window)
        => (ViewerPlaybackController)typeof(ViewerWindow).GetField("_playback", Hidden)!.GetValue(window)!;

    private static object? Call(ViewerWindow window, string name, params object?[] args)
        => typeof(ViewerWindow).GetMethod(name, Hidden)!.Invoke(window, args);
}
