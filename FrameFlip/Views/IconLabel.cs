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
