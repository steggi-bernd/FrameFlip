using System.IO;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>Eine Einstellung eines Knotens, wie der Farbstreifen sie zeigt.</summary>
public abstract record NodeField(string LabelKey)
{
    /// <summary>
    /// Aendert den Aufbau des Graphen und nicht nur eine Einstellung - einen Ausgang,
    /// ein Kabel. Das haelt die Seite selbst fest und rechnet selbst neu; der Streifen
    /// meldet danach nichts mehr, sonst rechnete dasselbe Bild zweimal.
    /// </summary>
    public bool Structural { get; init; }
}

/// <summary>Ein Regler.</summary>
public sealed record SliderField(string LabelKey, double Min, double Max, Func<double> Get, Action<double> Set,
                                 double Default, string Format = "0.00") : NodeField(LabelKey);

/// <summary>Eine Auswahl aus festen Moeglichkeiten.</summary>
public sealed record ChoiceField(string LabelKey, IReadOnlyList<(string Key, int Value)> Options,
                                 Func<int> Get, Action<int> Set) : NodeField(LabelKey);

/// <summary>Ein Schalter.</summary>
/// <param name="Text">Steht statt der Beschriftung da, unuebersetzt - etwa der Name eines Passes.</param>
public sealed record SwitchField(string LabelKey, Func<bool> Get, Action<bool> Set, string? Text = null)
    : NodeField(LabelKey);

/// <summary>Ein Knopf - fuer etwas, das man tut, statt es einzustellen.</summary>
public sealed record ButtonField(string LabelKey, Action Click) : NodeField(LabelKey);

/// <summary>Ein Satz, der erklaert - ohne etwas einzustellen.</summary>
public sealed record InfoField(string Text) : NodeField("");

/// <summary>
/// Was die Felder ueber die Seite wissen muessen: den Graphen, die Passe der Datei und
/// den Weg, auf dem eine Aenderung am Aufbau festgehalten wird.
/// </summary>
/// <param name="Passes">Die Passe, die ein Ausgang der Datei werden koennen - wie sie in der Datei heissen, und wie in der Liste.</param>
/// <param name="Change">Fuehrt eine Aenderung am Aufbau aus - mit Rueckgaengig, Nachlesen und Neurechnen.</param>
public sealed record NodeFieldContext(NodeGraph Graph, IReadOnlyList<(string Name, string Label)> Passes,
                                      Action<Action> Change);

/// <summary>
/// Was sich an einem Knoten einstellen laesst, der keine eigene Karte im Farbstreifen
/// hat - Mischen, Maske, Platzieren und die Grundkorrektur des Bildes.
///
/// Werkzeugknoten und Ebenenkorrekturen zeigen stattdessen ihre Karten: Dort gibt es
/// die Regler schon, beschriftet und mit ihren Grenzen, und ein zweiter Satz davon
/// waere eine zweite Stelle, an der eine Grenze falsch stehen kann.
///
/// Die Grenzen hier sind dieselben wie im Ebenenstreifen.
/// </summary>
public static class NodeFields
{
    /// <param name="context">
    /// Ohne ihn fehlt, was die Seite kennt: die Passe der Datei am Dateiknoten, das Kabel
    /// an einer Passmaske und das Leeren einer Kryptomatte.
    /// </param>
    public static IReadOnlyList<NodeField> For(Node node, NodeFieldContext? context = null) => node switch
    {
        MixNode mix => Mix(mix),
        MaskNode mask => Mask(mask, context),
        PlaceNode place => Place(place.Place),
        ExposureTintNode tint => new NodeField[]
        {
            new SliderField("S_Exposure", -6, 6, () => tint.Exposure, v => tint.Exposure = (float)v, 0, "+0.00;-0.00;0"),
            new SliderField("S_NodeTintRed", 0, 2, () => tint.Tint.R, v => tint.Tint.R = (float)v, 1),
            new SliderField("S_NodeTintGreen", 0, 2, () => tint.Tint.G, v => tint.Tint.G = (float)v, 1),
            new SliderField("S_NodeTintBlue", 0, 2, () => tint.Tint.B, v => tint.Tint.B = (float)v, 1),
        },
        LightNode light => new NodeField[]
        {
            new SliderField("S_Exposure", -6, 6, () => light.Exposure, v => light.Exposure = v, 0, "+0.00;-0.00;0"),
            new SliderField("S_Saturation", 0, 2, () => light.Saturation, v => light.Saturation = v, 1),
        },
        ToneNode tone => new NodeField[]
        {
            new SliderField("S_BlackPoint", 0, 0.5, () => tone.BlackPoint, v => tone.BlackPoint = v, 0),
            new SliderField("S_WhitePoint", 0.5, 1.5, () => tone.WhitePoint, v => tone.WhitePoint = v, 1),
            new SliderField("S_Gamma", 0.2, 3, () => tone.Gamma, v => tone.Gamma = v, 1),
            new SliderField("S_Contrast", 0.2, 3, () => tone.Contrast, v => tone.Contrast = v, 1),
        },
        OverlayNode overlay => Overlay(overlay),
        PointToolNode { Tool: VibranceTool vibrance } => new NodeField[]
        {
            new SliderField("S_Vibrance", -1, 1, () => vibrance.Amount, v => vibrance.Amount = (float)v, 0, "+0.00;-0.00;0"),
        },
        PictureNode picture => new NodeField[]
        {
            new InfoField(picture.Path),
            new SwitchField("S_FollowSequence", () => picture.FollowSequence, v => picture.FollowSequence = v),
        },
        RenderNode render => Render(render, context),
        ViewNode => new NodeField[] { new InfoField(Strings.T("S_NodeViewHint")) },
        RestrictNode => new NodeField[] { new InfoField(Strings.T("S_NodeRestrictHint")) },
        FallbackNode => new NodeField[] { new InfoField(Strings.T("S_NodeFallbackHint")) },
        BlackNode => new NodeField[] { new InfoField(Strings.T("S_NodeBlackHint")) },
        OutputNode => new NodeField[] { new InfoField(Strings.T("S_NodeOutputHint")) },
        _ => Array.Empty<NodeField>(),
    };

