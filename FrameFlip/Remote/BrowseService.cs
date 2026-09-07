using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
using FrameFlip.Projects;

namespace FrameFlip.Remote;

/// <summary>
/// Was das Handy in der Bibliothek darf: blaettern, ansehen, holen.
///
/// Die drei Dinge sind bewusst verschieden teuer, und das ist der ganze Entwurf:
///
/// BLAETTERN kostet ein paar Zeilen JSON. Es geht ueber dieselben Klassen wie der
/// Projektbrowser am Rechner - gleiche Gruppierung, gleiche Ordner, gleiche
/// Vermutungen. Zwei Antworten auf dieselbe Frage waeren zwei Gelegenheiten, sich
/// zu widersprechen.
///
/// ANSEHEN kostet ein verkleinertes Bild, ein paar hundert Kilobyte. Das Original
/// bleibt liegen. Das ist der Normalfall: Wer unterwegs nachsieht, ob der Frame
/// etwas geworden ist, will kein 40-MB-EXR aufs Handy.
///
/// HOLEN kostet die ganze Datei und ist deshalb ein eigener Schritt, den jemand
/// ausloest - Stueck fuer Stueck, mit Quittungen, siehe <see cref="FileTransfer"/>.
///
/// ABSPIELEN kostet gar nichts extra, und das ist Absicht: Es beantwortet allein die
/// Frage, in welcher Reihenfolge die Bilder eines Ordners ein Film sind. Die Bilder
/// selbst holt der Spieler ueber dasselbe ANSEHEN wie jeder andere - eins nach dem
/// anderen, verkleinert, im Speicher. Ein zweiter Weg fuer Frames waere eine zweite
/// Gelegenheit, sich zu widersprechen, und er brauchte einen eigenen Grund. Es gibt
/// keinen: Ein Frame ist ein Bild.
///
/// Alles davon geht nur, wenn die Bibliothek am Rechner freigegeben ist, und alles
/// davon liest nur. Geschrieben wird hier nichts, nirgends.
/// </summary>
public sealed class BrowseService : IDisposable
{
    /// <summary>Soviele Uebertragungen gleichzeitig. Mehr braucht niemand, mehr belegt nur Speicher.</summary>
    public const int MaxTransfers = 2;

    /// <summary>Breite der Ansicht, wenn die App keine nennt.</summary>
    public const int DefaultViewWidth = 1280;

    private readonly Func<AppSettings> _settings;
    private readonly Action<byte[]> _send;
    private readonly object _gate = new();
    private readonly Dictionary<int, FileTransfer> _transfers = new();

    private int _nextId;

    public BrowseService(Func<AppSettings> settings, Action<byte[]> send)
    {
        _settings = settings;
        _send = send;
    }

    /// <summary>Das Tor, wie es gerade eingestellt ist. Bei jedem Befehl neu gelesen.</summary>
    private LibraryVault Vault()
    {
        var known = ProjectLibrary.Load();

        return new LibraryVault(_settings().LibraryAccessEnabled, known.Folders, known.RenderOutputs);
    }

    /// <summary>Ob dieser Befehl hierher gehoert - vor dem Ausfuehren zu fragen.</summary>
    public static bool Handles(string command)
        => command is "projects" or "folder" or "seq" or "view" or "fetch" or "ack" or "stop";

    /// <summary>
    /// Einen Befehl beantworten. false heisst: gehoert nicht hierher.
    ///
    /// Jede Antwort geht raus, auch die ablehnende. Eine Anfrage ohne Antwort ist die
    /// schlechteste Art zu scheitern - das Handy wartet dann, und niemand sagt ihm
    /// worauf. Genau dieser Fehler stand schon einmal als "Bild wird geholt" auf dem
    /// Bildschirm, bis jemand die App neu startete.
    /// </summary>
    public bool Handle(string command, JsonElement root)
    {
        switch (command)
        {
            case "projects": Projects(); return true;
            case "folder": Folder(Text(root, "p")); return true;
            case "seq": Sequence(Text(root, "p")); return true;
            case "view": View(Text(root, "p"), Number(root, "w") ?? DefaultViewWidth); return true;
            case "fetch": Fetch(Text(root, "p")); return true;
            case "ack": Ack(Number(root, "id"), Number(root, "i")); return true;
            case "stop": Stop(Number(root, "id")); return true;
            default: return false;
        }
    }

    // ---------------------------------------------------------------- Blaettern

