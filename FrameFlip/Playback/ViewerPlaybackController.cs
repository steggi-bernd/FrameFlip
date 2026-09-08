using FrameFlip.Sequencing;

namespace FrameFlip.Playback;

/// <summary>
/// Wiedergabezustand einer Vorschausequenz. Alle Uebergaenge laufen auf dem UI-Thread;
/// nur IsPlaying wird auch vom Decoder gelesen. Fenster, Timer, Cache und Darstellung
/// bleiben beim Viewer. So lassen sich Navigation und Pufferregeln ohne WPF pruefen.
/// </summary>
internal sealed class ViewerPlaybackController
{
    private readonly Func<long> _tickCount;
    private volatile bool _playing;
    private long _bufferingSince;

    internal ViewerPlaybackController(int frameCount, int startIndex, bool loop, Func<long>? tickCount = null)
    {
        _tickCount = tickCount ?? (() => Environment.TickCount64);
        Loop = loop;
        ResetSequence(frameCount, startIndex);
    }

    internal PlaybackClock Clock { get; } = new();
    internal int FrameCount { get; private set; }
    internal int Index { get; private set; }
    internal int ShownIndex { get; private set; } = -1;
    internal int Direction { get; private set; } = 1;
    internal bool Loop { get; set; }
    internal bool IsPlaying => _playing;
    internal bool IsBuffering { get; private set; }
    internal bool ResumeAfterBuffering { get; private set; }
    internal int InPoint { get; private set; } = -1;
    internal int OutPoint { get; private set; } = -1;
    internal bool HasRange => InPoint >= 0 || OutPoint >= 0;

    internal void ResetSequence(int frameCount, int startIndex)
    {
        Pause();
        FrameCount = frameCount;
        Index = Math.Clamp(startIndex, 0, Math.Max(0, frameCount - 1));
        ShownIndex = -1;
        Direction = 1;
        InPoint = OutPoint = -1;
    }

    internal void MarkPresented(int index) => ShownIndex = index;

    internal (int First, int Last) ActiveRange()
    {
        int first = InPoint >= 0 ? InPoint : 0;
        int last = OutPoint >= 0 ? OutPoint : FrameCount - 1;
        return first <= last ? (first, last) : (last, first);
    }

    internal void SetInPoint()
    {
        InPoint = Index;
        if (OutPoint >= 0 && OutPoint < InPoint) OutPoint = -1;
    }

    internal void SetOutPoint()
    {
        OutPoint = Index;
        if (InPoint >= 0 && InPoint > OutPoint) InPoint = -1;
    }

    internal void ClearRange() => InPoint = OutPoint = -1;

    internal bool Step(int delta)
    {
        var (first, last) = ActiveRange();
        int next = SequenceMath.OffsetInRange(Index, delta, first, last, Loop);
        if (next < 0) return false;
        Index = next;
        Direction = delta >= 0 ? 1 : -1;
        return true;
    }

    internal bool Seek(int index)
    {
        if (index < 0 || index >= FrameCount || index == Index) return false;
        Direction = index >= Index ? 1 : -1;
        Index = index;
        if (IsPlaying) Clock.Seek(index);
        return true;
    }

    /// <summary>Uhrziel auf den aktiven Bereich abbilden; ein Fallbackbild bleibt davon unabhaengig.</summary>
    internal int ResolveTarget(long rawTarget, out bool pastEnd)
    {
        var (first, last) = ActiveRange();
        int target = SequenceMath.ResolveInRange(rawTarget, first, last, Loop, out pastEnd);
        if (pastEnd || (target >= 0 && target != ShownIndex)) Index = target;
        return target;
    }

    internal bool Play()
    {
        if (IsPlaying || FrameCount <= 1) return false;
        IsBuffering = false;
        _playing = true;
        Direction = 1;
        Clock.Start(Index);
        return true;
    }

    /// <returns>Ob eine laufende Wiedergabe angehalten wurde.</returns>
    internal bool Pause()
    {
        ResumeAfterBuffering = false;
        IsBuffering = false;
        if (!IsPlaying) return false;
        _playing = false;
        Clock.Stop();
        if (ShownIndex >= 0) Index = ShownIndex;
        return true;
    }

    internal bool EnterBuffering(bool resume)
    {
        if (FrameCount <= 1) return false;
        if (IsPlaying)
        {
            _playing = false;
            Clock.Stop();
            if (ShownIndex >= 0) Index = ShownIndex;
        }
        IsBuffering = true;
        ResumeAfterBuffering = resume;
        _bufferingSince = _tickCount();
        return true;
    }

    /// <summary>Vorlauf nie groesser waehlen, als Ausschnitt und Ring bereitstellen koennen.</summary>
    internal int WarmupTarget(int configured, int prefetchAhead, int? capacity)
    {
        int frames = configured > 0 ? configured : (int)Math.Ceiling(Clock.Fps * 1.5);
        var (first, last) = ActiveRange();
        int reachable = Math.Min(prefetchAhead, Math.Max(1, last - first));
        int fitsInRing = capacity - 1 ?? reachable;
        return Math.Clamp(frames, 2, Math.Max(2, Math.Min(reachable, fitsInRing)));
    }

    internal BufferCompletion CompleteBuffering(int readyAhead, int target, int cachedFrames)
    {
        var (first, last) = ActiveRange();
        bool wholeSequence = cachedFrames >= last - first + 1;
        bool timedOut = _tickCount() - _bufferingSince > 8000;
        if (readyAhead < target && !wholeSequence && !timedOut) return BufferCompletion.Waiting;
        IsBuffering = false;
        bool resume = ResumeAfterBuffering;
        ResumeAfterBuffering = false;
        return resume ? BufferCompletion.Resume : BufferCompletion.Paused;
    }

    internal void Stop()
    {
        _playing = false;
        Clock.Stop();
    }
}

internal enum BufferCompletion { Waiting, Paused, Resume }
