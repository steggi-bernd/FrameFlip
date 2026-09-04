using System.IO;

namespace FrameFlip.Sequencing;

public readonly record struct SequenceFrame(int Number, string Path, string FileName);

/// <summary>Zerlegter Dateiname: render_0042.png -> ("render_", 4, "", ".png").</summary>
public sealed record SequencePattern(string Directory, string Prefix, int Padding, string Suffix, string Extension)
{
    public string Describe() => $"{Prefix}{new string('#', Math.Max(Padding, 1))}{Suffix}{Extension}";
}

public sealed class ImageSequence
{
    public ImageSequence(SequencePattern pattern, IReadOnlyList<SequenceFrame> frames)
    {
        Pattern = pattern;
        Frames = frames;
    }

    public SequencePattern Pattern { get; }
    public IReadOnlyList<SequenceFrame> Frames { get; }

    public int Count => Frames.Count;
    public int StartNumber => Frames.Count > 0 ? Frames[0].Number : 0;
    public int EndNumber => Frames.Count > 0 ? Frames[^1].Number : 0;

    /// <summary>Erwartete Frameanzahl bei lueckenloser Sequenz.</summary>
    public int SpanLength => Frames.Count > 0 ? EndNumber - StartNumber + 1 : 0;

    /// <summary>True, wenn der Render abgebrochen wurde oder Frames fehlen.</summary>
    public bool HasGaps => SpanLength != Count;

    public int Padding => Math.Max(Pattern.Padding, 1);

    public string NumberFormat => new('0', Padding);

    /// <summary>
    /// Framenummern, die innerhalb des Nummernbereichs fehlen. Bei einem
    /// abgebrochenen oder auf mehrere Rechner verteilten Render ist das genau die
    /// Liste, die nachgerendert werden muss.
    /// </summary>
    public IReadOnlyList<int> MissingNumbers()
    {
        if (Frames.Count < 2 || !HasGaps) return Array.Empty<int>();

        var missing = new List<int>();
        for (int i = 1; i < Frames.Count; i++)
        {
            for (int n = Frames[i - 1].Number + 1; n < Frames[i].Number; n++)
                missing.Add(n);
        }

        return missing;
    }

    public int IndexOfNumber(int number)
    {
        for (int i = 0; i < Frames.Count; i++)
            if (Frames[i].Number == number) return i;
        return -1;
    }

    /// <summary>
    /// Position des Frames, dessen Nummer der gesuchten am naechsten liegt. Die
    /// Zeitleiste spannt den Nummernbereich auf, also zeigt sie auch auf Nummern,
    /// die es nicht gibt - dann soll der naechstgelegene vorhandene Frame kommen.
    /// </summary>
    public int IndexNearestNumber(int number)
    {
        if (Frames.Count == 0) return -1;

        int low = 0, high = Frames.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            int value = Frames[mid].Number;

            if (value == number) return mid;
            if (value < number) low = mid + 1;
            else high = mid - 1;
        }

        // low steht jetzt hinter der Luecke, high davor.
        if (low >= Frames.Count) return Frames.Count - 1;
        if (high < 0) return 0;

        return number - Frames[high].Number <= Frames[low].Number - number ? high : low;
    }

    public int IndexOfPath(string path)
    {
        for (int i = 0; i < Frames.Count; i++)
            if (string.Equals(Frames[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public string DirectoryName => Pattern.Directory;

    public string DisplayName => Path.GetFileName(Pattern.Directory) + " – " + Pattern.Describe();
}
