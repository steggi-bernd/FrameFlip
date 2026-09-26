using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Das Mausrad im Atelier und der Abstand der Pinseltupfer: Zoom um den Zeiger mit
/// Einrasten, Strg und Rad fuer den Abstand, und ein Pinselzug, der nicht davon
/// abhaengt, wie oft die Maus meldet.
/// </summary>
public static class WheelAndSpacingInvariants
{
    public static void Run()
    {
        TheStepsStopAtTheLandmarks();
        TheStrokeIgnoresTheMouseRate();
        TheStrokeReplaysExactly();
        ThePageZoomsAroundThePointer();
    }

    private static void TheStepsStopAtTheLandmarks()
    {
        Check.Group("Mausrad: Stufen des Zooms");

        double fit = 0.4;

        Check.Near(ZoomSteps.Next(fit, 1, fit), fit * ZoomSteps.Factor, 1e-9, "eine Raste hinein ist ein Faktor");
        Check.Near(ZoomSteps.Next(0.9, 1, fit), 1.0, 1e-9, "ein Schritt ueber 100 % haelt bei 100 %");
        Check.Near(ZoomSteps.Next(1.1, -1, fit), 1.0, 1e-9, "auch von oben");
        Check.Near(ZoomSteps.Next(0.45, -1, fit), fit, 1e-9, "heraus haelt es an der Einpassung");
        Check.Near(ZoomSteps.Next(fit, -3, fit), fit, 1e-9, "und geht nicht darunter");
        Check.Near(ZoomSteps.Next(7.5, 2, fit), ZoomSteps.Max, 1e-9, "hoechstens 800 %");

        // Liegen 100 % und die Einpassung beide im Schritt, haelt es an der naeheren.
        Check.Near(ZoomSteps.Next(0.8, 5, 0.9), 0.9, 1e-9, "zwei Stufen im Schritt: die erste zaehlt");

        // Ein kleines Bild, das eingepasst groesser als 100 % steht: 100 % liegt unter der
        // Einpassung und ist mit dem Rad nicht zu erreichen, die Einpassung ist das Ende.
        Check.Near(ZoomSteps.Next(2.0, -1, 2.0), 2.0, 1e-9, "ueber 100 % eingepasst: die Einpassung ist die Untergrenze");
    }

    private static void TheStrokeIgnoresTheMouseRate()
    {
        Check.Group("Pinsel: der Abstand haengt nicht an der Maus");

        // Derselbe Weg, einmal in zwei Meldungen, einmal in hundert. Frueher setzte jede
        // Meldung einen Tupfer - langsam gezogen trug derselbe Strich mehr auf.
        float Cover(int reports, float spacing)
        {
            var mask = PaintedMask.For(800, 400);
            var stroke = new PaintStroke { Radius = 30, Flow = 0.4f, Hardness = 0.3f, Spacing = spacing };

            stroke.Begin(mask, 100, 200);
            for (int i = 1; i <= reports; i++) stroke.To(mask, 100 + 600f * i / reports, 200);

            return mask.Cover().Sum(b => (float)b);
        }

        float few = Cover(2, 0.25f), many = Cover(100, 0.25f);

        Check.That(Math.Abs(few - many) <= few * 0.01f,
                   "zwei oder hundert Mausmeldungen fuer denselben Weg tragen gleich viel auf",
                   $"{few:0} gegen {many:0}");

        Check.That(Cover(100, 1.5f) < Cover(100, 0.25f) * 0.8f,
                   "weiter Abstand setzt weniger Tupfer und traegt weniger auf");

        // Bei doppeltem Radius Abstand und harter Kante liegen Luecken zwischen den Tupfern.
        var dotted = PaintedMask.For(800, 400);
        var dots = new PaintStroke { Radius = 12, Flow = 1f, Hardness = 1f, Spacing = 3f };
        dots.Begin(dotted, 100, 200);
        dots.To(dotted, 700, 200);

        var row = Enumerable.Range(100 / PaintedMask.Coarse, 600 / PaintedMask.Coarse)
                            .Select(x => dotted.Cover()[(200 / PaintedMask.Coarse) * dotted.Width + x]).ToArray();

        Check.That(row.Contains((byte)0) && row.Count(v => v > 200) > 10,
                   "ueber 200 % Abstand liegen einzelne Tupfer mit Luecken");

        // Was ein Zug meldet, umfasst alles, was er geaendert hat.
        var before = PaintedMask.For(800, 400);
        var after = PaintedMask.For(800, 400);
        var probe = new PaintStroke { Radius = 25, Flow = 0.8f };
        probe.Begin(after, 300, 150);
        var bounds = probe.To(after, 420, 260).Union(PaintBounds.Around(300, 150, 25));

        bool inside = true;
        for (int y = 0; y < after.Height; y++)
            for (int x = 0; x < after.Width; x++)
            {
                if (after.Cover()[y * after.Width + x] == before.Cover()[y * after.Width + x]) continue;
                float cx = (x + 0.5f) * PaintedMask.Coarse, cy = (y + 0.5f) * PaintedMask.Coarse;
                inside &= cx >= bounds.X0 - PaintedMask.Coarse && cx <= bounds.X1 + PaintedMask.Coarse &&
                          cy >= bounds.Y0 - PaintedMask.Coarse && cy <= bounds.Y1 + PaintedMask.Coarse;
            }

        Check.That(inside, "der gemeldete Bereich umfasst jeden geaenderten Maskenpunkt");
    }

