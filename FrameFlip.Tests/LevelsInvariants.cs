using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Nodes;
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
        AutoAndPipettesCompute();
        AutoReadsWhatArrives();
    }

    /// <summary>W2b: Auto und Pipetten als reine Rechnung auf einer Stichprobe.</summary>
    private static void AutoAndPipettesCompute()
    {
        Check.Group("Tonwert: Auto und Pipetten rechnen");

        // Eine flaue Stichprobe mit Farbstich: Rot 0,2 bis 0,6, Gruen 0,3 bis 0,7, Blau 0,4 bis 0,8.
        var rgb = new float[3000];
        for (int i = 0; i < 1000; i++)
        {
            float s = i / 999f;
            rgb[i * 3] = 0.2f + 0.4f * s;
            rgb[i * 3 + 1] = 0.3f + 0.4f * s;
            rgb[i * 3 + 2] = 0.4f + 0.4f * s;
        }

        var levels = new LevelsTool();
        LevelsAuto.Apply(levels, rgb, LevelsAutoKind.Levels);
        Check.That(Math.Abs(levels.Red.InBlack - 0.2f) < 0.005f && Math.Abs(levels.Red.InWhite - 0.6f) < 0.005f &&
                   Math.Abs(levels.Blue.InBlack - 0.4f) < 0.005f && Math.Abs(levels.Blue.InWhite - 0.8f) < 0.005f &&
                   levels.Master.IsNeutral,
                   "Auto: Schwarz und Weiss je Kanal an seinen Grenzen, gemeinsam neutral",
                   $"R {levels.Red.InBlack:0.000}-{levels.Red.InWhite:0.000}, B {levels.Blue.InBlack:0.000}-{levels.Blue.InWhite:0.000}");

        var contrast = new LevelsTool { Red = new LevelsChannel { Gamma = 2 } };
        LevelsAuto.Apply(contrast, rgb, LevelsAutoKind.Contrast);
        Check.That(Math.Abs(contrast.Master.InBlack - 0.2f) < 0.005f && Math.Abs(contrast.Master.InWhite - 0.8f) < 0.005f &&
                   contrast.Red.IsNeutral,
                   "Kontrast: gemeinsam von der dunkelsten bis zur hellsten Stelle aller Kanaele, die Kanaele neutral");

        var colour = new LevelsTool();
        LevelsAuto.Apply(colour, rgb, LevelsAutoKind.Colour);
        colour.Prepare();
        double[] means = new double[3];
        for (int i = 0; i < 1000; i++)
        {
            float r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
            colour.Apply(ref r, ref g, ref b);
            means[0] += r;
            means[1] += g;
            means[2] += b;
        }

        Check.That(Math.Abs(means[0] - means[1]) / 1000 < 0.01 && Math.Abs(means[1] - means[2]) / 1000 < 0.01,
                   "Farbe: danach liegen die Kanaele im Mittel gleich - der Stich ist weg");

        var flat = new LevelsTool();
        LevelsAuto.Apply(flat, new float[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f }, LevelsAutoKind.Levels);
        Check.That(flat.IsNeutral, "ein Bild aus einem einzigen Ton: nichts zu strecken, alles bleibt");

        var picked = new LevelsTool();
        LevelsAuto.Pick(picked, LevelsPickKind.Black, 0.1f, 0.15f, 0.2f);
        LevelsAuto.Pick(picked, LevelsPickKind.White, 0.8f, 0.85f, 0.9f);
        Check.That(Math.Abs(picked.Green.InBlack - 0.15f) < 1e-5 && Math.Abs(picked.Blue.InWhite - 0.9f) < 1e-5,
                   "Pipetten Schwarz und Weiss: der angeklickte Ton wird je Kanal der Punkt");

        LevelsAuto.Pick(picked, LevelsPickKind.Gray, 0.4f, 0.5f, 0.6f);
        float pr = 0.4f, pg = 0.5f, pb = 0.6f;
        picked.Apply(ref pr, ref pg, ref pb);
        Check.That(Math.Abs(pr - pg) < 0.01f && Math.Abs(pg - pb) < 0.01f,
                   "Pipette Grau: der angeklickte Ton wird grau", $"{pr:0.000} {pg:0.000} {pb:0.000}");
    }

    /// <summary>
    /// W2b auf der Seite: Auto misst, was beim Tonwert ANKOMMT - zweimal gedrueckt dasselbe, und
    /// eine Kurve dahinter aendert nichts. Die Pipette setzt den Punkt und gibt die Maus zurueck.
    /// Im Stapel und im Knotenmodus.
    /// </summary>
    private static void AutoReadsWhatArrives()
    {
        Check.Group("Tonwert: Auto liest, was ankommt");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-tonwert-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "flau.png");

        // Flau und mit Stich: Rot 60..140, Gruen 80..160, Blau 100..180 - von links nach rechts.
        const int W = 96, H = 40;
        var pixels = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                int ramp = x * 80 / (W - 1);
                pixels[i] = (byte)(100 + ramp);
                pixels[i + 1] = (byte)(80 + ramp);
                pixels[i + 2] = (byte)(60 + ramp);
                pixels[i + 3] = 255;
            }
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1200, Height = 800, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        void Pump(double seconds = 0.3)
        {
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(5);
            }
        }

        try
        {
            window.Show();
            page.Open(path);

            var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < end && ((System.Windows.Controls.TextBlock)page.FindName("SourceText")).Text.Length == 0) Pump(0.05);
            Pump();

            var panel = (GradingPanel)page.FindName("Tools");
            var column = (ToolColumn)page.FindName("MouseTools");
            var tool = panel.Levels;

            Check.That(page.AutoLevels(tool, LevelsAutoKind.Levels), "im Stapel: Auto misst");
            float black = tool.Red.InBlack, white = tool.Red.InWhite;

            Check.That(Math.Abs(black - 60 / 255f) < 0.02f && Math.Abs(white - 140 / 255f) < 0.02f,
                       "Rot: Schwarz bei 60, Weiss bei 140 - dort, wo der Kanal anfaengt und aufhoert",
                       $"{black * 255:0} bis {white * 255:0}");

            page.AutoLevels(tool, LevelsAutoKind.Levels);
            Check.That(tool.Red.InBlack == black && tool.Red.InWhite == white,
                       "ein zweites Auto liefert dasselbe - gemessen wird vor dem Tonwert, nicht im fertigen Bild");

            // Eine kraeftige Kurve NACH dem Tonwert darf Auto nicht verschieben.
            panel.Stack.Tools.OfType<CurvesTool>().First().Master =
                new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5f, 0.85f), new CurvePoint(1, 1) });
            panel.LevelsChangedOutside();
            Pump();

            page.AutoLevels(tool, LevelsAutoKind.Levels);
            Check.That(Math.Abs(tool.Red.InBlack - black) < 1e-5 && Math.Abs(tool.Red.InWhite - white) < 1e-5,
                       "eine Kurve hinter dem Tonwert aendert Auto nicht");

            // Die Pipette Schwarz: waehlen, ins Bild klicken - der Punkt sitzt, die Maus geht zurueck.
            var blackPick = (ToggleButton)panel.FindName("LevelsPickBlack");
            blackPick.IsChecked = true;
            Pump(0.1);

            Check.That(page.LevelsPicking && column.Tool == AtelierTool.Pick, "die Pipette wartet - die Maus ist die Pipette");

            page.LevelsPickAt(W - 1, H / 2);
            Check.That(Math.Abs(tool.Red.InBlack - 140 / 255f) < 0.02f && !page.LevelsPicking &&
                       blackPick.IsChecked != true && column.Tool != AtelierTool.Pick,
                       "ein Klick: der Ton dort wird Schwarz, die Pipette ist aus, die Maus wieder, was sie war",
                       $"{tool.Red.InBlack * 255:0}");

            // Im Knotenmodus: ein Tonwertknoten, Auto misst an seinem Eingang.
            page.ConvertToNodes();
            Pump(0.5);
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Levels"));
            Pump(0.3);

            var node = page.Graph!.Nodes.OfType<PointToolNode>().FirstOrDefault(n => n.Tool is LevelsTool);
            var nodeTool = node?.Tool as LevelsTool;

            Check.That(nodeTool is not null && page.AutoLevels(nodeTool, LevelsAutoKind.Levels) && !nodeTool.IsNeutral,
                       "im Knotenmodus: ein Tonwertknoten, Auto misst an seinem Eingang");

            if (nodeTool is not null)
            {
                float nodeBlack = nodeTool.Green.InBlack;
                page.AutoLevels(nodeTool, LevelsAutoKind.Levels);
                Check.That(Math.Abs(nodeTool.Green.InBlack - nodeBlack) < 1e-5, "und auch dort liefert ein zweites Auto dasselbe");
            }
        }
        finally
        {
            window.Close();
            Pump(0.2);
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
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
