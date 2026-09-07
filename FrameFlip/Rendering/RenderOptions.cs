namespace FrameFlip.Rendering;

/// <summary>Womit gerechnet wird.</summary>
public enum RenderEngine { Cycles, Eevee, Workbench }

/// <summary>Worauf gerechnet wird. Gilt nur fuer Cycles.</summary>
public enum RenderDevice { Unchanged, Cpu, Gpu }

/// <summary>Wie viele Farbkanaele in die Datei kommen.</summary>
public enum ColorMode { Unchanged, Bw, Rgb, Rgba }

/// <summary>
/// Was ein Render-Auftrag einstellen darf.
///
/// Zwei Sorten Feld stecken hier nebeneinander, und der Unterschied ist wichtig:
///
/// Was NULL ist, bleibt so, wie es in der .blend-Datei steht. Das ist der
/// Normalfall und die sicherere Voreinstellung - wer aus der Ferne rendert, will
/// meistens genau das, was er am Rechner eingerichtet hat, und nicht die Meinung
/// eines Formulars.
///
/// Was gesetzt ist, wird ausdruecklich ueberschrieben. Blenders Kommandozeile kann
/// davon nur einen Teil (Format, Ausgabe, Bildbereich); alles andere - Samples,
/// Aufloesung, Farbtiefe, Entrauschen - geht ueber einen Python-Ausdruck, den
/// <see cref="BlenderInvocation"/> daraus baut.
/// </summary>
public sealed record RenderOptions
{
    /// <summary>Der Rahmen: ein Einzelbild oder eine Folge.</summary>
    public bool Animation { get; init; }

    /// <summary>Erstes und letztes Bild. Bei einem Einzelbild zaehlt nur <see cref="Frame"/>.</summary>
    public int? First { get; init; }

    public int? Last { get; init; }

    /// <summary>Schrittweite. 2 heisst jedes zweite Bild.</summary>
    public int? Step { get; init; }

    /// <summary>Das eine Bild bei einem Einzelbild-Render. Null heisst: das aus der Datei.</summary>
    public int? Frame { get; init; }

    /// <summary>Szene, falls die Datei mehrere hat. Leer heisst: die eingestellte.</summary>
    public string Scene { get; init; } = string.Empty;

    public RenderEngine? Engine { get; init; }

    public RenderDevice Device { get; init; } = RenderDevice.Unchanged;

    /// <summary>Samples je Bild. Bei Cycles das Maximum, bei EEVEE die Zahl der Durchgaenge.</summary>
    public int? Samples { get; init; }

    /// <summary>Entrauschen ein oder aus. Null laesst es, wie es steht.</summary>
    public bool? Denoise { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    /// <summary>Aufloesung in Prozent. 50 rechnet in halber Kantenlaenge.</summary>
    public int? Percentage { get; init; }

    /// <summary>Blenders Formatname: PNG, JPEG, OPEN_EXR, TIFF, WEBP. Leer heisst: unveraendert.</summary>
    public string Format { get; init; } = string.Empty;

    public ColorMode Color { get; init; } = ColorMode.Unchanged;

    /// <summary>Bit je Kanal: 8, 16 oder 32. Was geht, haengt am Format.</summary>
    public int? Depth { get; init; }

    /// <summary>PNG-Kompression oder JPEG-Qualitaet, je nach Format. 0 bis 100.</summary>
    public int? Quality { get; init; }

    /// <summary>Durchsichtiger Hintergrund statt Welt.</summary>
    public bool? Transparent { get; init; }

    /// <summary>Threads fuer die CPU. Null heisst: alle.</summary>
    public int? Threads { get; init; }

    /// <summary>
    /// Alles in Grenzen bringen, statt es abzulehnen.
    ///
    /// Eine Zahl, die von einem Handy kommt, kann alles sein - auch eine, die
    /// Blender in die Knie zwingt. 100000 Samples sind kein Angriff, sondern ein
    /// Vertipper; abgelehnt zu werden waere hier die schlechtere Antwort als
    /// begrenzt zu werden.
    /// </summary>
    public RenderOptions Normalized()
    {
        int? first = First is int a ? Math.Clamp(a, 0, 1_048_574) : null;
        int? last = Last is int b ? Math.Clamp(b, 0, 1_048_574) : null;

        // Ein Bildbereich, der rueckwaerts laeuft, rendert nichts - und sieht dabei
        // aus wie ein haengender Auftrag.
        if (first is int start && last is int end && end < start) last = start;

        return this with
        {
            First = first,
            Last = last,
            Step = Step is int step ? Math.Clamp(step, 1, 1000) : null,
            Frame = Frame is int frame ? Math.Clamp(frame, 0, 1_048_574) : null,
            Samples = Samples is int samples ? Math.Clamp(samples, 1, 100_000) : null,
            Width = Width is int width ? Math.Clamp(width, 4, 65_536) : null,
            Height = Height is int height ? Math.Clamp(height, 4, 65_536) : null,
            Percentage = Percentage is int percent ? Math.Clamp(percent, 1, 400) : null,
            Depth = Depth is int depth ? (depth <= 8 ? 8 : depth <= 16 ? 16 : 32) : null,
            Quality = Quality is int quality ? Math.Clamp(quality, 0, 100) : null,
            Threads = Threads is int threads ? Math.Clamp(threads, 0, 1024) : null,
            Scene = Scene.Trim(),
            Format = Format.Trim().ToUpperInvariant(),
        };
    }
}