    private static void TheStrokeReplaysExactly()
    {
        Check.Group("Pinsel: ein Zug laesst sich nachspielen");

        var random = new Random(3);

        // Ein Stand, auf dem gemalt und radiert wird - leer liesse das Radieren nichts zu tun.
        var basis = PaintedMask.For(640, 360);
        var ground = new PaintStroke { Radius = 90, Flow = 0.9f };
        ground.Begin(basis, 120, 180);
        ground.To(basis, 520, 180);

        PaintedMask Copy()
        {
            var copy = PaintedMask.For(640, 360);
            basis.Cover().CopyTo(copy.Cover(), 0);
            return copy;
        }

        foreach (bool erase in new[] { false, true, false })
        {
            var stroke = new PaintStroke
            {
                Radius = 8 + random.Next(40),
                Flow = 0.2f + (float)random.NextDouble() * 0.8f,
                Hardness = (float)random.NextDouble(),
                Opacity = 0.5f + (float)random.NextDouble() * 0.5f,
                Spacing = 0.05f + (float)random.NextDouble() * 1.5f,
                Erase = erase,
            };

            var live = Copy();

            stroke.Begin(live, random.Next(640), random.Next(360));
            for (int i = 0; i < 60; i++) stroke.To(live, random.Next(640), random.Next(360));

            var replayed = Copy();
            stroke.Replay(replayed);

            Check.That(!live.Cover().AsSpan().SequenceEqual(basis.Cover()) &&
                       live.Cover().AsSpan().SequenceEqual(replayed.Cover()),
                       "nachgespielt ergibt Byte fuer Byte dasselbe Raster" + (erase ? " (Radierzug)" : ""));
        }
    }

    private static void ThePageZoomsAroundThePointer()
    {
        Check.Group("Mausrad: Zoom im Atelier");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-wheel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "bild.png");

        const int W = 1600, H = 900;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            pixels[i * 4] = (byte)(i % W * 255 / W);
            pixels[i * 4 + 1] = (byte)(i / W * 255 / H);
            pixels[i * 4 + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1200, Height = 800, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();
            page.Open(path);

            var size = (TextBlock)page.FindName("SourceText");
            Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0);
            Pump(TimeSpan.FromSeconds(0.4), () => false);

            var scroll = (ScrollViewer)page.FindName("ImageScroll");
            var display = (Image)page.FindName("Display");
            var tools = (ToolColumn)page.FindName("MouseTools");

            tools.Select(AtelierTool.Move, notify: true);
            page.UpdateLayout();

            Check.That(page.ZoomLevel == 0, "zu Beginn eingepasst");

            // Ein Punkt rechts unten im Bild: Er soll nach jedem Schritt unter dem Zeiger bleiben.
            var inView = new Point(scroll.ActualWidth * 0.7, scroll.ActualHeight * 0.65);

            (double U, double V) Under()
            {
                var at = scroll.TranslatePoint(inView, display);
                ImageHit.Exact(at.X, at.Y, display.ActualWidth, display.ActualHeight, W, H, true, out double u, out double v);
                return (u, v);
            }

            void Wheel(double notches)
            {
                page.WheelZoom(scroll.TranslatePoint(inView, display), inView, notches);
                page.UpdateLayout();
            }

