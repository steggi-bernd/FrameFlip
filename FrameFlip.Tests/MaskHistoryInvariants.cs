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
using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Der Verlauf einer gemalten Maske (docs/Projekte-und-Masken.md, Abschnitt 3.5). Der
/// Massstab ist jedes Mal die Maske selbst: Ein Stand muss Byte fuer Byte die Deckung
/// ergeben, die sie hatte, als er entstand - ob aus einem Schnappschuss oder aus
/// nachgespielten Strichen, ob frisch oder gespeichert und wieder gelesen.
/// </summary>
public static class MaskHistoryInvariants
{
    // Wie ein Bild von 480 mal 320: Die Maske ist vier Mal groeber.
    private const int ImageWidth = 480, ImageHeight = 320;

    public static void Run()
    {
        ReplayMatchesPainting();
        StatesComeWithArea();
        StatesComeBack();
        ForeignChangesMakeSnapshots();
        TheOldestFallAway();
        HistoriesSurviveSaving();
        RecordingIsCheap();
        ThePageRestores();
    }

    /// <summary>Die Voraussetzung: Ein Strich nachgespielt ergibt dasselbe Raster wie gemalt.</summary>
    private static void ReplayMatchesPainting()
    {
        Check.Group("Maskenverlauf: Nachspielen ist Malen");

        var random = new Random(4);
        bool same = true;

        for (int round = 0; round < 20; round++)
        {
            var live = PaintedMask.For(ImageWidth, ImageHeight);
            var stroke = Stroke(random, live);

            var again = PaintedMask.For(ImageWidth, ImageHeight);
            stroke.Replay(again);

            same &= live.Cover().AsSpan().SequenceEqual(again.Cover());
        }

        Check.That(same, "zwanzig zufaellige Striche: nachgespielt Byte fuer Byte dieselbe Maske");
    }

    /// <summary>
    /// Ein neuer Stand erst ab einem Zehntel der Flaeche - und dieselbe Stelle zehnmal
    /// uebermalt zaehlt einmal.
    /// </summary>
    private static void StatesComeWithArea()
    {
        Check.Group("Maskenverlauf: ein Stand je Zehntel der Flaeche");

        var paint = PaintedMask.For(ImageWidth, ImageHeight);
        var history = MaskHistory.Start(paint);

        // Immer wieder derselbe kleine Fleck.
        bool made = false;

        for (int i = 0; i < 12; i++)
        {
            var dab = new PaintStroke { Radius = 20, Flow = 0.3f, Hardness = 0.5f };
            dab.Begin(paint, 100, 100);
            dab.To(paint, 120, 104);
            paint.Keep();

            made |= history.Record(paint, dab);
        }

        Check.That(!made && history.States.Count == 1 && history.Pending is > 0 and < MaskHistory.Threshold,
                   "zwoelfmal derselbe Fleck: kein neuer Stand - er zaehlt einmal", $"{history.Pending:P1}");

        // Breite Striche quer ueber das Bild.
        int strokes = 0;

        while (!made && strokes < 40)
        {
            var wide = new PaintStroke { Radius = 30, Flow = 0.8f };
            wide.Begin(paint, 0, 40 + strokes * 36);
            wide.To(paint, ImageWidth, 40 + strokes * 36);
            paint.Keep();

            made = history.Record(paint, wide);
            strokes++;
        }

        var state = history.States[^1];

        Check.That(made && history.States.Count == 2 && state.Changed >= MaskHistory.Threshold && history.Pending == 0,
                   "breite Striche: ein neuer Stand, sobald ein Zehntel der Flaeche anders ist - danach zaehlt es von vorn",
                   $"{strokes} Striche, {state.Changed:P1}");
        Check.That(state.Snapshot is null && state.Strokes!.Count == 12 + strokes,
                   "der Stand haelt nur die Striche fest, nicht die Maske");
    }

