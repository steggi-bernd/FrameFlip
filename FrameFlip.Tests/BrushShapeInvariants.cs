using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Pinselspitzen (docs/Atelier-Werkzeugplan.md, W1): eckig und rund, gestreckt, gedreht,
/// mit dem Strich gedreht und auf ein Objekt begrenzt. Jede Form deckt, was sie verspricht,
/// und nichts daneben - und jeder Strich spielt Byte fuer Byte nach, wie er gemalt wurde.
/// </summary>
public static class BrushShapeInvariants
{
    // Ein Bild von 240 mal 160: Die Maske hat 60 mal 40 Punkte.
    private const int ImageWidth = 240, ImageHeight = 160;
    private const int Cols = ImageWidth / PaintedMask.Coarse, Rows = ImageHeight / PaintedMask.Coarse;

    public static void Run()
    {
        OldStrokesStayRound();
        ShapesCoverWhatTheyShould();
        TheAngleFollowsTheStroke();
        ALimitKeepsTheStrokeOut();
        EveryStrokeReplaysExactly();
        ThePageBindsToTheObject();
    }

    /// <summary>Ein Strich aus der Zeit davor liest sich als rund und ungebunden - und rechnet wie vorher.</summary>
    private static void OldStrokesStayRound()
    {
        Check.Group("Pinsel: alte Striche bleiben rund");

        var old = JsonSerializer.Deserialize<PaintStroke>(
            """{"Radius":20,"Hardness":0.5,"Flow":0.7,"Opacity":1,"Spacing":0.25,"Erase":false,"Path":[30,30,90,70]}""",
            AtelierProjectStore.Options)!;

        Check.That(old.Shape == BrushShape.Round && old.Aspect == 1f && old.Angle == 0f && !old.Follow && old.Limit is null && old.Reach == old.Radius,
                   "ohne die neuen Felder: rund, ungestreckt, ungedreht, ungebunden");

        // Derselbe Weg mit dem alten Tupfer von Hand gesetzt ergibt dasselbe Raster.
        var replayed = PaintedMask.For(ImageWidth, ImageHeight);
        old.Replay(replayed);

        var byHand = PaintedMask.For(ImageWidth, ImageHeight);
        var fresh = new PaintStroke { Radius = 20, Hardness = 0.5f, Flow = 0.7f, Opacity = 1f, Spacing = 0.25f };
        fresh.Begin(byHand, 30, 30);
        fresh.To(byHand, 90, 70);

        Check.That(replayed.Cover().AsSpan().SequenceEqual(byHand.Cover()), "und rechnen wie vorher");
    }

    /// <summary>Eckig deckt die Ecken, gedreht reicht weiter, gestreckt wird schmal.</summary>
    private static void ShapesCoverWhatTheyShould()
    {
        Check.Group("Pinsel: Formen");

        byte At(PaintedMask m, int col, int row) => m.Cover()[row * Cols + col];

        PaintedMask Dab(BrushShape shape, float angle = 0f, float aspect = 1f)
        {
            var mask = PaintedMask.For(ImageWidth, ImageHeight);
            mask.Stamp(120, 80, 40, 1f, 1f, 1f, 1f, new BrushTip(shape, aspect, angle), null);
            return mask;
        }

        // Radius 40 Bildpunkte sind 10 Maskenpunkte. Die Mitte liegt bei (30, 20).
        var round = Dab(BrushShape.Round);
        var square = Dab(BrushShape.Square);

        Check.That(At(square, 37, 27) == 255 && At(round, 37, 27) == 0,
                   "eckig deckt die Ecke, rund nicht", $"eckig {At(square, 37, 27)}, rund {At(round, 37, 27)}");
        Check.That(At(square, 42, 20) == 0 && At(square, 30, 32) == 0, "und nichts ausserhalb der Seiten");

        var turned = Dab(BrushShape.Square, angle: 45f);
        Check.That(At(turned, 42, 20) == 255 && At(square, 42, 20) == 0,
                   "um 45 Grad gedreht reicht die Ecke weiter hinaus, auf der Achse");

        var flat = Dab(BrushShape.Square, aspect: 4f);
        Check.That(At(flat, 38, 20) == 255 && At(flat, 30, 24) == 0 && At(flat, 30, 22) == 255,
                   "gestreckt: lang in der Breite, schmal in der Hoehe");

        var ellipse = Dab(BrushShape.Round, aspect: 4f);
        Check.That(At(ellipse, 38, 20) == 255 && At(ellipse, 30, 24) == 0,
                   "rund und gestreckt ist eine Ellipse");
    }