    /// <summary>
    /// Die Datei - und jeder ihrer Passe als Schalter. Eingeschaltet wird er ein eigener
    /// Ausgang, an den sich ein Kabel stecken laesst; ausgeschaltet nimmt er seine Kabel
    /// mit. Zwanzig Ausgaenge, von denen keiner steckt, waeren nur ein hoher Knoten.
    /// </summary>
    private static NodeField[] Render(RenderNode render, NodeFieldContext? context)
    {
        var fields = new List<NodeField> { new InfoField(Strings.T("S_NodeRenderHint")) };

        if (context is null || context.Passes.Count == 0) return fields.ToArray();

        fields.Add(new InfoField(Strings.T("S_NodeRenderPasses")));

        foreach (var (name, label) in context.Passes)
        {
            fields.Add(new SwitchField("", () => render.Passes.Contains(name),
                                       on => context.Change(() => NodeEdits.ShowPass(context.Graph, render, name, on)),
                                       label) { Structural = true });
        }

        return fields.ToArray();
    }

    private static IReadOnlyList<(string, int)> BlendModes()
        => Blending.All.Select(entry => (entry.Key, (int)entry.Mode)).ToList();

    private static NodeField[] Mix(MixNode mix) => new NodeField[]
    {
        new ChoiceField("S_BlendMode", BlendModes(), () => (int)mix.Mode, v => mix.Mode = (BlendMode)v),
        new SliderField("S_Opacity", 0, 1, () => mix.Opacity, v => mix.Opacity = (float)v, 1),
        new SwitchField("S_NodeClip", () => mix.Clip, v => mix.Clip = v),
        new SwitchField("S_NodeInDisplay", () => mix.InDisplay, v => mix.InDisplay = v),
        new SliderField("S_Matte", 0, 0.9, () => mix.MatteFloor, v => mix.MatteFloor = (float)v, 0),
        new SliderField("S_Reveal", 0, 1, () => mix.Reveal, v => mix.Reveal = (float)v, 0),
    };

    private static NodeField[] Place(LayerTransform place) => new NodeField[]
    {
        new SliderField("S_PlaceX", -1, 1, () => place.OffsetX, v => place.OffsetX = (float)v, 0, "+0.00;-0.00;0"),
        new SliderField("S_PlaceY", -1, 1, () => place.OffsetY, v => place.OffsetY = (float)v, 0, "+0.00;-0.00;0"),
        new SliderField("S_PlaceScale", 0.05, 4, () => place.Scale, v => place.Scale = (float)v, 1),
        new SliderField("S_PlaceRotation", -180, 180, () => place.Rotation, v => place.Rotation = (float)v, 0, "0°"),
        new SliderField("S_NodeCropLeft", 0, 0.5, () => place.CropLeft, v => place.CropLeft = (float)v, 0),
        new SliderField("S_NodeCropTop", 0, 0.5, () => place.CropTop, v => place.CropTop = (float)v, 0),
        new SliderField("S_NodeCropRight", 0, 0.5, () => place.CropRight, v => place.CropRight = (float)v, 0),
        new SliderField("S_NodeCropBottom", 0, 0.5, () => place.CropBottom, v => place.CropBottom = (float)v, 0),
    };

    private static NodeField[] Overlay(OverlayNode overlay)
    {
        var fields = new List<NodeField>
        {
            new ChoiceField("S_BlendMode", BlendModes(), () => (int)overlay.Mode, v => overlay.Mode = (BlendMode)v),
            new SliderField("S_Opacity", 0, 1, () => overlay.Opacity, v => overlay.Opacity = (float)v, 1),
            new SliderField("S_Exposure", -6, 6, () => overlay.Exposure, v => overlay.Exposure = (float)v, 0, "+0.00;-0.00;0"),
            new SliderField("S_Matte", 0, 0.9, () => overlay.MatteFloor, v => overlay.MatteFloor = (float)v, 0),
            new SliderField("S_Reveal", 0, 1, () => overlay.Reveal, v => overlay.Reveal = (float)v, 0),
        };

        fields.AddRange(Place(overlay.Place));
        return fields.ToArray();
    }

