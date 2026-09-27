using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Die Tonwertkorrektur (W2 im Werkzeugplan): die Rechnung, das Speichern, ihr Platz im
/// Stapel und die Anfasser unter dem Histogramm.
/// </summary>
public static class LevelsInvariants
{
    public static void Run()
    {
        TheMathIsLevels();
        ItIsSavedAndCopied();
        ItStandsBeforeTheCurves();
        TheHandlesSetThePoints();
    }

    private static void TheMathIsLevels()
    {
        Check.Group("Tonwert: die Rechnung");

        var neutral = new LevelsTool();
        neutral.Prepare();
        float r = 0.3f, g = 1.4f, b = -0.1f;
        neutral.Apply(ref r, ref g, ref b);
        Check.That(neutral.IsNeutral && r == 0.3f && g == 1.4f && b == -0.1f,
                   "in Grundstellung aendert sich nichts - auch nicht ueber Weiss oder unter Schwarz");

        var channel = new LevelsChannel { InBlack = 0.2f, InWhite = 0.8f };
        channel.Prepare();
        Check.Near(channel.Map(0.2f), 0, 1e-5, "der Schwarzpunkt wird Schwarz");
        Check.Near(channel.Map(0.8f), 1, 1e-5, "der Weisspunkt wird Weiss");
        Check.Near(channel.Map(0.5f), 0.5, 1e-5, "dazwischen gleichmaessig gestreckt");
        Check.That(channel.Map(0.05f) == 0 && channel.Map(0.95f) == 1, "was darueber hinaus liegt, wird abgeschnitten");

        var bright = new LevelsChannel { Gamma = 2 };
        bright.Prepare();
        Check.Near(bright.Map(0.25f), 0.5, 1e-5, "Gamma 2: ein Viertel wird mittleres Grau - die Mitten hellen auf");

        foreach (float gamma in new[] { 0.3f, 0.7f, 1f, 1.5f, 4f })
        {
            float position = LevelsChannel.GrayPosition(gamma);
            var probe = new LevelsChannel { Gamma = gamma };
            probe.Prepare();

            Check.Near(probe.Map(position), 0.5, 1e-4, $"Gamma {gamma}: der Grauregler steht dort, wo mittleres Grau entsteht");
            Check.Near(LevelsChannel.GammaAt(position), gamma, 1e-3, $"Gamma {gamma}: Lage und Gamma sind einander umkehrbar");
        }

        var output = new LevelsChannel { OutBlack = 0.1f, OutWhite = 0.9f };
        output.Prepare();
        Check.That(Math.Abs(output.Map(0) - 0.1f) < 1e-5 && Math.Abs(output.Map(1) - 0.9f) < 1e-5,
                   "der Ausgang: Schwarz wird 0,1, Weiss 0,9");

        // Erst gemeinsam, dann je Kanal - wie bei den Kurven.
        var tool = new LevelsTool
        {
            Master = new LevelsChannel { InWhite = 0.5f },
            Red = new LevelsChannel { OutWhite = 0.5f },
        };
        tool.Prepare();
        float rr = 0.25f, gg = 0.25f, bb = 0.25f;
        tool.Apply(ref rr, ref gg, ref bb);
        Check.That(Math.Abs(rr - 0.25f) < 1e-5 && Math.Abs(gg - 0.5f) < 1e-5 && Math.Abs(bb - 0.5f) < 1e-5,
                   "erst die gemeinsame Einstellung, dann die je Kanal", $"{rr} {gg} {bb}");
    }

    private static void ItIsSavedAndCopied()
    {
        Check.Group("Tonwert: speichern und kopieren");

        var stack = new GradingStack();
        stack.Tools.Add(new LevelsTool
        {
            Master = new LevelsChannel { InBlack = 0.1f, Gamma = 1.3f, InWhite = 0.9f },
            Blue = new LevelsChannel { OutBlack = 0.05f },
        });

        string json = JsonSerializer.Serialize(stack);
        var back = JsonSerializer.Deserialize<GradingStack>(json);

        Check.That(json.Contains("\"levels\"") && back?.Tools.Single() is LevelsTool
                   {
                       Master: { InBlack: 0.1f, Gamma: 1.3f, InWhite: 0.9f },
                       Blue.OutBlack: 0.05f,
                   },
                   "der Tonwert kommt mit seinem Typ und seinen Werten zurueck");

        var copy = stack.Clone();
        ((LevelsTool)copy.Tools[0]).Master.InBlack = 0.4f;
        Check.That(((LevelsTool)stack.Tools[0]).Master.InBlack == 0.1f, "die Kopie ist eine eigene - das Original bleibt");
    }