            // Der erste Schritt: Das Bild fuellt den Ausschnitt in der Breite, also bleibt
            // der Punkt waagrecht stehen. Senkrecht steht es noch mittig, solange es dort
            // kleiner ist als der Ausschnitt - dort laesst sich nichts festhalten.
            var start = Under();
            Wheel(1);
            var first = Under();

            Check.That(Math.Abs(first.U - start.U) < 1.5,
                       "der erste Schritt haelt den Punkt dort, wo das Bild den Ausschnitt fuellt",
                       $"{start.U:0} -> {first.U:0}");

            // Bis 100 %, dann weiter: Dort fuellt das Bild den Ausschnitt in beiden Richtungen.
            // Nicht eine feste Zahl Rasten - wie viele es bis 100 % sind, haengt von der
            // Einpassung ab und damit von der Groesse des Ausschnitts auf dem Rechner, auf
            // dem die Probe laeuft. In der CI endeten drei Rasten vor, zwei danach genau auf
            // 100 %.
            for (int step = 0; step < 16 && page.ZoomLevel < 1.0; step++) Wheel(1);

            var anchored = Under();
            Wheel(2);
            var deeper = Under();

            Check.That(page.ZoomLevel > 1 && Math.Abs(deeper.U - anchored.U) < 1.0 && Math.Abs(deeper.V - anchored.V) < 1.0,
                       "ueber 100 % bleibt der Bildpunkt unter dem Zeiger in beiden Richtungen stehen",
                       $"{page.ZoomLevel * 100:0} %, ({anchored.U:0.0},{anchored.V:0.0}) -> ({deeper.U:0.0},{deeper.V:0.0})");

            for (int step = 0; step < 12; step++)
            {
                Wheel(-1);
                page.UpdateLayout();
            }

            Check.That(page.ZoomLevel == 0 && double.IsNaN(display.Width) &&
                       scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                       "heraus rastet es wieder eingepasst und mittig ein");

            // Mit einem anderen Werkzeug gehoert das Rad nicht dem Zoom.
            tools.Select(AtelierTool.Pick, notify: true);
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            scroll.RaiseEvent(wheel);

            Check.That(!wheel.Handled && page.ZoomLevel == 0, "bei der Pipette zoomt das Rad nicht");

            tools.Select(AtelierTool.Move, notify: true);
            wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            scroll.RaiseEvent(wheel);

            Check.That(wheel.Handled && page.ZoomLevel > 0, "beim Verschieben zoomt es");

            // Der Pinsel: Strg und Rad stellen den Abstand, der Regler zieht mit.
            tools.Select(AtelierTool.Brush, notify: true);
            page.UpdateLayout();

            var adorner = (PlacementAdorner)page.FindName("Placement");
            var panel = (PropertiesPanel)page.FindName("Properties");

            adorner.StepSpacing(1, new Point(100, 100));
            Check.Near(adorner.BrushSpacing, 0.30, 1e-6, "eine Raste: 25 % -> 30 %");
            Check.Near(panel.BrushSpacing, 0.30, 1e-6, "der Regler in der Eigenschaftsleiste zieht mit");
            Check.That(adorner.Knob == PlacementAdorner.BrushKnob.Spacing && !adorner.KnobDragged,
                       "der Ring zeigt den Abstand, haelt aber die Maus nicht fest");

            adorner.StepSpacing(4, new Point(100, 100));
            Check.Near(adorner.BrushSpacing, 0.5, 1e-6, "bis 50 % in 5-%-Schritten");
            adorner.StepSpacing(1, new Point(100, 100));
            Check.Near(adorner.BrushSpacing, 0.6, 1e-6, "darueber in 10 %");
            adorner.StepSpacing(-1, new Point(100, 100));
            Check.Near(adorner.BrushSpacing, 0.5, 1e-6, "und zurueck");
            adorner.StepSpacing(-40, new Point(100, 100));
            Check.Near(adorner.BrushSpacing, PaintStroke.MinSpacing, 1e-6, "nie unter 5 %");
            adorner.StepSpacing(60, new Point(100, 100));
            Check.Near(adorner.BrushSpacing, PaintStroke.MaxSpacing, 1e-6, "nie ueber 400 %");

            adorner.EndKnob();
            panel.SetBrush(panel.BrushRadius, panel.BrushHardness, PaintStroke.DefaultSpacing);
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static void Pump(TimeSpan timeout, Func<bool> until)
    {
        var end = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < end && !until())
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }
    }
}
