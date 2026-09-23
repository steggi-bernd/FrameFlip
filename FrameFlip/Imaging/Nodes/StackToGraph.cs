using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Macht aus dem Stapel einen Graphen, der dasselbe Bild rechnet.
///
/// Das ist der eine Weg in den Knotenmodus, und er geht nur in diese Richtung - siehe
/// docs/Atelier-Nodes.md. Er folgt dem Composer Schritt fuer Schritt: dieselben
/// Ebenen, die er ueberspringt, dieselben Traeger und Schnittgruppen, dieselbe
/// Reihenfolge der Werkzeuge am fertigen Bild. Ob das stimmt, prueft ein Test, der
/// viele zufaellige Stapel auf beiden Wegen rechnet und dieselben Bytes verlangt.
///
/// Der Graph bekommt Kopien - an ihm zu drehen veraendert den Stapel nicht, und
/// umgekehrt.
/// </summary>
public static class StackToGraph
{
    /// <summary>Wie tief Gruppen stehen duerfen - dieselbe Grenze wie im Composer.</summary>
    private const int MaxDepth = 8;

    public static NodeGraph Convert(LayerStack? layers, ImageAdjustments? adjustments, GradingStack? picture)
    {
        var graph = new NodeGraph();
        var render = graph.Add(new RenderNode());

        (Node Node, string Output) image = layers is null || layers.IsPassThrough
            ? (render, RenderNode.Picture)
            : new Walk(graph, render).Build(layers);

        image = Picture(graph, render, image, adjustments ?? ImageAdjustments.Neutral,
                        picture?.Clone() ?? new GradingStack(), layers);

        var output = graph.Add(new OutputNode());
        graph.Connect(image.Node, image.Output, output, "Bild");

        NodeLayout.Arrange(graph);

        return graph;
    }

    /// <summary>
    /// Der Gang durch den Stapel - der Composer, in Knoten statt in Bildpunkten.
    /// </summary>
    private sealed class Walk
    {
        private readonly NodeGraph _graph;
        private readonly RenderNode _render;

        /// <summary>Was bisher zusammengesetzt ist.</summary>
        private (Node Node, string Output) _below;

        /// <summary>
        /// Die offene Schnittgruppe: ihr Traeger samt allem, was an ihn geschnitten ist,
        /// und womit sie am Ende auf das Bisherige kommt.
        /// </summary>
        private (Node Node, string Output)? _group;
        private ImageLayer? _carrier;
        private (Node Node, string Output)? _carrierMask;

        public Walk(NodeGraph graph, RenderNode render)
        {
            _graph = graph;
            _render = render;
            _below = (graph.Add(new BlackNode()), "Bild");
        }

        public (Node, string) Build(LayerStack stack)
        {
            Layers(stack.Layers, depth: 0);
            Close();

            // Hat keine Ebene beigetragen, zeigt sich das Bild der Datei.
            var fallback = _graph.Add(new FallbackNode());
            _graph.Connect(_below.Node, _below.Output, fallback, "Stapel");
            _graph.Connect(_render, RenderNode.Picture, fallback, "Bild");

            return (fallback, "Bild");
        }

        private void Layers(IEnumerable<ImageLayer> layers, int depth)
        {
            foreach (var layer in layers)
            {
                if (!layer.Visible || layer.Opacity <= 0.0005f) continue;

                // Was obenauf liegt, kommt erst nach der Bildwerdung - als eigener Knoten.
                if (layer.OnTop && layer.Content == LayerContent.Image) continue;

                if (layer.Content == LayerContent.Group)
                {
                    if (depth >= MaxDepth)
                    {
                        Layers(layer.Children, depth);
                        continue;
                    }

                    Group(layer, depth);
                    continue;
                }

                Step(layer);
            }
        }

        /// <summary>
        /// Eine Gruppe: Ihre Kinder rechnen auf dem weiter, was darunter liegt, und das
        /// Ergebnis kommt mit ihrer Mischung, Deckkraft und Maske auf den gesicherten Stand.
        /// </summary>
        private void Group(ImageLayer group, int depth)
        {
            Close();

            var saved = _below;

            Layers(group.Children, depth + 1);
            Close();

            var mask = Mask(group.Mask, layer: _below, under: saved);

            var mix = _graph.Add(new MixNode
            {
                Mode = group.Mode,
                Opacity = Math.Clamp(group.Opacity, 0f, 1f),
                InDisplay = group.BlendInDisplay,
            });

            _graph.Connect(saved.Node, saved.Output, mix, "Unten");
            _graph.Connect(_below.Node, _below.Output, mix, "Oben");
            if (mask is { } m) _graph.Connect(m.Node, m.Output, mix, "Faktor");

            _below = (mix, "Bild");
        }

