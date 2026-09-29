using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Wie sich ein Werkzeug auf einer Folge verhaelt (docs/Atelier-Werkzeugplan.md, 3.2).
/// Entscheidet, wo es auf einer Folge erscheint - gebraucht ab dem Symbol fuer "nur dieses Bild".
/// </summary>
public enum ToolTiming
{
    /// <summary>Rechnet je Bild neu aus dem Bild oder seinen Paessen - auf einer Folge immer richtig.</summary>
    Adapts,

    /// <summary>Eine feste Lage im Bild, gleich auf jedem Bild der Folge.</summary>
    Still,

    /// <summary>Gilt nur fuer das Bild, auf dem es entstand.</summary>
    PerFrame,

    /// <summary>Entsteht auf einem Bild und wird ueber die Folge weitergegeben.</summary>
    Carried,
}

/// <summary>Was geschieht, wenn ein Werkzeug gewaehlt wird.</summary>
public enum ToolAction
{
    /// <summary>Ein Effekt: im Stapel seine Karte im Farbstreifen, im Knotenmodus ein Knoten.</summary>
    Effect,

    /// <summary>Eine Maske als Knoten.</summary>
    Mask,

    /// <summary>Der Pinsel - als Zug, als Flaeche oder als Stempel.</summary>
    Brush,

    /// <summary>Die gemalte Maske bearbeiten - das Menue dazu.</summary>
    MaskEdit,
}

/// <summary>Ein Werkzeug der Leiste: wo es steht, wie es heisst, was es tut.</summary>
public sealed record ToolEntry(string Key, string Category, string TitleKey, string Glyph, ToolTiming Timing, ToolAction Action)
{
    /// <summary>Die Knotenart - bei Effekten und Masken.</summary>
    public NodeKind? Kind { get; init; }

    /// <summary>Die Karte im Farbstreifen - bei Effekten, die es im Stapel gibt.</summary>
    public string? Section { get; init; }

    /// <summary>Beim Pinsel: Zug oder Flaeche.</summary>
    public PaintArea Area { get; init; }

    /// <summary>Beim Pinsel: der Stempel - sonst bleibt die Spitze, wie sie ist.</summary>
    public BrushShape? Shape { get; init; }

    /// <summary>Nur im Knotenmodus: Im Stapel gibt es dafuer keinen Platz.</summary>
    public bool NodesOnly { get; init; }

    /// <summary>Ein Satz dazu - fuer den Tooltip.</summary>
    public string? HintKey { get; init; }

    /// <summary>Weitere Woerter fuer die Suche.</summary>
    public string Words { get; init; } = "";

    /// <summary>So steht es in der Liste der Suche: Zeichen, Name, Kategorie.</summary>
    public override string ToString()
        => $"{Glyph}  {Localization.Strings.T(TitleKey)}   ·  {Localization.Strings.T(Category)}";
}

/// <summary>
/// Alle Werkzeuge der Leiste, nach den Kategorien aus dem Werkzeugplan (Entscheidung 6).
///
/// Die Effekte und Masken kommen aus <see cref="NodeCatalog"/> - derselben Liste, aus der der
/// Hub im Knotenmodus baut. Hier steht nur, in welche Kategorie jede Art gehoert, wie sie
/// sich auf einer Folge verhaelt und welches Zeichen sie traegt. Die Bausteine des Graphen
/// selbst - Mischen, Platzieren, Schwarz - bleiben im Hub: Sie tun dem Bild nichts, sie
/// verbinden nur.
/// </summary>
public static class ToolCatalog
{
    public const string Select = "S_ToolCatSelect";
    public const string Paint = "S_ToolCatPaint";
    public const string Tone = "S_ToolCatTone";
    public const string Colour = "S_ToolCatColour";
    public const string Details = "S_ToolCatDetails";
    public const string Optics = "S_ToolCatOptics";
    public const string Light = "S_ToolCatLight";
    public const string Glitch = "S_ToolCatGlitch";
    public const string Time = "S_ToolCatTime";
    public const string Retouch = "S_ToolCatRetouch";

    /// <summary>Die Kategorien in der Reihenfolge des Bandes - mit ihren Zeichen.</summary>
    public static readonly IReadOnlyList<(string Key, string Glyph)> Categories = new[]
    {
        (Select, "⬚"), (Paint, "✎"), (Tone, "◐"), (Colour, "◑"), (Details, "◈"),
        (Optics, "◉"), (Light, "✦"), (Glitch, "▤"), (Time, "⧗"), (Retouch, "✚"),
    };

    /// <summary>Die Kategorien, die schon etwas enthalten. Zeit und Retusche kommen mit W9 und W4.</summary>
    public static IEnumerable<string> Shown => Categories.Select(c => c.Key).Where(c => All.Any(e => e.Category == c));

    public static string GlyphOf(string category) => Categories.FirstOrDefault(c => c.Key == category).Glyph ?? "•";

