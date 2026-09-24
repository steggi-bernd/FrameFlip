using System.Globalization;
using FrameFlip.Sequencing;

namespace FrameFlip.Dashboard;

internal enum DashboardTick { None, Show, Stop }

/// <summary>
/// Besitzt Kopf, Bereich, Abspielzustand, Follow und Bildrate des Dashboards.
/// Alles zaehlt in Framenummern, nicht in Positionen: Eine Luecke bleibt eine Luecke,
/// und ein gesetzter Bereich ueberdauert das Nachwachsen der Folge. Zeitgeber, Eingaben
/// und Darstellung bleiben beim Fenster; alle Aufrufe kommen vom UI-Thread.
///
/// Der <see cref="Playback.ViewerPlaybackController"/> passt hier nicht: Er rechnet mit
/// Positionen, verwirft einen gegenlaeufigen Bereich statt ihn zu begrenzen und kennt
/// weder Follow noch eine wachsende Folge.
/// </summary>
internal sealed class DashboardPlaybackController(Func<ImageSequence?> sequence)
{
    internal const double DefaultFps = 24;

    internal int Head { get; private set; }
    internal int InPoint { get; private set; }
    internal int OutPoint { get; private set; }
    internal bool IsPlaying { get; private set; }

    /// <summary>Ob der Kopf auf dem neuesten Bild bleiben soll.</summary>
    internal bool Follow { get; private set; } = true;

    internal double Fps { get; private set; } = DefaultFps;
    internal TimeSpan Interval => TimeSpan.FromSeconds(1.0 / Fps);

    /// <summary>Eine neue Auswahl gilt zunaechst ganz.</summary>
    internal void ResetRange(ImageSequence shown)
    {
        InPoint = shown.StartNumber;
        OutPoint = shown.EndNumber;
    }

    /// <summary>
    /// Den vorhandenen Frame, der der Nummer am naechsten liegt, zum Kopf machen.
    /// Liefert seine Position in der Folge oder -1, wenn es keine Bilder gibt.
    /// </summary>
    internal int Seek(int number)
    {
        if (sequence() is not { Count: > 0 } shown) return -1;
        int index = shown.IndexNearestNumber(number);
        if (index < 0) return -1;
        Head = shown.Frames[index].Number;
        return index;
    }

    /// <summary>Ein einzelnes Bild spielt nicht. Ein erneuter Start ist erlaubt.</summary>
    internal bool Play()
    {
        if (sequence() is not { Count: > 1 }) return false;
        IsPlaying = true;
        return true;
    }

    /// <returns>Ob eine laufende Wiedergabe angehalten wurde.</returns>
    internal bool Pause()
    {
        if (!IsPlaying) return false;
        IsPlaying = false;
        return true;
    }

    /// <summary>
    /// Was der naechste Takt zeigt. Er geht vom Kopf aus, auch wenn der vor dem Start
    /// steht. Hinter dem Ende beginnt die Schleife beim Start, oder die Wiedergabe endet.
    /// </summary>
    internal DashboardTick Advance(bool loop, out int number)
    {
        number = Head;
        if (sequence() is not { Count: > 0 } shown) return DashboardTick.None;

        int index = shown.IndexNearestNumber(Head) + 1;
        if (index >= shown.Count || shown.Frames[index].Number > OutPoint)
        {
            if (!loop) return DashboardTick.Stop;
            number = InPoint;
            return DashboardTick.Show;
        }

        number = shown.Frames[index].Number;
        return DashboardTick.Show;
    }

    /// <summary>Einzelschritte folgen den vorhandenen Frames und enden an der Folge, nicht am Bereich.</summary>
    internal int? StepTarget(int delta)
    {
        if (sequence() is not { Count: > 0 } shown) return null;
        int index = Math.Clamp(shown.IndexNearestNumber(Head) + delta, 0, shown.Count - 1);
        return shown.Frames[index].Number;
    }

    /// <summary>Der Start rueckt hoechstens bis ans Ende.</summary>
    internal void MarkIn() => InPoint = Math.Min(Head, OutPoint);

    /// <summary>Das Ende rueckt hoechstens bis an den Start.</summary>
    internal void MarkOut() => OutPoint = Math.Max(Head, InPoint);

    /// <summary>
    /// Die Folge wurde neu eingelesen. Ein Ende am alten Folgenende waechst mit, ein
    /// von Hand gesetztes bleibt stehen. Liefert den Frame, auf den Follow springt.
    /// </summary>
    internal int? Rescan(ImageSequence previous, ImageSequence fresh)
    {
        bool grewAtEnd = fresh.EndNumber > previous.EndNumber;
        if (OutPoint == previous.EndNumber) OutPoint = fresh.EndNumber;
        if (InPoint < fresh.StartNumber) InPoint = fresh.StartNumber;
        return Follow && grewAtEnd && !IsPlaying ? fresh.EndNumber : null;
    }

    /// <summary>Liefert den Frame, auf den eingeschaltetes Follow im Stillstand springt.</summary>
    internal int? SetFollow(bool on)
    {
        Follow = on;
        return on && !IsPlaying && sequence() is { Count: > 0 } shown ? shown.EndNumber : null;
    }

    /// <summary>Die gespeicherte Rate uebernehmen.</summary>
    internal void UseRate(double rate) => Fps = Clean(rate);

    /// <summary>
    /// Eine getippte oder gewaehlte Rate. Ein Komma zaehlt als Dezimaltrenner;
    /// Unlesbares laesst die geltende Rate stehen. Liefert die dann geltende Rate.
    /// </summary>
    internal double TakeRate(string? text)
    {
        if (double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            Fps = Clean(parsed);
        return Fps;
    }

    /// <summary>Dieselben Grenzen wie in den Einstellungen - eine Regel, ein Ort.</summary>
    internal static double Clean(double rate)
        => double.IsNaN(rate) || rate <= 0 ? DefaultFps : Math.Clamp(rate, 1, 240);
}