        /// <summary>Eine Ebene oder Einstellungsebene.</summary>
        private void Step(ImageLayer layer)
        {
            bool clipped = layer.Clipped && _group is not null;

            // Eine Ebene, die nicht angeschnitten ist, rechnet mit dem, was schon da ist
            // - und dazu gehoert die offene Gruppe darunter. Sie wird ZUERST
            // geschlossen, wie im Composer: Eine Einstellungsebene und die Masken, die
            // den Untergrund lesen, saehen sonst den Stand vor der letzten Ebene.
            if (!clipped) Close();

            var under = clipped ? _group!.Value : _below;

            (Node Node, string Output) image;
            (Node Node, string Output)? mask;

            if (layer.Content == LayerContent.Adjustment)
            {
                // Eine Einstellungsebene nimmt als Eingang, worauf sie wirkt - und wird
                // erst korrigiert, dann belichtet, wie im Composer.
                var grade = _graph.Add(new LayerGradeNode
                {
                    Adjustments = layer.Adjustments,
                    Tools = layer.Tools?.Clone(),
                    Adjustment = true,
                });

                _graph.Connect(under.Node, under.Output, grade, "Bild");
                image = Scaled(layer, (grade, "Bild"));
                mask = Mask(layer.Mask, layer: image, under: under);
            }
            else
            {
                var source = Source(layer);

                var place = _graph.Add(new PlaceNode { Place = layer.Place.Clone() });
                _graph.Connect(source.Node, source.Output, place, "Bild");

                image = Scaled(layer, (place, "Bild"));
                mask = Mask(layer.Mask, layer: image, under: under);

                if (!layer.Grade().IsNeutral)
                {
                    var grade = _graph.Add(new LayerGradeNode
                    {
                        Adjustments = layer.Adjustments,
                        Tools = layer.Tools?.Clone(),
                    });

                    _graph.Connect(image.Node, image.Output, grade, "Bild");

                    if (layer.Mask.Scope == MaskScope.Colour)
                    {
                        // Die Maske sagt hier, WO korrigiert wird - und nicht mehr, wo
                        // die Ebene zu sehen ist.
                        var restrict = _graph.Add(new RestrictNode());

                        _graph.Connect(image.Node, image.Output, restrict, "Vorher");
                        _graph.Connect(grade, "Bild", restrict, "Nachher");
                        if (mask is { } m) _graph.Connect(m.Node, m.Output, restrict, "Maske");

                        image = (restrict, "Bild");
                        mask = null;
                    }
                    else
                    {
                        image = (grade, "Bild");
                    }
                }
            }

            if (clipped)
            {
                var mix = Mix(layer, clip: true);

                _graph.Connect(_group!.Value.Node, _group.Value.Output, mix, "Unten");
                _graph.Connect(image.Node, image.Output, mix, "Oben");
                if (mask is { } m) _graph.Connect(m.Node, m.Output, mix, "Faktor");

                _group = (mix, "Bild");
                return;
            }

            // Eine neue Ebene, die nicht angeschnitten ist, oeffnet ihre eigene Gruppe -
            // auch wenn nichts an sie geschnitten wird. Die vorige ist oben schon zu.
            _group = image;
            _carrier = layer;
            _carrierMask = mask;
        }

        /// <summary>Die offene Gruppe auf das Bisherige - mit Mischung, Deckkraft und Maske ihres Traegers.</summary>
        private void Close()
        {
            if (_group is not { } group || _carrier is null) return;

            var mix = Mix(_carrier, clip: false);

            _graph.Connect(_below.Node, _below.Output, mix, "Unten");
            _graph.Connect(group.Node, group.Output, mix, "Oben");
            if (_carrierMask is { } m) _graph.Connect(m.Node, m.Output, mix, "Faktor");

            _below = (mix, "Bild");
            _group = null;
            _carrier = null;
            _carrierMask = null;
        }

        private MixNode Mix(ImageLayer layer, bool clip) => _graph.Add(new MixNode
        {
            Mode = layer.Mode,
            Opacity = Math.Clamp(layer.Opacity, 0f, 1f),
            InDisplay = layer.BlendInDisplay,
            MatteFloor = layer.MatteFloor,
            Reveal = layer.Reveal,
            Clip = clip,
        });

        /// <summary>Woher eine Ebene ihr Bild nimmt: ein Pass der Datei oder eine Bilddatei.</summary>
        private (Node Node, string Output) Source(ImageLayer layer)
        {
            if (layer.Content == LayerContent.Pass)
            {
                if (layer.Source.Length == 0) return (_render, RenderNode.Picture);

                if (!_render.Passes.Contains(layer.Source)) _render.Passes.Add(layer.Source);

                return (_render, layer.Source);
            }

            var picture = _graph.Add(new PictureNode { Path = layer.Source, FollowSequence = layer.FollowSequence });

            return (picture, "Bild");
        }

        /// <summary>Belichtung und Toenung - nur wenn es etwas zu multiplizieren gibt.</summary>
        private (Node Node, string Output) Scaled(ImageLayer layer, (Node Node, string Output) image)
        {
            float gain = MathF.Pow(2f, layer.Exposure);

            if (gain * layer.Tint.R == 1f && gain * layer.Tint.G == 1f && gain * layer.Tint.B == 1f) return image;

            var scale = _graph.Add(new ExposureTintNode { Exposure = layer.Exposure, Tint = layer.Tint.Clone() });
            _graph.Connect(image.Node, image.Output, scale, "Bild");

            return (scale, "Bild");
        }