    /// <summary>Mit dem Strich gedreht legt sich eine flache Spitze in die Richtung des Weges.</summary>
    private static void TheAngleFollowsTheStroke()
    {
        Check.Group("Pinsel: der Winkel folgt dem Strich");

        PaintedMask Vertical(bool follow)
        {
            var mask = PaintedMask.For(ImageWidth, ImageHeight);
            var stroke = new PaintStroke { Radius = 32, Hardness = 1f, Flow = 1f, Shape = BrushShape.Square, Aspect = 4f, Follow = follow };
            stroke.Begin(mask, 120, 20);
            stroke.To(mask, 120, 140);
            stroke.Finish(mask);
            return mask;
        }

        // Halbe Breite 8 Maskenpunkte, halbe Hoehe 2. Senkrecht gezogen: mit dem Strich
        // gedreht bleibt er schmal, sonst liegt die Spitze quer und wird breit.
        var follows = Vertical(follow: true);
        var fixedAngle = Vertical(follow: false);

        byte Side(PaintedMask m) => m.Cover()[20 * Cols + 36];

        Check.That(Side(follows) == 0 && Side(fixedAngle) == 255,
                   "senkrecht gezogen: mit dem Strich schmal, ohne breit", $"{Side(follows)} / {Side(fixedAngle)}");

        // Der erste Tupfer liegt schon in der Richtung des Weges, nicht quer.
        Check.That(follows.Cover()[5 * Cols + 36] == 0, "auch der erste Tupfer liegt in der Richtung des Weges");

        // Ein Klick ohne Bewegung malt trotzdem - beim Loslassen.
        var click = PaintedMask.For(ImageWidth, ImageHeight);
        var tap = new PaintStroke { Radius = 20, Hardness = 1f, Flow = 1f, Shape = BrushShape.Square, Follow = true };
        bool nothingYet = tap.Begin(click, 120, 80).IsEmpty && click.Cover().All(b => b == 0);
        tap.Finish(click);

        Check.That(nothingYet && click.Cover()[20 * Cols + 30] == 255, "ein Klick ohne Bewegung setzt seinen Tupfer beim Loslassen");
    }

    /// <summary>Ein begrenzter Strich malt nur, so weit die Begrenzung reicht.</summary>
    private static void ALimitKeepsTheStrokeOut()
    {
        Check.Group("Pinsel: begrenzt auf ein Objekt");

        // Links deckt das "Objekt" ganz, rechts nicht, in der Mitte zur Haelfte.
        var limit = new byte[Cols * Rows];
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
                limit[row * Cols + col] = col < 29 ? (byte)255 : col < 31 ? (byte)128 : (byte)0;

        var mask = PaintedMask.For(ImageWidth, ImageHeight);
        var stroke = new PaintStroke { Radius = 24, Hardness = 1f, Flow = 1f, Limit = PaintedMask.Pack(limit) };
        stroke.Begin(mask, 10, 80);
        stroke.To(mask, 230, 80);

        var cover = mask.Cover();
        byte left = cover[20 * Cols + 10], edge = cover[20 * Cols + 30], right = cover[20 * Cols + 50];

        Check.That(left == 255 && right == 0, "links, auf dem Objekt, gemalt - rechts nichts", $"{left} / {right}");
        Check.That(edge is > 0 and < 255, "an der weichen Kante zum Teil", $"{edge}");
        Check.That(stroke.Reach == stroke.Radius, "die Begrenzung aendert die Reichweite nicht");
    }

