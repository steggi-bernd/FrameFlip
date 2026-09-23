using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Views;

/// <summary>Eine Art Knoten, die sich hinzufuegen laesst.</summary>
/// <param name="Group">Unter welchem Punkt des Menues sie steht.</param>
/// <param name="TitleKey">Wie sie heisst.</param>
/// <param name="Create">Legt einen neuen Knoten dieser Art an - in Grundstellung.</param>
/// <param name="Section">Die Kachel der Palette, die fuer sie steht - oder keine.</param>
public sealed record NodeKind(string Group, string TitleKey, Func<Node> Create, string? Section = null);

/// <summary>
/// Was sich im Knotenmodus hinzufuegen laesst - fuer das Menue im Editor und fuer die
/// Palette im Farbstreifen.
///
/// Die Effekte stehen in derselben Ordnung und unter denselben Namen wie in der Palette:
/// Wer den Stapel kennt, findet sie wieder. Dazu kommt, was es nur im Graphen gibt -
/// Mischen, Masken, Platzieren -, weil der Stapel es in seinen Ebenen versteckt.
/// </summary>
public static class NodeCatalog
{
    public const string Layers = "S_NodeMenuLayers";
    public const string Masks = "S_NodeMenuMasks";
    public const string Picture = "S_NodeMenuPicture";

    public static readonly IReadOnlyList<NodeKind> All = new NodeKind[]
    {
        new(Layers, "S_NodeMix", () => new MixNode()),
        new(Layers, "S_NodePlace", () => new PlaceNode()),
        new(Layers, "S_NodeExposureTint", () => new ExposureTintNode()),
        new(Layers, "S_NodeLayerGrade", () => new LayerGradeNode { Tools = new GradingStack() }),
        new(Layers, "S_NodeRestrict", () => new RestrictNode()),
        new(Layers, "S_NodeBlack", () => new BlackNode()),

        new(Masks, "S_MaskLuminance", () => new MaskNode { Mask = new LayerMask { Kind = MaskKind.Luminance } }),
        new(Masks, "S_MaskUnderlying", () => new MaskNode { Mask = new LayerMask { Kind = MaskKind.Underlying } }),
        new(Masks, "S_MaskColour", () => new MaskNode { Mask = new LayerMask { Kind = MaskKind.Colour } }),
        new(Masks, "S_MaskGradient", () => new MaskNode { Mask = new LayerMask { Kind = MaskKind.Gradient } }),
        new(Masks, "S_MaskPainted", () => new MaskNode { Mask = new LayerMask { Kind = MaskKind.Painted } }),

        new(Picture, "S_NodeLight", () => new LightNode()),
        new(Picture, "S_NodeView", () => new ViewNode()),
        new(Picture, "S_NodeTone", () => new ToneNode()),

        new("S_GroupBasics", "S_Correction", () => new LayerGradeNode { Tools = new GradingStack() }, "Basic"),
        new("S_GroupBasics", "S_Curves", () => new PointToolNode { Tool = new CurvesTool() }, "Curve"),
        new("S_GroupBasics", "S_WhiteBalance", () => new PointToolNode { Tool = new WhiteBalanceTool() }, "WhiteBalance"),
        new("S_GroupBasics", "S_Zones", () => new PointToolNode { Tool = new LiftGammaGainTool() }, "Zones"),
        new("S_GroupBasics", "S_ColourBands", () => new PointToolNode { Tool = new HslTool() }, "Bands"),
        new("S_GroupBasics", "S_Vibrance", () => new PointToolNode { Tool = new VibranceTool() }),

        new("S_GroupLight", "S_Dehaze", () => new LocalNode { Tool = new DehazeTool() }, "Dehaze"),
        new("S_GroupLight", "S_Bloom", () => new LocalNode { Tool = new BloomTool() }, "Bloom"),
        new("S_GroupLight", "S_Halation", () => new LocalNode { Tool = new HalationTool() }, "Halation"),
        new("S_GroupLight", "S_Noise", () => new LocalNode { Tool = new NoiseTool() }, "Noise"),
        new("S_GroupLight", "S_Clarity", () => new LocalNode { Tool = new ClarityTool() }, "Clarity"),
        new("S_GroupLight", "S_Texture", () => new LocalNode { Tool = new TextureTool() }, "Texture"),
        new("S_GroupLight", "S_Sharpen", () => new LocalNode { Tool = new SharpenTool() }, "Sharpen"),

        new("S_GroupOptics", "S_Motion", () => new DataNode { Tool = new MotionBlurTool() }, "Motion"),
        new("S_GroupOptics", "S_Displace", () => new DataNode { Tool = new DisplaceTool() }, "Displace"),
        new("S_GroupOptics", "S_DepthField", () => new DataNode { Tool = new DepthFieldTool() }, "Depth"),
        new("S_GroupOptics", "S_Distortion", () => new GeometryNode { Tool = new DistortionTool() }, "Distortion"),
        new("S_GroupOptics", "S_Chromatic", () => new GeometryNode { Tool = new ChromaticTool() }, "Chromatic"),
        new("S_GroupOptics", "S_Vignette", () => new OpticsNode { Tool = new VignetteTool() }, "Vignette"),

        new("S_GroupFilm", "S_Dither", () => new OpticsNode { Tool = new DitherTool() }, "Dither"),
        new("S_GroupFilm", "S_NodeDiffusion", () => new FramePassNode { Pass = new DiffusionTool() }),
        new("S_GroupFilm", "S_Sort", () => new FramePassNode { Pass = new SortTool() }, "Sort"),
        new("S_GroupFilm", "S_Grain", () => new OpticsNode { Tool = new GrainTool() }, "Grain"),

        new("S_GroupTable", "S_Lut", () => new PointToolNode { Tool = new LutTool() }, "Lut"),
    };

    /// <summary>Die Art, fuer die eine Kachel der Palette steht.</summary>
    public static NodeKind? ForSection(string section) => All.FirstOrDefault(k => k.Section == section);

    /// <summary>
    /// Die Renderdaten, die ein neuer Knoten braucht - an den Ausgang der Datei, der sie
    /// liefert. Ein Tiefenschaerfe-Knoten ohne Tiefe ruht, und niemand wuesste warum.
    /// </summary>
    public static void WireData(NodeGraph graph, Node node)
    {
        // Eine Passmaske bekommt ihren Pass als Kabel - man sieht, woher sie liest.
        if (node is MaskNode { Mask: { Kind: MaskKind.Pass, Source.Length: > 0 } mask } &&
            graph.Nodes.OfType<RenderNode>().FirstOrDefault() is { } file &&
            NodeEdits.ShowPass(graph, file, mask.Source, on: true))
        {
            graph.Connect(file, mask.Source, node, "Pass");
            return;
        }

        if (node is not DataNode { Tool: { } tool }) return;

        string? output = tool.Needs switch
        {
            PassNeed.Depth => RenderNode.Depth,
            PassNeed.Motion => RenderNode.Motion,
            PassNeed.Normal => RenderNode.Normal,
            _ => null,
        };

        if (output is not null && graph.Nodes.OfType<RenderNode>().FirstOrDefault() is { } render)
            graph.Connect(render, output, node, "Daten");
    }
}