    /// <summary>
    /// Viele Staende - jeder fuenfte ein Schnappschuss, die anderen Striche - und jeder ergibt
    /// genau die Maske, die sie hatte, als er entstand.
    /// </summary>
    private static void StatesComeBack()
    {
        Check.Group("Maskenverlauf: jeder Stand kommt Byte fuer Byte zurueck");

        var paint = PaintedMask.For(ImageWidth, ImageHeight);
        var history = MaskHistory.Start(paint);
        var truth = new List<byte[]> { paint.Cover().ToArray() };
        var random = new Random(9);

        while (history.States.Count < 12)
        {
            var stroke = Stroke(random, paint);
            paint.Keep();

            if (history.Record(paint, stroke)) truth.Add(paint.Cover().ToArray());
        }

        bool all = true;
        for (int i = 0; i < history.States.Count; i++) all &= history.CoverOf(i).AsSpan().SequenceEqual(truth[i]);

        var snapshots = history.States.Select((s, i) => (s, i)).Where(p => p.s.Snapshot is not null).Select(p => p.i).ToList();

        Check.That(all, "zwoelf Staende: jeder ist die Maske von damals");
        Check.That(snapshots.SequenceEqual(new[] { 0, 5, 10 }), "Schnappschuesse beim ersten und dann bei jedem fuenften",
                   string.Join(", ", snapshots));
    }

    /// <summary>
    /// Aendert sich die Maske anders als durch einen Strich - Strg+Z, ein Strich im Stapel -,
    /// laesst sich das nicht nachspielen: Der naechste Stand ist ein Schnappschuss.
    /// </summary>
    private static void ForeignChangesMakeSnapshots()
    {
        Check.Group("Maskenverlauf: fremde Aenderungen");

        var paint = PaintedMask.For(ImageWidth, ImageHeight);
        var history = MaskHistory.Start(paint);
        var random = new Random(12);

        // Ein erster Stand aus Strichen.
        while (history.States.Count < 2)
        {
            var stroke = Stroke(random, paint);
            paint.Keep();
            history.Record(paint, stroke);
        }

        // Wie Strg+Z: Die Maske springt auf etwas anderes, ohne Strich.
        var other = PaintedMask.For(ImageWidth, ImageHeight);
        for (int i = 0; i < 6; i++) Stroke(random, other);
        paint.Replace(other.Cover());

        byte[] truth = Array.Empty<byte>();

        while (history.States.Count < 3)
        {
            var stroke = Stroke(random, paint);
            paint.Keep();

            if (history.Record(paint, stroke)) truth = paint.Cover().ToArray();
        }

        Check.That(history.States[2].Snapshot is not null && history.CoverOf(2).AsSpan().SequenceEqual(truth),
                   "danach ist der naechste Stand ein Schnappschuss - und stimmt");

        // Ein Stand, zu dem zurueckgekehrt wurde: Von dort geht es mit einem Schnappschuss weiter.
        paint.Replace(history.CoverOf(1));
        history.Restored(paint);

        Check.That(history.Pending == 0, "nach dem Wiederherstellen zaehlt es von vorn");

        while (history.States.Count < 4)
        {
            var stroke = Stroke(random, paint);
            paint.Keep();

            if (history.Record(paint, stroke)) truth = paint.Cover().ToArray();
        }

        Check.That(history.States[3].Snapshot is not null && history.CoverOf(3).AsSpan().SequenceEqual(truth),
                   "und der erste Stand danach ist ein Schnappschuss");

        // Festhalten, was seit dem letzten Stand dazukam.
        var little = new PaintStroke { Radius = 12, Flow = 0.5f };
        little.Begin(paint, 50, 50);
        paint.Keep();
        history.Record(paint, little);

        int before = history.States.Count;
        Check.That(history.Checkpoint(paint) && history.States.Count == before + 1 &&
                   history.CoverOf(before).AsSpan().SequenceEqual(paint.Cover()),
                   "Festhalten macht auch aus wenig einen Stand - vor dem Wiederherstellen");
        Check.That(!history.Checkpoint(paint), "und ohne Aenderung keinen");
    }

