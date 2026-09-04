using System.IO;
using System.Windows.Threading;
using FrameFlip.Decoding;
using FrameFlip.Diagnostics;
using FrameFlip.Sequencing;

namespace FrameFlip.Caching;

public readonly record struct CacheStats(int CachedFrames, int Capacity, long BytesInUse, int AheadReady, int ActiveWorkers);

/// <summary>
/// Ringpuffer um die aktuelle Position. Die Zahl der Decoder-Threads, ihre Prioritaet,
/// die Fenstergroesse und das nutzbare Budget kommen aus dem ResourceProfile und
/// aendern sich zur Laufzeit mit der Systemlast.
///
/// Sperrdisziplin: unter _gate laufen ausschliesslich Dictionary-Operationen.
/// Dekodieren und Kopieren finden immer ausserhalb statt - sonst koennte der
/// UI-Thread auf einen Lowest-Priority-Thread warten (Prioritaetsinversion unter
/// Renderlast).
/// </summary>
public sealed class FrameCache : IDisposable
{
    private readonly ImageSequence _sequence;
    private readonly FrameDecoderRegistry _decoders;
    private readonly PixelBufferPool _pool = new();

    /// <summary>Zweite Stufe auf der Platte. Null, wenn abgeschaltet.</summary>
    private readonly RawFrameCache? _rawCache;
    private readonly Dictionary<int, FrameBuffer> _frames = new();
    private readonly Dictionary<int, long> _retryAfter = new();
    private readonly HashSet<int> _inFlight = new();
    private readonly object _gate = new();
    private readonly Thread[] _workers;
    private readonly AutoResetEvent[] _wakes;

    private readonly int _decodeWidth;
    private readonly int _decodeHeight;
    private readonly long _budgetBytes;
    private int _configuredAhead;
    private int _configuredBehind;

    private int _position;
    private int _direction = 1;
    private bool _loop = true;
    private int _urgent = -1;

    private int _ahead;
    private int _behind;
    private int _capacity = 2;
    private int _frameBytes;
    private double _windowScale = 1.0;
    private double _budgetScale = 1.0;

    private int _activeWorkers = 1;

    /// <summary>
    /// Untergrenze fuer die Threadzahl, unabhaengig vom Lastprofil.
    ///
    /// Das Profil kennt nur die Systemlast, nicht den eigenen Bedarf. Laeuft der Ring
    /// trocken, ist aber genau der Bedarf die entscheidende Groesse: es nuetzt nichts,
    /// hoeflich mit einem Thread zu arbeiten, waehrend die Wiedergabe stockt und
    /// zehn Kerne daneben leerlaufen.
    /// </summary>
    private int _minWorkers = 1;
    private volatile bool _shutdown;
    private bool _disposed;

    /// <summary>Wird auf einem Decoder-Thread ausgeloest.</summary>
    public event Action<int>? FrameReady;

    /// <param name="rawCache">
    /// Zweite Cachestufe auf der Platte, oder null. Der Ring uebernimmt sie nicht in
    /// sein Eigentum nicht - wer sie erzeugt, raeumt sie auch weg.
    /// </param>
    public FrameCache(ImageSequence sequence, FrameDecoderRegistry decoders,
                      int decodeWidth, int decodeHeight, long budgetBytes,
                      int ahead, int behind, bool loop,
                      int maxWorkers, ResourceProfile profile,
                      RawFrameCache? rawCache = null)
    {
        _rawCache = rawCache;
        _sequence = sequence;
        _decoders = decoders;
        _decodeWidth = Math.Max(1, decodeWidth);
        _decodeHeight = Math.Max(1, decodeHeight);

        // Keine Untergrenze ausser der physikalischen: das konfigurierte Budget gilt.
        // Zwei Frames bleiben immer im Speicher (aktueller plus naechster).
        _budgetBytes = Math.Max(1L, budgetBytes);

        _configuredAhead = Math.Max(1, ahead);
        _configuredBehind = Math.Max(0, behind);
        _ahead = _configuredAhead;
        _behind = _configuredBehind;
        _loop = loop;
        _windowScale = profile.WindowScale;
        _budgetScale = profile.BudgetScale;

        int workerCount = Math.Clamp(maxWorkers, 1, 8);
        _workers = new Thread[workerCount];
        _wakes = new AutoResetEvent[workerCount];
        _activeWorkers = Math.Clamp(profile.DecoderThreads, 1, workerCount);

        for (int i = 0; i < workerCount; i++)
        {
            _wakes[i] = new AutoResetEvent(false);
            _workers[i] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"FrameFlip.Decoder{i}",
                Priority = profile.ThreadPriority
            };
        }

