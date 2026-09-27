using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Werkzeugleiste (docs/Atelier-Werkzeugplan.md, Phase U): ein Katalog, aus dem Leiste,
/// Suche und Hub lesen, das Band der Kategorien mit ihren Werkzeugen, und was ein Werkzeug
/// tut - im Stapel eine Karte, im Knotenmodus ein Knoten, beim Pinsel seine Art.
/// </summary>
public static class ToolBandInvariants
{
    public static void Run()
    {
        TheCatalogIsWhole();
        TheBandWorks();
    }

    private static void TheCatalogIsWhole()
    {
        Check.Group("Werkzeugleiste: der Katalog");

        var sections = NodeCatalog.All.Where(k => k.Section is not null).ToList();
        Check.That(sections.All(k => ToolCatalog.All.Count(e => e.Kind == k) == 1),
                   "jede Karte des Farbstreifens steht genau einmal in der Leiste",
                   string.Join(", ", sections.Where(k => ToolCatalog.All.Count(e => e.Kind == k) != 1).Select(k => k.TitleKey)));

        Check.That(ToolCatalog.All.Select(e => e.Key).Distinct().Count() == ToolCatalog.All.Count, "jedes Werkzeug einmal");

        var untranslated = ToolCatalog.All.Select(e => e.TitleKey)
            .Concat(ToolCatalog.Categories.Select(c => c.Key))
            .Where(key => Strings.T(key) == key)
            .ToList();
        Check.That(untranslated.Count == 0, "jeder Name und jede Kategorie ist uebersetzt", string.Join(", ", untranslated));

        var shown = ToolCatalog.Shown.ToList();
        Check.That(shown.SequenceEqual(ToolCatalog.Categories.Select(c => c.Key).Where(shown.Contains)) &&
                   !shown.Contains(ToolCatalog.Time) && !shown.Contains(ToolCatalog.Retouch) && shown.Contains(ToolCatalog.Paint),
                   "das Band zeigt die Kategorien in ihrer Reihenfolge - Zeit und Retusche erst, wenn sie etwas haben");

        Check.That(ToolCatalog.All.Where(e => e.Action == ToolAction.Mask).All(e => e.NodesOnly) &&
                   ToolCatalog.All.Where(e => e.Section is not null).All(e => !e.NodesOnly),
                   "Masken nur im Knotenmodus, jede Karte auch im Stapel");

        var vignette = ToolCatalog.Find("vign", Strings.T);
        var brush = ToolCatalog.Find(Strings.T("S_ToolBrush").ToLowerInvariant(), Strings.T);
        Check.That(vignette.FirstOrDefault()?.Section == "Vignette" && brush.FirstOrDefault()?.Key == "brush",
                   "die Suche findet beim Namen, und was damit anfaengt, steht vorn");
        Check.That(ToolCatalog.Find("feather", Strings.T).Any(e => e.Key == "mask-edit"), "und auch ueber weitere Woerter");

        // Die gezeichneten Zeichen (Entscheidung 9): Jedes laesst sich lesen und bleibt in
        // seinem Raster von 24 - ein Tippfehler im Pfad zeigte sich sonst erst beim Zeichnen,
        // und ein Zeichen ausserhalb des Rasters ragte aus seinem Knopf.
        var broken = new List<string>();

        foreach (string key in Icons.Keys)
        {
            try
            {
                var bounds = Icons.Of(key).Bounds;
                if (bounds.IsEmpty || bounds.Left < -0.5 || bounds.Top < -0.5 || bounds.Right > 24.5 || bounds.Bottom > 24.5)
                    broken.Add($"{key} {bounds}");
            }
            catch (FormatException)
            {
                broken.Add(key + " (unlesbar)");
            }
        }

        Check.That(broken.Count == 0, "jedes gezeichnete Zeichen ist lesbar und bleibt im Raster", string.Join(", ", broken));
        Check.That(ToolCatalog.All.Where(e => e.Category == ToolCatalog.Paint).All(e => Icons.Has(e.Key)),
                   "jedes Werkzeug der Zeile Malen hat ein gezeichnetes Zeichen");
    }

    private static void TheBandWorks()
    {
        Check.Group("Werkzeugleiste: auf der Seite");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-leiste-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "bild.png");

        const int W = 64, H = 40;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { pixels[i * 4] = (byte)(i % W * 4); pixels[i * 4 + 1] = 120; pixels[i * 4 + 2] = 60; pixels[i * 4 + 3] = 255; }
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
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (DateTime.UtcNow < end)
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
            while (DateTime.UtcNow < end && ((TextBlock)page.FindName("SourceText")).Text.Length == 0) Pump(0.05);

            var band = (ToolBand)page.FindName("ToolBand");
            var tools = (GradingPanel)page.FindName("Tools");
            var placement = (PlacementAdorner)page.FindName("Placement");
            var column = (ToolColumn)page.FindName("MouseTools");

