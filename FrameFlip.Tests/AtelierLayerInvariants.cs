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
            APlainImageHidesTheStrip(folder);
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
    /// Ein gewoehnliches Bild hat nichts zu schichten. Der Streifen bleibt weg -
    /// zweihundert Punkte Hoehe fuer eine einzige Zeile waeren im schmalen Streifen
    /// der teuerste Platz, den es gibt.
    /// </summary>
    private static void APlainImageHidesTheStrip(string folder)
    {
        Check.Group("Ohne Passe bleibt der Streifen weg");

        // Ein PNG, ueber denselben Weg geschrieben, den auch der Export nimmt.
        string png = Path.Combine(folder, "einzel.png");
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using (var file = File.Create(png)) encoder.Save(file);

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
            var file = (System.Windows.Controls.TextBlock)page.FindName("FileText");

            bool loaded = Pump(TimeSpan.FromSeconds(10), () => file.Text.Contains("einzel"));
            Check.That(loaded, "das Bild wird geladen");

            Check.That(strip.Visibility != Visibility.Visible,
                       "und der Streifen bleibt weg");
        }
        finally
        {
            window.Close();
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