    /// <summary>Hoechstens zwanzig Staende: Der aelteste faellt weg, und wer hinter ihm steht, bleibt richtig.</summary>
    private static void TheOldestFallAway()
    {
        Check.Group("Maskenverlauf: hoechstens zwanzig Staende");

        var paint = PaintedMask.For(ImageWidth, ImageHeight);
        var history = MaskHistory.Start(paint);
        var truth = new Dictionary<int, byte[]> { [1] = paint.Cover().ToArray() };
        var random = new Random(17);

        while (history.NextNumber <= 27)
        {
            var stroke = Stroke(random, paint);
            paint.Keep();

            if (history.Record(paint, stroke)) truth[history.States[^1].Number] = paint.Cover().ToArray();
        }

        bool all = true;
        for (int i = 0; i < history.States.Count; i++) all &= history.CoverOf(i).AsSpan().SequenceEqual(truth[history.States[i].Number]);

        Check.That(history.States.Count == MaskHistory.Depth && history.States[0].Number == 8 && history.States[^1].Number == 27,
                   "27 Staende: die ersten sieben sind weg, die Nummern bleiben",
                   $"{history.States.Count}: {history.States[0].Number} bis {history.States[^1].Number}");
        Check.That(history.States[0].Snapshot is not null, "der aelteste verbliebene ist ein Schnappschuss");
        Check.That(all, "und jeder Stand ist noch die Maske von damals");
    }

    /// <summary>Gespeichert und wieder gelesen - im Ordner neben dem Projekt - ergibt jeder Stand dasselbe.</summary>
    private static void HistoriesSurviveSaving()
    {
        Check.Group("Maskenverlauf: gespeichert und wieder gelesen");

        var paint = PaintedMask.For(ImageWidth, ImageHeight);
        var history = MaskHistory.Start(paint);
        var random = new Random(23);

        while (history.States.Count < 8)
        {
            var stroke = Stroke(random, paint);
            paint.Keep();
            history.Record(paint, stroke);
        }

        var read = JsonSerializer.Deserialize<MaskHistory>(JsonSerializer.SerializeToUtf8Bytes(history, AtelierProjectStore.Options),
                                                           AtelierProjectStore.Options)!;

        bool all = read.States.Count == history.States.Count;
        for (int i = 0; i < history.States.Count && all; i++) all &= read.CoverOf(i).AsSpan().SequenceEqual(history.CoverOf(i));

        Check.That(all, "als Text gespeichert und gelesen: jeder Stand wie vorher");

        // Die Ablage: in "verlauf" im Ordner neben der Projektdatei, am Quellordner.
        string folder = Path.Combine(Path.GetTempPath(), "frameflip-verlauf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            var key = SequenceKey.Of(Path.Combine(folder, "render_0001.exr"))!;
            var store = new AtelierProjectStore(() => Path.Combine(folder, "ersatz"));
            var mask = new LayerMask { Kind = MaskKind.Painted, Paint = PaintedMask.For(ImageWidth, ImageHeight) };

            var keeper = new MaskHistoryKeeper(store);
            keeper.Enter(key);
            keeper.Watch(mask, 1, mask.Paint);

            bool made = false;
            while (!made)
            {
                var stroke = Stroke(random, mask.Paint);
                mask.Paint.Keep();
                made = keeper.Record(mask, 1, mask.Paint, stroke);
            }

            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));

            string expected = store.SidePaths(key, Path.Combine("verlauf", mask.Id + "-alle.json")).First();
            Check.That(File.Exists(expected) && expected.StartsWith(Path.Combine(folder, "FrameFlip") + Path.DirectorySeparatorChar) &&
                       Path.GetDirectoryName(Path.GetDirectoryName(expected))!.EndsWith(AtelierProjectStore.DataExtension),
                       "der Verlauf liegt im Ordner FrameFlip neben der Projektdatei", expected);

