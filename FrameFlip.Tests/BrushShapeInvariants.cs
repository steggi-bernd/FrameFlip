using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
        TheDirectionHoldsSteady();
        TheDiamondReachesFurther();
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
        Check.That(old.Version == 0 && old.Squish == 0f, "und in der Rechnung, mit der er gemalt wurde - Fassung 0, ohne Karo");

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

    /// <summary>
    /// Folgt der Winkel dem Strich, bleibt die Spitze ruhig: Ein zittriger Strich nach unten
    /// malt gleichmaessig, eine Umkehr dreht die Spitze nicht, und der erste Tupfer wartet
    /// auf eine Richtung, die mehr ist als ein Mausschritt.
    /// </summary>
    private static void TheDirectionHoldsSteady()
    {
        Check.Group("Pinsel: die Richtung bleibt ruhig");

        // Eckig, 1:8, Radius 32 - halbe Laenge 8 Maskenpunkte, halbe Breite einer.
        PaintedMask Draw(int version, float angle, IEnumerable<(float X, float Y)> path, out PaintStroke stroke)
        {
            var mask = PaintedMask.For(ImageWidth, ImageHeight);
            stroke = new PaintStroke
            {
                Radius = 32, Hardness = 1f, Flow = 1f, Shape = BrushShape.Square, Aspect = 8f,
                Angle = angle, Follow = true, Version = version,
            };

            bool first = true;
            foreach (var (x, y) in path)
            {
                if (first) stroke.Begin(mask, x, y);
                else stroke.To(mask, x, y);
                first = false;
            }

            stroke.Finish(mask);
            return mask;
        }

        // Nach unten, Bildpunkt fuer Bildpunkt, mit einem Zittern von einem Punkt zur Seite -
        // so meldet eine Maus einen langsamen Strich.
        var shaky = Enumerable.Range(0, 121).Select(i => (120f + (i % 2), 20f + i)).ToList();

        // Wie breit die Spur im mittleren Teil ist: der aeusserste bemalte Maskenpunkt links
        // und rechts der Mitte, ueber die Zeilen 12 bis 28.
        int Widest(PaintedMask mask)
        {
            var cover = mask.Cover();
            int widest = 0;

            for (int row = 12; row <= 28; row++)
                for (int col = 0; col < Cols; col++)
                    if (cover[row * Cols + col] > 0) widest = Math.Max(widest, Math.Abs(col - 30));

            return widest;
        }

        var before = Draw(0, 0f, shaky, out _);
        var steady = Draw(1, 0f, shaky, out var along);

        Check.That(Widest(before) >= 4, "vorher (Fassung 0): das Zittern dreht die Spitze schraeg, die Spur franst aus", $"{Widest(before)}");
        Check.That(Widest(steady) <= 2, "jetzt: 0 Grad liegt laengs zum Weg - eine schmale, gleichmaessige Spur", $"{Widest(steady)}");
        Check.That(Math.Abs(along.TipAngle - 90f) < 3f, "und der Ring zeigt die Spitze senkrecht, wie sie gerade malt", $"{along.TipAngle:0.#}");

        // 90 Grad liegt quer: breit, und in jeder Zeile gleich breit.
        var wide = Draw(1, 90f, shaky, out _);
        var cover = wide.Cover();
        bool even = Enumerable.Range(12, 17).All(row => cover[row * Cols + 30 - 7] == 255 && cover[row * Cols + 30 + 7] == 255);
        Check.That(even, "90 Grad liegt quer: in jeder Zeile die volle Breite der Spitze");

        // Hin und zurueck: Die Umkehr dreht die Spitze nicht.
        var back = shaky.Concat(Enumerable.Range(0, 100).Select(i => (120f + (i % 2), 140f - i))).ToList();
        Check.That(Widest(Draw(1, 0f, back, out _)) <= 2, "hin und zurueck: die Umkehr dreht die Spitze nicht");

        // Ein Mausschritt ist noch keine Richtung.
        var mask = PaintedMask.For(ImageWidth, ImageHeight);
        var waiting = new PaintStroke { Radius = 32, Hardness = 1f, Flow = 1f, Shape = BrushShape.Square, Aspect = 8f, Follow = true, Version = 1 };
        waiting.Begin(mask, 120, 20);
        bool none = waiting.To(mask, 121, 21).IsEmpty && mask.Cover().All(b => b == 0);
        waiting.To(mask, 120, 60);

        Check.That(none && mask.Cover()[5 * Cols + 30] == 255 && mask.Cover()[5 * Cols + 34] == 0,
                   "der erste Tupfer wartet auf eine Richtung - und liegt dann laengs");
    }

    /// <summary>Das Karo: zwei Ecken auseinander, zwei aufeinander zu - die Spitze kommt weiter hinaus.</summary>
    private static void TheDiamondReachesFurther()
    {
        Check.Group("Pinsel: Karo");

        byte At(PaintedMask m, int col, int row) => m.Cover()[row * Cols + col];

        PaintedMask Dab(BrushShape shape, float squish)
        {
            var mask = PaintedMask.For(ImageWidth, ImageHeight);
            mask.Stamp(120, 80, 40, 1f, 1f, 1f, 1f, new BrushTip(shape, 1f, 0f, squish), null);
            return mask;
        }

        // Mitte (30, 20), Radius 10 Maskenpunkte. Voll gezogen reicht die lange Diagonale
        // 14 Punkte weit, die kurze 2.
        var square = Dab(BrushShape.Square, 0f);
        var diamond = Dab(BrushShape.Square, 1f);

        Check.That(At(diamond, 42, 32) == 255 && At(square, 42, 32) == 0 && At(diamond, 18, 8) == 255,
                   "die lange Diagonale reicht ueber die Ecke des Quadrats hinaus - auf beiden Seiten");
        Check.That(At(square, 38, 12) == 255 && At(diamond, 38, 12) == 0,
                   "die kurze kommt herein: wo das Quadrat eine Ecke hat, ist beim Karo nichts");

        var zero = Dab(BrushShape.Square, 0f);
        var half = Dab(BrushShape.Square, 0.5f);
        Check.That(zero.Cover().AsSpan().SequenceEqual(square.Cover()) && !half.Cover().AsSpan().SequenceEqual(square.Cover()),
                   "ohne Karo das Quadrat wie bisher, halb gezogen etwas dazwischen");

        Check.That(Dab(BrushShape.Round, 1f).Cover().AsSpan().SequenceEqual(Dab(BrushShape.Round, 0f).Cover()),
                   "die runde Spitze kennt kein Karo");

        var stroke = new PaintStroke { Radius = 40, Shape = BrushShape.Square, Squish = 1f };
        Check.That(Math.Abs(stroke.Reach - 40f * MathF.Sqrt(2f) * 1.4f) < 0.01f, "die Reichweite waechst mit der langen Diagonale", $"{stroke.Reach:0.##}");

        var corners = new BrushTip(BrushShape.Square, 1f, 0f, 1f).Corners(10, 10);
        Check.That(Math.Abs(corners[0].X - 14) < 1e-4 && Math.Abs(corners[0].Y - 14) < 1e-4 &&
                   Math.Abs(corners[1].X + 2) < 1e-4 && Math.Abs(corners[1].Y - 2) < 1e-4,
                   "der Ring zeigt dieselben Ecken, die gemalt werden",
                   string.Join(" ", corners.Select(c => $"({c.X:0.######}, {c.Y:0.######})")));
    }

    /// <summary>Alle Flaechen einer Zeichnung, auch in Gruppen - fuer die Probe der Vorschau.</summary>
    private static IEnumerable<Geometry> Geometries(Drawing? drawing)
    {
        switch (drawing)
        {
            case GeometryDrawing g when g.Geometry is not null:
                yield return g.Geometry;
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                    foreach (var inner in Geometries(child))
                        yield return inner;
                break;
        }
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
                Squish = random.Next(2) == 0 ? 0f : (float)random.NextDouble(),
                Follow = random.Next(2) == 0,
                Limit = random.Next(3) == 0 ? packed : null,
                Version = random.Next(2),
            };

            // Grosse Spruenge und kleine Zitterer - die Glaettung hat mit beiden zu tun.
            var single = PaintedMask.For(ImageWidth, ImageHeight);
            float px = random.Next(ImageWidth), py = random.Next(ImageHeight);
            stroke.Begin(single, px, py);

            for (int i = 0; i < 12; i++)
            {
                if (i % 3 == 0) { px = random.Next(ImageWidth); py = random.Next(ImageHeight); }
                else { px += random.Next(-3, 4); py += random.Next(-3, 4); }

                stroke.To(single, px, py);
            }

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

            // Das Karo: nur fuer die eckige Spitze, und es kommt beim Pinsel an.
            var squish = (Slider)properties.FindName("BrushSquishSlider");
            var square = (ToggleButton)properties.FindName("BrushSquareToggle");
            squish.Value = 0.5;
            Check.That(squish.IsEnabled && placement.BrushSquish == 0.5f, "eckig: das Karo laesst sich stellen und kommt beim Pinsel an");

            var made = placement.NewStroke(10, 10);
            Check.That(made.Version == PaintStroke.CurrentVersion && made.Squish == 0.5f && made.Shape == BrushShape.Square,
                       "ein neuer Strich traegt die heutige Rechnung und das Karo");

            square.IsChecked = false;
            Check.That(!squish.IsEnabled && placement.NewStroke(10, 10).Squish == 0f, "rund: der Regler ruht, und kein Karo im Strich");
            square.IsChecked = true;

            // Der Umriss am Zeiger: eckig, 90 Grad, 1:8 - hoch und schmal, nicht rund.
            ((Slider)properties.FindName("BrushAngleSlider")).Value = 90;
            ((Slider)properties.FindName("BrushAspectSlider")).Value = 8;
            squish.Value = 0;

            var outline = placement.TipOutline(new System.Windows.Point(100, 100), 40, placement.BrushAngle);
            var box = outline.Bounds;
            Check.That(outline is PathGeometry && Math.Abs(box.Height - 80) < 0.5 && Math.Abs(box.Width - 10) < 0.5,
                       "der Umriss steht hochkant, 80 hoch und 10 breit", $"{box.Width:0.#} x {box.Height:0.#}");

            // Die Vorschau beim Ziehen von Haerte und Abstand zeigt dieselbe Spitze - keinen Kreis.
            bool Ellipses(PlacementAdorner.BrushKnob knob)
            {
                placement.BeginKnob(new System.Windows.Point(200, 200), knob);
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                    typeof(PlacementAdorner).GetMethod("RenderKnob", flags)!.Invoke(placement, new object[] { context });
                placement.EndKnob();

                return Geometries(visual.Drawing).Any(g => g is EllipseGeometry);
            }

            Check.That(!Ellipses(PlacementAdorner.BrushKnob.Hardness) && !Ellipses(PlacementAdorner.BrushKnob.Spacing),
                       "Haerte und Abstand zeigen die eckige Spitze, keinen Kreis");

            square.IsChecked = false;
            ((Slider)properties.FindName("BrushAspectSlider")).Value = 1;
            Check.That(Ellipses(PlacementAdorner.BrushKnob.Hardness), "und beim runden Pinsel den Kreis wie bisher");
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
