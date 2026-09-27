using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FrameFlip.Views;

/// <summary>
/// Die gezeichneten Zeichen der Leisten - Umrisse auf einem Raster von 24, mit runden Enden,
/// nach dem Vorbild von Tabler Icons (Entscheidung 9 im Werkzeugplan). Selbst gezeichnet und
/// nicht als Schrift eingebunden: Es sind wenige, und eine Schrift waere eine Abhaengigkeit
/// mit eigener Lizenz fuer ein Dutzend Striche.
///
/// Der Strich wird nach dem Skalieren gezogen und bleibt deshalb bei jeder Groesse gleich
/// fein. Das Knotensymbol der Spalte links bleibt, wie es ist - so gewuenscht.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        // Werkzeuge der Zeile "Malen" - unter ihrem Schluessel im Katalog.
        ["brush"] = "M3,21 v-4 a4,4 0 1 1 4,4 h-4 M21,3 a16,16 0 0 0 -12.8,10.2 M21,3 a16,16 0 0 1 -10.2,12.8 M10.6,9 a9,9 0 0 1 4.4,4.4",
        ["rectangle"] = "M5,5 h14 a2,2 0 0 1 2,2 v10 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-10 a2,2 0 0 1 2,-2 z",
        ["ellipse"] = "M3,12 a9,6 0 1 0 18,0 a9,6 0 1 0 -18,0",
        ["lasso"] = "M4.028,13.252 c-0.657,-0.972 -1.028,-2.078 -1.028,-3.252 c0,-3.866 4.03,-7 9,-7 s9,3.134 9,7 " +
                    "s-4.03,7 -9,7 c-1.913,0 -3.686,-0.464 -5.144,-1.255 M3,15 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M5,17 c0,1.42 0.316,2.805 1,4",
        ["stamp"] = "M21,17.85 h-18 c0,-4.05 1.421,-4.05 3.79,-4.05 c5.21,0 1.21,-4.59 1.21,-6.8 a4,4 0 1 1 8,0 " +
                    "c0,2.21 -4,6.8 1.21,6.8 c2.369,0 3.79,0 3.79,4.05 z M5,21 h14",
        ["S_MaskPainted"] = "M5,3 h14 a2,2 0 0 1 2,2 v14 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-14 a2,2 0 0 1 2,-2 z " +
                            "M7,15 c2,-4 4,2 6,-2 s3,-3 4,-1",
        ["mask-edit"] ="M12,6 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M4,6 h8 M16,6 h4 M6,12 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 " +
                        "M4,12 h2 M10,12 h10 M15,18 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M4,18 h11 M19,18 h1",

        // Die Einstellungsleiste.
        ["round"] = "M3,12 a9,9 0 1 0 18,0 a9,9 0 1 0 -18,0",
        ["square"] = "M5,3 h14 a2,2 0 0 1 2,2 v14 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-14 a2,2 0 0 1 2,-2 z",
        ["follow"] = "M4.05,11 a8,8 0 1 1 0.5,4 M4.05,20 v-5 h5",
        ["object"] = "M12,3 l8,4.5 v9 l-8,4.5 l-8,-4.5 v-9 z M12,12 l8,-4.5 M12,12 v9 M12,12 l-8,-4.5",
        ["edge"] = "M4,8 v-2 a2,2 0 0 1 2,-2 h2 M4,16 v2 a2,2 0 0 0 2,2 h2 M16,4 h2 a2,2 0 0 1 2,2 v2 M16,20 h2 a2,2 0 0 0 2,-2 v-2",

        // Die Regler der Einstellungsleiste (Entscheidung 10) - der Name steht im Hinweis.
        ["size"] = "M16,4 h4 v4 M14,10 l6,-6 M8,20 h-4 v-4 M4,20 l6,-6",
        ["hardness"] = "M3,12 a9,9 0 1 0 18,0 a9,9 0 1 0 -18,0 M8,12 a4,4 0 1 0 8,0 a4,4 0 1 0 -8,0",
        ["flow"] = "M7.502,19.423 c2.602,2.105 6.395,2.105 8.996,0 c2.602,-2.105 3.262,-5.708 1.566,-8.546 l-4.89,-7.26 " +
                   "c-0.42,-0.625 -1.287,-0.803 -1.936,-0.397 a1.376,1.376 0 0 0 -0.41,0.397 l-4.893,7.26 c-1.695,2.838 -1.035,6.441 1.567,8.546 z",
        ["opacity"] = "M5,4 h14 a1,1 0 0 1 1,1 v14 a1,1 0 0 1 -1,1 h-14 a1,1 0 0 1 -1,-1 v-14 a1,1 0 0 1 1,-1 M4,14 l10,-10 M4,20 l16,-16 M10,20 l10,-10",
        ["spacing"] = "M3,12 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M10,12 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M17,12 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0",
        ["angle"] = "M21,19 H3 L12,4 M14,19 a11,11 0 0 0 -5.34,-9.43",
        ["aspect"] = "M7,8 l-4,4 l4,4 M17,8 l4,4 l-4,4 M3,12 h18",
        ["squish"] = "M12,3 l9,9 l-9,9 l-9,-9 z",
        ["tolerance"] = "M3,12 c3,-5 6,5 9,0 s6,5 9,0",
        ["jitter"] = "M18,4 l3,3 l-3,3 M18,20 l3,-3 l-3,-3 M3,7 h3 a5,5 0 0 1 5,5 a5,5 0 0 0 5,5 h5 M21,7 h-5 a4.98,4.98 0 0 0 -3,1 M9,16 a4.98,4.98 0 0 1 -3,1 h-3",
        ["scatter"] = "M4,7 a1.5,1.5 0 1 0 3,0 a1.5,1.5 0 1 0 -3,0 M14,5 a1.5,1.5 0 1 0 3,0 a1.5,1.5 0 1 0 -3,0 " +
                      "M9,13 a1.5,1.5 0 1 0 3,0 a1.5,1.5 0 1 0 -3,0 M17,15 a1.5,1.5 0 1 0 3,0 a1.5,1.5 0 1 0 -3,0 M5,18 a1.5,1.5 0 1 0 3,0 a1.5,1.5 0 1 0 -3,0",
        ["pressure"] = "M4,20 h4 l10.5,-10.5 a2.828,2.828 0 1 0 -4,-4 l-10.5,10.5 v4 M13.5,6.5 l4,4",
        ["history"] = "M12,8 v4 l2,2 M3.05,11 a9,9 0 1 1 0.5,4 M3,20 v-5 h5",

        // Die Ebenenliste.
        ["plus"] = "M12,5 v14 M5,12 h14",
        ["adjustment"] = "M4,8 h4 v4 h-4 z M6,4 v4 M6,12 v8 M10,14 h4 v4 h-4 z M12,4 v10 M12,18 v2 M16,5 h4 v4 h-4 z M18,4 v1 M18,9 v11",

        // Original zeigen und Speichern - oben rechts.
        ["eye"] = "M10,12 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M21,12 c-2.4,4 -5.4,6 -9,6 c-3.6,0 -6.6,-2 -9,-6 c2.4,-4 5.4,-6 9,-6 c3.6,0 6.6,2 9,6",
        ["save"] = "M6,4 h10 l4,4 v10 a2,2 0 0 1 -2,2 h-12 a2,2 0 0 1 -2,-2 v-12 a2,2 0 0 1 2,-2 M10,14 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M14,4 v4 h-6 v-4",

        // Bild oeffnen.
        ["open"] = "M15,8 h0.01 M3,6 a3,3 0 0 1 3,-3 h12 a3,3 0 0 1 3,3 v12 a3,3 0 0 1 -3,3 h-12 a3,3 0 0 1 -3,-3 z " +
                   "M3,16 l5,-5 c0.928,-0.893 2.072,-0.893 3,0 l5,5 M14,14 l1,-1 c0.928,-0.893 2.072,-0.893 3,0 l3,3",
    };

    private static readonly Dictionary<string, Geometry> Parsed = new(StringComparer.Ordinal);

    /// <summary>Die Schluessel, fuer die es ein Zeichen gibt - fuer die Probe.</summary>
    public static IReadOnlyCollection<string> Keys => Paths.Keys;

    /// <summary>Ob es fuer diesen Schluessel ein gezeichnetes Zeichen gibt.</summary>
    public static bool Has(string? key) => key is not null && Paths.ContainsKey(key);

    /// <summary>Das Zeichen auf dem Raster von 24 - geteilt und eingefroren.</summary>
    public static Geometry Of(string key)
    {
        if (Parsed.TryGetValue(key, out var geometry)) return geometry;

        geometry = Geometry.Parse(Paths[key]);
        geometry.Freeze();
        Parsed[key] = geometry;

        return geometry;
    }

    /// <summary>
    /// Das Zeichen als Form in der gewuenschten Groesse. Die Farbe kommt von der Schrift des
    /// Knopfs, in dem es steht - gedrueckt, gedaempft oder unter der Maus wie sein Text.
    /// </summary>
    public static Path Shape(string key, double size = 14)
    {
        var scaled = new GeometryGroup { Transform = new ScaleTransform(size / 24, size / 24) };
        scaled.Children.Add(Of(key));
        scaled.Freeze();

        var path = new Path
        {
            Data = scaled,
            Width = size,
            Height = size,
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = false,
        };

        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
                        new System.Windows.Data.Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = System.Windows.Data.RelativeSource.Self });

        return path;
    }
}

