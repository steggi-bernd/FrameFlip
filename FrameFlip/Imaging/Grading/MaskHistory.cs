using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Ein Stand im Verlauf einer gemalten Maske.</summary>
public sealed class MaskState
{
    /// <summary>Laufend ueber die ganze Geschichte der Maske - ein weggefallener Stand nimmt seine Nummer mit.</summary>
    public int Number { get; set; }

    public DateTime SavedUtc { get; set; }

    /// <summary>Welcher Anteil der Flaeche sich seit dem Stand davor geaendert hat, 0 bis 1.</summary>
    public float Changed { get; set; }

    /// <summary>Die ganze Deckung, gepackt wie <see cref="PaintedMask.Data"/> - oder null, dann stehen hier nur Striche.</summary>
    public string? Snapshot { get; set; }

    /// <summary>Die Striche seit dem Stand davor - nur bei einem Stand ohne Schnappschuss.</summary>
    public List<PaintStroke>? Strokes { get; set; }
}

/// <summary>
/// Der Verlauf einer gemalten Maske (docs/Projekte-und-Masken.md, Abschnitt 3.5): Stände,
/// zu denen man zurueckkehren kann, unabhaengig vom Strg+Z der Seite.
///
/// Ein neuer Stand entsteht, sobald sich seit dem letzten ein Zehntel der Flaeche
/// geaendert hat. Gezaehlt werden Maskenpunkte, nicht Striche: Zehnmal ueber dieselbe
/// Stelle zaehlt einmal. So haelt der Verlauf fest, was sich lohnt, und nicht jeden
/// Tupfer.
///
/// Gespeichert wird hybrid. Jeder fuenfte Stand ist ein Schnappschuss, dazwischen stehen
/// nur die Striche; ein Stand wird aus dem Schnappschuss davor und den nachgespielten
/// Strichen gewonnen. Das traegt, weil ein Strich nachgespielt genau dasselbe Raster
/// ergibt (<see cref="PaintStroke.Replay"/>). Hat sich die Maske anders als durch einen
/// Strich geaendert - Strg+Z, Wiederherstellen, ein Strich im Stapel -, wird der naechste
/// Stand ein Schnappschuss: Nachspielen wuerde dort etwas anderes ergeben.
/// </summary>
public sealed class MaskHistory
{
    /// <summary>So viele Staende hoechstens - zwischen den zehn und zwanzig, die geplant waren, das Obere.</summary>
    public const int Depth = 20;

    /// <summary>Ab diesem Anteil geaenderter Flaeche ein neuer Stand.</summary>
    public const float Threshold = 0.10f;

    /// <summary>Jeder wievielte Stand ein Schnappschuss ist.</summary>
    public const int SnapshotEvery = 5;

    /// <summary>Groesse der Maske in Maskenpunkten - ein Verlauf gehoert zu genau einer Groesse.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    public int NextNumber { get; set; } = 1;

    public List<MaskState> States { get; set; } = new();

    /// <summary>Die Deckung beim letzten Stand - geaendert heisst: anders als hier.</summary>
    private byte[]? _reference;

    /// <summary>Die Deckung nach dem letzten erfassten Strich - von hier aus wird der naechste nachgespielt.</summary>
    private byte[]? _last;

    /// <summary>Welche Maskenpunkte sich seit dem letzten Stand geaendert haben, und wie viele.</summary>
    private bool[]? _changed;

    private int _count;

    /// <summary>Die Striche seit dem letzten Stand.</summary>
    private readonly List<PaintStroke> _pending = new();

    /// <summary>Ob sich die Maske seit dem letzten Stand auch anders als durch einen Strich geaendert hat.</summary>
    private bool _foreign;

    /// <summary>Der Anteil der Flaeche, der sich seit dem letzten Stand geaendert hat.</summary>
    [JsonIgnore]
    public float Pending => Width * Height == 0 ? 0 : (float)_count / (Width * Height);

    /// <summary>Beginnt einen Verlauf: der erste Stand ist die Maske, wie sie jetzt ist.</summary>
    public static MaskHistory Start(PaintedMask paint)
    {
        var history = new MaskHistory { Width = paint.Width, Height = paint.Height };

        paint.Keep();
        history.States.Add(new MaskState { Number = history.NextNumber++, SavedUtc = DateTime.UtcNow, Snapshot = paint.Data });
        history.Follow(paint);

        return history;
    }

    /// <summary>Ob dieser Verlauf zu einer Maske dieser Groesse gehoert.</summary>
    public bool Fits(PaintedMask paint) => paint.Width == Width && paint.Height == Height && States.Count > 0;