    private void Projects()
    {
        var vault = Vault();

        // Fuer die Projektliste zaehlt allein die Bibliothek. Ein Ausgabeordner
        // erlaubt, sein Ergebnis anzusehen - nicht, sich umzusehen.
        if (!vault.LibraryEnabled)
        {
            Refuse("projects", null, "The project folders are not shared on the machine.");
            return;
        }

        var items = new List<object>();

        foreach (var project in ProjectLibrary.Scan())
        {
            string folder = project.Folder.Length > 0
                ? project.Folder
                : Path.GetDirectoryName(project.Newest?.Path ?? string.Empty) ?? string.Empty;

            // Was ausserhalb der freigegebenen Ordner liegt, steht nicht in der Liste -
            // sonst zeigte die App auf etwas, das sie nie oeffnen kann.
            if (!vault.Contains(folder)) continue;

            items.Add(new
            {
                n = project.Name,
                p = folder,
                v = project.VersionCount,
                a = project.AutosaveCount,
                tu = project.TouchedUtc.ToString("o"),
            });
        }

        Send(new { t = "projects", ok = true, items });
    }

    private void Folder(string? path)
    {
        var vault = Vault();

        if (!vault.Enabled)
        {
            Refuse("folder", path, "The project folders are not shared on the machine.");
            return;
        }

        string? folder = vault.ResolveFolder(path);

        if (folder is null)
        {
            Refuse("folder", path, "This folder is not part of the shared library.");
            return;
        }

        var dirs = ProjectScanner.Children(folder)
                                 .Select(tile => new { n = tile.Name, p = tile.Path, i = tile.Images, d = tile.Folders })
                                 .ToList();

        var files = new List<object>();
        int more = 0;

        try
        {
            foreach (string file in Directory.EnumerateFiles(folder)
                                             .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                FileUse use = LibraryVault.UseOf(file);

                if (use == FileUse.None) continue;

                // Ein Frameordner hat leicht zehntausend Bilder. Die Liste bleibt
                // endlich, und wieviele fehlen, steht dabei.
                if (files.Count >= LibraryVault.PageSize)
                {
                    more++;
                    continue;
                }

                var info = new FileInfo(file);

                files.Add(new
                {
                    n = info.Name,
                    p = file,
                    s = info.Length,
                    u = Kind(use),
                    m = info.LastWriteTimeUtc.ToString("o"),
                });
            }
        }
        catch (Exception)
        {
            // Ein Ordner, der waehrend des Blaetterns verschwindet, ist kein Absturz.
        }

        // Nach oben nur, solange man dabei nicht aus der Bibliothek faellt.
        string? up = Path.GetDirectoryName(folder);

        Send(new
        {
            t = "folder",
            ok = true,
            p = folder,
            n = Path.GetFileName(folder),
            up = up is not null && vault.ResolveFolder(up) is not null ? up : null,
            dirs,
            files,
            more,
        });
    }

    // ---------------------------------------------------------------- Abspielen

    /// <summary>
    /// Die Bilder eines Ordners als Folge - Reihenfolge, sonst nichts.
    ///
    /// Angenommen wird ein Ordner ODER ein Bild darin. Wer in der Dateiliste auf
    /// einen Frame tippt und ihn abspielen will, meint die Folge, in der er steht;
    /// ihn zu zwingen, erst eine Ebene hoeher zu gehen, waere eine Umstaendlichkeit
    /// ohne Gewinn.
    ///
    /// Zurueck gehen nur die Namen, nicht die vollen Pfade: Sie teilen sich alle
    /// denselben Ordner, und bei zehntausend Frames ist der Unterschied die halbe
    /// Nachricht. Das Trennzeichen steht dabei, statt dass die App es raet - sie
    /// laeuft auf einem System, das ein anderes benutzt.
    /// </summary>
    private void Sequence(string? path)
    {
        var vault = Vault();

        if (!vault.Enabled)
        {
            Refuse("seq", path, "The project folders are not shared on the machine.");
            return;
        }

        string? folder = vault.ResolveFolder(path);

        if (folder is null && vault.ResolveFile(path, out FileUse use) is string file && use == FileUse.Image)
            folder = vault.ResolveFolder(Path.GetDirectoryName(file));

        if (folder is null)
        {
            Refuse("seq", path, "This folder is not part of the shared library.");
            return;
        }

        var frames = FrameSequence.Of(folder);

        if (frames.Count == 0)
        {
            // Kein Grund zu schweigen: Ein leerer Ordner ist eine Antwort, und ohne
            // sie wartet die App auf einen Film, den es nicht gibt.
            Refuse("seq", path, "There are no images in this folder.");
            return;
        }

        Send(new
        {
            t = "seq",
            ok = true,
            p = folder,
            n = Path.GetFileName(folder),
            sep = Path.DirectorySeparatorChar.ToString(),
            total = frames.Count,
            items = frames.Take(FrameSequence.MaxFrames).Select(frame => Path.GetFileName(frame)).ToList(),
        });
    }

    // ---------------------------------------------------------------- Ansehen