            var again = new MaskHistoryKeeper(store);
            again.Enter(key);
            var found = again.Find(mask, 1, mask.Paint);

            Check.That(found is not null && found.States.Count == 2 && found.CoverOf(1).AsSpan().SequenceEqual(mask.Paint.Cover()),
                       "eine neue Sitzung liest ihn wieder - mit dem Stand, den die Maske hat");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Das Aufzeichnen beim Loslassen darf den Pinsel nicht bremsen - bei einer Maske in der
    /// Groesse eines 4K-Bildes, mit einem langen Strich.
    /// </summary>
    private static void RecordingIsCheap()
    {
        Check.Group("Maskenverlauf: Aufzeichnen kostet beim Loslassen wenig");

        var paint = PaintedMask.For(3840, 2160);
        var history = MaskHistory.Start(paint);

        var stroke = new PaintStroke { Radius = 60, Flow = 0.7f };
        stroke.Begin(paint, 400, 400);
        for (int i = 1; i <= 40; i++) stroke.To(paint, 400 + i * 20, 400 + i * 8);
        paint.Keep();

        // Vorbereitet ausserhalb der Messung: je Runde ein frischer Verlauf, der die Maske vor
        // dem Strich kennt, und die Maske danach. Gemessen wird nur, was beim Loslassen laeuft.
        var blank = PaintedMask.For(3840, 2160);
        var prepared = new Queue<(MaskHistory History, PaintedMask Painted)>();

        for (int i = 0; i < 12; i++)
        {
            var fresh = MaskHistory.Start(PaintedMask.FromCover(blank.Width, blank.Height, blank.Cover()));
            var painted = PaintedMask.FromCover(blank.Width, blank.Height, blank.Cover());
            stroke.Replay(painted);
            prepared.Enqueue((fresh, painted));
        }

        var best = Measure.Fastest(5, t => t[0] < 4, () =>
        {
            var (fresh, painted) = prepared.Dequeue();
            fresh.Record(painted, stroke);
        });

        Check.Timing(best[0] < 10, "ein langer Strich bei 4K: Aufzeichnen beim Loslassen unter 10 ms", $"{best[0]:F2} ms");
        Check.That(history.States.Count == 1, "(ein Strich allein macht noch keinen Stand)");
    }

    /// <summary>
    /// Auf der Seite, im Knotenmodus: Malen legt Staende an, "Maskenverlauf ..." zeigt sie, ein
    /// Klick stellt einen wieder her - ein Schritt, den Strg+Z zuruecknimmt.
    /// </summary>
    private static void ThePageRestores()
    {
        Check.Group("Maskenverlauf: auf der Seite");

        string T(string key) => Localization.Strings.T(key);

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-verlauf-seite-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "bild.png");