            // Das Band: eine Kategorie zeigt ihre Werkzeuge.
            band.Choose(ToolCatalog.Optics);
            Check.That(band.ToolButtons.Count == ToolCatalog.All.Count(e => e.Category == ToolCatalog.Optics),
                       "die Kategorie Optik zeigt ihre Werkzeuge", $"{band.ToolButtons.Count}");

            band.Choose(ToolCatalog.Select);
            Check.That(band.ToolButtons.All(b => !b.IsEnabled), "im Stapel sind die Masken als Knoten gedaempft - nicht versteckt");

            // Im Stapel: ein Effekt oeffnet seine Karte im Farbstreifen.
            var vignette = ToolCatalog.All.Single(e => e.Section == "Vignette");
            page.UseTool(vignette);
            Pump();
            Check.That(tools.IsShown("Vignette"), "Vignette aus der Leiste: ihre Karte steht im Farbstreifen");

            // Der Pinsel in einer Art: Werkzeug und Art kommen an.
            page.UseTool(ToolCatalog.All.Single(e => e.Key == "rectangle"));
            Pump();
            Check.That(column.Tool == AtelierTool.Brush && placement.BrushArea == PaintArea.Rectangle,
                       "Rechteck aus der Leiste: der Pinsel zieht Rechtecke auf");

            // U3: Die Pinselleiste zeigt nur, was zum Werkzeug gehoert - und die Zeile, was wirkt.
            var properties = (PropertiesPanel)page.FindName("Properties");
            bool Shown(string name) => ((FrameworkElement)properties.FindName(name)).Visibility == Visibility.Visible;

            // Was wirklich zu sehen ist - nicht nur der eigene Schalter, sondern auch der der
            // Gruppe, in der es steht. Eine falsch geschachtelte Gruppe versteckt sonst still
            // die Regler einer anderen.
            bool Seen(string name) => ((UIElement)properties.FindName(name)).IsVisible;

            Check.That(band.Active == "rectangle" && !Shown("BrushTipRow") && !Shown("BrushShapeRow") && !Shown("BrushPressureRow") &&
                       !Shown("BrushModeRow"),
                       "Rechteck: gedrueckt in der Zeile, und keine Spitze, kein Abstand, kein Druck in der Pinselleiste");
            Check.That(Seen("BrushOpacitySlider") && Seen("BrushObjectToggle") && !Seen("BrushSizeSlider") &&
                       !Seen("BrushSpacingSlider") && !Seen("BrushFlowSlider") && !Seen("BrushAngleSlider"),
                       "Rechteck: Deckkraft und Bindung stehen wirklich da, Groesse, Staerke, Abstand und Form nicht");

            page.UseTool(ToolCatalog.All.Single(e => e.Key == "brush"));
            Pump();
            Check.That(placement.BrushArea == PaintArea.None, "Pinsel aus der Leiste: wieder Zuege");
            Check.That(band.Active == "brush" && Shown("BrushTipRow") && Shown("BrushShapeRow") && !Shown("BrushSquishRow") && !Shown("BrushStampRow"),
                       "Pinsel: die Spitze ist wieder da - das Karo nur eckig, der Stempel nur als Stempel");
            Check.That(new[] { "BrushSizeSlider", "BrushHardnessSlider", "BrushFlowSlider", "BrushOpacitySlider",
                               "BrushSpacingSlider", "BrushSquareToggle", "BrushAngleSlider", "BrushAspectSlider",
                               "BrushFollowToggle", "BrushPressureBox" }.All(Seen),
                       "Pinsel: Groesse, Haerte, Staerke, Deckkraft, Abstand, Spitze und Druck stehen wirklich da",
                       string.Join(", ", new[] { "BrushSizeSlider", "BrushHardnessSlider", "BrushFlowSlider", "BrushSpacingSlider",
                                                 "BrushAngleSlider", "BrushPressureBox" }.Where(n => !Seen(n))));

            var round = (System.Windows.Controls.Primitives.ToggleButton)properties.FindName("BrushRoundToggle");
            var square = (System.Windows.Controls.Primitives.ToggleButton)properties.FindName("BrushSquareToggle");

            Check.That(round.IsChecked == true && square.IsChecked != true, "rund ist gedrueckt, solange die Spitze nicht eckig ist");

            square.IsChecked = true;
            Check.That(Shown("BrushSquishRow") && round.IsChecked != true, "eckig: jetzt gibt es das Karo - und rund ist nicht mehr gedrueckt");

            round.IsChecked = true;
            Check.That(square.IsChecked != true && properties.BrushShape == BrushShape.Round && !Shown("BrushSquishRow"),
                       "ein Klick auf rund macht die Spitze wieder rund");

            round.IsChecked = false;
            Check.That(round.IsChecked == true, "rund laesst sich nicht abschalten - es ist, was bleibt, wenn eckig aus ist");

