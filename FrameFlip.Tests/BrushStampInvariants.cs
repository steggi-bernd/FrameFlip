using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Der Stempelpinsel (docs/Atelier-Werkzeugplan.md, W1): eine Spitze aus einem Bild, je
/// Tupfer mit festem Zufall gedreht und gestreut. Die Spitze liest sich aus Helligkeit oder
/// Deckkraft, sie malt, was sie zeigt, und jeder Strich spielt Byte fuer Byte nach - auch
/// ohne die Datei, aus der sie kam.
/// </summary>
public static class BrushStampInvariants
{
    private const int ImageWidth = 240, ImageHeight = 160;
    private const int Cols = ImageWidth / PaintedMask.Coarse, Rows = ImageHeight / PaintedMask.Coarse;

    public static void Run()
    {
        TheTipReadsWhatItShows();
        TheStampPaintsItsTip();
        ChanceIsFixed();
        ThePageTakesATip();
    }

    /// <summary>BGRA-Pixel, in denen jeder Punkt aus seiner Lage berechnet wird.</summary>
    private static byte[] Bgra(int width, int height, Func<int, int, (byte Grey, byte Alpha)> at)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (grey, alpha) = at(x, y);
                int i = (y * width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = grey;
                pixels[i + 3] = alpha;
            }
        }

        return pixels;
    }

    /// <summary>Links weiss, rechts schwarz - eine Spitze, die nur mit ihrer linken Haelfte malt.</summary>
    private static StampTip HalfTip() => StampTip.FromBgra(Bgra(16, 16, (x, _) => (x < 8 ? (byte)255 : (byte)0, (byte)255)), 16, 16)!;

    private static void TheTipReadsWhatItShows()
    {
        Check.Group("Stempel: die Spitze");

        var half = HalfTip().Values()!;
        Check.That(half[5 * 16 + 2] == 255 && half[5 * 16 + 13] == 0, "ohne Transparenz: Weiss malt, Schwarz nicht");

        // Mit Transparenz traegt die Deckkraft die Form - die Farbe darunter ist gleichgueltig.
        var drop = StampTip.FromBgra(Bgra(16, 16, (x, y) => ((byte)0, (x - 8) * (x - 8) + (y - 8) * (y - 8) < 25 ? (byte)255 : (byte)0)), 16, 16)!;
        var values = drop.Values()!;
        Check.That(values[8 * 16 + 8] == 255 && values[0] == 0, "mit Transparenz: die Deckkraft, auch wenn die Farbe schwarz ist");

        var wide = StampTip.FromBgra(Bgra(512, 128, (x, _) => ((byte)(x % 2 == 0 ? 255 : 0), (byte)255)), 512, 128)!;
        Check.That(wide.Width == StampTip.MaxSide && wide.Height == 32 && Math.Abs(wide.Values()![0] - 128) <= 1,
                   "verkleinert auf 128 an der langen Seite, als Mittel ueber die Punkte", $"{wide.Width} x {wide.Height}, {wide.Values()![0]}");

        Check.That(StampTip.FromBgra(Bgra(8, 8, (_, _) => ((byte)0, (byte)255)), 8, 8) is null, "eine Spitze, die nichts malt, gibt es nicht");

        // Aus einer Datei, ueber denselben Weg wie der Knopf.
        string folder = Path.Combine(Path.GetTempPath(), "frameflip-stempel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "tropfen.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null,
                Bgra(16, 16, (x, y) => ((byte)0, (x - 8) * (x - 8) + (y - 8) * (y - 8) < 25 ? (byte)255 : (byte)0)), 16 * 4)));
            using (var file = File.Create(path)) encoder.Save(file);

            var loaded = StampTip.Load(path);
            Check.That(loaded is not null && loaded.Values()!.AsSpan().SequenceEqual(values), "aus einer PNG gelesen wie aus den Pixeln");
            Check.That(StampTip.Load(Path.Combine(folder, "fehlt.png")) is null, "eine fehlende Datei ist keine Spitze - kein Absturz");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static PaintedMask Dab(StampTip? tip, float angle, BrushShape shape = BrushShape.Stamp)
    {
        var mask = PaintedMask.For(ImageWidth, ImageHeight);
        var stroke = new PaintStroke { Radius = 40, Flow = 1f, Hardness = 1f, Shape = shape, Stamp = tip, Angle = angle };
        stroke.Begin(mask, 120, 80);
        return mask;
    }

    private static void TheStampPaintsItsTip()
    {
        Check.Group("Stempel: ein Tupfer");

        byte At(PaintedMask m, int x, int y) => m.Cover()[y * Cols + x];

        // Radius 40 = 10 Maskenpunkte, Mitte (30, 20): die Spitze links voll, rechts leer.
        var straight = Dab(HalfTip(), 0);
        Check.That(At(straight, 24, 20) == 255 && At(straight, 36, 20) == 0 && At(straight, 24, 12) == 255,
                   "der Tupfer zeigt die Spitze: links gemalt, rechts nicht");

        var turned = Dab(HalfTip(), 180);
        Check.That(At(turned, 36, 20) == 255 && At(turned, 24, 20) == 0, "um 180 Grad gedreht: umgekehrt");

        Check.That(At(straight, 18, 20) == 0 && At(straight, 30, 7) == 0, "ausserhalb des Rahmens der Spitze nichts");

        // Ohne Spitze malt der Stempel rund - wie ein Pinsel, nicht wie nichts.
        Check.That(Dab(null, 0).Cover().AsSpan().SequenceEqual(Dab(null, 0, BrushShape.Round).Cover()),
                   "ohne Spitze malt der Stempel wie der runde Pinsel");
    }

    private static void ChanceIsFixed()
    {
        Check.Group("Stempel: fester Zufall");

        PaintedMask Draw(int seed, out PaintStroke stroke)
        {
            var mask = PaintedMask.For(ImageWidth, ImageHeight);
            stroke = new PaintStroke
            {
                Radius = 20, Flow = 0.8f, Spacing = 0.6f, Shape = BrushShape.Stamp, Stamp = HalfTip(),
                Jitter = 1f, Scatter = 0.8f, Seed = seed,
            };
            stroke.Begin(mask, 20, 80);
            stroke.To(mask, 220, 70);
            stroke.To(mask, 120, 140);
            return mask;
        }

        var first = Draw(7, out var recorded);
        var same = Draw(7, out _);
        var other = Draw(8, out _);

        Check.That(first.Cover().AsSpan().SequenceEqual(same.Cover()), "dieselbe Saat: derselbe Strich");
        Check.That(!first.Cover().AsSpan().SequenceEqual(other.Cover()), "eine andere Saat: ein anderer Strich");

        var read = JsonSerializer.Deserialize<PaintStroke>(JsonSerializer.Serialize(recorded, AtelierProjectStore.Options), AtelierProjectStore.Options)!;
        var again = PaintedMask.For(ImageWidth, ImageHeight);
        read.Replay(again);

        Check.That(read.Stamp is { Width: 16, Height: 16 } && read.Seed == 7 && again.Cover().AsSpan().SequenceEqual(first.Cover()),
                   "aus Text gelesen - mit Spitze und Saat - Byte fuer Byte derselbe Strich");

        // Und im Maskenverlauf, zwischen gewoehnlichen Strichen.
        var paint = PaintedMask.For(ImageWidth, ImageHeight);
        var history = MaskHistory.Start(paint);
        var states = new List<byte[]> { paint.Cover().ToArray() };

        for (int round = 0; round < 6; round++)
        {
            var stroke = new PaintStroke
            {
                Radius = 12 + 6 * round, Flow = 0.9f, Spacing = 0.5f,
                Shape = round % 2 == 0 ? BrushShape.Stamp : BrushShape.Round,
                Stamp = round % 2 == 0 ? HalfTip() : null, Jitter = 0.7f, Scatter = 0.5f, Seed = 100 + round,
                Erase = round == 3,
            };

            stroke.Begin(paint, 30 + 25 * round, 30);
            stroke.To(paint, 200 - 20 * round, 130);
            paint.Keep();
            if (history.Record(paint, stroke)) states.Add(paint.Cover().ToArray());
        }

        bool exact = true;
        for (int i = 0; i < history.States.Count; i++)
            exact &= history.CoverOf(i).AsSpan().SequenceEqual(states[states.Count - history.States.Count + i]);

        Check.That(exact && history.States.Count > 3, "im Maskenverlauf: jeder Stand genau", $"{history.States.Count} Staende");
    }

    private static void ThePageTakesATip()
    {
        Check.Group("Stempel: auf der Seite");

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1000, Height = 700, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            var properties = (PropertiesPanel)page.FindName("Properties");
            var placement = (PlacementAdorner)page.FindName("Placement");
            var square = (ToggleButton)properties.FindName("BrushSquareToggle");
            var stamp = (ToggleButton)properties.FindName("BrushStampToggle");
            var jitter = (Slider)properties.FindName("BrushJitterSlider");

            Check.That(!jitter.IsEnabled, "ohne Stempel ruhen Zufall und Streuung");

            square.IsChecked = true;
            properties.UseStamp(HalfTip(), "halb.png");

            Check.That(stamp.IsChecked == true && square.IsChecked == false && jitter.IsEnabled,
                       "eine Spitze laden schaltet den Stempel ein - und das Eckige aus");
            Check.That(placement.BrushShape == BrushShape.Stamp && placement.BrushStamp is { Width: 16 },
                       "Spitze und Form kommen beim Pinsel an");

            var one = placement.NewStroke(10, 10);
            var two = placement.NewStroke(10, 10);
            Check.That(one.Stamp is not null && one.Jitter == 0.5f && one.Scatter == 0.25f && one.Seed != two.Seed,
                       "jeder neue Strich traegt die Spitze und eine eigene Saat");

            var outline = placement.TipOutline(new System.Windows.Point(100, 100), 40, 0);
            Check.That(outline is PathGeometry && Math.Abs(outline.Bounds.Width - 80) < 0.5,
                       "der Ring zeigt den Rahmen der Spitze");

            square.IsChecked = true;
            Check.That(stamp.IsChecked == false && placement.NewStroke(10, 10).Stamp is null,
                       "zurueck zum Eckigen: kein Stempel im Strich");
        }
        finally
        {
            window.Close();
        }
    }
}