    /// <summary>Die Maske - je nach Art andere Regler, wie im Ebenenstreifen.</summary>
    private static NodeField[] Mask(MaskNode node, NodeFieldContext? context)
    {
        var mask = node.Mask;

        var fields = new List<NodeField>
        {
            new SwitchField("S_MaskInvert", () => mask.Invert, v => mask.Invert = v),
        };

        switch (mask.Kind)
        {
            case MaskKind.Luminance or MaskKind.Underlying:
                fields.Add(new SliderField("S_MaskFrom", 0, 1, () => mask.Low, v => mask.Low = (float)v, 0));
                fields.Add(new SliderField("S_MaskTo", 0, 1, () => mask.High, v => mask.High = (float)v, 1));
                fields.Add(new SliderField("S_MaskSoft", 0, 1, () => mask.Softness, v => mask.Softness = (float)v, 0.1));
                break;

            case MaskKind.Colour:
                fields.Add(new SliderField("S_MaskHue", 0, 360, () => mask.Hue, v => mask.Hue = (float)v, 0, "0°"));
                fields.Add(new SliderField("S_MaskSpread", 1, 180, () => mask.Spread, v => mask.Spread = (float)v, 30, "0°"));
                fields.Add(new SliderField("S_MaskSoft", 0, 1, () => mask.Softness, v => mask.Softness = (float)v, 0.1));
                fields.Add(new SliderField("S_MaskBlack", 0, 1, () => mask.Low, v => mask.Low = (float)v, 0));
                fields.Add(new SliderField("S_MaskWhite", 0, 1, () => mask.High, v => mask.High = (float)v, 1));
                break;

            case MaskKind.Gradient:
                fields.Add(new SliderField("S_MaskAngle", 0, 360, () => mask.Angle, v => mask.Angle = (float)v, 90, "0°"));
                fields.Add(new SliderField("S_MaskCentre", 0, 1, () => mask.Centre, v => mask.Centre = (float)v, 0.5));
                fields.Add(new SliderField("S_MaskWidth", 0, 2, () => mask.Width, v => mask.Width = (float)v, 0.5));
                break;

            case MaskKind.Pass:
                fields.Add(new InfoField(PassOf(node, context)));
                fields.Add(new SliderField("S_MaskBlack", 0, 1, () => mask.Low, v => mask.Low = (float)v, 0));
                fields.Add(new SliderField("S_MaskWhite", 0, 1, () => mask.High, v => mask.High = (float)v, 1));
                break;

            case MaskKind.Painted:
                fields.Add(new InfoField(Strings.T("S_NodePaintHint")));
                fields.Add(new SliderField("S_MaskBlack", 0, 1, () => mask.Low, v => mask.Low = (float)v, 0));
                fields.Add(new SliderField("S_MaskWhite", 0, 1, () => mask.High, v => mask.High = (float)v, 1));
                break;

            case MaskKind.Cryptomatte:
                fields.Add(new InfoField(Strings.T("S_NodeCryptoHint")));

                if (mask.Picks.Count > 0)
                {
                    fields.Add(new InfoField(Strings.T("S_NodeCryptoPicked",
                        string.Join(", ", mask.Picks.Select(p => p.Name.Length > 0 ? p.Name : "?")))));

                    if (context is not null)
                        fields.Add(new ButtonField("S_NodeCryptoClear", () => context.Change(mask.Picks.Clear)) { Structural = true });
                }

                fields.Add(new SliderField("S_MaskBlack", 0, 1, () => mask.Low, v => mask.Low = (float)v, 0));
                fields.Add(new SliderField("S_MaskWhite", 0, 1, () => mask.High, v => mask.High = (float)v, 1));
                break;
        }

        return fields.ToArray();
    }

    /// <summary>
    /// Woher eine Passmaske liest. Das Kabel geht vor; ohne Kabel liest sie den Pass,
    /// den sie beim Namen nennt - so wie im Stapel, aus dem sie vielleicht kommt.
    /// </summary>
    private static string PassOf(MaskNode node, NodeFieldContext? context)
    {
        string Label(string name)
            => context?.Passes.FirstOrDefault(p => p.Name == name).Label is { Length: > 0 } label ? label : name;

        if (context?.Graph.Into(node.Id, "Pass") is { } link && context.Graph.Find(link.From) is { } from)
        {
            string name = from is PictureNode picture ? Path.GetFileName(picture.Path) : Label(link.Output);
            return Strings.T("S_NodeMaskPassWired", name);
        }

        return node.Mask.Source.Length > 0
            ? Strings.T("S_NodeMaskPassNamed", Label(node.Mask.Source))
            : Strings.T("S_NodeMaskPassNone");
    }
}