    /// <summary>
    /// Der Tonwert steht vor den Kurven - auch in einem Stapel von frueher, der ihn nicht kennt.
    /// Angehaengt kaeme er hinter Kurven und LUT, und dieselbe Einstellung wirkte anders.
    /// </summary>
    private static void ItStandsBeforeTheCurves()
    {
        Check.Group("Tonwert: sein Platz im Stapel");

        var panel = new GradingPanel();

        var old = new GradingStack();
        old.Tools.Add(new WhiteBalanceTool());
        old.Tools.Add(new CurvesTool());
        old.Tools.Add(new LutTool());

        panel.Load(null, old);

        int levels = panel.Stack.Tools.FindIndex(t => t is LevelsTool);
        int curves = panel.Stack.Tools.FindIndex(t => t is CurvesTool);

        Check.That(levels >= 0 && levels < curves, "in einem Stapel von frueher: vor den Kurven eingesetzt", $"{levels} vor {curves}");

        panel.Load(null, new GradingStack());
        levels = panel.Stack.Tools.FindIndex(t => t is LevelsTool);
        curves = panel.Stack.Tools.FindIndex(t => t is CurvesTool);

        Check.That(levels >= 0 && levels < curves, "in einem frischen Stapel ebenso");
    }

    private static void TheHandlesSetThePoints()
    {
        Check.Group("Tonwert: die Anfasser");

        var channel = new LevelsChannel();
        var editor = new LevelsEditor { Channel = channel, Width = 262, Height = 150 };
        editor.Measure(new Size(262, 150));
        editor.Arrange(new Rect(0, 0, 262, 150));
        editor.UpdateLayout();

        // 7 Punkte Rand, 248 nutzbar: x = 7 + Wert * 248.
        double At(double value) => 7 + value * 248;

        editor.Drag(LevelsEditor.Handle.InBlack, At(0.2));
        editor.Drag(LevelsEditor.Handle.InWhite, At(0.8));
        Check.That(Math.Abs(channel.InBlack - 0.2f) < 0.01f && Math.Abs(channel.InWhite - 0.8f) < 0.01f,
                   "Schwarz und Weiss stehen, wo sie hingezogen wurden", $"{channel.InBlack} {channel.InWhite}");

        editor.Drag(LevelsEditor.Handle.InBlack, At(0.95));
        Check.That(channel.InBlack < channel.InWhite, "Schwarz laesst sich nicht hinter Weiss ziehen");

        editor.Drag(LevelsEditor.Handle.InBlack, At(0.2));
        editor.Drag(LevelsEditor.Handle.Gray, At(0.2 + 0.6 * 0.25));
        Check.Near(channel.Gamma, 2, 0.05, "der Grauregler auf einem Viertel zwischen Schwarz und Weiss: Gamma 2");

        Check.That(editor.HandleAt(new System.Windows.Point(At(0.8), 110)) == LevelsEditor.Handle.InWhite,
                   "unter dem Weisspunkt greift man den Weisspunkt");

        editor.Reset(LevelsEditor.Handle.Gray);
        Check.That(channel.Gamma == 1, "Doppelklick auf Grau: wieder Gamma 1");

        // Zeichnen - mit Verteilung, ohne, ganz klein.
        foreach (var (what, histogram, w, h) in new (string, int[]?, int, int)[]
                 {
                     ("mit Verteilung", Enumerable.Range(0, 256).Select(i => i * (255 - i)).ToArray(), 262, 150),
                     ("ohne Verteilung", null, 262, 150),
                     ("mit leerer Verteilung", new int[256], 262, 150),
                     ("sehr schmal", null, 12, 150),
                 })
        {
            try
            {
                var drawn = new LevelsEditor { Channel = channel, Background = histogram, Width = w, Height = h };
                drawn.Measure(new Size(w, h));
                drawn.Arrange(new Rect(0, 0, w, h));
                drawn.UpdateLayout();

                var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                target.Render(drawn);
                Check.That(target.PixelWidth == w, $"{what}: gezeichnet");
            }
            catch (Exception ex)
            {
                Check.That(false, $"{what}: gezeichnet", $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
