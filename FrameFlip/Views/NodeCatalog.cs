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
    public const string Convert = "S_NodeMenuConvert";

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
        new(Masks, "S_NodeMaskMath", () => new MaskMathNode()),
        new(Masks, "S_NodeMaskShape", () => new MaskShapeNode()),
        new(Masks, "S_NodeCutout", () => new CutoutNode()),

        new(Convert, "S_NodeMapRange", () => new MapRangeNode()),
        new(Convert, "S_NodeColorRamp", () => new ColorRampNode()),

        new(Picture, "S_NodeLight", () => new LightNode()),
        new(Picture, "S_NodeView", () => new ViewNode()),
        new(Picture, "S_NodeTone", () => new ToneNode()),

        new("S_GroupBasics", "S_Correction", () => new LayerGradeNode { Tools = new GradingStack() }, "Basic"),
        new("S_GroupBasics", "S_Levels", () => new PointToolNode { Tool = new LevelsTool() }, "Levels"),
        new("S_GroupBasics", "S_Equalise", () => new PointToolNode { Tool = new EqualiseTool() }, "Equalise"),
        new("S_GroupBasics", "S_Curves", () => new PointToolNode { Tool = new CurvesTool() }, "Curve"),
        new("S_GroupBasics", "S_WhiteBalance", () => new PointToolNode { Tool = new WhiteBalanceTool() }, "WhiteBalance"),
        new("S_GroupBasics", "S_Zones", () => new PointToolNode { Tool = new LiftGammaGainTool() }, "Zones"),
        new("S_GroupBasics", "S_ColourBands", () => new PointToolNode { Tool = new HslTool() }, "Bands"),
        new("S_GroupBasics", "S_Vibrance", () => new PointToolNode { Tool = new VibranceTool() }),
        new("S_GroupBasics", "S_Match", () => new PointToolNode { Tool = new MatchTool() }, "Match"),

        new("S_GroupLight", "S_Dehaze", () => new LocalNode { Tool = new DehazeTool() }, "Dehaze"),
        new("S_GroupLight", "S_Bloom", () => Started(new LocalNode { Tool = new BloomTool() }), "Bloom"),
        new("S_GroupLight", "S_Halation", () => Started(new LocalNode { Tool = new HalationTool() }), "Halation"),
        new("S_GroupLight", "S_Noise", () => new LocalNode { Tool = new NoiseTool() }, "Noise"),
        new("S_GroupLight", "S_Clarity", () => new LocalNode { Tool = new ClarityTool() }, "Clarity"),
        new("S_GroupLight", "S_Clahe", () => new FramePassNode { Pass = new ClaheTool() }, "Clahe"),
        new("S_GroupLight", "S_Texture", () => new LocalNode { Tool = new TextureTool() }, "Texture"),
        new("S_GroupLight", "S_Sharpen", () => new LocalNode { Tool = new SharpenTool() }, "Sharpen"),

        new("S_GroupOptics", "S_Motion", () => Started(new DataNode { Tool = new MotionBlurTool() }), "Motion"),
        new("S_GroupOptics", "S_Displace", () => Started(new DataNode { Tool = new DisplaceTool() }), "Displace"),
        new("S_GroupOptics", "S_DepthField", () => Started(new DataNode { Tool = new DepthFieldTool() }), "Depth"),
        new("S_GroupOptics", "S_Distortion", () => Started(new GeometryNode { Tool = new DistortionTool() }), "Distortion"),
        new("S_GroupOptics", "S_Chromatic", () => Started(new GeometryNode { Tool = new ChromaticTool() }), "Chromatic"),
        new("S_GroupOptics", "S_Vignette", () => Started(new OpticsNode { Tool = new VignetteTool() }), "Vignette"),

        new("S_GroupFilm", "S_Dither", () => Started(new OpticsNode { Tool = new DitherTool() }), "Dither"),
        new("S_GroupFilm", "S_NodeDiffusion", () => Started(new FramePassNode { Pass = new DiffusionTool() })),
        new("S_GroupFilm", "S_Sort", () => Started(new FramePassNode { Pass = new SortTool() }), "Sort"),
        new("S_GroupFilm", "S_Grain", () => Started(new OpticsNode { Tool = new GrainTool() }), "Grain"),
        new("S_GroupFilm", "S_Deflicker", () => new OpticsNode { Tool = new DeflickerTool() }, "Deflicker"),

        new("S_GroupTable", "S_Lut", () => new PointToolNode { Tool = new LutTool() }, "Lut"),
    };

    /// <summary>
    /// Ein neuer Effekt mit seinem sichtbaren Startwert - siehe <see cref="EffectStart"/>.
    /// Korrekturen gehen nicht hier durch, sie beginnen neutral.
    /// </summary>
    private static Node Started(Node node)
    {
        EffectStart.Apply(node switch
        {
            LocalNode { Tool: { } tool } => tool,
            OpticsNode { Tool: { } tool } => tool,
            GeometryNode { Tool: { } tool } => tool,
            DataNode { Tool: { } tool } => tool,
            FramePassNode { Pass: { } pass } => pass,
            _ => null,
        });

        return node;
    }

    /// <summary>Die Art, fuer die eine Kachel der Palette steht.</summary>
    public static NodeKind? ForSection(string section) => All.FirstOrDefault(k => k.Section == section);

    /// <summary>
    /// Die Renderdaten, die ein neuer Knoten braucht - an den Ausgang der Datei, der sie
    /// liefert. Ein Tiefenschaerfe-Knoten ohne Tiefe ruht, und niemand wuesste warum.
    /// </summary>
    public static void WireData(NodeGraph graph, Node node)
    {
        // Eine Passmaske bekommt ihren Pass als Kabel - man sieht, woher sie liest.
        if (node is MaskNode { Mask: { Kind: MaskKind.Pass, Source.Length: > 0 } mask } && NodeGroups.File(graph) is not null)
        {
            graph.Connect(NodeGroups.PassSource(graph, mask.Source), mask.Source, node, "Pass");
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

        if (output is not null && NodeGroups.File(graph) is { } render)
            graph.Connect(render, output, node, "Daten");
    }
}