/// <summary>Ein Eintrag eines Auswahlfelds mit Zeichen und Namen - als Daten, nicht als Element.</summary>
public sealed record IconChoice(string Icon, string Text);

/// <summary>
/// Zeichen und Name nebeneinander - der Inhalt eines Chips, wie im Entwurf. Ohne Namen nur das
/// Zeichen. Der Name ist auch der Name fuer Bildschirmleser, damit ein Knopf mit diesem Inhalt
/// nicht stumm ist.
/// </summary>
public sealed class IconLabel : StackPanel
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(IconLabel),
                                    new PropertyMetadata(null, (d, _) => ((IconLabel)d).Build()));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(IconLabel),
                                    new PropertyMetadata(null, (d, _) => ((IconLabel)d).Build()));

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    public IconLabel()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Der Schluessel des Zeichens in <see cref="Icons"/>.</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Der Name daneben. Leer: nur das Zeichen.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Die Kantenlaenge des Zeichens.</summary>
    public double IconSize { get; set; } = 14;

    private void Build()
    {
        Children.Clear();

        if (Icons.Has(Icon)) Children.Add(Icons.Shape(Icon!, IconSize));

        if (!string.IsNullOrEmpty(Text))
        {
            _text.Text = Text;
            _text.Margin = new Thickness(Children.Count > 0 ? 5 : 0, 0, 0, 0);
            Children.Add(_text);
        }

        AutomationProperties.SetName(this, Text ?? "");
    }
}
