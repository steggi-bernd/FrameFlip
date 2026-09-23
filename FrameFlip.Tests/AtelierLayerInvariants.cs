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
            TheDockRemembersHowItStood(path);
            ShowingALayerShowsItAtOnce(folder);
            TheReportedSessionComesBack(folder);
            RasterReachesTheAtelier(folder);
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
            var dock = (DockHost)page.FindName("Dock");
            Check.That(strip is not null && dock is not null, "der Streifen ist Teil der Seite");
            if (strip is null || dock is null) return;

            Check.That(!dock.IsShown("layers"), "ohne Bild bleibt er aus dem Weg");

            page.Open(path);

            // Die Ebenen stehen in einem eigenen Feld. Nach dem Oeffnen hat es etwas
            // zu zeigen - und wer es nach vorn holt, sieht den Streifen.
            bool arrived = Pump(TimeSpan.FromSeconds(10), () => dock.IsAvailable("layers"));

            Check.That(arrived, "nach dem Oeffnen sind die Ebenen verfuegbar");
            if (!arrived) return;

            dock.Activate("layers");
            page.UpdateLayout();

            Check.That(dock.IsShown("layers") && strip.ActualHeight > 0, "und zeigen den Streifen");

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

            var dock = (DockHost)page.FindName("Dock");

            Check.That(dock.IsAvailable("layers"), "die Ebenen sind auch hier verfuegbar");
            Check.That(!strip.HasChoice, "aber es gibt keine Passe zu waehlen");

            // Frueher begann der Streifen eingeklappt, um der Farbe keinen Platz zu
            // nehmen. Jetzt teilt er sich mit ihr eine Gruppe als Reiter und nimmt
            // niemandem etwas: Solange die Farbe vorn liegt, ist er gar nicht da.
            Check.That(dock.IsShown("colour") && !dock.IsShown("layers"),
                       "solange die Farbe vorn liegt, nimmt er ihr keinen Platz");

            dock.Activate("layers");
            page.UpdateLayout();

            var body = (FrameworkElement)strip.FindName("Body");

            Check.That(dock.IsShown("layers") && body.Visibility == Visibility.Visible,
                       "und vorn geholt steht er offen - zugeklappt liesse er das Feld leer");
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

            // Und jetzt die Luecke, durch die der eigentliche Fehler ging.
            //
            // Geprueft wurde hier immer nur die BELICHTUNG - und die steht in
            // Adjustments. Alles, was im Werkzeugstapel liegt, blieb ungeprueft:
            // Farbbereiche, Zonen, Kurven, Weissabgleich. Genau die kamen an der
            // Ebene nie an, weil nur Adjustments zurueckgeschrieben wurde.
            //
            // Im Fenster war das die verwirrendste Art von Fehler, die es gibt: Zwei
            // Regler nebeneinander, einer wirkt, der andere nicht.
            var zones = (System.Windows.Controls.Slider)tools.FindName("GainBrightSlider");

            zones.Value = zones.Maximum;

            var gain = second.Tools!.Tools.OfType<LiftGammaGainTool>().FirstOrDefault();

            Check.That(gain is not null, "die Ebene fuehrt die Zonen");

            Check.That(gain is not null && !gain.IsNeutral,
                       "und ein Zonenregler kommt an der Ebene an - nicht nur die Belichtung",
                       gain is null ? "fehlt" : $"Gain {gain.Gain.R:0.00}");

            var warmth = (System.Windows.Controls.Slider)tools.FindName("TemperatureSlider");

            warmth.Value = warmth.Maximum;

            var balance = second.Tools!.Tools.OfType<WhiteBalanceTool>().FirstOrDefault();

            Check.That(balance is not null && !balance.IsNeutral,
                       "der Weissabgleich ebenso",
                       balance is null ? "fehlt" : "neutral geblieben");

            // Und die erste Ebene darf davon nichts abbekommen haben.
            var firstGain = first.Tools!.Tools.OfType<LiftGammaGainTool>().FirstOrDefault();

            Check.That(firstGain is null || firstGain.IsNeutral,
                       "waehrend die andere Ebene unberuehrt bleibt");

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
    /// Rastern muss im ATELIER ankommen - nicht nur im Prozessor.
    ///
    /// Die Rechnung stimmt, der Prozessor traegt alle neun Verfahren, und das
    /// Bedienfeld schreibt das Richtige in den Stapel. Alles drei ist geprueft, und
    /// gemeldet wurde trotzdem dreimal "kein Unterschied". Zwischen dem Bedienfeld
    /// und dem Bildpunkt auf dem Schirm liegt naemlich noch die Seite: Sie haelt eine
    /// vorbereitete Korrektur, tauscht sie beim Wechsel der gewaehlten Ebene aus und
    /// zeichnet erst nach einem Zeitgeber wieder voll.
    ///
    /// Hier wird deshalb die Seite bedient wie von Hand - Regler ziehen, Liste
    /// umstellen - und das GEZEICHNETE Bild verglichen.
    /// </summary>
    private static void RasterReachesTheAtelier(string folder)
    {
        Check.Group("Rastern kommt im Atelier an");

        string picture = Path.Combine(folder, "raster_bild.png");

        Write(picture, 128);

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1100,
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
            page.Open(picture);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");

            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Pump(TimeSpan.FromSeconds(1.2), () => false);

            var display = (System.Windows.Controls.Image)page.FindName("Display");
            var tools = (GradingPanel)page.FindName("Tools");

            var box = (System.Windows.Controls.ComboBox)tools.FindName("DitherPatternBox");
            var amount = (System.Windows.Controls.Slider)tools.FindName("DitherSlider");
            var levels = (System.Windows.Controls.Slider)tools.FindName("DitherLevelsSlider");

            if (box is null || amount is null || levels is null)
            {
                Check.That(false, "die Rasterliste ist Teil der Farbspalte");
                return;
            }

            byte[] plain = Shot(display);

            levels.Value = 2;
            amount.Value = 1;

            ItSurvivesASelectionChange(page, tools, box, display, plain);

            // Vier: geordnet, zufaellig, Linien, Floyd-Steinberg - dann Atkinson.
            foreach (int pick in new[] { 0, 4 })
            {
                box.SelectedIndex = pick;

                // FEST gewartet und nicht auf das Ergebnis: Der volle Durchgang kommt
                // erst nach einem Zeitgeber, und die Fehlerdiffusion laeuft ueberhaupt
                // nur dort. Wer auf den Unterschied wartet, hat einen Test, der bei
                // Erfolg schnell und bei Misserfolg langsam ist - und der bestanden
                // gilt, sobald die Maschine gerade schnell genug war.
                //
                // Die feste Zeit ist das Siebenfache des Zeitgebers (180 ms) - Rand
                // genug fuer ein kleines Bild. Frueher waren es fuenf Sekunden an
                // zwoelf Stellen, und die Reihe lief ueber eine Minute.
                Pump(TimeSpan.FromSeconds(1.2), () => false);

                Check.That(tools.Prepared.Frame.Length == (pick >= 3 ? 1 : 0),
                           $"Eintrag {pick}: der vorbereitete Stapel fuehrt den Rahmendurchgang",
                           $"{tools.Prepared.Frame.Length}");

                Check.That(Differs(plain, Shot(display)),
                           $"Eintrag {pick} veraendert das gezeichnete Bild");
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Eine Rastereinstellung muss einen WECHSEL DER GEWAEHLTEN EBENE ueberleben.
    ///
    /// Das war der eigentliche Grund, und er ist unangenehm gut versteckt: Die
    /// Aufnahme des Stapels zaehlte zwei ihrer sechs Listen auf. Weil sie sofort in
    /// die Einstellungen zurueckgeschrieben wird und der naechste Ladevorgang daraus
    /// liest, wischte jeder Wechsel der Auswahl alles aus, was nicht aufgezaehlt war
    /// - Optik, Geometrie, Renderdaten, Rahmendurchgaenge.
    ///
    /// Im Fenster sah das aus, als taete der Regler nichts. Er tat etwas, und kurz
    /// darauf nahm es ihm jemand wieder ab. Geprueft wird deshalb nicht der Regler,
    /// sondern was nach einem Klick woanders noch davon uebrig ist.
    /// </summary>
    private static void ItSurvivesASelectionChange(
        AtelierPage page, GradingPanel tools,
        System.Windows.Controls.ComboBox box,
        System.Windows.Controls.Image display, byte[] plain)
    {
        // Atkinson - eine Fehlerdiffusion, also ein Rahmendurchgang.
        box.SelectedIndex = 4;

        Pump(TimeSpan.FromSeconds(1.2), () => false);

        Check.That(tools.Prepared.Frame.Length == 1, "gesetzt ist der Durchgang da",
                   $"{tools.Prepared.Frame.Length}");

        // Und jetzt das, was jeder tut: eine Ebene anklicken und zurueck.
        var strip = (LayerPanel)page.FindName("Layers");

        strip.AddAdjustment();

        Pump(TimeSpan.FromSeconds(1.2), () => false);

        var pass = strip.Stack.Layers.First(l => l.Content == LayerContent.Pass);

        Select(strip, pass);

        Pump(TimeSpan.FromSeconds(1.2), () => false);

        Check.That(tools.Prepared.Frame.Length == 1,
                   "nach einem Wechsel der Auswahl ist er immer noch da",
                   $"{tools.Prepared.Frame.Length}");

        Check.That(Differs(plain, Shot(display)),
                   "und das Bild zeigt ihn weiterhin");

        // Und dieselbe Frage fuer die Optik, die an derselben Luecke haengt.
        box.SelectedIndex = 0;

        Pump(TimeSpan.FromSeconds(1.2), () => false);

        Check.That(tools.Prepared.Optics.Length > 0,
                   "auch das geordnete Raster ueberlebt", $"{tools.Prepared.Optics.Length}");
    }

    /// <summary>
    /// Die gemeldete Sitzung, Zeile fuer Zeile aus der Einstellungsdatei nachgebaut.
    ///
    /// Vier Ebenen: unten das Bild selbst als Pass - AUSGEBLENDET und mit einem
    /// winzigen Versatz -, darueber zwei Bildebenen auf Normal und ganz oben eine
    /// dritte, die auf DIESELBE DATEI zeigt wie das geoeffnete Bild. Nur die oberste
    /// ist sichtbar.
    ///
    /// Das ist keine Aufstellung, die jemand von Hand baut, und genau deshalb kam sie
    /// in keinem der vier Nachbauten vor. Sie entsteht, wenn eine Sitzung
    /// zurueckkommt.
    /// </summary>
    private static void TheReportedSessionComesBack(string folder)
    {
        Check.Group("Die gemeldete Sitzung, aus der Einstellungsdatei nachgebaut");

        string picture = Path.Combine(folder, "sitzung_bild.png");
        string glareA = Path.Combine(folder, "sitzung_glanz_a.png");
        string glareB = Path.Combine(folder, "sitzung_glanz_b.png");

        Write(picture, 70);
        Write(glareA, 150);
        Write(glareB, 210);

        // Die Reihenfolge NACH dem gemeldeten Zug: das Bild ganz nach unten gezogen.
        var saved = new LayerStack
        {
            Layers =
            {
                new ImageLayer
                {
                    Content = LayerContent.Image, Source = picture, Name = "Bild0035",
                    Mode = BlendMode.Normal, Visible = true,
                },
                new ImageLayer
                {
                    Content = LayerContent.Pass, Source = "", Name = "Bild",
                    Mode = BlendMode.Normal, Visible = false,
                    Place = new LayerTransform { OffsetX = -0.00036f, OffsetY = 0.0128f },
                },
                new ImageLayer
                {
                    Content = LayerContent.Image, Source = glareB, Name = "Glanz B",
                    Mode = BlendMode.Normal, Visible = false,
                },
                new ImageLayer
                {
                    Content = LayerContent.Image, Source = glareA, Name = "Glanz A",
                    Mode = BlendMode.Normal, Visible = false,
                },
            },
        };

        var settings = new AppSettings { Layers = saved };
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 900,
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
            page.Open(picture);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");

            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Pump(TimeSpan.FromSeconds(2), () => false);

            var strip = (LayerPanel)page.FindName("Layers");
            var display = (System.Windows.Controls.Image)page.FindName("Display");
            var live = strip.Stack.Layers;

            Check.That(live.Count == 4, "vier Ebenen kommen zurueck", $"{live.Count}");
            if (live.Count != 4) return;

            byte[] start = Shot(display);

            // Von oben nach unten einblenden - genau die gemeldete Reihenfolge.
            strip.SetVisible(live[3], true);
            Pump(TimeSpan.FromSeconds(6), () => Differs(Shot(display), start));

            byte[] one = Shot(display);

            Check.That(Differs(start, one),
                       "die oberste Glanzebene erscheint, sobald sie eingeblendet wird");

            // Die zweite liegt UNTER der ersten, und die erste ist auf Normal bei
            // voller Deckkraft - sie deckt alles. Dass sich dann nichts aendert, ist
            // richtig; geprueft wird deshalb die zweite FUER SICH.
            strip.SetVisible(live[3], false);
            strip.SetVisible(live[2], true);

            Pump(TimeSpan.FromSeconds(6), () => Differs(Shot(display), start));

            Check.That(Differs(start, Shot(display)),
                       "und die zweite ebenso, wenn sie nicht verdeckt wird");
        }
        finally
        {
            window.Close();
        }

        // Und dieselbe Aufstellung rein gerechnet, ohne Fenster: Damit steht fest, ob
        // der Composer sie verschluckt oder die Seite sie nie zu sehen bekommt.
        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);

        foreach (string file in new[] { picture, glareA, glareB })
        {
            var loaded = LayeredFrameLoader.Load(file, null);
            if (loaded is not null) sources[file] = loaded;
        }

        if (sources.TryGetValue(picture, out var ground)) sources[""] = ground;

        if (sources.Count < 4)
        {
            Check.That(false, "die drei Dateien lassen sich lesen", $"{sources.Count}");
            return;
        }

        float Middle(LayerStack stack)
        {
            var built = LayerComposer.Compose(stack, sources);

            return built is null ? -1f : built.R[built.Height / 2 * built.Width + built.Width / 2];
        }

        var bare = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Image, Source = picture, Mode = BlendMode.Normal },
                new ImageLayer { Content = LayerContent.Pass, Source = "", Visible = false,
                                 Place = new LayerTransform { OffsetY = 0.0128f } },
                new ImageLayer { Content = LayerContent.Image, Source = glareB, Visible = false,
                                 Mode = BlendMode.Normal },
                new ImageLayer { Content = LayerContent.Image, Source = glareA, Visible = false,
                                 Mode = BlendMode.Normal },
            },
        };

        float without = Middle(bare);

        bare.Layers[3].Visible = true;

        float with = Middle(bare);

        Console.WriteLine($"         gerechnet: ohne Glanz {without:0.0000}, mit Glanz {with:0.0000}");

        Check.That(Math.Abs(with - without) > 0.01f,
                   "der Composer selbst sieht die eingeblendete Ebene sehr wohl",
                   $"{without:0.0000} gegen {with:0.0000}");
    }

    /// <summary>
    /// Eine eingeblendete Ebene muss SOFORT erscheinen - nicht erst, wenn man noch
    /// eine zweite einblendet.
    ///
    /// Gemeldet wurde genau das: Erst die beiden Glanzebenen einblenden, nichts
    /// passiert; danach die hintere Bildebene einblenden, und ploetzlich stehen alle
    /// drei da. Das ist der Fingerabdruck eines Durchgangs, der mit einem Stand
    /// rechnet, der schon veraltet ist - die zweite Aenderung holt dann nach, was die
    /// erste versaeumt hat.
    ///
    /// Gemessen wird am WIRKLICH GEZEICHNETEN Bild und nicht am Stapel: Ob eine Ebene
    /// im Stapel auf sichtbar steht, war nie die Frage.
    /// </summary>
    private static void ShowingALayerShowsItAtOnce(string folder)
    {
        Check.Group("Eine eingeblendete Ebene erscheint sofort");

        string ground = Path.Combine(folder, "sicht_grund.png");
        string glow = Path.Combine(folder, "sicht_glanz.png");

        Write(ground, 40);
        Write(glow, 200);

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 900,
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
            page.Open(ground);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");

            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            var strip = (LayerPanel)page.FindName("Layers");
            var display = (System.Windows.Controls.Image)page.FindName("Display");

            // Zwei Glanzebenen obenauf auf Screen, dazu eine gewoehnliche Bildebene -
            // der gemeldete Aufbau.
            strip.AddImage(glow);
            strip.AddImage(glow);
            strip.AddImage(glow);

            Pump(TimeSpan.FromSeconds(5), () => display.Source is not null);

            var added = strip.Stack.Layers.Where(l => l.Content == LayerContent.Image).ToList();

            if (added.Count < 3)
            {
                Check.That(false, "drei Bildebenen liegen im Stapel", $"{added.Count}");
                return;
            }

            var first = added[0];
            var glowA = added[1];
            var glowB = added[2];

            foreach (var layer in new[] { glowA, glowB })
            {
                layer.OnTop = true;
                layer.Mode = BlendMode.Screen;
            }

            foreach (var layer in new[] { first, glowA, glowB }) strip.SetVisible(layer, false);

            Pump(TimeSpan.FromSeconds(1.2), () => false);

            byte[] hidden = Shot(display);

            // Jetzt die beiden Glanzebenen - und zwar NUR die.
            strip.SetVisible(glowA, true);
            strip.SetVisible(glowB, true);

            Pump(TimeSpan.FromSeconds(1.2), () => false);

            byte[] withGlow = Shot(display);

            Check.That(Differs(hidden, withGlow),
                       "die beiden Glanzebenen erscheinen, sobald man sie einblendet");

            // Und die Gegenprobe auf die gemeldete Reihenfolge: Erst die dritte Ebene
            // einzublenden darf nicht der Augenblick sein, in dem die ersten beiden
            // auftauchen.
            strip.SetVisible(first, true);

            Pump(TimeSpan.FromSeconds(1.2), () => false);

            byte[] all = Shot(display);

            Check.That(Differs(withGlow, all),
                       "und die dritte ebenso, wenn sie an der Reihe ist");

            // ---- und jetzt derselbe Griff an Ebenen, die NIE GELESEN wurden ----
            //
            // Das ist der Unterschied, auf den es ankommt. Oben lagen die Dateien
            // schon im Vorrat: Sie wurden beim Hinzufuegen gelesen, und Ausblenden
            // wirft sie nicht weg. Wer FrameFlip schliesst und wieder oeffnet, hat
            // eine ausgeblendete Ebene dagegen NOCH NIE gelesen - sie wird erst
            // geholt, wenn sie gebraucht wird, und das laeuft nebenher.
            //
            // Ein Bild neu zu oeffnen leert den Vorrat und stellt genau diesen Stand
            // her, ohne dass das Fenster dafuer zugemacht werden muss.
            foreach (var layer in new[] { first, glowA, glowB }) strip.SetVisible(layer, false);

            page.Open(ground);

            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird wieder geladen");
                return;
            }

            Pump(TimeSpan.FromSeconds(2), () => false);

            // Nach dem Oeffnen kann der Stapel neu aufgebaut worden sein - dann
            // zeigen die alten Verweise auf Ebenen, die niemand mehr ansieht.
            var again = strip.Stack.Layers.Where(l => l.Content == LayerContent.Image).ToList();

            Console.WriteLine($"         Ebenen nach dem Oeffnen: {again.Count}, " +
                              $"dieselben Objekte: {again.Count > 2 && ReferenceEquals(again[1], glowA)}");

            if (again.Count > 2)
            {
                glowA = again[1];
                glowB = again[2];
                first = again[0];
            }

            byte[] cold = Shot(display);

            strip.SetVisible(glowA, true);
            strip.SetVisible(glowB, true);

            // Gelesen wird nebenher - es wird also gewartet, und zwar auf das BILD
            // und nicht auf eine feste Zeit.
            Pump(TimeSpan.FromSeconds(8), () => Differs(Shot(display), cold));

            byte[] warm = Shot(display);

            Check.That(Differs(cold, warm),
                       "auch ungelesene Ebenen erscheinen, sobald man sie einblendet");

            // ---- und der gemeldete Aufbau: das GRUNDBILD selbst ausgeblendet ----
            //
            // Genau so kam die Sitzung zurueck: Nur eine Bildebene hatte einen
            // gefuellten Punkt, alles andere war aus - auch die unterste Ebene, also
            // das Bild selbst. Von oben nach unten eingeblendet passierte nichts, bis
            // die unterste an der Reihe war; dann erschienen alle auf einmal.
            //
            // Das ist die eine Aufstellung, die sonst nirgends vorkommt, denn von
            // Hand blendet niemand das Bild aus, auf dem er arbeitet.
            var picture = strip.Stack.Layers.FirstOrDefault(l => l.Content == LayerContent.Pass);

            if (picture is null)
            {
                Check.That(false, "es gibt eine Bildebene");
                return;
            }

            foreach (var layer in new[] { glowA, glowB }) strip.SetVisible(layer, false);

            strip.SetVisible(first, true);
            strip.SetVisible(picture, false);

            Pump(TimeSpan.FromSeconds(2), () => false);

            byte[] noPicture = Shot(display);

            strip.SetVisible(glowA, true);

            Pump(TimeSpan.FromSeconds(6), () => Differs(Shot(display), noPicture));

            Check.That(Differs(noPicture, Shot(display)),
                       "eine Glanzebene erscheint auch, wenn das Grundbild aus ist");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Ein Schnitt darf die Ebene nie ganz wegschneiden.
    ///
    /// Sonst gibt es sie noch, sie zeigt aber nichts, und alle acht Griffe liegen
    /// aufeinander - herauszukommen waere nur noch ueber den Doppelklick, und den
    /// muesste man erst kennen. Ein Rest bleibt deshalb immer stehen.
    /// </summary>
    private static void TheCropAlwaysLeavesSomething()
    {
        Check.Group("Ein Schnitt laesst immer einen Rest stehen");

        var place = new LayerTransform();

        // Die linke Kante weit ueber die rechte hinausgezogen.
        var far = PlacementAdorner.WithCrop(place, PlacementAdorner.CropGrip.Left, 5f, 0.5f);

        Check.That(far.CropLeft + far.CropRight <= 0.985f,
                   "nach links ueber die rechte Kante hinaus bleibt ein Rest",
                   $"{far.CropLeft:0.000} + {far.CropRight:0.000}");

        // Und nach aussen: Ein Schnitt ist nie negativ - das waere ein Rand aus
        // nichts, den niemand verlangt hat.
        var out_ = PlacementAdorner.WithCrop(place, PlacementAdorner.CropGrip.Top, 0.5f, -3f);

        Check.That(out_.CropTop >= 0f, "und nach aussen gezogen bleibt er bei null",
                   $"{out_.CropTop:0.000}");

        // Der gewoehnliche Fall muss dabei genau ankommen.
        var half = PlacementAdorner.WithCrop(place, PlacementAdorner.CropGrip.BottomRight,
                                             0.75f, 0.6f);

        Check.Near(half.CropRight, 0.25, 0.001, "eine Ecke setzt beide Kanten - rechts");
        Check.Near(half.CropBottom, 0.4, 0.001, "und unten");
    }

    /// <summary>Das gezeichnete Bild als Bytes - die einzige Wahrheit, die zaehlt.</summary>
    private static byte[] Shot(System.Windows.Controls.Image display)
    {
        if (display.Source is not System.Windows.Media.Imaging.BitmapSource source)
            return Array.Empty<byte>();

        int stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];

        source.CopyPixels(pixels, stride, 0);

        return pixels;
    }

    /// <summary>Ob zwei Aufnahmen sich sichtbar unterscheiden.</summary>
    private static bool Differs(byte[] a, byte[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return a.Length != b.Length;

        long sum = 0;

        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);

        double mean = (double)sum / a.Length;

        Console.WriteLine($"         Unterschied im Bild: {mean:0.00} Stufen je Byte");

        return mean > 0.5;
    }

    /// <summary>
    /// Die rechte Spalte: zwei Reiter, jeder in voller Hoehe, Breite gemerkt.
    ///
    /// Frueher standen hier Eigenschaften, Farbe und Ebenen uebereinander, mit einem
    /// Griff dazwischen - und kaempften um dieselbe Hoehe. Die Geschichte davor steht
    /// weiter unten und bleibt stehen, weil sie erklaert, warum es Reiter wurden.
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
    private static void TheDockRemembersHowItStood(string path)
    {
        Check.Group("Die Andockflaeche: Reiter, Zonen, und sie merkt sich, wie sie stand");

        // Eine Breite, die NICHT die Voreinstellung ist - sonst prueft der Test nur,
        // dass eine Voreinstellung eine Voreinstellung ist. Und noch keine Anordnung:
        // Wer von der alten Spalte kommt, soll seine Breite mitnehmen.
        var settings = new AppSettings { AtelierColumnWidth = 380 };
        int saved = 0;
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => saved++);

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

            var dock = (DockHost)page.FindName("Dock");
            var strip = (LayerPanel)page.FindName("Layers");
            var colour = (GradingPanel)page.FindName("Tools");
            var slot = (System.Windows.Controls.ContentControl)page.FindName("HistogramSlot");

            Check.That(dock is not null && strip is not null && colour is not null && slot is not null,
                       "die Seite hat eine Andockflaeche mit Farbe, Ebenen und Verteilung");

            if (dock is null || strip is null || colour is null || slot is null) return;

            Check.That(ReferenceEquals(slot.Content, colour.HistogramBlock),
                       "die Verteilung steht in ihrem eigenen Feld, nicht mehr im Farbfeld");

            Check.Near(dock.Layout.RightWidth, 380, 1e-9, "die gemerkte Breite der alten Spalte gilt weiter");
            Check.That(colour.ActualWidth is > 330 and < 381, "und so breit ist die Farbe auch",
                       $"{colour.ActualWidth:0}");

            // Das war der Kern der ersten Klage: Drei Abschnitte uebereinander
            // kaempften um dieselbe Hoehe. In einer Gruppe steht immer genau EIN Feld
            // da - und das hat alles.
            Check.That(dock.IsShown("colour") && dock.IsShown("histogram"),
                       "zu Beginn liegen die Farbe und die Verteilung vorn");

            Check.That(!dock.IsAvailable("layers") && !dock.IsShown("layers"),
                       "ohne Bild haben die Ebenen nichts zu zeigen");

            page.Open(path);

            if (!Pump(TimeSpan.FromSeconds(10), () => dock.IsAvailable("layers")))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            page.UpdateLayout();

            double colourHeight = colour.ActualHeight;

            dock.Activate("layers");
            page.UpdateLayout();

            Check.That(dock.IsShown("layers") && !dock.IsShown("colour"),
                       "der Ebenenreiter zeigt die Ebenen - und nur sie");

            // Beide bekommen dieselbe volle Hoehe. Das ist die Zusage des Reiters:
            // Kein Feld schrumpft, weil ein anderes Platz braucht.
            Check.Near(strip.ActualHeight, colourHeight, 1,
                       "und zwar in derselben vollen Hoehe wie vorher die Farbe");

            Check.That(colourHeight > 500,
                       "und die ist fast die ganze Spalte, nicht ein Drittel davon",
                       $"{colourHeight:0}");

            // Und nun der Grund fuer das Andocken: die Ebenen nach links, als eigenes
            // Feld. Danach stehen Farbe UND Ebenen gleichzeitig da.
            int before = saved;

            dock.MovePanel("layers", DockZone.Left, 0, asTab: false);
            page.UpdateLayout();

            Check.That(dock.IsShown("layers") && dock.IsShown("colour"),
                       "nach links gezogen stehen Ebenen und Farbe nebeneinander");

            Check.That(strip.ActualWidth > 200 && colour.ActualWidth > 200,
                       "und beide haben Platz",
                       $"Ebenen {strip.ActualWidth:0}, Farbe {colour.ActualWidth:0}");

            Check.That(saved > before && settings.AtelierDock?.Find("layers") is { Zone: DockZone.Left },
                       "die Anordnung wird sofort gemerkt");

            // Eine neue Seite mit denselben Einstellungen - wie beim naechsten Start.
            var again = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
            var againDock = (DockHost)again.FindName("Dock");

            Check.That(againDock.Layout.Find("layers") is { Zone: DockZone.Left } &&
                       againDock.Layout.Find("colour") is { Zone: DockZone.Right },
                       "und beim naechsten Start steht alles wieder, wo es stand");

            dock.MovePanel("histogram", DockZone.Bottom, 0, asTab: false);
            page.UpdateLayout();

            Check.That(settings.AtelierDock?.Find("histogram") is { Zone: DockZone.Bottom } &&
                       ReferenceEquals(slot.Content, colour.HistogramBlock) && slot.ActualHeight > 60,
                       "auch unter das Bild - und die Verteilung zieht mit");

            dock.ResetLayout();
            page.UpdateLayout();

            Check.That(dock.Layout.Find("layers") is { Zone: DockZone.Right, Group: 1 } &&
                       dock.Layout.Find("histogram") is { Zone: DockZone.Right, Group: 0 },
                       "und wer sich verzogen hat, kommt zur Grundanordnung zurueck");

            // Nun dieselben Wege mit der Zielsuche, die auch die Maus nimmt - an
            // Punkten, die aus den Feldern selbst gerechnet sind. Die Geometrie der
            // Ziele ist, was beim Ziehen schiefgehen kann.
            System.Windows.Point In(FrameworkElement element, double x, double y)
                => element.TranslatePoint(new System.Windows.Point(element.ActualWidth * x, y < 0 ? y : element.ActualHeight * y), dock);

            // Den Ebenenreiter an den unteren Rand seiner eigenen Gruppe: Die Gruppe
            // teilt sich, Farbe oben, Ebenen darunter.
            Check.That(dock.DropAt("layers", In(colour, 0.5, 0.97)), "am unteren Rand der Farbe ist ein Ziel");
            page.UpdateLayout();

            Check.That(dock.Layout.Right.Count == 3 &&
                       dock.Layout.Right[1].Panels.SequenceEqual(new[] { "colour" }) &&
                       dock.Layout.Right[2].Panels.SequenceEqual(new[] { "layers" }),
                       "ein Reiter an den eigenen Rand gezogen teilt die Gruppe",
                       string.Join(" | ", dock.Layout.Right.Select(g => string.Join(",", g.Panels))));

            Check.That(dock.IsShown("colour") && dock.IsShown("layers"), "und beide stehen da");

            // Die Verteilung auf die Reiterleiste der Ebenen: dort als Reiter, und
            // ihre alte Gruppe verschwindet.
            Check.That(dock.DropAt("histogram", In(strip, 0.5, -12)), "die Reiterleiste ist ein Ziel");
            page.UpdateLayout();

            Check.That(dock.Layout.Right.Count == 2 &&
                       dock.Layout.Right[1].Panels.SequenceEqual(new[] { "layers", "histogram" }) &&
                       dock.Layout.Right[1].Active == "histogram",
                       "auf die Reiterleiste gezogen wird es ein Reiter - und liegt vorn",
                       string.Join(" | ", dock.Layout.Right.Select(g => string.Join(",", g.Panels))));

            // Die Farbe an den linken Bildrand: Dort entsteht die linke Zone.
            var centre = dock.Center!;

            Check.That(dock.DropAt("colour", In(centre, 0, 0.5) + new System.Windows.Vector(20, 0)),
                       "der linke Bildrand ist ein Ziel, solange links nichts steht");
            page.UpdateLayout();

            Check.That(dock.Layout.Find("colour") is { Zone: DockZone.Left } && dock.IsShown("colour"),
                       "und dort steht die Farbe dann");

            // Die Bildmitte ist kein Ziel - dort loslassen heisst: doch nicht.
            string before2 = System.Text.Json.JsonSerializer.Serialize(dock.Layout);

            Check.That(!dock.DropAt("layers", In(centre, 0.5, 0.5)), "die Bildmitte ist kein Ziel");

            // Und ein Feld allein in seiner Gruppe hat dort keinen anderen Ort.
            Check.That(!dock.DropAt("colour", In(colour, 0.5, 0.5)),
                       "allein in der eigenen Gruppe ist jeder Ort derselbe");

            Check.That(System.Text.Json.JsonSerializer.Serialize(dock.Layout) == before2,
                       "wo kein Ziel ist, aendert sich nichts");

            dock.ResetLayout();
            page.UpdateLayout();

            // Gemeldet: Die Felder liessen sich ueber ihre Reiter holen, aber nicht
            // wieder wegnehmen. Ein zweiter Klick klappt jetzt ein - und der Platz
            // geht an die Nachbarn, zuletzt an das Bild.
            double histogramHigh = slot.ActualHeight;
            double pictureWide = centre.ActualWidth;

            dock.Toggle("colour");
            page.UpdateLayout();

            Check.That(!dock.IsShown("colour") && !dock.IsShown("layers") && dock.IsShown("histogram"),
                       "ein zweiter Klick auf den vorderen Reiter klappt die Gruppe ein");
            Check.That(slot.ActualHeight > histogramHigh + 200,
                       "und die Verteilung darueber bekommt den Platz",
                       $"{histogramHigh:0} -> {slot.ActualHeight:0}");

            dock.Toggle("histogram");
            page.UpdateLayout();

            Check.That(centre.ActualWidth > pictureWide + 250,
                       "ist die ganze Zone eingeklappt, wird sie ein Streifen - und das Bild bekommt die Breite",
                       $"{pictureWide:0} -> {centre.ActualWidth:0}");

            dock.Toggle("layers");
            page.UpdateLayout();

            Check.That(dock.IsShown("layers") && !dock.IsShown("histogram") &&
                       Math.Abs(centre.ActualWidth - pictureWide) < 2,
                       "ein Klick im Streifen holt das Feld - und die Zone kommt in alter Breite zurueck",
                       $"{centre.ActualWidth:0} statt {pictureWide:0}");

            Check.That(settings.AtelierDock?.Right[0].Collapsed == true,
                       "was eingeklappt bleibt, merken sich die Einstellungen");

            dock.ResetLayout();
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

            // Der Pinsel: dasselbe Element, dritter Modus. Ohne gemalte Maske an der
            // gewaehlten Ebene faengt er NICHTS - ein Pinsel, der auf nichts malt und
            // trotzdem die Maus nimmt, ist der unangenehmste Zustand von allen.
            Check.That(page.HandleToolKey(System.Windows.Input.Key.B), "B waehlt den Pinsel");

            page.UpdateLayout();

            Check.That(column.Tool == AtelierTool.Brush, "naemlich den Pinsel", $"{column.Tool}");
            Check.That(frame.Mode == AdornerMode.Paint, "und der Rahmen malt", $"{frame.Mode}");

            // Er faengt auch OHNE Maske - sonst gaebe es keinen ersten Strich, mit
            // dem eine entstehen koennte, und der Ring am Zeiger waere unsichtbar.
            Check.That(frame.IsHitTestVisible, "er faengt, auch bevor es eine Maske gibt");

            // Aber er legt NICHTS an, solange niemand malt. Wer ein Werkzeug nur
            // anfasst, um zu sehen, was es tut, soll keine Ebene erzeugt haben.
            var strip = (LayerPanel)page.FindName("Layers");
            int before = strip.Stack.Layers.Count;

            page.HandleToolKey(System.Windows.Input.Key.V);
            page.HandleToolKey(System.Windows.Input.Key.B);
            page.UpdateLayout();

            Check.That(strip.Stack.Layers.Count == before,
                       "und legt dabei keine Ebene an - erst der Strich tut das",
                       $"{strip.Stack.Layers.Count} statt {before}");

            page.HandleToolKey(System.Windows.Input.Key.V);
            page.UpdateLayout();

            // Zuschneiden benutzt DENSELBEN Rahmen wie das Verschieben, nur mit
            // anderen Griffen. Ein zweiter Rahmen daneben haette die Umrechnung ein
            // zweites Mal gebraucht, und zwei Umrechnungen laufen frueher oder
            // spaeter um einen Bildpunkt auseinander.
            Check.That(page.HandleToolKey(System.Windows.Input.Key.C), "C waehlt das Zuschneiden");

            page.UpdateLayout();

            Check.That(column.Tool == AtelierTool.Crop, "naemlich das Zuschneiden", $"{column.Tool}");
            Check.That(frame.IsHitTestVisible, "der Rahmen faengt auch dort");
            Check.That(frame.Mode == AdornerMode.Crop, "und zwar als Schnittrahmen", $"{frame.Mode}");

            page.HandleToolKey(System.Windows.Input.Key.V);
            page.UpdateLayout();

            Check.That(frame.Mode == AdornerMode.Place,
                       "und zurueck als Greifrahmen", $"{frame.Mode}");

            // Die Eigenschaftsspalte sagt, was das gewaehlte Werkzeug tut - und sie
            // zeigt die Pipettenwerte nur bei der Pipette. Ein Abschnitt, der bei
            // jedem Werkzeug dasselbe zeigt, koennte auch weg.
            var properties = (PropertiesPanel)page.FindName("Properties");
            var pickBody = (FrameworkElement)properties.FindName("PickBody");
            var name = (System.Windows.Controls.TextBlock)properties.FindName("ToolName");

            Check.That(properties is not null && pickBody is not null,
                       "die Eigenschaftsspalte ist Teil der Seite");

            Check.That(pickBody.Visibility != Visibility.Visible,
                       "beim Verschieben zeigt sie keine Pipettenwerte");

            string moved = name.Text;

            page.HandleToolKey(System.Windows.Input.Key.I);
            page.UpdateLayout();

            Check.That(pickBody.Visibility == Visibility.Visible,
                       "bei der Pipette sehr wohl");

            Check.That(name.Text.Length > 0 && name.Text != moved,
                       "und der Name wechselt mit dem Werkzeug", $"{moved} -> {name.Text}");

            page.HandleToolKey(System.Windows.Input.Key.V);
            page.UpdateLayout();

            TheCropAlwaysLeavesSomething();
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