        const int W = 480, H = 320;
        var bytes = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            bytes[i * 4] = (byte)(i % W * 255 / W);
            bytes[i * 4 + 1] = (byte)(i / W * 255 / H);
            bytes[i * 4 + 2] = 140;
            bytes[i * 4 + 3] = 255;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(
            W, H, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bytes, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

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

            // Die erste Maske - wie beim ersten Strich - und der Pinsel darauf.
            var paint = (PaintedMask)typeof(AtelierPage).GetMethod("MakeNodeMask", flags)!.Invoke(page, null)!;
            ((ToolColumn)page.FindName("MouseTools")).Select(AtelierTool.Brush, notify: true);
            Pump(TimeSpan.FromSeconds(0.3), () => false);

            var graph = page.Graph!;
            var mask = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
            var adorner = (PlacementAdorner)page.FindName("Placement");
            var last = typeof(PlacementAdorner).GetProperty("LastStroke", flags)!;
            var painted = typeof(AtelierPage).GetMethod("OnPainted", flags)!;
            var undo = (System.Collections.IList)typeof(AtelierPage).GetField("_undo", flags)!.GetValue(page)!;

            // Striche wie mit der Maus: gemalt, festgehalten, losgelassen.
            var random = new Random(31);
            var truth = new List<byte[]> { paint.Cover().ToArray() };

            for (int i = 0; i < 60 && truth.Count < 3; i++)
            {
                var stroke = new PaintStroke { Radius = 30, Flow = 0.8f };
                int y = random.Next(H);
                stroke.Begin(paint, 0, y);
                stroke.To(paint, W, y + random.Next(-20, 20));
                paint.Keep();

                last.SetValue(adorner, stroke);
                painted.Invoke(page, new object[] { true });
                painted.Invoke(page, new object[] { false });

                var rows = page.MaskHistoryRows(mask, out _)!;
                if (rows.Count > truth.Count) truth.Add(paint.Cover().ToArray());
            }

            Check.That(truth.Count == 3, "Malen legt Staende an - je Zehntel der Flaeche einen", $"{truth.Count - 1} neue");

            page.ShowNodeMenu(mask, new Point(10, 10));
            Check.That(page.NodeMenu!.Items.Contains(T("S_MaskMenuHistory")), "im Menue einer gemalten Maske: Maskenverlauf",
                       string.Join(", ", page.NodeMenu.Items));
            page.NodeMenu.Invoke(T("S_MaskMenuHistory"));

            var popup = page.MaskHistoryMenu;
            Check.That(popup is not null && popup.Numbers.SequenceEqual(new[] { 3, 2, 1 }), "das Fenster zeigt die Staende, den neuesten oben",
                       popup is null ? null : string.Join(", ", popup.Numbers));

            var rowsNow = page.MaskHistoryRows(mask, out _)!;
            Check.That(rowsNow[0].Current && rowsNow.Skip(1).All(r => !r.Current) && rowsNow[^1].First,
                       "der neueste ist der aktuelle, der aelteste der Anfang");

            // Ein frueherer Stand, per Klick.
            byte[] before = paint.Cover().ToArray();
            int steps = undo.Count;

            popup!.Choose(2);
            Pump(TimeSpan.FromSeconds(0.2), () => false);

            var now = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted).Mask.PaintFor(0)!;

            Check.That(now.Cover().AsSpan().SequenceEqual(truth[1]) && undo.Count == steps + 1,
                       "ein Klick stellt Stand 2 wieder her - Byte fuer Byte, als ein Schritt im Verlauf der Seite");

            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(0.2), () => false);

            var back = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted).Mask.PaintFor(0)!;
            Check.That(back.Cover().AsSpan().SequenceEqual(before), "Strg+Z nimmt es zurueck");

            // Im Ordner neben dem Projekt.
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            string history = new AtelierProjectStore().SidePaths(SequenceKey.Of(path)!, Path.Combine("verlauf", mask.Mask.Id + "-alle.json")).First();
            Check.That(File.Exists(history) && history.StartsWith(Path.Combine(folder, "FrameFlip") + Path.DirectorySeparatorChar),
                       "der Verlauf liegt im Ordner FrameFlip neben den Bildern", history);
        }
        finally
        {
            window.Close();
            Pump(TimeSpan.FromSeconds(0.2), () => false);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Ein zufaelliger Strich mit weichem Rand - gemalt auf <paramref name="paint"/>.</summary>
    private static PaintStroke Stroke(Random random, PaintedMask paint)
    {
        var stroke = new PaintStroke
        {
            Radius = 8 + random.Next(40),
            Flow = 0.3f + 0.7f * (float)random.NextDouble(),
            Hardness = (float)random.NextDouble(),
            Opacity = 0.6f + 0.4f * (float)random.NextDouble(),
            Spacing = 0.1f + (float)random.NextDouble(),
            Erase = random.Next(5) == 0,
        };

        stroke.Begin(paint, random.Next(ImageWidth), random.Next(ImageHeight));
        for (int i = 0; i < 4; i++) stroke.To(paint, random.Next(ImageWidth), random.Next(ImageHeight));

        return stroke;
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
