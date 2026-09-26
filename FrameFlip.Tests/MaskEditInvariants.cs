using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Maske bearbeiten (docs/Atelier-Werkzeugplan.md, W1): umkehren, fuellen, leeren, weiche
/// Kante, ausweiten, schrumpfen. Jede Bearbeitung tut, was sie sagt, spielt als Strich genau
/// nach - und auf der Seite ist sie ein Schritt fuer Strg+Z und einer im Maskenverlauf.
/// </summary>
public static class MaskEditInvariants
{
    // 240 mal 160 Bildpunkte, 60 mal 40 Maskenpunkte.
    private const int ImageWidth = 240, ImageHeight = 160;
    private const int Cols = ImageWidth / PaintedMask.Coarse, Rows = ImageHeight / PaintedMask.Coarse;

    public static void Run()
    {
        TheEditsDoWhatTheySay();
        RefiningFollowsTheImage();
        EditsReplayExactly();
        ThePageTakesEditsAsSteps();
    }

    /// <summary>Ein Quadrat von 10 mal 10 Maskenpunkten (20..29, 10..19) - der Pruefling.</summary>
    private static PaintedMask Square()
    {
        var cover = new byte[Cols * Rows];
        for (int y = 10; y < 20; y++)
            for (int x = 20; x < 30; x++)
                cover[y * Cols + x] = 255;

        return PaintedMask.FromCover(Cols, Rows, cover);
    }

    private static byte At(PaintedMask m, int x, int y) => m.Cover()[y * Cols + x];

    private static void TheEditsDoWhatTheySay()
    {
        Check.Group("Maske bearbeiten: was die Schritte tun");

        var invert = Square();
        invert.Apply(PaintEdit.Invert, 0);
        Check.That(At(invert, 25, 15) == 0 && At(invert, 5, 5) == 255, "umkehren: innen leer, aussen voll");
        invert.Apply(PaintEdit.Invert, 0);
        Check.That(invert.Cover().AsSpan().SequenceEqual(Square().Cover()), "zweimal umkehren ist wieder das Original");

        var fill = Square();
        fill.Apply(PaintEdit.Fill, 0);
        var clear = Square();
        clear.Apply(PaintEdit.Clear, 0);
        Check.That(fill.Cover().All(b => b == 255) && clear.Cover().All(b => b == 0), "fuellen macht alles voll, leeren alles leer");

        // Ausweiten um 8 Bildpunkte = 2 Maskenpunkte: die Seiten ruecken zwei hinaus, die Ecken werden rund.
        var grow = Square();
        grow.Apply(PaintEdit.Grow, 8);
        Check.That(At(grow, 31, 15) == 255 && At(grow, 32, 15) == 0 && At(grow, 30, 20) == 255 && At(grow, 31, 21) == 0,
                   "ausweiten: zwei Punkte hinaus, die Ecke rund");

        // Schrumpfen um 8 Bildpunkte: zwei Punkte vom Rand herein.
        var shrink = Square();
        shrink.Apply(PaintEdit.Shrink, 8);
        Check.That(At(shrink, 21, 15) == 0 && At(shrink, 22, 15) == 255 && At(shrink, 27, 15) == 255 && At(shrink, 28, 15) == 0,
                   "schrumpfen: zwei Punkte herein, auf beiden Seiten");

        // Weiche Kante: aus der harten Kante wird ein Verlauf, der nie ansteigt.
        var soft = Square();
        soft.Apply(PaintEdit.Feather, 10);
        var row = Enumerable.Range(25, 15).Select(x => At(soft, x, 15)).ToArray();
        bool falling = row.Zip(row.Skip(1)).All(p => p.First >= p.Second);
        bool ramp = row.Any(b => b is > 10 and < 245);
        Check.That(falling && ramp && At(soft, 25, 15) > 200 && At(soft, 50, 15) == 0,
                   "weiche Kante: ein stetiger Verlauf nach aussen, die Mitte bleibt, weit draussen nichts",
                   string.Join(" ", row));

        // Eine volle Maske laeuft am Bildrand nicht aus.
        var full = Square();
        full.Apply(PaintEdit.Fill, 0);
        full.Apply(PaintEdit.Feather, 25);
        full.Apply(PaintEdit.Shrink, 8);
        Check.That(full.Cover().All(b => b == 255), "am Bildrand: eine volle Maske bleibt voll - der Rand zaehlt nicht als leer");
    }

