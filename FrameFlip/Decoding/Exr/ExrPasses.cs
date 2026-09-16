namespace FrameFlip.Decoding.Exr;

/// <summary>Ein Pass in einer Datei - ein Satz Kanaele, der zusammen ein Bild ergibt.</summary>
/// <param name="Name">
/// Der Gruppenname, also alles vor dem letzten Punkt: "ViewLayer.GlossDir". Leer bei
/// einer schmucklosen Datei mit blossem R, G, B.
/// </param>
/// <param name="Grey">True, wenn alle drei Kanaele derselbe sind - Tiefe, Nebel, eine Maske.</param>
public readonly record struct ExrPass(string Name, string Red, string Green, string Blue,
                                      string? Alpha, bool Grey)
{
    /// <summary>
    /// Der Name ohne den Vorsatz der Ansichtsebene. "ViewLayer.GlossDir" wird zu
    /// "GlossDir" - in einer Liste von zwanzig Passen steht sonst zwanzigmal
    /// dasselbe davor, und die Spalte ist schmal.
    /// </summary>
    public string ShortName
    {
        get
        {
            if (Name.Length == 0) return "RGB";

            int dot = Name.LastIndexOf('.');
            return dot >= 0 && dot < Name.Length - 1 ? Name[(dot + 1)..] : Name;
        }
    }
}

/// <summary>
/// Zaehlt die Passe einer EXR auf.
///
/// Ein Multilayer-Render aus Blender bringt zwanzig und mehr mit: Combined, DiffCol,
/// DiffDir, GlossDir, Emit, Env, AO, Shadow, Depth, Mist, Normal, Vector, dazu die
/// Kryptomatten. Sie stecken alle in einer Datei, unterschieden nur durch den Namen
/// des Kanals - "ViewLayer.GlossDir.R" und so fort.
///
/// Gruppiert wird am letzten Punkt, weil der den Kanalbuchstaben abtrennt und nicht
/// die Ebenen untereinander. Welche Buchstaben eine Gruppe fuehrt, entscheidet dann,
/// was sie ist: R/G/B ist Farbe, X/Y/Z eine Richtung, ein einzelner Buchstabe eine
/// Groesse je Bildpunkt.
/// </summary>
public static class ExrPasses
{
    public static IReadOnlyList<ExrPass> List(ExrHeader header)
        => List(header.Channels.Select(c => c.Name));

    public static IReadOnlyList<ExrPass> List(IEnumerable<string> channelNames)
    {
        // Die Reihenfolge der Datei bleibt die Reihenfolge der Liste. Blender
        // schreibt Combined zuerst, und wer die Datei kennt, sucht die Passe dort,
        // wo er sie schon einmal gesehen hat.
        var groups = new List<string>();
        var leaves = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string name in channelNames)
        {
            int dot = name.LastIndexOf('.');
            string group = dot < 0 ? "" : name[..dot];
            string leaf = dot < 0 ? name : name[(dot + 1)..];

            if (!leaves.TryGetValue(group, out var list))
            {
                list = new List<string>();
                leaves[group] = list;
                groups.Add(group);
            }

            list.Add(leaf);
        }

        var passes = new List<ExrPass>();

        foreach (string group in groups)
        {
            var list = leaves[group];

            string Full(string leaf) => group.Length == 0 ? leaf : group + "." + leaf;

            bool Has(string leaf) => list.Contains(leaf, StringComparer.Ordinal);

            // Gross UND klein, und zwar nicht aus Grosszuegigkeit: Blender schreibt
            // die Kryptomatten mit kleinen Buchstaben - "ViewLayer.CryptoObject00.r"
            // - waehrend alles andere gross geschrieben ist. Nur auf Grossbuchstaben
            // zu pruefen hiesse, ausgerechnet die Kryptomatten nicht zu erkennen.
            //
            // Geprueft wird jeweils das ganze Tripel, nicht Buchstabe fuer Buchstabe:
            // Ein Gemisch aus "R" und "g" gaebe es in keiner Datei, und es
            // zusammenzusetzen waere geraten.
            if (Has("R") && Has("G") && Has("B"))
            {
                passes.Add(new ExrPass(group, Full("R"), Full("G"), Full("B"),
                                       Has("A") ? Full("A") : null, Grey: false));
                continue;
            }

            if (Has("r") && Has("g") && Has("b"))
            {
                passes.Add(new ExrPass(group, Full("r"), Full("g"), Full("b"),
                                       Has("a") ? Full("a") : null, Grey: false));
                continue;
            }

            // Normalen, Positionen, Bewegungsvektoren. Als Farbe zu zeigen ist die
            // uebliche Darstellung und beantwortet die Frage, was darin steht.
            if (Has("X") && Has("Y") && Has("Z"))
            {
                passes.Add(new ExrPass(group, Full("X"), Full("Y"), Full("Z"), null, Grey: false));
                continue;
            }

            // Tiefe, Nebel, ein Index, eine Maske: ein Kanal, dreimal gezeigt.
            if (list.Count == 1)
            {
                string only = Full(list[0]);
                passes.Add(new ExrPass(group, only, only, only, null, Grey: true));
                continue;
            }

            // Alles andere - eine Gruppe mit zwei Kanaelen oder mit Buchstaben, die
            // hier nicht vorgesehen sind. Der erste als Graustufe ist eine Antwort,
            // die sich ansehen laesst; sie zu verschweigen waere keine.
            if (list.Count > 1)
            {
                string first = Full(list[0]);
                passes.Add(new ExrPass(group, first, first, first, null, Grey: true));
            }
        }

        return passes;
    }

    /// <summary>
    /// Die Passe einer Datei, ohne sie zu lesen. Leer, wenn die Datei sich nicht
    /// oeffnen laesst - eine unlesbare Datei ist keine Ausnahme, sondern ein
    /// Ergebnis: sie hat keine Passe, die man anbieten koennte.
    /// </summary>
    public static IReadOnlyList<ExrPass> Of(string path)
    {
        try
        {
            using var stream = ExrReader.Open(path);
            return List(ExrHeaderReader.Read(stream));
        }
        catch (Exception)
        {
            return Array.Empty<ExrPass>();
        }
    }

    /// <summary>
    /// Sucht den Pass, der gemeint ist. Null, wenn es ihn nicht gibt.
    ///
    /// Gesucht wird auch ueber den Kurznamen, weil ein Rezept eine Datei ueberdauert:
    /// Wer den Stapel auf einer Sequenz eingerichtet hat, deren Ansichtsebene
    /// "ViewLayer" heisst, soll ihn auch auf einer anwenden koennen, in der sie
    /// anders heisst. Sonst waere jede Umbenennung in Blender ein verlorenes Rezept.
    /// </summary>
    public static ExrPass? Find(IReadOnlyList<ExrPass> passes, string name)
    {
        foreach (var pass in passes)
            if (pass.Name.Equals(name, StringComparison.Ordinal)) return pass;

        int dot = name.LastIndexOf('.');
        string shortName = dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;

        foreach (var pass in passes)
            if (pass.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase)) return pass;

        return null;
    }
}