        for (int i = 0; i < workerCount; i++) _workers[i].Start(i);
    }

    public int MaxWorkers => _workers.Length;

    public int DecodeWidth => _decodeWidth;

    public int DecodeHeight => _decodeHeight;

    // ---------------------------------------------------------------- Steuerung

    /// <summary>Verschiebt das Puffer-Fenster. direction kleiner 0 dreht die Vorausladerichtung um.</summary>
    public void SetPosition(int index, int direction, bool loop, bool urgent)
    {
        lock (_gate)
        {
            _position = index;
            _direction = direction >= 0 ? 1 : -1;
            _loop = loop;
            _urgent = urgent ? index : -1;
        }
        SignalAll();
    }

    /// <summary>Uebernimmt ein neues Lastprofil: Threadzahl, Prioritaet, Fenster und Budget.</summary>
    public void ApplyProfile(ResourceProfile profile)
    {
        if (_shutdown) return;

        lock (_gate)
        {
            _windowScale = profile.WindowScale;
            _budgetScale = profile.BudgetScale;
            RecomputeWindow();
            EvictOutsideWindow();
            TrimToCapacity();
        }

        Volatile.Write(ref _activeWorkers,
            Math.Clamp(Math.Max(profile.DecoderThreads, Volatile.Read(ref _minWorkers)), 1, _workers.Length));

        foreach (var worker in _workers)
        {
            try { worker.Priority = profile.ThreadPriority; }
            catch (Exception) { /* Thread schon beendet */ }
        }

        SignalAll();
    }

    /// <summary>
    /// Setzt Vor- und Ruecklauf neu, ohne den Ring zu verwerfen.
    ///
    /// Wird gebraucht, wenn sich die Bildrate aendert: der Vorlauf ist in Frames
    /// angegeben, bedeutet bei 60 fps aber nur die halbe Zeit wie bei 30. Ein
    /// Neuaufbau des Rings waere dafuer zu grob - er kostete ein Nachpuffern,
    /// obwohl die bereits dekodierten Frames voellig brauchbar bleiben.
    /// </summary>
    public void SetWindow(int ahead, int behind)
    {
        lock (_gate)
        {
            int newAhead = Math.Max(1, ahead);
            int newBehind = Math.Max(0, behind);

            if (newAhead == _configuredAhead && newBehind == _configuredBehind) return;

            _configuredAhead = newAhead;
            _configuredBehind = newBehind;

            RecomputeWindow();
            EvictOutsideWindow();
            TrimToCapacity();
        }

        SignalAll();
    }

    /// <summary>
    /// Hebt die Untergrenze fuer die Threadzahl an, weil der Ring leergelaufen ist.
    ///
    /// Wird vom Fenster gerufen, wenn die Wiedergabe wegen fehlender Frames aussetzen
    /// musste. Das Lastprofil braucht bis zu zehn Sekunden bis zur naechsten Messung -
    /// so lange darf die Wiedergabe nicht auf Besserung warten.
    /// </summary>
    public int RaiseWorkerFloor()
    {
        int floor = Math.Min(_workers.Length, Volatile.Read(ref _minWorkers) + 2);
        Volatile.Write(ref _minWorkers, floor);

        int active = Math.Max(Volatile.Read(ref _activeWorkers), floor);
        Volatile.Write(ref _activeWorkers, active);

        SignalAll();
        return active;
    }

    /// <summary>Nimmt die Anhebung zurueck - etwa beim Pausieren.</summary>
    public void ResetWorkerFloor() => Volatile.Write(ref _minWorkers, 1);

    public bool Contains(int index)
    {
        lock (_gate) return _frames.ContainsKey(index);
    }

    /// <summary>
    /// Uebergibt den Frame an den Aufrufer. Der Lookup laeuft unter dem Lock,
    /// das Kopieren ausdruecklich nicht.
    /// </summary>
    public bool TryPresent(int index, Action<FrameBuffer> present)
    {
        FrameBuffer? buffer;
        lock (_gate)
        {
            if (!_frames.TryGetValue(index, out buffer)) return false;
            buffer.AddRef();
        }

        try
        {
            present(buffer);
        }
        finally
        {
            if (buffer.Release()) _pool.Return(buffer.Pixels);
        }

        return true;
    }

    /// <summary>Der naechstbeste bereits dekodierte Frame vor dem gewuenschten.</summary>
    public int BestAvailableBefore(int desired, int lastShown, int lookback)
    {
        lock (_gate)
        {
            for (int k = 1; k <= lookback; k++)
            {
                int candidate = SequenceMath.Offset(desired, -k * _direction, _sequence.Count, _loop);
                if (candidate < 0 || candidate == lastShown) return -1;
                if (_frames.ContainsKey(candidate)) return candidate;
            }
        }
        return -1;
    }

    /// <summary>Wie viele Frames ab der aktuellen Position lueckenlos in Laufrichtung bereitliegen.</summary>
    public int ReadyAhead()
    {
        lock (_gate) return CountReadyAhead();
    }

    public CacheStats GetStats()
    {
        lock (_gate)
        {
            return new CacheStats(_frames.Count, _capacity, (long)_frames.Count * _frameBytes,
                                  CountReadyAhead(), Volatile.Read(ref _activeWorkers));
        }
    }

    private int CountReadyAhead()
    {
        // Liegt die ganze Sequenz im Ring, ist die Antwort ohne Suche klar. Der
        // Kurzschluss ist noetig, weil _ahead dann der Sequenzlaenge entspricht und
        // die Schleife bei jedem Bild ueber tausende Eintraege liefe.
        if (_frames.Count >= _sequence.Count) return Math.Min(_ahead, _sequence.Count - 1);

        int ready = 0;
        for (int k = 1; k <= _ahead; k++)
        {
            int i = SequenceMath.Offset(_position, k * _direction, _sequence.Count, _loop);
            if (i < 0 || !_frames.ContainsKey(i)) break;
            ready++;
        }
        return ready;
    }

    // ---------------------------------------------------------------- Worker

    private void WorkerLoop(object? state)
    {
        int index = (int)state!;
        var wake = _wakes[index];

        try
        {
            while (!_shutdown)
            {
                if (index >= Volatile.Read(ref _activeWorkers))
                {
                    // Ueberzaehliger Thread: parkt, bis das Lastprofil ihn wieder freigibt.
                    wake.WaitOne(250);
                    continue;
                }

                int target = SelectNextTarget();
                if (target < 0)
                {
                    // Obergrenze, damit fehlgeschlagene Frames (halb geschriebene Datei
                    // aus einem laufenden Render) spaeter erneut versucht werden.
                    wake.WaitOne(200);
                    continue;
                }

                FrameBuffer? decoded = Decode(target);

                bool stored = false;
                lock (_gate)
                {
                    _inFlight.Remove(target);

                    if (decoded is null)
                    {
                        _retryAfter[target] = Environment.TickCount64 + 2000;
                    }
                    else if (!_shutdown && !_frames.ContainsKey(target))
                    {
                        _frames[target] = decoded;
                        _retryAfter.Remove(target);
                        stored = true;
                    }
                }

                if (decoded is not null && !stored)
                {
                    if (decoded.Release()) _pool.Return(decoded.Pixels);
                }
                else if (stored)
                {
                    FrameReady?.Invoke(target);
                }
            }
        }
        catch (Exception)
        {
            // Ein Decoderfehler darf den Prozess nicht mitnehmen.
        }
        finally
        {
            // WPF-Imaging legt beim ersten BitmapImage einen Dispatcher fuer diesen
            // Thread an. Ohne Shutdown bliebe er samt Queue am Leben.
            Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
        }
    }

    private void SignalAll()
    {
        foreach (var wake in _wakes)
        {
            try { wake.Set(); }
            catch (ObjectDisposedException) { return; }
        }
    }

    /// <summary>Naechster zu dekodierender Index, oder -1 wenn nichts zu tun ist.</summary>
    private int SelectNextTarget()
    {
        lock (_gate)
        {
            if (_shutdown) return -1;

            EvictOutsideWindow();

            long now = Environment.TickCount64;

            foreach (int candidate in DesiredOrder())
            {
                if (_frames.ContainsKey(candidate)) continue;
                if (_inFlight.Contains(candidate)) continue;      // ein anderer Thread ist schon dran
                if (_retryAfter.TryGetValue(candidate, out long until) && now < until) continue;

                if (_frames.Count + _inFlight.Count >= _capacity && !MakeRoomFor(candidate)) return -1;

                _inFlight.Add(candidate);
                return candidate;
            }

            return -1;
        }
    }

    /// <summary>
    /// Ladereihenfolge: erst das dringende Sprungziel, dann die Position selbst,
    /// dann vollstaendig in Laufrichtung, erst danach entgegen der Laufrichtung.
    /// Bei aktivem Loop wrappt das Vorausladen ueber das Sequenzende hinaus -
    /// sonst gaebe es genau am Loop-Punkt den sichtbaren Ruckler.
    /// </summary>
    private IEnumerable<int> DesiredOrder()
    {
        int count = _sequence.Count;
        if (count == 0) yield break;

        if (_urgent >= 0 && _urgent < count) yield return _urgent;
        yield return _position;

        for (int k = 1; k <= _ahead; k++)
        {
            int i = SequenceMath.Offset(_position, k * _direction, count, _loop);
            if (i < 0) break;
            yield return i;
        }

        for (int k = 1; k <= _behind; k++)
        {
            int i = SequenceMath.Offset(_position, -k * _direction, count, _loop);
            if (i < 0) break;
            yield return i;
        }
    }

    /// <summary>
    /// Entfernung vom Fenstermittelpunkt in Laufrichtung. Frames entgegen der
    /// Laufrichtung liegen hinter allen Vorausframes, fliegen unter Budgetdruck also
    /// zuerst raus. int.MaxValue = ausserhalb des Fensters.
    /// </summary>
    private int Score(int index)
    {
        int count = _sequence.Count;
        if (index == _urgent) return -1;
        if (index == _position) return 0;

        int forward;
        int backward;

        if (_loop)
        {
            int raw = ((index - _position) % count + count) % count;
            forward = _direction > 0 ? raw : (count - raw) % count;
            backward = forward == 0 ? 0 : count - forward;
        }
        else
        {
            int relative = (index - _position) * _direction;
            forward = relative >= 0 ? relative : int.MaxValue;
            backward = relative < 0 ? -relative : int.MaxValue;
        }

        if (forward != int.MaxValue && forward <= _ahead) return forward;
        if (backward != int.MaxValue && backward <= _behind) return _ahead + backward;
        return int.MaxValue;
    }

    private void EvictOutsideWindow()
    {
        if (_frames.Count == 0) return;

        List<int>? drop = null;
        foreach (var entry in _frames)
        {
            if (Score(entry.Key) == int.MaxValue)
                (drop ??= new List<int>()).Add(entry.Key);
        }

        if (drop is null) return;
        foreach (int index in drop) Evict(index);
    }

    /// <summary>Nach einer Budgetkuerzung: auf die neue Kapazitaet herunterraeumen.</summary>
    private void TrimToCapacity()
    {
        while (_frames.Count > _capacity)
        {
            int worstIndex = -1;
            int worstScore = int.MinValue;

            foreach (var entry in _frames)
            {
                int score = Score(entry.Key);
                if (score > worstScore) { worstScore = score; worstIndex = entry.Key; }
            }

            if (worstIndex < 0) break;
            Evict(worstIndex);
        }
    }

    /// <summary>Wirft den entferntesten Frame raus, falls der Kandidat naeher liegt.</summary>
    private bool MakeRoomFor(int candidate)
    {
        int candidateScore = Score(candidate);
        int worstIndex = -1;
        int worstScore = int.MinValue;

        foreach (var entry in _frames)
        {
            int score = Score(entry.Key);
            if (score > worstScore) { worstScore = score; worstIndex = entry.Key; }
        }

        if (worstIndex < 0 || worstScore <= candidateScore) return false;

        Evict(worstIndex);
        return true;
    }

    private void Evict(int index)
    {
        if (!_frames.Remove(index, out var buffer)) return;
        if (buffer.Release()) _pool.Return(buffer.Pixels);
    }

    /// <summary>
    /// Beschafft einen Frame - in der Reihenfolge, in der es am wenigsten kostet:
    /// erst der Rohcache auf der Platte, dann das Entpacken der Quelldatei.
    ///
    /// Gemessen an 1080p-Material: 6 ms fuer den rohen Block gegen 31 ms fuer das
    /// PNG. Die Ersparnis traegt genau dort, wo der Ring nicht ausreicht - beim
    /// zweiten Durchlauf einer langen Sequenz.
    /// </summary>
    private FrameBuffer? Decode(int index)
    {
        if (index < 0 || index >= _sequence.Count) return null;

        var frame = _sequence.Frames[index];

        if (_rawCache is { } raw &&
            raw.TryRead(index, frame.Path, _pool.Rent, out var cached, out int w, out int h, out int s))
        {
            NoteFrameSize(s * h);
            return new FrameBuffer(cached, w, h, s, index);
        }

        var decoder = _decoders.For(Path.GetExtension(frame.Path));
        if (decoder is null) return null;

        try
        {
            if (!decoder.TryDecode(frame.Path, _decodeWidth, _decodeHeight, _pool.Rent, out var decoded))
                return null;

            NoteFrameSize(decoded.Stride * decoded.Height);

            // Ablegen, damit der naechste Durchlauf ihn billiger bekommt. Der
            // Schreibvorgang kostet rund 2,5 ms und laeuft auf demselben Thread -
            // das ist gegenueber den 31 ms des Entpackens vertretbar.
            _rawCache?.Write(index, frame.Path, decoded.Pixels,
                             decoded.Width, decoded.Height, decoded.Stride);

            return new FrameBuffer(decoded.Pixels, decoded.Width, decoded.Height, decoded.Stride, index);
        }
        catch (Exception)
        {
            // Halb geschriebene oder gesperrte Datei: spaeter erneut versuchen.
            return null;
        }
    }

    private void NoteFrameSize(int frameBytes)
    {
        if (frameBytes <= 0) return;

        lock (_gate)
        {
            if (frameBytes == _frameBytes) return;
            _frameBytes = frameBytes;
            RecomputeWindow();
        }
    }

    /// <summary>
    /// Aus Framegroesse, Budget und Lastprofil folgen Kapazitaet und Fenstergroesse.
    /// Reicht das Budget nicht fuer das gewuenschte Fenster, schrumpft das Fenster -
    /// es wird nie darueber hinaus allokiert.
    /// </summary>
    private void RecomputeWindow()
    {
        int desiredAhead = Math.Max(1, (int)Math.Round(_configuredAhead * _windowScale));
        int desiredBehind = Math.Max(0, (int)Math.Round(_configuredBehind * _windowScale));

        if (_frameBytes <= 0)
        {
            _ahead = desiredAhead;
            _behind = desiredBehind;
            return;
        }

        long budget = Math.Max(_frameBytes * 2L, (long)(_budgetBytes * _budgetScale));
        long fits = Math.Max(2, budget / _frameBytes);

        // Passt die ganze Sequenz ins Budget, wird sie vollstaendig gehalten.
        //
        // Sonst begrenzte das Fenster aus Vor- und Ruecklauf den Ring auch dann,
        // wenn im Budget noch reichlich Platz war: bei 2 GB und 8 MB je Frame passen
        // 240 Bilder hinein, gehalten wurden aber nur 151. Alles darueber hinaus fiel
        // heraus und musste bei jedem Loop-Durchlauf neu dekodiert werden - genau der
        // Fall, in dem einmal Laden fuer immer gereicht haette.
        if (fits >= _sequence.Count && _sequence.Count > 0)
        {
            _capacity = _sequence.Count;
            _ahead = Math.Max(1, _sequence.Count - 1);
            _behind = 0;                    // bei vollstaendigem Ring gibt es kein "zurueck"
            _pool.Configure(_frameBytes, _capacity + _workers.Length);
            return;
        }

        // Ist im Budget mehr Platz als das Fenster verlangt, wird er auch benutzt:
        // jeder zusaetzlich gehaltene Frame ist Reserve gegen Schwankungen, und der
        // Speicher ist ohnehin schon zugesagt. Ohne diesen Schritt blieb der Ring bei
        // 151 Frames stehen, obwohl 259 hineingepasst haetten.
        int room = (int)Math.Min(fits, _sequence.Count);
        if (room > desiredAhead + desiredBehind + 1)
            desiredAhead = Math.Max(desiredAhead, room - 1 - desiredBehind);

        _capacity = (int)Math.Min(fits, desiredAhead + desiredBehind + 1);

        int usable = _capacity - 1;
        if (usable < desiredAhead + desiredBehind)
        {
            double factor = usable / (double)(desiredAhead + desiredBehind);
            _ahead = Math.Max(1, (int)(desiredAhead * factor));
            _behind = Math.Max(0, usable - _ahead);
        }
        else
        {
            _ahead = desiredAhead;
            _behind = desiredBehind;
        }

        // Reserve fuer die Puffer, die gerade dekodiert werden.
        _pool.Configure(_frameBytes, _capacity + _workers.Length);
    }

    // ---------------------------------------------------------------- Abbau

    /// <summary>
    /// Gibt alle gepufferten Frames sofort frei, ohne auf die Decoder-Threads zu warten.
    /// Wird beim Wechsel der Dekodier-Aufloesung gebraucht: sonst haelten alter und neuer
    /// Ring gleichzeitig ihre Puffer und das RAM-Budget waere kurzzeitig doppelt belegt.
    /// </summary>
    public void ReleaseBuffers()
    {
        lock (_gate)
        {
            foreach (var entry in _frames)
                if (entry.Value.Release()) _pool.Return(entry.Value.Pixels);

            _frames.Clear();
            _retryAfter.Clear();
        }

        _pool.Clear();
    }

    /// <summary>Signalisiert das Ende, ohne zu blockieren.</summary>
    public void BeginShutdown()
    {
        _shutdown = true;
        SignalAll();
    }

    /// <summary>
    /// Wartet auf die Decoder-Threads und gibt alles frei. Nicht auf dem UI-Thread
    /// aufrufen - ein laufender Decode kann unter Renderlast dauern.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        BeginShutdown();
        foreach (var worker in _workers) worker.Join(TimeSpan.FromSeconds(5));

        lock (_gate)
        {
            foreach (var entry in _frames)
                if (entry.Value.Release()) _pool.Return(entry.Value.Pixels);

            _frames.Clear();
            _retryAfter.Clear();
            _inFlight.Clear();
        }

        _pool.Clear();
        foreach (var wake in _wakes) wake.Dispose();
        FrameReady = null;
    }
}