    /// <summary>
    /// Kanten verfeinern: Eine grob gemalte Maske ragt vier Punkte ueber eine Kante im Bild
    /// hinaus - danach nicht mehr. Wo das Bild eben ist, bleibt sie, wie sie war.
    /// </summary>
    private static void RefiningFollowsTheImage()
    {
        Check.Group("Maske bearbeiten: Kanten verfeinern");

        // Das Bild: links dunkel, ab Spalte 30 hell. Die Maske: bis Spalte 33 voll.
        var guide = new float[Cols * Rows];
        var rough = new byte[Cols * Rows];
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Cols; x++)
            {
                guide[y * Cols + x] = x < 30 ? 0.1f : 0.9f;
                rough[y * Cols + x] = (byte)(x < 34 ? 255 : 0);
            }
        }

        var refined = MaskRefine.Refine(rough, guide, Cols, Rows, radius: 4);
        byte At(int x) => refined[20 * Cols + x];

        Check.That(At(31) < 60 && At(33) < 60, "was ueber die Kante hinausragte, zieht sich zurueck", $"{At(31)} / {At(33)}");
        Check.That(At(20) > 240 && At(28) > 200, "diesseits der Kante bleibt die Maske voll", $"{At(20)} / {At(28)}");
        Check.That(At(45) < 5, "weit jenseits bleibt sie leer");

        // Umgekehrt: Die Maske hoert drei Punkte vor der Kante auf - und waechst bis an sie.
        var shy = new byte[Cols * Rows];
        for (int i = 0; i < shy.Length; i++) shy[i] = (byte)(i % Cols < 27 ? 255 : 0);
        var grown = MaskRefine.Refine(shy, guide, Cols, Rows, radius: 4);
        Check.That(grown[20 * Cols + 28] > 240 && grown[20 * Cols + 29] > 240 && grown[20 * Cols + 30] < 15,
                   "hoert sie davor auf, waechst sie bis an die Kante", $"{grown[20 * Cols + 28]} {grown[20 * Cols + 29]} {grown[20 * Cols + 30]}");

        // Ohne Kante im Bild sagt es nichts - die Maske bleibt, wie sie war.
        var flat = Enumerable.Repeat(0.5f, Cols * Rows).ToArray();
        Check.That(MaskRefine.Refine(rough, flat, Cols, Rows, radius: 4).AsSpan().SequenceEqual(rough),
                   "ohne Kante im Bild bleibt die Maske unveraendert");

        // Die Fuehrung aus einem Bild: je Maskenpunkt die gestauchte Helligkeit seines Feldes.
        var frame = new Imaging.FloatFrame
        {
            Width = 8, Height = 4,
            R = Enumerable.Range(0, 32).Select(i => i % 8 < 4 ? 0f : 3f).ToArray(),
            G = Enumerable.Range(0, 32).Select(i => i % 8 < 4 ? 0f : 3f).ToArray(),
            B = Enumerable.Range(0, 32).Select(i => i % 8 < 4 ? 0f : 3f).ToArray(),
        };
        var fromFrame = MaskRefine.Guide(frame, 2, 1, PaintedMask.Coarse);
        Check.That(fromFrame[0] == 0f && Math.Abs(fromFrame[1] - MathF.Sqrt(0.75f)) < 1e-4f,
                   "die Fuehrung staucht die Helligkeit - ein Glanzlicht erschlaegt die Schatten nicht", $"{fromFrame[0]} / {fromFrame[1]}");
    }

    private static void EditsReplayExactly()
    {
        Check.Group("Maske bearbeiten: als Strich genau nachgespielt");

        var paint = Square();
        var history = MaskHistory.Start(paint);
        var states = new List<byte[]> { paint.Cover().ToArray() };

        // Eine Fuehrung fuer das Verfeinern - das Ergebnis reist im Strich mit.
        var guide = Enumerable.Range(0, Cols * Rows).Select(i => i % Cols < 26 ? 0.2f : 0.8f).ToArray();

        foreach (var (edit, amount) in new[]
                 {
                     (PaintEdit.Grow, 5f), (PaintEdit.Feather, 10f), (PaintEdit.Refine, 16f), (PaintEdit.Invert, 0f),
                     (PaintEdit.Shrink, 2f), (PaintEdit.Feather, 25f), (PaintEdit.Invert, 0f),
                 })
        {
            var stroke = edit == PaintEdit.Refine
                ? new PaintStroke
                  {
                      Edit = edit, Amount = amount, Version = PaintStroke.CurrentVersion,
                      Result = PaintedMask.Pack(MaskRefine.Refine(paint.Cover(), guide, Cols, Rows, 4)),
                  }
                : new PaintStroke { Edit = edit, Amount = amount, Version = PaintStroke.CurrentVersion };
            var read = JsonSerializer.Deserialize<PaintStroke>(JsonSerializer.Serialize(stroke, AtelierProjectStore.Options), AtelierProjectStore.Options)!;

            read.Replay(paint);
            paint.Keep();
            if (history.Record(paint, stroke)) states.Add(paint.Cover().ToArray());
        }

        bool exact = true;
        for (int i = 0; i < history.States.Count; i++)
            exact &= history.CoverOf(i).AsSpan().SequenceEqual(states[states.Count - history.States.Count + i]);

        Check.That(exact && history.States.Count > 3, "aus Text gelesen und im Maskenverlauf: jeder Stand Byte fuer Byte", $"{history.States.Count} Staende");

        var old = JsonSerializer.Deserialize<PaintStroke>("""{"Radius":20,"Path":[1,1]}""", AtelierProjectStore.Options)!;
        Check.That(old.Edit == PaintEdit.None && old.Amount == 0f, "ein alter Strich ist keine Bearbeitung");

        var lost = Square();
        new PaintStroke { Edit = PaintEdit.Refine, Amount = 8 }.Replay(lost);
        Check.That(lost.Cover().AsSpan().SequenceEqual(Square().Cover()), "Verfeinern ohne gespeichertes Ergebnis raet nicht - es laesst die Maske stehen");
    }

    /// <summary>
    /// Auf der Seite, im Knotenmodus mit einer gemalten Maske: Umkehren ist ein Schritt fuer
    /// Strg+Z und einer im Verlauf; was nichts aendert, ist keiner.
    /// </summary>
    private static void ThePageTakesEditsAsSteps()
    {
        Check.Group("Maske bearbeiten: auf der Seite");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-maske-bearbeiten-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "bild.png");

        const int W = 320, H = 200;
        var bytes = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            // Eine Kante mitten im Bild: links dunkel, ab x = 160 hell.
            bytes[i * 4] = (byte)(i % W < 160 ? 20 : 230);
            bytes[i * 4 + 1] = (byte)(i % W < 160 ? 30 : 220);
            bytes[i * 4 + 2] = (byte)(i / W * 255 / H);
            bytes[i * 4 + 3] = 255;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(
            W, H, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bytes, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1100, Height = 750, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        try
        {
            window.Show();
            page.Open(path);

            var size = (TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            page.ConvertToNodes();
            Pump(TimeSpan.FromSeconds(0.4), () => false);

            var paint = (PaintedMask)typeof(AtelierPage).GetMethod("MakeNodeMask", flags)!.Invoke(page, null)!;
            ((ToolColumn)page.FindName("MouseTools")).Select(AtelierTool.Brush, notify: true);
            Pump(TimeSpan.FromSeconds(0.3), () => false);

            var properties = (PropertiesPanel)page.FindName("Properties");
            Check.That(properties.EditButton.Visibility == Visibility.Visible,
                       "der Pinsel auf einer gemalten Maske: der Knopf zum Bearbeiten ist da");

            var mask = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);

            // Ein Strich, damit es etwas umzukehren gibt.
            var stroke = new PaintStroke { Radius = 30, Flow = 1f, Hardness = 1f };
            stroke.Begin(paint, 40, 100);
            stroke.To(paint, 280, 100);
            paint.Keep();
            typeof(PlacementAdorner).GetProperty("LastStroke", flags)!.SetValue(page.FindName("Placement"), stroke);
            typeof(AtelierPage).GetMethod("OnPainted", flags)!.Invoke(page, new object[] { false });
            Pump(TimeSpan.FromSeconds(0.3), () => false);

            var before = paint.Cover().ToArray();
            int rowsBefore = page.MaskHistoryRows(mask, out _)?.Count ?? 0;

            Check.That(page.EditMask(mask, PaintEdit.Invert), "umkehren wirkt");
            Pump(TimeSpan.FromSeconds(0.3), () => false);

            var current = mask.Mask.PaintFor(0)!;
            bool inverted = current.Cover().Zip(before).All(p => p.First == 255 - p.Second);
            int rowsAfter = page.MaskHistoryRows(mask, out _)?.Count ?? 0;

            Check.That(inverted, "die Maske auf der Seite ist umgekehrt");
            Check.That(rowsAfter == rowsBefore + 1, "und der Maskenverlauf hat einen Stand mehr", $"{rowsBefore} -> {rowsAfter}");

            // Strg+Z: zurueck zum Stand davor.
            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(0.3), () => false);
            var undone = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted).Mask.PaintFor(0)!;
            Check.That(undone.Cover().AsSpan().SequenceEqual(before), "Strg+Z nimmt das Umkehren zurueck");

            // Kanten verfeinern: ein grobes Rechteck, das 16 Bildpunkte ueber die Kante bei x = 160
            // hinausragt - danach nicht mehr. Ein Schritt im Verlauf und fuer Strg+Z.
            var refineMask = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
            var roughPaint = refineMask.Mask.PaintFor(0)!;
            var box = new PaintStroke { Area = PaintArea.Rectangle, Path = PaintStroke.RectanglePath(40, 20, 176, 80) };
            box.Fill(roughPaint);
            roughPaint.Keep();
            typeof(PlacementAdorner).GetProperty("LastStroke", flags)!.SetValue(page.FindName("Placement"), box);
            typeof(AtelierPage).GetMethod("OnPainted", flags)!.Invoke(page, new object[] { false });
            Pump(TimeSpan.FromSeconds(0.3), () => false);

            var beforeRefine = refineMask.Mask.PaintFor(0)!.Cover().ToArray();
            int cols = roughPaint.Width;
            int rowsBeforeRefine = page.MaskHistoryRows(refineMask, out _)?.Count ?? 0;

            Check.That(page.EditMask(refineMask, PaintEdit.Refine, 16), "Kanten verfeinern wirkt auf der Seite");
            Pump(TimeSpan.FromSeconds(0.3), () => false);

            var snapped = refineMask.Mask.PaintFor(0)!.Cover();
            Check.That(beforeRefine[12 * cols + 42] == 255 && snapped[12 * cols + 42] < 40 && snapped[12 * cols + 30] == 255,
                       "das Rechteck zieht sich an die Kante im Bild zurueck", $"{snapped[12 * cols + 42]} / {snapped[12 * cols + 30]}");
            Check.That((page.MaskHistoryRows(refineMask, out _)?.Count ?? 0) == rowsBeforeRefine + 1,
                       "und steht im Maskenverlauf");

            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(0.3), () => false);
            Check.That(page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted)
                           .Mask.PaintFor(0)!.Cover().AsSpan().SequenceEqual(beforeRefine),
                       "Strg+Z nimmt das Verfeinern zurueck");

            // Leeren an einer leeren Maske aendert nichts - und ist dann kein Schritt.
            var fresh = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
            page.EditMask(fresh, PaintEdit.Clear);
            Check.That(!page.EditMask(fresh, PaintEdit.Clear), "was nichts aendert, ist kein Schritt");
        }
        finally
        {
            window.Close();
            Pump(TimeSpan.FromSeconds(0.2), () => false);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static bool Pump(TimeSpan timeout, Func<bool> until)
    {
        var end = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < end)
        {
            if (until()) return true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(5);
        }

        return until();
    }
}
