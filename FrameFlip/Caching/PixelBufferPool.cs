namespace FrameFlip.Caching;

/// <summary>
/// Pool wiederverwendbarer Pixelpuffer. Im eingeschwungenen Zustand rotieren immer
/// dieselben Arrays durch den Ringpuffer - dadurch waehrend der Wiedergabe keine
/// LOH-Allokation und keine GC-Pausen.
///
/// Vergeben wird nach KAPAZITAET, nicht nach Geometrie: Rent liefert ein Array mit
/// mindestens der geforderten Byteanzahl, moeglicherweise mehr. Die Bildbreite darf
/// deshalb niemals aus der Arraylaenge abgeleitet werden - der FrameBuffer fuehrt
/// Breite, Hoehe und Stride getrennt. Genau daran scheitern Vorschauen sonst mit
/// schwarzen Raendern: ein Stride aus der Poolgroesse statt aus der Bildbreite laesst
/// den Rest der Zeile uninitialisiert.
///
/// Die Kapazitaetsvergabe hat einen zweiten, praktischen Grund: beim Zoomen wechselt
/// die Dekodier-Aufloesung staendig. Mit exakter Groessenbindung wuerde der Pool bei
/// jedem Wechsel komplett verworfen und jeder Frame neu auf dem LOH allokiert.
/// </summary>
public sealed class PixelBufferPool
{
    /// <summary>
    /// Ein vorhandener Puffer wird wiederverwendet, solange er nicht mehr als die
    /// Haelfte zu gross ist. Ohne diese Obergrenze wuerde ein einzelner Puffer in
    /// voller Aufloesung den Pool dauerhaft aufblaehen, nachdem wieder herausgezoomt
    /// wurde - und das Speicherziel im Leerlauf waere hin.
    /// </summary>
    private const double MaxOversize = 1.5;

    private readonly object _gate = new();
    private readonly List<byte[]> _free = new();
    private int _payloadBytes;
    private int _maxRetained = 4;
    private int _live;

    /// <param name="payloadBytes">Nutzlast eines Frames, also Stride * Hoehe.</param>
    public void Configure(int payloadBytes, int maxRetained)
    {
        lock (_gate)
        {
            _payloadBytes = payloadBytes;
            _maxRetained = Math.Max(2, maxRetained);
            TrimLocked();
        }
    }

    /// <summary>Liefert ein Array mit <c>Length &gt;= minBytes</c>.</summary>
    public byte[] Rent(int minBytes)
    {
        if (minBytes <= 0) return Array.Empty<byte>();

        lock (_gate)
        {
            int limit = (int)Math.Min(int.MaxValue, minBytes * MaxOversize);

            // Den knappsten passenden Puffer nehmen, damit grosse Puffer fuer die
            // Faelle uebrig bleiben, die sie wirklich brauchen.
            int best = -1;
            for (int i = 0; i < _free.Count; i++)
            {
                int length = _free[i].Length;
                if (length < minBytes || length > limit) continue;
                if (best < 0 || length < _free[best].Length) best = i;
            }

            _live++;

            if (best >= 0)
            {
                var buffer = _free[best];
                _free.RemoveAt(best);
                return buffer;
            }
        }

        return new byte[minBytes];
    }

    public void Return(byte[] buffer)
    {
        lock (_gate)
        {
            _live--;
            if (buffer.Length <= 0 || _free.Count >= _maxRetained) return;

            // Puffer, die fuer die aktuelle Framegroesse zu gross oder zu klein sind,
            // gar nicht erst behalten - so schrumpft der Pool nach einem Zoom von
            // selbst wieder.
            if (_payloadBytes > 0 &&
                (buffer.Length < _payloadBytes || buffer.Length > _payloadBytes * MaxOversize))
                return;

            _free.Add(buffer);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _free.Clear();
            _payloadBytes = 0;
            _live = 0;
        }
    }

    public long RetainedBytes
    {
        get
        {
            lock (_gate)
            {
                long total = 0;
                foreach (var buffer in _free) total += buffer.Length;
                return total;
            }
        }
    }

    public int LiveBuffers { get { lock (_gate) return _live; } }

    private void TrimLocked()
    {
        if (_payloadBytes <= 0) return;

        for (int i = _free.Count - 1; i >= 0; i--)
        {
            int length = _free[i].Length;
            if (length < _payloadBytes || length > _payloadBytes * MaxOversize) _free.RemoveAt(i);
        }

        while (_free.Count > _maxRetained) _free.RemoveAt(_free.Count - 1);
    }
}