    /// <summary>
    /// Ein Bild verkleinert schicken - ohne das Original zu uebertragen.
    ///
    /// Fuer ein Video geht das nicht: Da gaebe es nichts zu verkleinern, was ein
    /// Einzelbild waere. Das steht dann auch so da, statt dass die App auf ein Bild
    /// wartet, das nie kommt.
    /// </summary>
    private void View(string? path, int width)
    {
        var vault = Vault();

        if (!vault.Enabled)
        {
            Refuse("view", path, "The project folders are not shared on the machine.");
            return;
        }

        string? file = vault.ResolveFile(path, out FileUse use);

        if (file is null)
        {
            Refuse("view", path, "This file is not part of the shared library.");
            return;
        }

        if (use != FileUse.Image)
        {
            Refuse("view", path, use == FileUse.Video
                       ? "A video cannot be viewed as an image - it has to stream."
                       : "That is not an image.");
            return;
        }

        byte[]? jpeg = PreviewEncoder.Encode(file, width);

        if (jpeg is null)
        {
            // Der haeufigste Fall ist EXR: Windows kann es nicht dekodieren, und dann
            // steht das da, statt dass jemand raet.
            Refuse("view", path, $"{Path.GetExtension(file).ToUpperInvariant()} could not be read here.");
            return;
        }

        _send(Envelope.Image(file, jpeg));
    }

    // ---------------------------------------------------------------- Holen

    private void Fetch(string? path)
    {
        var vault = Vault();

        if (!vault.Enabled)
        {
            Refuse("fetch", path, "The project folders are not shared on the machine.");
            return;
        }

        string? file = vault.ResolveFile(path, out FileUse use);

        if (file is null || use == FileUse.None)
        {
            Refuse("fetch", path, "This file is not part of the shared library.");
            return;
        }

        FileTransfer transfer;

        lock (_gate)
        {
            Sweep();

            if (_transfers.Count >= MaxTransfers)
            {
                Refuse("fetch", path, "Enough transfers are running already.");
                return;
            }

            try
            {
                transfer = new FileTransfer(++_nextId, file, _send);
            }
            catch (Exception)
            {
                Refuse("fetch", path, "The file could not be opened.");
                return;
            }

            _transfers[transfer.Id] = transfer;
        }

        Send(new
        {
            t = "fetch",
            ok = true,
            id = transfer.Id,
            p = file,
            n = Path.GetFileName(file),
            s = transfer.Bytes,
            c = Envelope.ChunkBytes,

            // Nur bei Filmen, und nur als Auskunft: Ob der Spieler am Handy
            // loslegen kann, bevor alles da ist, haengt an der Datei und nicht an
            // der Leitung. Wer das weiss, wartet nicht auf etwas, das nicht kommt.
            st = use == FileUse.Video ? MovieProbe.Word(MovieProbe.Of(file)) : "",
        });

        transfer.Pump();
    }

    private void Ack(int? id, int? index)
    {
        if (id is not int transferId || index is not int chunk) return;

        FileTransfer? transfer;

        lock (_gate) _transfers.TryGetValue(transferId, out transfer);

        if (transfer is null) return;

        transfer.Ack(chunk);

        if (!transfer.Complete) return;

        lock (_gate)
        {
            transfer.Dispose();
            _transfers.Remove(transferId);
        }

        Send(new { t = "sent", id = transferId, ok = true });
    }

    private void Stop(int? id)
    {
        if (id is not int transferId) return;

        lock (_gate)
        {
            if (!_transfers.TryGetValue(transferId, out FileTransfer? transfer)) return;

            transfer.Dispose();
            _transfers.Remove(transferId);
        }

        Send(new { t = "stopped", id = transferId });
    }

    /// <summary>Fertige und abgebrochene Vorgaenge wegraeumen. Nur unter der Sperre zu rufen.</summary>
    private void Sweep()
    {
        foreach (int done in _transfers.Where(pair => pair.Value.Complete || pair.Value.Closed)
                                       .Select(pair => pair.Key)
                                       .ToList())
        {
            _transfers[done].Dispose();
            _transfers.Remove(done);
        }
    }

    // ---------------------------------------------------------------- Kleinteile

    private static string Kind(FileUse use) => use switch
    {
        FileUse.Image => "image",
        FileUse.Video => "video",
        FileUse.Blend => "blend",
        _ => "",
    };

    private void Refuse(string topic, string? path, string why)
        => Send(new { t = topic, ok = false, p = path ?? "", why });

    /// <summary>
    /// Die Antwort verschicken.
    ///
    /// Zusammengesetzt wird sie vom Serialisierer, nicht von Hand: In den Antworten
    /// stehen Windows-Pfade, und die sind voller Rueckwaertsschraegstriche. Eine
    /// zusammengeklebte Zeichenkette waere genau dort falsch, wo es niemandem
    /// auffaellt - beim Ordner mit dem Anfuehrungszeichen im Namen.
    /// </summary>
    private void Send(object payload) => _send(Envelope.Json(JsonSerializer.Serialize(payload)));

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : null;

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (FileTransfer transfer in _transfers.Values) transfer.Dispose();

            _transfers.Clear();
        }
    }
}