            page.UseTool(ToolCatalog.All.Single(e => e.Key == "stamp"));
            Pump();
            Check.That(Shown("BrushStampRow") && !Shown("BrushTipShapes"),
                       "Stempel: seine Spitze statt der Wahl rund oder eckig");
            page.UseTool(ToolCatalog.All.Single(e => e.Key == "brush"));
            Pump();
            Check.That(!Shown("BrushStampRow") && Shown("BrushTipShapes") && round.IsChecked == true,
                       "zurueck zum Pinsel: wieder rund oder eckig");

            band.Choose(ToolCatalog.Paint);
            var brushButton = band.ToolButtons.Single(b => b.Tag is ToolEntry { Key: "brush" });
            Check.That(brushButton.Content is IconLabel && System.Windows.Automation.AutomationProperties.GetName(brushButton) == Strings.T("S_ToolBrush"),
                       "der Pinsel in der Zeile: gezeichnetes Zeichen, und sein Name fuer Bildschirmleser");

            column.Select(AtelierTool.Move, notify: true);
            Pump();
            Check.That(band.Active is null, "ein anderes Mauswerkzeug: kein Malwerkzeug mehr gedrueckt");

            // Die Suche waehlt die Kategorie, in der das Gefundene steht.
            var found = band.Search("vign");
            Check.That(found.FirstOrDefault()?.Section == "Vignette", "die Suche in der Leiste findet die Vignette");
            band.Take(found[0]);
            Check.That(band.Category == ToolCatalog.Optics, "und zeigt ihre Kategorie");

            // Im Knotenmodus: gedaempftes wird nutzbar, ein Effekt wird ein Knoten.
            page.ConvertToNodes();
            Pump(0.5);
            band.Choose(ToolCatalog.Select);
            Check.That(band.InNodes && band.ToolButtons.All(b => b.IsEnabled), "im Knotenmodus sind die Masken nutzbar");

            int before = page.Graph!.Nodes.Count;
            page.UseTool(vignette);
            Pump();
            Check.That(page.Graph!.Nodes.Count == before + 1 &&
                       page.Graph.Nodes.OfType<OpticsNode>().Any(n => n.Tool is VignetteTool),
                       "Vignette aus der Leiste im Knotenmodus: ein Knoten mehr, mit der Vignette");

            var luminance = ToolCatalog.All.Single(e => e.TitleKey == "S_MaskLuminance");
            page.UseTool(luminance);
            Pump();
            Check.That(page.Graph!.Nodes.OfType<MaskNode>().Any(m => m.Mask.Kind == MaskKind.Luminance),
                       "eine Maske aus der Leiste: ein Maskenknoten");

            // R3: Der Chip "Einstellung" unter der Ebenenliste legt eine Einstellungsebene an.
            var layerList = (NodeLayerList)page.FindName("NodeLayers");
            var editor = (NodeEditor)page.FindName("NodeView");
            var mixesBefore = page.Graph!.Nodes.OfType<MixNode>().ToList();

            Check.That(layerList.Chips.Add.Content is IconLabel && layerList.Chips.Adjust.Content is IconLabel,
                       "unter der Liste: Ebene und Einstellung als Chips mit Zeichen und Namen");

            layerList.Chips.Adjust.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump();

            var layerMix = page.Graph!.Nodes.OfType<MixNode>().Except(mixesBefore).SingleOrDefault();
            string? grade = layerMix is null ? null : page.Graph.Into(layerMix.Id, "Oben")?.From;

            Check.That(grade is not null && page.Graph.Find(grade) is LayerGradeNode { Adjustment: true },
                       "der Chip Einstellung legt eine Einstellungsebene an");

            if (layerMix is not null && grade is not null)
            {
                // In der Liste gewaehlt: Die Vignette kommt in den Zweig der Ebene, vor ihr
                // Mischen - sie wirkt nur auf diese Ebene, nicht auf alles darunter.
                typeof(AtelierPage).GetMethod("OnLayerChosen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(page, new object[] { layerMix });
                Pump();

                page.UseTool(vignette);
                Pump();

                var into = page.Graph.Into(layerMix.Id, "Oben");
                var inserted = into is null ? null : page.Graph.Find(into.From);

                Check.That(inserted is OpticsNode { Tool: VignetteTool } &&
                           page.Graph.Into(inserted.Id, NodeEdits.Through(inserted).Input!)?.From == grade,
                           "in der Liste gewaehlt: die Vignette kommt in den Zweig der Ebene, vor ihr Mischen");
                Check.That(NodeLayerList.Of(page.Graph).Single(l => ReferenceEquals(l.Mix, layerMix)).Effects == 1,
                           "und die Ebene zaehlt einen eigenen Effekt - die Zeile zeigt fx");

                // Im Graphen gewaehlt: wie bisher dahinter.
                editor.Select(layerMix);
                Pump();
                page.UseTool(vignette);
                Pump();

                Check.That(page.Graph.Into(layerMix.Id, "Oben")?.From == inserted!.Id &&
                           page.Graph.Links.Any(l => l.From == layerMix.Id && page.Graph.Find(l.To) is OpticsNode { Tool: VignetteTool }),
                           "im Graphen gewaehlt: der Effekt kommt wie bisher hinter das Mischen");
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
}
