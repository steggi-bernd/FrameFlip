using System.IO;
using System.Windows;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Verdrahtung zwischen Datei, Ebenenstreifen und Atelierseite.
///
/// Die Rechnung stimmt - das steht in <see cref="PassRebuildInvariants"/>. Hier
/// geht es um die Frage danach: Kommt sie auch dort an, wo jemand sie sieht? Ein
/// Streifen, der sich nicht zeigt, ein Stapel, der nicht gelesen wird, ein
/// Exportweg, der die Ebenen ignoriert - das sind die Fehler, die keine Meldung
/// erzeugen, sondern nur ein Bild, das anders aussieht als erwartet.
/// </summary>
public static class AtelierLayerInvariants
{
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(),
                                     "frameflip-ebenen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "render_0001.exr");
        File.WriteAllBytes(path, ExrPassSample.Bytes());

        try
        {
            TheStripAppears(path);
            TheSharedLoaderRebuilds(path);
            APlainImageLayersToo(folder);
            EachLayerKeepsItsOwnTools(path);
            TheFrameOnlyGrabsWhenItShould(path);
            TheToolDecidesWhatTheMouseDoes(path);
            TheColumnRemembersHowItStood(path);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { /* ein liegengebliebener Rest ist kein Testfehler */ }
        }
    }

    /// <summary>
    /// Eine Datei mit Passen muss den Streifen zeigen, und zwar gefuellt.
    ///
    /// Das Lesen laeuft nebenher, deshalb wird die Nachrichtenschleife gepumpt, bis
    /// das Ergebnis eintrifft. Bleibt es aus, ist das ein Fehler und kein Grund zu
    /// warten - eine Seite, die nach zehn Sekunden noch nichts anzeigt, zeigt auch
    /// nach einer Minute nichts an.
    /// </summary>
    private static void TheStripAppears(string path)
    {
        Check.Group("Der Ebenenstreifen erscheint bei einer Datei mit Passen");

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1000,
            Height = 700,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();

            var strip = (LayerPanel)page.FindName("Layers");
            Check.That(strip is not null, "der Streifen ist Teil der Seite");
            if (strip is null) return;

            Check.That(strip.Visibility != Visibility.Visible,
                       "ohne Bild bleibt er aus dem Weg");

            page.Open(path);

            bool arrived = Pump(TimeSpan.FromSeconds(10),
                                () => strip.Visibility == Visibility.Visible);

            Check.That(arrived, "nach dem Oeffnen zeigt er sich");
            if (!arrived) return;

            Check.That(strip.HasChoice, "und weiss, dass es Passe zu waehlen gibt");

            // Die Grundstellung ist das Bild selbst - nicht ein selbstgebauter
            // Stapel. Wer eine Datei oeffnet, soll sehen, was darin steht.
            Check.That(strip.Stack.IsPassThrough, "gerechnet wird zunaechst nichts");

            // Und der Aufbau aus den Passen laesst sich anstossen.
            strip.RebuildFromPasses();

            Check.That(strip.Stack.Layers.Count == 16, "der Passestapel steht",
                       $"{strip.Stack.Layers.Count}");
            Check.That(settings.Layers is not null && settings.Layers.Layers.Count == 16,
                       "und wird fuer das naechste Mal gemerkt");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Der Weg, den Vorschau UND Export nehmen.
    ///
    /// Es gibt genau einen - das ist der Punkt. Haette der Export einen eigenen,
    /// saehe die ausgegebene Sequenz eines Tages anders aus als das Bild, nach dem
    /// jemand sie eingestellt hat, und der Unterschied fiele nach dreihundert
    /// Bildern auf.
    /// </summary>
    private static void TheSharedLoaderRebuilds(string path)
    {
        Check.Group("Der gemeinsame Ladeweg setzt die Passe zusammen");

        var rendered = FloatFrame.FromExrPass(path, "ViewLayer.Combined");
        Check.That(rendered is not null, "das gerenderte Bild liegt vor");
        if (rendered is null) return;

        // Ohne Stapel: die Datei, wie sie ist.
        var plain = LayeredFrameLoader.Load(path, null);
        Check.That(plain is not null, "ohne Stapel wird schlicht gelesen");

        // Mit dem Passestapel: dieselbe Rechnung wie in der Vorschau, und sie muss
        // wieder den Render ergeben.
        var stack = PassStack.Rebuild(FrameFlip.Decoding.Exr.ExrPasses.Of(path));
        var built = LayeredFrameLoader.Load(path, stack);

        Check.That(built is not null, "mit Stapel wird zusammengesetzt");
        if (built is null) return;

        Check.That(!ReferenceEquals(built, plain), "und es ist ein anderes Bild als das gelesene");

        float worst = 0f;
        for (int i = 0; i < built.PixelCount; i++)
        {
            worst = MathF.Max(worst, Off(built.R[i], rendered.R[i]));
            worst = MathF.Max(worst, Off(built.G[i], rendered.G[i]));
            worst = MathF.Max(worst, Off(built.B[i], rendered.B[i]));
        }

        Check.That(worst < 0.005f, "und entspricht dem Render",
                   $"groesste Abweichung {worst * 100:0.##} %");

        // Ein Stapel, dessen Passe die Datei nicht fuehrt, darf nicht in ein
        // schwarzes Bild laufen - Rezepte wandern zwischen Dateien.
        var foreign = new LayerStack
        {
            Layers = { new ImageLayer { Source = "AndereDatei.GibtEsNicht" } },
        };

        var fallback = LayeredFrameLoader.Load(path, foreign);
        Check.That(fallback is not null, "ein fremdes Rezept ergibt trotzdem ein Bild");

        static float Off(float a, float b) => MathF.Abs(a - b) / MathF.Max(0.01f, MathF.Abs(b));
    }

    /// <summary>
    /// Ein PNG hat keine Passe - schichten laesst es sich trotzdem.
    ///
    /// Das ist die Unterscheidung, die anfangs falsch getroffen war: PASSE braucht
    /// das Format, EBENEN nicht. Ein PNG fuehrt genau ein Bild, aber dasselbe Bild
    /// ein zweites Mal und auf Multiplizieren gestellt ist der Griff, mit dem in
    /// Photoshop jeder Kontrast anfaengt - und die Rechnung dahinter ist dieselbe.
    ///
    /// Der Streifen zeigt sich deshalb bei jedem Bild. Nur beginnt er eingeklappt,
    /// wenn es nichts zu waehlen gibt: eine einzeilige Liste waere im schmalen
    /// Streifen der teuerste Platz, den es gibt.
    /// </summary>
    private static void APlainImageLayersToo(string folder)
    {
        Check.Group("Auch ein Einzelbild laesst sich schichten");

        // Ein graues PNG - hell genug, dass Multiplizieren sichtbar abdunkelt.
        string png = Path.Combine(folder, "einzel.png");
        Write(png, 160);

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1000,
            Height = 700,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();
            page.Open(png);

            var strip = (LayerPanel)page.FindName("Layers");

            // Gewartet wird auf die Masse in der Kopfzeile und NICHT auf den
            // Dateinamen: Den setzt die Seite schon beim Aufruf, die Masse erst,
            // wenn die Datei wirklich gelesen ist. Auf das falsche Zeichen zu warten
            // heisst, gar nicht zu warten - und der Test misst dann nichts.
            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");

            bool loaded = Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0);
            Check.That(loaded, "das Bild wird geladen");
            if (!loaded) return;

            Check.That(strip.Visibility == Visibility.Visible,
                       "der Streifen zeigt sich auch hier");
            Check.That(!strip.HasChoice, "aber es gibt keine Passe zu waehlen");

            var body = (FrameworkElement)strip.FindName("Body");
            Check.That(body.Visibility != Visibility.Visible,
                       "und er beginnt eingeklappt, statt Platz zu nehmen");
        }
        finally
        {
            window.Close();
        }

        // Und die Rechnung dahinter: verdoppeln und multiplizieren dunkelt ab.
        var plain = LayeredFrameLoader.Load(png, null);
        Check.That(plain is not null, "das PNG laesst sich lesen");
        if (plain is null) return;

        var doubled = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "", Mode = BlendMode.Normal },
                new ImageLayer { Source = "", Mode = BlendMode.Multiply },
            },
        };

        var darker = LayeredFrameLoader.Load(png, doubled);
        Check.That(darker is not null, "und mit zwei Ebenen zusammensetzen");
        if (darker is null) return;

        Check.Near(darker.R[0], plain.R[0] * plain.R[0], 1e-4,
                   "multiplizieren quadriert den Wert");
        Check.That(darker.R[0] < plain.R[0], "das Bild wird also dunkler",
                   $"{darker.R[0]:0.####} statt {plain.R[0]:0.####}");

        // Dasselbe auf Negativ multiplizieren hellt auf - die Gegenprobe, dass hier
        // wirklich gemischt und nicht nur skaliert wird.
        var lifted = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "", Mode = BlendMode.Normal },
                new ImageLayer { Source = "", Mode = BlendMode.Screen },
            },
        };

        var brighter = LayeredFrameLoader.Load(png, lifted)!;
        Check.That(brighter.R[0] > plain.R[0], "und negativ multiplizieren hellt auf",
                   $"{brighter.R[0]:0.####} statt {plain.R[0]:0.####}");
    }

    /// <summary>Ein einfarbiges PNG mit dem gegebenen Grauwert.</summary>
    private static void Write(string path, byte grey)
    {
        var pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = grey;
            pixels[i + 3] = 255;
        }

        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// Jede Einstellungsebene behaelt ihre eigenen Werkzeuge.
    ///
    /// Das ist die Stelle, an der diese Oberflaeche kaputtgehen konnte. Es gibt
    /// EINEN Werkzeugstreifen und beliebig viele Ebenen; er haengt sich beim
    /// Anklicken um. Reicht er dabei seinen eigenen Stapel weiter, halten am Ende
    /// alle Ebenen denselben - und wer an der zweiten dreht, verstellt die erste
    /// gleich mit. Umgekehrt: Schreibt er nicht zurueck, was er beim Laden neu
    /// angelegt hat, ist es beim naechsten Umschalten weg.
    ///
    /// Beides sieht man dem Bild nicht an. Man merkt es Wochen spaeter an einer
    /// Einstellung, die sich nicht mehr erklaeren laesst.
    /// </summary>
    private static void EachLayerKeepsItsOwnTools(string path)
    {
        Check.Group("Jede Einstellungsebene behaelt ihre Werkzeuge");

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1000,
            Height = 800,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();
            page.Open(path);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            var strip = (LayerPanel)page.FindName("Layers");
            var tools = (GradingPanel)page.FindName("Tools");

            // Zwei Einstellungsebenen anlegen und jeder einen eigenen Wert geben.
            strip.AddAdjustment();
            var first = strip.EditedLayer;
            Check.That(first is not null, "die erste Korrektur steht");
            if (first is null) return;

            var exposure = (System.Windows.Controls.Slider)tools.FindName("ExposureSlider");
            exposure.Value = -2.0;

            Check.Near(first.Adjustments!.Exposure, -2.0, 0.001,
                       "und der Regler landet in ihr");

            strip.AddAdjustment();
            var second = strip.EditedLayer;
            Check.That(second is not null && !ReferenceEquals(second, first),
                       "die zweite ist eine andere Ebene");
            if (second is null) return;

            Check.Near(exposure.Value, 0.0, 0.001,
                       "der Streifen steht bei der neuen Ebene auf Grundstellung");
            Check.Near(first.Adjustments!.Exposure, -2.0, 0.001,
                       "und die erste behaelt ihren Wert");

            exposure.Value = 1.5;

            Check.Near(second.Adjustments!.Exposure, 1.5, 0.001, "die zweite bekommt ihren");
            Check.Near(first.Adjustments!.Exposure, -2.0, 0.001,
                       "ohne die erste mitzuziehen");

            // Die Werkzeugstapel duerfen nicht dieselben Objekte sein.
            Check.That(!ReferenceEquals(first.Tools, second.Tools),
                       "beide haben ihren eigenen Werkzeugstapel");

            var firstBalance = first.Tools!.Tools.OfType<WhiteBalanceTool>().FirstOrDefault();
            var secondBalance = second.Tools!.Tools.OfType<WhiteBalanceTool>().FirstOrDefault();

            Check.That(firstBalance is not null && secondBalance is not null,
                       "und jede ihre eigenen Werkzeuge");
            Check.That(!ReferenceEquals(firstBalance, secondBalance),
                       "die nicht dasselbe Objekt sind");

            // Und zurueck: Der Streifen muss die erste wieder so zeigen, wie sie war.
            Select(strip, first);

            Check.Near(exposure.Value, -2.0, 0.001,
                       "zurueckgewechselt steht der Regler wieder auf ihrem Wert");

            // Eine Passebene gibt die Werkzeuge ans fertige Bild zurueck.
            var pass = strip.Stack.Layers.First(l => l.Content == LayerContent.Pass);
            Select(strip, pass);

            Check.That(strip.EditedLayer is null, "auf einer Passebene ist keine Korrektur gewaehlt");

            exposure.Value = 0.75;

            Check.That(settings.Adjustments is not null, "der Regler gehoert wieder dem Bild");
            Check.Near(settings.Adjustments!.Exposure, 0.75, 0.001, "und landet dort");
            Check.Near(first.Adjustments!.Exposure, -2.0, 0.001,
                       "die Ebenen bleiben davon unberuehrt");
            Check.Near(second.Adjustments!.Exposure, 1.5, 0.001, "beide");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Die rechte Spalte: Ebenen unten, Groessen verstellbar, Aufteilung gemerkt.
    ///
    /// Drei Klagen in einer. "Alles sehr starr" - die Spalte war dreihundert Punkte
    /// breit und der Ebenenstreifen so hoch, wie er eben wurde. "Dass die Ebenen ganz
    /// oben sind, ist ungewohnt" - sie standen oben, weil von oben nach unten gelesen
    /// dort die Rechenreihenfolge stand. Das Argument war gut und hat gegen zwanzig
    /// Jahre Gewohnheit verloren.
    ///
    /// Geprueft wird die Reihenfolge ueber die Gitterzeile und nicht ueber
    /// Bildschirmkoordinaten: Zeilennummern stimmen auch dann, wenn das Fenster im
    /// Test nie wirklich gezeichnet wird.
    /// </summary>
    private static void TheColumnRemembersHowItStood(string path)
    {
        Check.Group("Die rechte Spalte: Ebenen unten und verstellbar");

        // Eine Aufteilung, die NICHT die Voreinstellung ist - sonst prueft der Test
        // nur, dass eine Voreinstellung eine Voreinstellung ist.
        var settings = new AppSettings { AtelierColumnWidth = 380, AtelierLayersHeight = 180 };
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1200,
            Height = 900,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();

            var column = (System.Windows.Controls.ColumnDefinition)page.FindName("RightColumn");
            var row = (System.Windows.Controls.RowDefinition)page.FindName("LayersRow");
            var scroll = (System.Windows.Controls.ScrollViewer)page.FindName("LayerScroll");
            var colour = (GradingPanel)page.FindName("Tools");

            Check.That(column is not null && row is not null && scroll is not null,
                       "die Spalte ist in drei Teile geteilt");

            if (column is null || row is null || scroll is null || colour is null) return;

            Check.Near(column.ActualWidth, 380, 1,
                       "die gemerkte Breite steht wieder da");

            // Die Ebenen liegen UNTER der Farbe. Das ist die eigentliche Umstellung.
            Check.That(System.Windows.Controls.Grid.GetRow(scroll) >
                       System.Windows.Controls.Grid.GetRow(colour),
                       "die Ebenen stehen unter der Farbkorrektur");

            Check.That(scroll.Visibility != Visibility.Visible,
                       "ohne Bild ist der Streifen weg");

            Check.Near(row.ActualHeight, 0, 0.5,
                       "und seine Zeile hat dann keine Hoehe - kein Loch am Rand");

            page.Open(path);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");

            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Pump(TimeSpan.FromSeconds(3), () => scroll.Visibility == Visibility.Visible);
            page.UpdateLayout();

            Check.That(scroll.Visibility == Visibility.Visible, "mit Bild zeigt er sich");

            Check.Near(row.ActualHeight, 180, 1,
                       "und zwar so hoch, wie er zuletzt stand");

            // Der Streifen selbst muss in einer Bildlaufflaeche sitzen: Seine Hoehe
            // waechst mit der Zahl der Ebenen, die Zeile nicht.
            Check.That(scroll.Content is LayerPanel,
                       "der Streifen sitzt in einer Bildlaufflaeche");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Das Werkzeug entscheidet, was die Maus im Bild tut - und sonst nichts.
    ///
    /// Zwei gemeldete Fehler hingen an derselben fehlenden Entscheidung: Der Rahmen
    /// sass am falschen Platz, und ein Zug traf die falsche Ebene. Beides, weil der
    /// Rahmen der AUSWAHL gehoerte statt einem Werkzeug - er war also immer da,
    /// sobald irgendeine Ebene gewaehlt war, und niemand konnte ihn abstellen.
    ///
    /// Geprueft wird deshalb nicht, ob der Rahmen richtig sitzt, sondern ob er
    /// UEBERHAUPT VERSCHWINDET, wenn ein anderes Werkzeug gilt. Das ist die
    /// Eigenschaft, aus der die Behebung folgt; wo er sitzt, prueft der Test
    /// darueber.
    ///
    /// Und nebenbei prueft dieser Test, dass die Spalte sich ueberhaupt laedt. Ein
    /// Stil mit dem falschen Zieltyp faellt weder dem Uebersetzer noch der uebrigen
    /// Reihe auf - er faellt erst auf, wenn jemand das Fenster oeffnet.
    /// </summary>
    private static void TheToolDecidesWhatTheMouseDoes(string path)
    {
        Check.Group("Das Werkzeug entscheidet, was die Maus tut");

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1000,
            Height = 800,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();

            var column = (ToolColumn)page.FindName("MouseTools");

            Check.That(column is not null, "die Werkzeugspalte ist Teil der Seite");
            if (column is null) return;

            Check.That(column.Tool == AtelierTool.Move,
                       "in der Grundstellung wird verschoben", $"{column.Tool}");

            var frame = (PlacementAdorner)page.FindName("Placement");

            page.Open(path);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");

            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            page.UpdateLayout();

            Check.That(frame.IsHitTestVisible, "beim Verschieben faengt der Rahmen");

            // Der Kern der Sache: ein anderes Werkzeug, und der Rahmen ist weg.
            Check.That(page.HandleToolKey(System.Windows.Input.Key.H),
                       "die Taste H waehlt ein Werkzeug");

            page.UpdateLayout();

            Check.That(column.Tool == AtelierTool.Hand, "naemlich die Hand", $"{column.Tool}");

            Check.That(!frame.IsHitTestVisible,
                       "und dann faengt der Rahmen nichts mehr - er gehoert dem Verschieben");

            // Und zurueck, damit "weg" nicht heisst "kaputt".
            Check.That(page.HandleToolKey(System.Windows.Input.Key.V), "V waehlt zurueck");

            page.UpdateLayout();

            Check.That(column.Tool == AtelierTool.Move, "wieder Verschieben", $"{column.Tool}");
            Check.That(frame.IsHitTestVisible, "und der Rahmen ist wieder da");

            // Eine Taste, die kein Werkzeug meint, muss durchgelassen werden - sonst
            // schluckt das Atelier jede Tastatureingabe des Fensters.
            Check.That(!page.HandleToolKey(System.Windows.Input.Key.Q),
                       "eine fremde Taste geht weiter");

            // Auswaehlen und Verschieben schliessen einander aus. Frueher konnten
            // beide zugleich gelten, und dann war nicht zu sagen, was ein Klick tut.
            Check.That(page.HandleToolKey(System.Windows.Input.Key.W), "W waehlt das Auswaehlen");

            page.UpdateLayout();

            Check.That(!frame.IsHitTestVisible, "beim Auswaehlen faengt der Rahmen nicht");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Der Greifrahmen darf nur dann Klicks annehmen, wenn er auch etwas zeigt.
    ///
    /// Er liegt ueber dem Bild. Ein unsichtbarer Rahmen, der trotzdem Klicks
    /// schluckt, waere der unangenehmste Zustand von allen: Das Auswaehlen einer
    /// Kryptomatte ginge nicht mehr, und man saehe keinen Grund dafuer.
    /// </summary>
    private static void TheFrameOnlyGrabsWhenItShould(string path)
    {
        Check.Group("Der Greifrahmen faengt nur, wenn er etwas zeigt");

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1000,
            Height = 800,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();

            var frame = (PlacementAdorner)page.FindName("Placement");
            Check.That(frame is not null, "der Rahmen ist Teil der Seite");
            if (frame is null) return;

            Check.That(!frame.IsHitTestVisible, "ohne Bild faengt er nichts");

            page.Open(path);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            page.UpdateLayout();

            // Nach dem Oeffnen ist eine Passebene gewaehlt - sie hat eine Flaeche,
            // also zeigt der Rahmen sie.
            Check.That(frame.IsHitTestVisible, "bei einer Passebene faengt er");

            // Eine Einstellungsebene hat keine Flaeche. Ein Rahmen um sie waere ein
            // Rahmen um etwas, das es nicht gibt.
            var strip = (LayerPanel)page.FindName("Layers");
            strip.AddAdjustment();
            page.UpdateLayout();

            Check.That(!frame.IsHitTestVisible, "bei einer Korrektur nicht");

            // Und zurueck.
            var pass = strip.Stack.Layers.First(l => l.Content == LayerContent.Pass);
            Select(strip, pass);
            page.UpdateLayout();

            Check.That(frame.IsHitTestVisible, "zurueck auf einem Pass wieder");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Waehlt eine Ebene so aus, wie ein Klick in die Liste es taete.</summary>
    private static void Select(LayerPanel strip, ImageLayer layer)
    {
        var list = (System.Windows.Controls.ListBox)strip.FindName("LayerList");

        foreach (System.Windows.Controls.ListBoxItem item in list.Items)
        {
            if (!ReferenceEquals(item.Tag, layer)) continue;

            item.IsSelected = true;
            strip.UpdateLayout();
            return;
        }
    }

    /// <summary>
    /// Laesst die Nachrichtenschleife laufen, bis die Bedingung eintritt oder die
    /// Zeit um ist. True, wenn sie eingetreten ist.
    /// </summary>
    private static bool Pump(TimeSpan timeout, Func<bool> until)
    {
        var end = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < end)
        {
            if (until()) return true;

            // Bis herunter zu Background abarbeiten: Die Antwort aus dem Lesefaden
            // kommt als gewoehnliche Nachricht, und die steht darueber.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(5);
        }

        return until();
    }
}