    /// <summary>
    /// Zufaellige Striche mit jeder Form, jedem Winkel, mit und ohne Begrenzung: gemalt und
    /// nachgespielt - auch aus Text gelesen und im Maskenverlauf - Byte fuer Byte dasselbe.
    /// </summary>
    private static void EveryStrokeReplaysExactly()
    {
        Check.Group("Pinsel: jeder Strich spielt genau nach");

        var random = new Random(41);
        var limit = new byte[Cols * Rows];
        for (int i = 0; i < limit.Length; i++) limit[i] = (byte)(i % Cols < 35 ? 255 : random.Next(256));
        string packed = PaintedMask.Pack(limit);

        bool live = true, text = true;
        var paint = PaintedMask.For(ImageWidth, ImageHeight);
        var history = MaskHistory.Start(paint);
        var truth = new List<byte[]> { paint.Cover().ToArray() };

        for (int round = 0; round < 40; round++)
        {
            var stroke = new PaintStroke
            {
                Radius = 6 + random.Next(30),
                Hardness = (float)random.NextDouble(),
                Flow = 0.3f + 0.7f * (float)random.NextDouble(),
                Opacity = 0.5f + 0.5f * (float)random.NextDouble(),
                Spacing = 0.1f + (float)random.NextDouble(),
                Erase = random.Next(6) == 0,
                Shape = random.Next(2) == 0 ? BrushShape.Round : BrushShape.Square,
                Aspect = 1f + 5f * (float)random.NextDouble(),
                Angle = random.Next(180),
                Follow = random.Next(2) == 0,
                Limit = random.Next(3) == 0 ? packed : null,
            };

            var single = PaintedMask.For(ImageWidth, ImageHeight);
            stroke.Begin(single, random.Next(ImageWidth), random.Next(ImageHeight));
            for (int i = 0; i < 4; i++) stroke.To(single, random.Next(ImageWidth), random.Next(ImageHeight));
            stroke.Finish(single);

            var again = PaintedMask.For(ImageWidth, ImageHeight);
            stroke.Replay(again);
            live &= again.Cover().AsSpan().SequenceEqual(single.Cover());

            var read = JsonSerializer.Deserialize<PaintStroke>(JsonSerializer.Serialize(stroke, AtelierProjectStore.Options), AtelierProjectStore.Options)!;
            var fromText = PaintedMask.For(ImageWidth, ImageHeight);
            read.Replay(fromText);
            text &= fromText.Cover().AsSpan().SequenceEqual(single.Cover());

            // Und auf der gemeinsamen Maske fuer den Verlauf.
            stroke.Replay(paint);
            paint.Keep();
            if (history.Record(paint, stroke)) truth.Add(paint.Cover().ToArray());
        }

        bool states = true;
        for (int i = 0; i < history.States.Count; i++)
            states &= history.CoverOf(i).AsSpan().SequenceEqual(truth[truth.Count - history.States.Count + i]);

        Check.That(live, "vierzig zufaellige Striche aller Formen: nachgespielt Byte fuer Byte");
        Check.That(text, "auch aus Text gelesen");
        Check.That(history.States.Count > 2 && states, "und der Maskenverlauf gibt jeden Stand genau zurueck", $"{history.States.Count} Staende");
    }

    /// <summary>
    /// Auf der Seite, an der kleinen Kryptomattendatei: Mit „nur aufs Objekt“ fragt der
    /// Pinsel die Deckung des Objekts unter dem Ansatz - innen voll, aussen nichts.
    /// </summary>
    private static void ThePageBindsToTheObject()
    {
        Check.Group("Pinsel: auf der Seite an ein Objekt gebunden");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-pinsel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));

        string path = Path.Combine(folder, "render_0001.exr");
        File.WriteAllBytes(path, CryptoSample.Bytes());

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1000, Height = 700, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();
            page.Open(path);

            var size = (TextBlock)page.FindName("SourceText");
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < end && size.Text.Length == 0)
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var frame = (Imaging.FloatFrame)typeof(AtelierPage).GetField("_frame", flags)!.GetValue(page)!;

            // Ein Punkt auf einem Objekt.
            string? limit = null;
            for (int y = 0; y < frame.Height && limit is null; y += 3)
                for (int x = 0; x < frame.Width && limit is null; x += 3)
                    limit = page.ObjectLimitAt(x, y);

            int cols = Math.Max(1, frame.Width / PaintedMask.Coarse), rows = Math.Max(1, frame.Height / PaintedMask.Coarse);
            var cover = limit is null ? null : PaintedMask.Unpack(limit, cols * rows);

            Check.That(cover is not null && cover.Any(b => b == 255) && cover.Any(b => b == 0),
                       "die Deckung eines Objekts in der Groesse der Maske - innen voll, aussen nichts");
            Check.That(page.ObjectLimitAt(-5, -5) is null, "neben dem Bild: ungebunden");

            // Die Regler kommen beim Pinsel an.
            var properties = (PropertiesPanel)page.FindName("Properties");
            ((ToggleButton)properties.FindName("BrushSquareToggle")).IsChecked = true;
            ((ToggleButton)properties.FindName("BrushObjectToggle")).IsChecked = true;
            ((Slider)properties.FindName("BrushAngleSlider")).Value = 30;

            var placement = (PlacementAdorner)page.FindName("Placement");
            Check.That(placement.BrushShape == BrushShape.Square && placement.BrushAngle == 30f && placement.LimitWanted is not null,
                       "eckig, 30 Grad und die Frage nach dem Objekt kommen beim Pinsel an");

            // Umschalt und Rad drehen die Spitze, der Regler zieht nach.
            placement.StepAngle(1);
            Check.That(placement.BrushAngle == 45f && ((Slider)properties.FindName("BrushAngleSlider")).Value == 45,
                       "ein Schritt am Rad dreht um 15 Grad - und der Regler zieht nach");
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