        private (Node Node, string Output)? Mask(LayerMask mask, (Node Node, string Output) layer,
                                                 (Node Node, string Output) under)
        {
            if (mask.Kind == MaskKind.None) return null;

            var node = _graph.Add(new MaskNode { Mask = mask.Clone() });

            _graph.Connect(layer.Node, layer.Output, node, "Ebene");
            _graph.Connect(under.Node, under.Output, node, "Untergrund");

            return (node, "Maske");
        }
    }

    /// <summary>
    /// Die Kette am fertigen Bild - in der Reihenfolge, die der Stapel festlegt. Ein
    /// ausgeschaltetes Werkzeug wird ein stummer Knoten: Seine Einstellung bleibt.
    ///
    /// Werkzeuge in Grundstellung werden KEINE Knoten. Der Farbstreifen haelt jedes
    /// Werkzeug einmal vor, auch die unbenutzten - als Knoten waeren das zwei Dutzend
    /// Kaesten, die nichts tun, und zwischen ihnen die paar, die etwas tun. Der Stapel
    /// ueberspringt sie beim Rechnen ohnehin.
    /// </summary>
    private static (Node, string) Picture(NodeGraph graph, RenderNode render, (Node Node, string Output) image,
                                          ImageAdjustments adjustments, GradingStack stack, LayerStack? layers)
    {
        var current = image;

        void Then(Node node, bool muted = false)
        {
            node.Muted = muted;
            graph.Add(node);
            graph.Connect(current.Node, current.Output, node, "Bild");
            current = (node, "Bild");
        }

        Then(new LightNode { Exposure = adjustments.Exposure, Saturation = adjustments.Saturation });

        foreach (var tool in stack.Tools.Where(t => t.Stage == GradingStage.SceneLinear && !t.IsNeutral))
            Then(new PointToolNode { Tool = tool }, stack.IsBypassed(tool.Kind));

        var optics = stack.Optics.OrderBy(t => t.Stage).ToList();

        foreach (var tool in optics.Where(t => t.Stage == OpticsStage.Lens && !t.IsNeutral))
            Then(new OpticsNode { Tool = tool }, stack.IsBypassed(tool.Kind));

        foreach (var tool in stack.Geometry.Where(t => !t.IsNeutral))
            Then(new GeometryNode { Tool = tool }, stack.IsBypassed(tool.Kind));

        foreach (var tool in stack.Data.Where(t => !t.IsNeutral).OrderBy(t => t.Stage))
        {
            var node = new DataNode { Tool = tool };
            Then(node, stack.IsBypassed(tool.Kind));

            string? output = tool.Needs switch
            {
                PassNeed.Depth => RenderNode.Depth,
                PassNeed.Motion => RenderNode.Motion,
                PassNeed.Normal => RenderNode.Normal,
                _ => null,
            };

            if (output is not null) graph.Connect(render, output, node, "Daten");
        }

        var local = stack.Local.OrderBy(t => t.Stage).ToList();

        foreach (var tool in local.Where(t => t.Stage <= LocalStage.Light && !t.IsNeutral))
            Then(new LocalNode { Tool = tool }, stack.IsBypassed(tool.Kind));

        foreach (var tool in optics.Where(t => t.Stage == OpticsStage.Film && !t.IsNeutral))
            Then(new OpticsNode { Tool = tool }, stack.IsBypassed(tool.Kind));

        Then(new ViewNode());

        Then(new ToneNode
        {
            BlackPoint = adjustments.BlackPoint,
            WhitePoint = adjustments.WhitePoint,
            Gamma = adjustments.Gamma,
            Contrast = adjustments.Contrast,
        });

        foreach (var tool in stack.Tools.Where(t => t.Stage == GradingStage.Display && !t.IsNeutral))
            Then(new PointToolNode { Tool = tool }, stack.IsBypassed(tool.Kind));

        foreach (var tool in local.Where(t => t.Stage > LocalStage.Light && !t.IsNeutral))
            Then(new LocalNode { Tool = tool }, stack.IsBypassed(tool.Kind));

        if (layers is not null)
        {
            foreach (var layer in layers.All())
            {
                if (!layer.OnTop || !layer.Visible || layer.Opacity <= 0.0005f) continue;
                if (layer.Content != LayerContent.Image) continue;

                var source = graph.Add(new PictureNode { Path = layer.Source, FollowSequence = layer.FollowSequence });

                var overlay = new OverlayNode
                {
                    Place = layer.Place.Clone(),
                    Mode = layer.Mode,
                    Opacity = layer.Opacity,
                    Exposure = layer.Exposure,
                    Tint = layer.Tint.Clone(),
                    MatteFloor = layer.MatteFloor,
                    Reveal = layer.Reveal,
                };

                Then(overlay);
                graph.Connect(source, "Bild", overlay, "Ebene");
            }
        }

        foreach (var pass in stack.Frame.Where(p => !p.IsNeutral))
            Then(new FramePassNode { Pass = pass }, stack.IsBypassed(pass.Kind));

        return current;
    }
}