    /// <summary>In welche Kategorie jede Art des Knotenkatalogs gehoert. Fehlt sie hier, gehoert sie dem Hub.</summary>
    private static readonly Dictionary<string, string> CategoryOfKind = new(StringComparer.Ordinal)
    {
        ["S_MaskLuminance"] = Select, ["S_MaskUnderlying"] = Select, ["S_MaskColour"] = Select,
        ["S_MaskGradient"] = Select, ["S_NodeMaskShape"] = Select, ["S_NodeMaskMath"] = Select, ["S_NodeCutout"] = Select,
        ["S_MaskPainted"] = Paint,

        ["S_Correction"] = Tone, ["S_Levels"] = Tone, ["S_Equalise"] = Tone, ["S_Curves"] = Tone, ["S_Zones"] = Tone, ["S_NodeTone"] = Tone, ["S_NodeMapRange"] = Tone,

        ["S_WhiteBalance"] = Colour, ["S_ColourBands"] = Colour, ["S_Vibrance"] = Colour, ["S_NodeColorRamp"] = Colour, ["S_Lut"] = Colour, ["S_Match"] = Colour,

        ["S_Clarity"] = Details, ["S_Clahe"] = Details, ["S_Texture"] = Details, ["S_Sharpen"] = Details, ["S_Noise"] = Details, ["S_Grain"] = Details,

        ["S_Motion"] = Optics, ["S_Displace"] = Optics, ["S_DepthField"] = Optics, ["S_Distortion"] = Optics,
        ["S_Chromatic"] = Optics, ["S_Vignette"] = Optics,

        ["S_Dehaze"] = Light, ["S_Bloom"] = Light, ["S_Halation"] = Light, ["S_NodeDiffusion"] = Light, ["S_NodeLight"] = Light, ["S_Deflicker"] = Light,

        ["S_Sort"] = Glitch, ["S_Dither"] = Glitch,
    };

    public static readonly IReadOnlyList<ToolEntry> All = Build();

    private static List<ToolEntry> Build()
    {
        var list = new List<ToolEntry>
        {
            // Malen: der Pinsel in seinen Arten, dann die Arbeit an der gemalten Maske.
            new("brush", Paint, "S_ToolBrush", "✎", ToolTiming.Still, ToolAction.Brush) { HintKey = "S_BrushModeStrokeHint", Words = "pinsel malen brush" },
            new("rectangle", Paint, "S_ToolRectangle", "▭", ToolTiming.Still, ToolAction.Brush) { Area = PaintArea.Rectangle, HintKey = "S_BrushModeRectangleHint" },
            new("ellipse", Paint, "S_ToolEllipse", "◯", ToolTiming.Still, ToolAction.Brush) { Area = PaintArea.Ellipse, HintKey = "S_BrushModeEllipseHint" },
            new("lasso", Paint, "S_ToolLasso", "➰", ToolTiming.Still, ToolAction.Brush) { Area = PaintArea.Lasso, HintKey = "S_BrushModeLassoHint" },
            new("stamp", Paint, "S_ToolStamp", "✿", ToolTiming.Still, ToolAction.Brush) { Shape = BrushShape.Stamp, HintKey = "S_BrushStampHint", Words = "spitze tip" },
            new("mask-edit", Paint, "S_MaskEdit", "◑", ToolTiming.Still, ToolAction.MaskEdit)
            {
                NodesOnly = true, HintKey = "S_MaskEditHint",
                Words = "umkehren fuellen leeren weiche kante ausweiten schrumpfen verfeinern invert fill feather grow shrink refine",
            },
        };

        foreach (var kind in NodeCatalog.All)
        {
            if (!CategoryOfKind.TryGetValue(kind.TitleKey, out var category)) continue;

            bool mask = kind.Group == NodeCatalog.Masks;
            string glyph = kind.Section is { } s ? GradingPanel.GlyphOf(s) ?? "•" : mask ? "◐" : "•";

            list.Add(new ToolEntry(kind.TitleKey, category, kind.TitleKey, glyph,
                                   kind.TitleKey == "S_MaskPainted" ? ToolTiming.Still : ToolTiming.Adapts,
                                   mask ? ToolAction.Mask : ToolAction.Effect)
            {
                Kind = kind,
                Section = kind.Section,
                NodesOnly = mask || kind.Section is null,
            });
        }

        return list;
    }

    /// <summary>
    /// Die Suche mit Strg+K: nach Name, Kategorie und weiteren Woertern, gross und klein gleich.
    /// Was mit dem Gesuchten ANFAENGT, steht vorn.
    /// </summary>
    public static IReadOnlyList<ToolEntry> Find(string text, Func<string, string> translate)
    {
        string wanted = text.Trim().ToLowerInvariant();
        if (wanted.Length == 0) return Array.Empty<ToolEntry>();

        return All
            .Select(e => (Entry: e, Title: translate(e.TitleKey).ToLowerInvariant(),
                          Other: (translate(e.Category) + " " + e.Words).ToLowerInvariant()))
            .Where(t => t.Title.Contains(wanted) || t.Other.Contains(wanted))
            .OrderBy(t => t.Title.StartsWith(wanted) ? 0 : t.Title.Contains(wanted) ? 1 : 2)
            .ThenBy(t => t.Title, StringComparer.CurrentCulture)
            .Select(t => t.Entry)
            .ToList();
    }
}
