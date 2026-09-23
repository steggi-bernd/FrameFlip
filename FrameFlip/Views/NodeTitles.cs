using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Wie Knoten und Anschluesse in der Oberflaeche heissen - und welche Karte im
/// Farbstreifen zu einem Werkzeugknoten gehoert.
///
/// Ein Werkzeugknoten heisst wie die Karte, die sein Werkzeug im Stapel hat. Wer
/// umschaltet, soll dieselben Namen wiederfinden und nicht eine zweite Sprache fuer
/// dieselben Dinge lernen.
/// </summary>
public static class NodeTitles
{
    /// <summary>Werkzeugkennung -> Abschnitt im Farbstreifen und sein Name.</summary>
    private static readonly Dictionary<string, (string Section, string Key)> Tools = new(StringComparer.Ordinal)
    {
        [CurvesTool.KindName] = ("Curve", "S_Curves"),
        [WhiteBalanceTool.KindName] = ("WhiteBalance", "S_WhiteBalance"),
        [LiftGammaGainTool.KindName] = ("Zones", "S_Zones"),
        [HslTool.KindName] = ("Bands", "S_ColourBands"),
        [VibranceTool.KindName] = ("Basic", "S_Vibrance"),
        [LutTool.KindName] = ("Lut", "S_Lut"),
        [DehazeTool.KindName] = ("Dehaze", "S_Dehaze"),
        [BloomTool.KindName] = ("Bloom", "S_Bloom"),
        [HalationTool.KindName] = ("Halation", "S_Halation"),
        [NoiseTool.KindName] = ("Noise", "S_Noise"),
        [ClarityTool.KindName] = ("Clarity", "S_Clarity"),
        [TextureTool.KindName] = ("Texture", "S_Texture"),
        [SharpenTool.KindName] = ("Sharpen", "S_Sharpen"),
        [MotionBlurTool.KindName] = ("Motion", "S_Motion"),
        [DisplaceTool.KindName] = ("Displace", "S_Displace"),
        [DepthFieldTool.KindName] = ("Depth", "S_DepthField"),
        [DistortionTool.KindName] = ("Distortion", "S_Distortion"),
        [ChromaticTool.KindName] = ("Chromatic", "S_Chromatic"),
        [VignetteTool.KindName] = ("Vignette", "S_Vignette"),
        [DitherTool.KindName] = ("Dither", "S_Dither"),
        [DiffusionTool.KindName] = ("Dither", "S_Dither"),
        [SortTool.KindName] = ("Sort", "S_Sort"),
        [GrainTool.KindName] = ("Grain", "S_Grain"),
    };

    /// <summary>Die Kennung des Werkzeugs, das ein Knoten traegt - oder null.</summary>
    public static string? ToolKind(Node node) => node switch
    {
        PointToolNode { Tool: { } tool } => tool.Kind,
        OpticsNode { Tool: { } tool } => tool.Kind,
        LocalNode { Tool: { } tool } => tool.Kind,
        GeometryNode { Tool: { } tool } => tool.Kind,
        DataNode { Tool: { } tool } => tool.Kind,
        FramePassNode { Pass: { } pass } => pass.Kind,
        _ => null,
    };

    /// <summary>Der Abschnitt im Farbstreifen, in dem das Werkzeug dieses Knotens steht.</summary>
    public static string? Section(Node node)
        => ToolKind(node) is { } kind && Tools.TryGetValue(kind, out var entry) ? entry.Section : null;

    public static string For(Node node) => node switch
    {
        // Ein Mischen mit dem Namen seiner Ebene - und der Mischart, die man sonst nirgends sieht.
        MixNode { Label: { Length: > 0 } label } mix => label + ": " + Strings.T(BlendKey(mix.Mode)),
        { Label: { Length: > 0 } label } => label,
        RenderNode => Strings.T("S_NodeRender"),
        PictureNode picture => picture.Path.Length > 0
            ? System.IO.Path.GetFileName(picture.Path)
            : Strings.T("S_NodePicture"),
        BlackNode => Strings.T("S_NodeBlack"),
        PlaceNode => Strings.T("S_NodePlace"),
        ExposureTintNode => Strings.T("S_NodeExposureTint"),
        MaskNode mask => Strings.T("S_NodeMask") + ": " + Strings.T(MaskKey(mask.Mask.Kind)),
        LayerGradeNode grade => Strings.T(grade.Adjustment ? "S_NodeAdjustment" : "S_NodeLayerGrade"),
        RestrictNode => Strings.T("S_NodeRestrict"),
        MixNode mix => Strings.T(mix.Clip ? "S_NodeMixClip" : "S_NodeMix") + ": " + Strings.T(BlendKey(mix.Mode)),
        FallbackNode => Strings.T("S_NodeFallback"),
        LightNode => Strings.T("S_NodeLight"),
        ViewNode => Strings.T("S_NodeView"),
        ToneNode => Strings.T("S_NodeTone"),
        OverlayNode => Strings.T("S_NodeOverlay"),
        MaskMathNode math => Strings.T("S_NodeMaskMath") + ": " + Strings.T(OperationKey(math.Operation)),
        MapRangeNode => Strings.T("S_NodeMapRange"),
        ColorRampNode => Strings.T("S_NodeColorRamp"),
        MaskShapeNode => Strings.T("S_NodeMaskShape"),
        OutputNode => Strings.T("S_NodeOutput"),
        _ when ToolKind(node) is { } kind && Tools.TryGetValue(kind, out var entry) => Strings.T(entry.Key),
        _ => node.GetType().Name,
    };

    /// <summary>
    /// Wie ein Anschluss heisst. Die Namen im Modell sind Kennungen und bleiben es; in
    /// der Oberflaeche stehen sie in der Sprache des Programms. Ein Pass heisst, wie er
    /// in der Datei heisst.
    /// </summary>
    public static string Socket(string name)
    {
        string key = "S_Socket" + name;
        string text = Strings.T(key);

        if (text != key) return text;

        // Ein Pass der Datei: ohne den Vorsatz der Ansichtsebene, wie in den Listen -
        // "ViewLayer.DiffCol" passt sonst kaum an einen Knoten.
        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    private static string OperationKey(MaskOperation operation) => operation switch
    {
        MaskOperation.Add => "S_MaskOpAdd",
        MaskOperation.Subtract => "S_MaskOpSubtract",
        MaskOperation.Minimum => "S_MaskOpMinimum",
        MaskOperation.Maximum => "S_MaskOpMaximum",
        MaskOperation.Difference => "S_MaskOpDifference",
        _ => "S_MaskOpMultiply",
    };

    private static string MaskKey(MaskKind kind) => kind switch
    {
        MaskKind.Luminance => "S_MaskLuminance",
        MaskKind.Underlying => "S_MaskUnderlying",
        MaskKind.Pass => "S_MaskPass",
        MaskKind.Gradient => "S_MaskGradient",
        MaskKind.Colour => "S_MaskColour",
        MaskKind.Cryptomatte => "S_MaskCryptomatte",
        MaskKind.Painted => "S_MaskPainted",
        _ => "S_MaskNone",
    };

    public static string BlendKey(BlendMode mode)
    {
        foreach (var (candidate, key) in Blending.All)
            if (candidate == mode) return key;

        return "S_BlendNormal";
    }
}
