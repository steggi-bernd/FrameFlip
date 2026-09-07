using System.IO;
using FrameFlip.Configuration;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Das Tor zwischen Handy und Festplatte.
///
/// Von allem, was FrameFlip tut, ist das die Stelle, an der ein Fehler wirklich weh
/// taete: Bis hierher konnte die Gegenseite nur zusehen. Ein Dateizugriff macht aus
/// dem Lesekanal einen Schreibkanal, und ein Name, der irgendwo anders hinzeigt als
/// in den Austauschordner, ist dann kein Schoenheitsfehler mehr.
///
/// Deshalb steht hier die Liste der Namen, die NICHT durchkommen duerfen, und zwar
/// ausfuehrlicher als die der erlaubten. Beim Rueckwaertslesen von Angriffen lernt
/// man: Es sind selten die Faelle, an die jemand gedacht hat.
/// </summary>
public static class VaultInvariants
{
    public static void Run()
    {
        Check.Group("Dateizugriff - was durch das Tor darf");

        // Der Normalfall.
        Check.That(FileVault.IsAllowedName("kitchen.blend"), "eine gewoehnliche .blend");
        Check.That(FileVault.IsAllowedName("Kitchen_v2.BLEND"), "Gross- und Kleinschreibung ist egal");
        Check.That(FileVault.IsAllowedName("szene mit leerzeichen.blend"), "Leerzeichen sind erlaubt");
        Check.That(FileVault.IsAllowedName("übung.blend"), "Umlaute auch");

        // Und alles, was nicht durchkommen darf.
        foreach (string bad in new[]
                 {
                     "",
                     "   ",
                     ".blend",
                     "kitchen.png",
                     "kitchen.blend1",
                     "kitchen.exe",
                     "kitchen.blend.exe",
                     @"..\kitchen.blend",
                     "../kitchen.blend",
                     @"C:\Windows\kitchen.blend",
                     @"\\server\share\kitchen.blend",
                     @"unter\ordner\kitchen.blend",
                     "unter/ordner/kitchen.blend",
                     "kitchen.blend:versteckt",
                     "kitchen.blend ",
                     " kitchen.blend",
                     "CON.blend",
                     "nul.blend",
                     "LPT1.blend",
                     "punkt..blend",
                 })
        {
            Check.That(!FileVault.IsAllowedName(bad), $"abgelehnt: {Show(bad)}");
        }

        // Der Name mit einem Steuerzeichen - schreibt sich schlecht in eine Liste.
        Check.That(!FileVault.IsAllowedName("kitchen\0.blend"), "abgelehnt: Name mit Nullbyte");
        Check.That(!FileVault.IsAllowedName("kitchen\n.blend"), "abgelehnt: Name mit Zeilenumbruch");
        Check.That(!FileVault.IsAllowedName(new string('a', 200) + ".blend"), "abgelehnt: uferloser Name");

        Check.Group("Dateizugriff - das Tor ist zu, bis es jemand aufmacht");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-tresor-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "kitchen.blend"), "x");
            File.WriteAllText(Path.Combine(root, "notiz.txt"), "x");

            // Ausgeschaltet heisst ausgeschaltet - auch fuer eine Datei, die es gibt.
            var closed = new FileVault(readEnabled: false, writeEnabled: false, folder: root);

            Check.That(!closed.ReadEnabled, "ohne Erlaubnis kein Lesen");
            Check.That(closed.List().Count == 0, "und keine Liste");
            Check.That(closed.Resolve("kitchen.blend") is null, "und kein Pfad");
            Check.That(closed.CanRead("kitchen.blend", out _) == VaultRefusal.Disabled,
                       "der Grund steht dabei");

            // Ohne Ordner geht ebenfalls nichts, auch mit Erlaubnis.
            var homeless = new FileVault(readEnabled: true, writeEnabled: true, folder: "");

            Check.That(!homeless.ReadEnabled, "ohne Ordner kein Zugriff");
            Check.That(homeless.CanRead("kitchen.blend", out _) == VaultRefusal.NoFolder, "auch hier mit Grund");

            // Schreiben ohne Lesen gibt es nicht.
            var writeOnly = new FileVault(readEnabled: false, writeEnabled: true, folder: root);

            Check.That(!writeOnly.WriteEnabled, "Schreiben ohne Lesen ist kein Zustand");

            Check.Group("Dateizugriff - mit Erlaubnis");

            var open = new FileVault(readEnabled: true, writeEnabled: false, folder: root);

            var list = open.List();

            Check.That(list.Count == 1, "nur .blend wird aufgelistet", list.Count.ToString());
            Check.That(list[0].Name == "kitchen.blend", "und zwar die richtige", list[0].Name);
            Check.That(list[0].Bytes == 1, "mit ihrer Groesse");

            Check.That(open.CanRead("kitchen.blend", out string? path) == VaultRefusal.None, "sie laesst sich holen");
            Check.That(path == Path.Combine(root, "kitchen.blend"), "und liegt, wo sie soll", path);

            Check.That(open.CanRead("gibtsnicht.blend", out _) == VaultRefusal.Missing,
                       "eine Datei, die es nicht gibt");

            // Der eigentliche Punkt: Kein Name fuehrt aus dem Ordner heraus.
            //
            // Angemerkt: Das Tor prueft zweimal - Namensregel, dann aufgeloester Pfad.
            // Solange die erste Pruefung haelt, kommt die zweite gar nicht zum Zuge;
            // beim Falsifizieren war das nachweisbar, das Entfernen der zweiten liess
            // keine Zusicherung fallen. Sie bleibt trotzdem stehen. Sie kostet drei
            // Zeilen und faengt genau den Fall, in dem jemand spaeter die erste
            // lockert - etwa fuer Unterordner.
            foreach (string escape in new[]
                     {
                         @"..\kitchen.blend",
                         @"..\..\Windows\System32\kitchen.blend",
                         @"unter\kitchen.blend",
                         @"C:\Windows\kitchen.blend",
                     })
            {
                Check.That(open.Resolve(escape) is null, $"fuehrt nicht hinaus: {Show(escape)}");
                Check.That(open.CanRead(escape, out _) == VaultRefusal.BadName, $"und wird begruendet: {Show(escape)}");
            }

            // Schreiben: erst verboten, dann erlaubt, aber nie ueberschreibend.
            Check.That(open.CanWrite("neu.blend", 100, out _) == VaultRefusal.WriteDisabled,
                       "ohne Schreiberlaubnis wird nichts abgelegt");

            var writable = new FileVault(readEnabled: true, writeEnabled: true, folder: root);

            Check.That(writable.CanWrite("neu.blend", 100, out string? target) == VaultRefusal.None,
                       "mit Erlaubnis schon");

            // Jede angenommene Datei traegt die Markierung: Man sieht in einem Ordner
            // voller .blend-Dateien sofort, welche von aussen kam - und das will man
            // wissen, BEVOR man sie oeffnet.
            Check.That(target == Path.Combine(root, "neu_exchanged.blend"),
                       "sie landet markiert im Austauschordner", target);

            // Und dieselbe Markierung ist der Grund, warum nie etwas verdraengt wird:
            // Selbst eine Datei, die genauso heisst wie die, an der jemand seit
            // Wochen arbeitet, bekommt einen anderen Namen.
            Check.That(writable.CanWrite("kitchen.blend", 100, out string? beside) == VaultRefusal.None,
                       "eine gleichnamige Datei darf kommen");

            Check.That(beside == Path.Combine(root, "kitchen_exchanged.blend"),
                       "sie legt sich daneben statt darueber", beside);

            Check.That(File.ReadAllText(Path.Combine(root, "kitchen.blend")) == "x",
                       "die vorhandene bleibt unangetastet");

            // Zweimal dasselbe: Auch die zweite verdraengt die erste nicht.
            File.WriteAllText(Path.Combine(root, "kitchen_exchanged.blend"), "erste");

            Check.That(writable.CanWrite("kitchen.blend", 100, out string? third) == VaultRefusal.None
                       && third == Path.Combine(root, "kitchen_exchanged_2.blend"),
                       "und die naechste bekommt eine Nummer", third);

            // Zurueckgeschickt wird nicht doppelt markiert.
            Check.That(FileVault.Exchanged("szene_exchanged.blend") == "szene_exchanged.blend",
                       "eine markierte Datei wird nicht noch einmal markiert",
                       FileVault.Exchanged("szene_exchanged.blend"));

            Check.That(FileVault.Exchanged("szene.blend") == "szene_exchanged.blend", "und eine andere schon");

            Check.That(writable.CanWrite("riesig.blend", FileVault.MaxBytes + 1, out _) == VaultRefusal.TooBig,
                       "und uferlos geht auch nicht");

            Check.That(writable.CanWrite(@"..\draussen.blend", 100, out _) == VaultRefusal.BadName,
                       "auch beim Schreiben fuehrt kein Weg hinaus");

            Check.That(FileVault.PartialPath(@"C:\a\b.blend").EndsWith(".teil"),
                       "eine halbe Datei heisst nicht wie eine ganze");

            // Jeder Grund hat einen Text - sonst stuende in der App eine leere Zeile.
            foreach (VaultRefusal refusal in Enum.GetValues<VaultRefusal>())
            {
                if (refusal == VaultRefusal.None) continue;

                Check.That(FileVault.Explain(refusal).Length > 10, $"der Grund {refusal} ist erklaert");
            }

            Check.Group("Dateizugriff - die Einstellungen halten sich selbst in Ordnung");

            var settings = new AppSettings
            {
                RemoteEnabled = true,
                PairingSecret = "x",
                FileAccessEnabled = true,
                FilePushEnabled = true,
                FileFolder = "  " + root + "  ",
            };

            settings.Normalize();

            Check.That(settings.FileFolder == root, "der Ordner wird beschnitten", settings.FileFolder);
            Check.That(settings.FileAccessEnabled, "und bleibt eingeschaltet");

            // Ohne Ordner faellt beides.
            settings.FileFolder = "";
            settings.Normalize();

            Check.That(!settings.FileAccessEnabled, "ohne Ordner kein Dateizugriff");
            Check.That(!settings.FilePushEnabled, "und erst recht kein Ablegen");

            // Ohne Fernsteuerung ebenso - es gibt dann niemanden, der zugreift.
            settings.FileFolder = root;
            settings.FileAccessEnabled = true;
            settings.FilePushEnabled = true;
            settings.RemoteEnabled = false;
            settings.PairingSecret = "";
            settings.Normalize();

            Check.That(!settings.FileAccessEnabled, "ohne Fernsteuerung kein Dateizugriff");
            Check.That(!settings.FilePushEnabled, "und ohne den auch kein Ablegen");

            // Der Render aus der Ferne braucht beides: jemanden, der ihn ausloest,
            // und ein Blender, das rechnet. Eingeschaltet ohne eines von beidem waere
            // ein Schalter, der etwas verspricht und nichts tut.
            var render = new AppSettings
            {
                RemoteEnabled = true,
                PairingSecret = "x",
                HeadlessRenderEnabled = true,
                BlenderPath = @"  C:\Blenderlender.exe  ",
            };

            render.Normalize();

            Check.That(render.BlenderPath == @"C:\Blenderlender.exe", "der Pfad wird beschnitten",
                       render.BlenderPath);
            Check.That(render.HeadlessRenderEnabled, "und bleibt eingeschaltet");

            render.BlenderPath = "";
            render.Normalize();

            Check.That(!render.HeadlessRenderEnabled, "ohne Blender kein Render aus der Ferne");

            render.BlenderPath = @"C:\Blenderlender.exe";
            render.HeadlessRenderEnabled = true;
            render.RemoteEnabled = false;
            render.PairingSecret = "";
            render.Normalize();

            Check.That(!render.HeadlessRenderEnabled, "und ohne Fernsteuerung auch nicht");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception) { }
        }

        Library();

        Check.Group("Dateizugriff - die Stueckelung");

        var payload = new byte[1000];
        new Random(7).NextBytes(payload);

        byte[] frame = Envelope.Chunk(42, 3, last: false, payload);

        Check.That(Envelope.TryRead(frame, out PayloadKind kind, out byte[] body), "ein Stueck ist lesbar");
        Check.That(kind == PayloadKind.Chunk, "und als solches erkannt");

        Check.That(Envelope.TryReadChunk(body, out int transfer, out int index, out bool last, out byte[] data),
                   "der Kopf laesst sich lesen");

        Check.That(transfer == 42, "der Vorgang stimmt", transfer.ToString());
        Check.That(index == 3, "die Nummer auch", index.ToString());
        Check.That(!last, "und es war nicht das letzte");
        Check.That(data.SequenceEqual(payload), "die Daten kommen unveraendert an");

        byte[] ending = Envelope.Chunk(42, 4, last: true, Array.Empty<byte>());

        Check.That(Envelope.TryRead(ending, out _, out byte[] endBody), "ein leeres Schlussstueck ist erlaubt");
        Check.That(Envelope.TryReadChunk(endBody, out _, out _, out bool wasLast, out byte[] empty) && wasLast,
                   "und als Schluss erkannt");
        Check.That(empty.Length == 0, "ohne Daten");

        // Ein abgeschnittener Kopf ist kein Stueck mit geratener Nummer.
        Check.That(!Envelope.TryReadChunk(new byte[] { 1, 2, 3 }, out _, out _, out _, out _),
                   "ein abgeschnittener Kopf wird verworfen");

        Check.That(Envelope.ChunkBytes < 1024 * 1024,
                   "ein Stueck bleibt unter der Grenze des Relays", Envelope.ChunkBytes.ToString());

        // Das angeforderte Bild traegt seinen Pfad mit sich.
        byte[] picture = Envelope.Image(@"F:\Projekte\TRACER\render\cam1_0001.png", new byte[] { 1, 2, 3 });

        Check.That(Envelope.TryRead(picture, out PayloadKind pictureKind, out byte[] pictureBody)
                   && pictureKind == PayloadKind.Image,
                   "ein angefordertes Bild ist als solches erkennbar");

        Check.That(Envelope.TryReadImage(pictureBody, out string picturePath, out byte[] jpeg)
                   && picturePath.EndsWith("cam1_0001.png") && jpeg.Length == 3,
                   "Pfad und Bild kommen unveraendert an", picturePath);

        // Eine gelogene Laengenangabe darf nicht hinter das Ende greifen.
        Check.That(!Envelope.TryReadImage(new byte[] { 0xFF, 0xFF, 1, 2 }, out _, out _),
                   "eine zu grosse Laengenangabe wird verworfen");

        Transfer();
    }

    /// <summary>
    /// Das Stueckeln einer Datei.
    ///
    /// Die eine Zusicherung, um die es hier wirklich geht: Es sind nie mehr Stuecke
    /// unterwegs, als das Fenster erlaubt. Der Relay puffert 32 Nachrichten je
    /// Gegenstelle - wer schneller schiebt, als die Gegenseite quittiert, verliert
    /// nicht die Uebertragung, sondern die Verbindung. Am Handy sieht man das als
    /// Abbruch mitten im Laden und rate danach lange.
    /// </summary>
    private static void Transfer()
    {
        Check.Group("Dateizugriff - Stueck fuer Stueck, mit Quittung");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-strom-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(folder);

            // Krumme Groesse mit Absicht: Das letzte Stueck ist dann kein volles.
            byte[] original = new byte[3000 + 517];
            new Random(11).NextBytes(original);

            string path = Path.Combine(folder, "gross.blend");
            File.WriteAllBytes(path, original);

            var frames = new List<byte[]>();

            using var transfer = new FileTransfer(7, path, frames.Add, chunkBytes: 1000, window: 2);

            Check.That(transfer.Bytes == original.Length, "die Groesse steht vorher fest",
                       transfer.Bytes.ToString());

            transfer.Pump();

            Check.That(frames.Count == 2, "erst kommt nur, was ins Fenster passt",
                       frames.Count.ToString());
            Check.That(!transfer.Complete, "und fertig ist es nicht");

            // Quittieren, bis nichts mehr kommt. Nach jeder Quittung darf hoechstens
            // ein Stueck nachruecken.
            int highest = -1;
            int guard = 0;

            while (!transfer.Complete && guard++ < 50)
            {
                int before = frames.Count;

                transfer.Ack(highest + 1);
                highest++;

                Check.That(frames.Count - before <= 1,
                           $"nach Quittung {highest} rueckt hoechstens ein Stueck nach",
                           (frames.Count - before).ToString());

                // Die eigentliche Zusicherung: Nie mehr unquittiert unterwegs als
                // das Fenster. Daran haengt, ob der Puffer des Relays haelt.
                Check.That(frames.Count - (highest + 1) <= 2,
                           $"nach Quittung {highest} sind hoechstens zwei Stuecke offen",
                           (frames.Count - (highest + 1)).ToString());

                if (highest >= 10) break;
            }

            Check.That(transfer.Complete, "am Ende ist der Vorgang fertig");

            // Und jetzt das Wesentliche: Kommt dieselbe Datei heraus?
            var rebuilt = new List<byte>();
            int expected = 0;
            bool sawLast = false;

            foreach (byte[] frame in frames)
            {
                Check.That(Envelope.TryRead(frame, out PayloadKind kind, out byte[] body)
                           && kind == PayloadKind.Chunk,
                           $"Stueck {expected} ist ein Stueck");

                Envelope.TryReadChunk(body, out int id, out int index, out bool last, out byte[] data);

                Check.That(id == 7, "mit der richtigen Vorgangsnummer");
                Check.That(index == expected, $"und in der Reihenfolge {expected}", index.ToString());

                rebuilt.AddRange(data);
                sawLast = last;
                expected++;
            }

            Check.That(sawLast, "das letzte Stueck ist als letztes gekennzeichnet");
            Check.That(rebuilt.Count == original.Length, "es kommt genauso viel an",
                       $"{rebuilt.Count} statt {original.Length}");
            Check.That(rebuilt.SequenceEqual(original), "und Byte fuer Byte dasselbe");

            // Eine Datei ohne Inhalt ist auch eine Datei.
            string empty = Path.Combine(folder, "leer.blend");
            File.WriteAllBytes(empty, Array.Empty<byte>());

            var emptyFrames = new List<byte[]>();
            using var emptyTransfer = new FileTransfer(8, empty, emptyFrames.Add, chunkBytes: 1000, window: 2);

            emptyTransfer.Pump();

            Check.That(emptyFrames.Count == 1, "eine leere Datei ist ein einziges Stueck",
                       emptyFrames.Count.ToString());

            emptyTransfer.Ack(0);

            Check.That(emptyTransfer.Complete, "und danach fertig");

            // Abbrechen hoert auf zu senden.
            var stopped = new List<byte[]>();
            var cancelled = new FileTransfer(9, path, stopped.Add, chunkBytes: 1000, window: 2);

            cancelled.Cancel();
            cancelled.Pump();

            Check.That(stopped.Count == 0, "ein abgebrochener Vorgang schickt nichts mehr",
                       stopped.Count.ToString());
            Check.That(cancelled.Closed, "und weiss, dass er zu ist");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Das zweite Tor: in den Projektordnern mitlesen.
    ///
    /// Hier ist die Grenze keine Namensregel, sondern ein Bereich - und damit faellt
    /// die Doppelpruefung des ersten Tors weg. Was hier haelt, haelt allein durch das
    /// Aufloesen des Pfades, und deshalb steht der unscheinbarste Fall gleich mit
    /// dabei: Ein Ordner, der mit dem Namen der Wurzel ANFAENGT, liegt nicht darin.
    /// </summary>
    private static void Library()
    {
        Check.Group("Bibliothek - was das Handy sehen darf");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-bib-" + Guid.NewGuid().ToString("N")[..8]);
        string sibling = root + "-geheim";

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TRACER", "render"));
            Directory.CreateDirectory(sibling);

            string frame = Path.Combine(root, "TRACER", "render", "cam1_0001.png");
            string movie = Path.Combine(root, "TRACER", "vorschau.mp4");
            string scene = Path.Combine(root, "TRACER", "tracer.blend");
            string script = Path.Combine(root, "TRACER", "start.exe");
            string secret = Path.Combine(sibling, "geheim.png");

            foreach (string path in new[] { frame, movie, scene, script, secret })
                File.WriteAllText(path, "x");

            // Ausgeschaltet sieht das Handy nichts.
            var closed = new LibraryVault(enabled: false, new[] { root });

            Check.That(!closed.Enabled, "ohne Erlaubnis keine Bibliothek");
            Check.That(closed.ResolveFile(frame, out _) is null, "und auch kein einzelner Frame");

            // Ohne Ordner ebenso.
            Check.That(!new LibraryVault(enabled: true, Array.Empty<string>()).Enabled,
                       "ohne eingetragenen Ordner auch nicht");

            var open = new LibraryVault(enabled: true, new[] { root });

            Check.That(open.Enabled, "mit Erlaubnis und Ordner schon");

            // Was durchkommt, und als was.
            Check.That(open.ResolveFile(frame, out FileUse asImage) == frame && asImage == FileUse.Image,
                       "ein Frame ist ein Bild");

            Check.That(open.ResolveFile(movie, out FileUse asVideo) == movie && asVideo == FileUse.Video,
                       "eine .mp4 ist ein Video");

            Check.That(open.ResolveFile(scene, out FileUse asBlend) == scene && asBlend == FileUse.Blend,
                       "und eine .blend ist eine .blend");

            Check.That(open.ResolveFile(script, out _) is null, "eine .exe kommt nicht durch");
            Check.That(LibraryVault.UseOf("kitchen.blend1") == FileUse.None, "Blenders Sicherung auch nicht");
            Check.That(LibraryVault.UseOf("notiz.txt") == FileUse.None, "und eine Textdatei ebenso wenig");

            // Der Fallstrick: Ein Nachbarordner, dessen Name mit der Wurzel anfaengt.
            Check.That(!open.Contains(secret),
                       "ein Ordner, der nur so HEISST wie die Wurzel, liegt nicht darin", secret);

            Check.That(open.ResolveFile(secret, out _) is null, "und seine Dateien sind unerreichbar");

            // Der Weg nach draussen ueber Punkte.
            string escape = Path.Combine(root, "TRACER", "..", "..", Path.GetFileName(sibling), "geheim.png");

            Check.That(open.ResolveFile(escape, out _) is null, "mit Punkten kommt man nicht hinaus");

            Check.That(open.ResolveFile(@"C:\Windows\System32\drivers\etc\hosts", out _) is null,
                       "und mit einem fremden Pfad erst recht nicht");

            // Ordner: die Wurzel selbst und alles darunter, sonst nichts.
            Check.That(open.ResolveFolder(root) == Path.TrimEndingDirectorySeparator(root),
                       "in die Wurzel darf man schauen");

            Check.That(open.ResolveFolder(Path.Combine(root, "TRACER", "render")) is not null,
                       "und in jeden Ordner darunter");

            Check.That(open.ResolveFolder(sibling) is null, "in den Nachbarn nicht");
            Check.That(open.ResolveFolder(Path.Combine(root, "gibtsnicht")) is null,
                       "und in einen, den es nicht gibt, auch nicht");

            // Mehrere Wurzeln nebeneinander.
            var two = new LibraryVault(enabled: true, new[] { root, sibling });

            Check.That(two.Contains(secret), "was eingetragen ist, ist erreichbar");
            Check.That(two.Roots.Count == 2, "beide Wurzeln stehen da", two.Roots.Count.ToString());

            var doubled = new LibraryVault(enabled: true, new[] { root, root, root + Path.DirectorySeparatorChar });

            Check.That(doubled.Roots.Count == 1, "derselbe Ordner zaehlt einmal", doubled.Roots.Count.ToString());

            // Der Ausgabeordner eines Renders: erreichbar ohne die Bibliothek, und
            // NUR er. Wer von unterwegs einen Render startet, muss sein Ergebnis
            // ansehen koennen - das ist aber keine Erlaubnis, sich umzusehen.
            string output = Path.Combine(root, "TRACER", "render", "tracer_001");

            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "frame_0001.png"), "x");

            var afterRender = new LibraryVault(enabled: false, roots: new[] { root }, outputs: new[] { output });

            Check.That(afterRender.Enabled, "mit einem Ausgabeordner ist etwas erreichbar");
            Check.That(!afterRender.LibraryEnabled, "die Bibliothek deshalb aber nicht");

            Check.That(afterRender.ResolveFolder(output) is not null, "in den Ausgabeordner darf man schauen");
            Check.That(afterRender.ResolveFile(Path.Combine(output, "frame_0001.png"), out _) is not null,
                       "und seine Bilder lesen");

            Check.That(afterRender.ResolveFolder(Path.Combine(root, "TRACER")) is null,
                       "in den Projektordner darueber nicht");

            Check.That(afterRender.ResolveFolder(Path.Combine(root, "TRACER", "render")) is null,
                       "und in den Ordner mit den anderen Laeufen auch nicht");

            Check.That(afterRender.ResolveFile(frame, out _) is null,
                       "ein Frame aus einem anderen Ordner bleibt unerreichbar");

            Check.That(afterRender.ResolveFolder(root) is null, "die Wurzel erst recht nicht");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception) { }
            try { Directory.Delete(sibling, true); } catch (Exception) { }
        }
    }

    /// <summary>Namen mit Sonderzeichen so schreiben, dass man sie im Bericht lesen kann.</summary>
    private static string Show(string text) => text.Length == 0 ? "(leer)" : text;
}