    /// <summary>
    /// Beginnt zu beobachten, falls das noch nicht geschieht - bei einem gelesenen Verlauf
    /// oder vor dem ersten Strich. Was die Maske vom letzten Stand unterscheidet, zaehlt
    /// schon, als fremde Aenderung: Das Nachspielen kennt es nicht.
    /// </summary>
    public void Follow(PaintedMask paint)
    {
        if (_last is not null) return;

        var cover = paint.Cover();

        _reference = CoverOf(States.Count - 1);
        _last = cover.ToArray();
        _changed = new bool[cover.Length];
        _count = 0;

        if (!cover.AsSpan().SequenceEqual(_reference))
        {
            _foreign = true;
            Mark(cover, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// Ein Strich ist zu Ende - die Maske traegt ihn schon. Ergibt er nachgespielt dieselbe
    /// Maske, wird er aufgezeichnet; sonst hat sich noch etwas anderes geaendert. True, wenn
    /// damit ein neuer Stand entstanden ist.
    /// </summary>
    public bool Record(PaintedMask paint, PaintStroke? stroke)
    {
        Follow(paint);

        var cover = paint.Cover();
        var again = FromCover(_last!);
        var touched = stroke?.Replay(again) ?? PaintBounds.Empty;

        if (stroke is not null && again.Cover().AsSpan().SequenceEqual(cover))
        {
            _pending.Add(stroke);

            if (!touched.IsEmpty)
            {
                Mark(cover,
                     (int)MathF.Floor(touched.X0 / PaintedMask.Coarse) - 1, (int)MathF.Floor(touched.Y0 / PaintedMask.Coarse) - 1,
                     (int)MathF.Ceiling(touched.X1 / PaintedMask.Coarse) + 1, (int)MathF.Ceiling(touched.Y1 / PaintedMask.Coarse) + 1);
            }
        }
        else
        {
            _foreign = true;
            Mark(cover, 0, 0, Width - 1, Height - 1);
        }

        _last = cover.ToArray();

        if (_count < Threshold * Width * Height) return false;

        Commit(paint);
        return true;
    }

    /// <summary>
    /// Haelt als Stand fest, was seit dem letzten dazukam - vor dem Wiederherstellen, damit
    /// es nicht verloren geht. True, wenn ein Stand entstanden ist.
    /// </summary>
    public bool Checkpoint(PaintedMask paint)
    {
        Follow(paint);

        var cover = paint.Cover();

        if (!cover.AsSpan().SequenceEqual(_last))
        {
            _foreign = true;
            Mark(cover, 0, 0, Width - 1, Height - 1);
            _last = cover.ToArray();
        }

        if (_count == 0) return false;

        Commit(paint);
        return true;
    }

    /// <summary>
    /// Die Maske traegt jetzt einen frueheren Stand. Von hier aus wird weitergezaehlt, und
    /// der naechste Stand ist ein Schnappschuss: Seine Striche setzen nicht auf dem Stand
    /// davor auf, sondern auf diesem.
    /// </summary>
    public void Restored(PaintedMask paint)
    {
        var cover = paint.Cover();

        _reference = cover.ToArray();
        _last = cover.ToArray();
        _changed = new bool[cover.Length];
        _count = 0;
        _pending.Clear();
        _foreign = true;
    }

    /// <summary>Die Deckung des Standes an dieser Stelle der Liste - der Schnappschuss davor, die Striche danach nachgespielt.</summary>
    public byte[] CoverOf(int index)
    {
        int start = States.FindLastIndex(index, s => s.Snapshot is not null);
        if (start < 0) return new byte[Math.Max(1, Width * Height)];

        var mask = new PaintedMask { Width = Width, Height = Height, Data = States[start].Snapshot! };

        for (int i = start + 1; i <= index; i++)
            foreach (var stroke in States[i].Strokes ?? new List<PaintStroke>())
                stroke.Replay(mask);

        return mask.Cover().ToArray();
    }

    /// <summary>Die Stelle eines Standes in der Liste - oder -1.</summary>
    public int IndexOf(int number) => States.FindIndex(s => s.Number == number);

    private void Commit(PaintedMask paint)
    {
        int lastSnapshot = States.FindLastIndex(s => s.Snapshot is not null);
        bool snapshot = _foreign || States.Count - lastSnapshot >= SnapshotEvery;

        if (snapshot) paint.Keep();

        States.Add(new MaskState
        {
            Number = NextNumber++,
            SavedUtc = DateTime.UtcNow,
            Changed = Pending,
            Snapshot = snapshot ? paint.Data : null,
            Strokes = snapshot ? null : _pending.ToList(),
        });

        var cover = paint.Cover();

        _reference = cover.ToArray();
        _changed = new bool[cover.Length];
        _count = 0;
        _pending.Clear();
        _foreign = false;

        Trim();
    }

    /// <summary>
    /// Der aelteste Stand faellt weg, wenn es zu viele sind. Steht hinter ihm einer aus
    /// Strichen, wird der zum Schnappschuss - ihm fehlte sonst sein Anfang.
    /// </summary>
    private void Trim()
    {
        while (States.Count > Depth)
        {
            if (States[1].Snapshot is null)
            {
                var kept = FromCover(CoverOf(1));
                kept.Keep();

                States[1].Snapshot = kept.Data;
                States[1].Strokes = null;
            }

            States.RemoveAt(0);
        }
    }

    /// <summary>Markiert, was sich in diesem Rechteck gegenueber dem letzten Stand geaendert hat.</summary>
    private void Mark(byte[] cover, int x0, int y0, int x1, int y1)
    {
        if (_reference is null || _changed is null) return;

        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(Width - 1, x1);
        y1 = Math.Min(Height - 1, y1);

        for (int y = y0; y <= y1; y++)
        {
            int row = y * Width;

            for (int x = x0; x <= x1; x++)
            {
                int at = row + x;

                if (!_changed[at] && cover[at] != _reference[at])
                {
                    _changed[at] = true;
                    _count++;
                }
            }
        }
    }

    private PaintedMask FromCover(byte[] cover) => PaintedMask.FromCover(Width, Height, cover);
}
