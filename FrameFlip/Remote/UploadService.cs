using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;

namespace FrameFlip.Remote;

/// <summary>
/// Dateien vom Handy entgegennehmen.
///
/// Das ist die einzige Stelle, an der etwas von aussen auf diese Platte gelangt, und
/// sie ist entsprechend eng: <see cref="FileVault"/> entscheidet ueber Ordner, Name
/// und Endung, hier steht nur der Ablauf.
///
/// Der Ablauf hat eine Eigenschaft, auf die es ankommt: Eine Datei entsteht unter
/// einem Namen, den Blender nicht oeffnet, und bekommt ihren richtigen erst, wenn
/// sie vollstaendig ist. Eine abgebrochene Uebertragung hinterlaesst damit keine
/// halbe .blend, die aussieht wie eine ganze - man merkt so etwas sonst erst, wenn
/// Blender sie nicht laden kann, und dann weiss niemand mehr, woher sie kam.
///
/// Quittiert wird jedes Stueck. Ohne das schiebt ein schnelles Handy schneller, als
/// der Relay puffert, und dann faellt nicht die Uebertragung aus, sondern die
/// Verbindung.
/// </summary>
public sealed class UploadService : IDisposable
{
    /// <summary>Eine Uebertragung auf einmal. Mehr braucht niemand, mehr verwirrt nur.</summary>
    private sealed class Incoming : IDisposable
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required string Target { get; init; }
        public required string Partial { get; init; }
        public required long Bytes { get; init; }
        public required FileStream Sink { get; init; }

        public int Expected { get; set; }
        public long Written { get; set; }

        public void Dispose()
        {
            try { Sink.Dispose(); } catch (Exception) { }
        }
    }

    private readonly Func<AppSettings> _settings;
    private readonly Action<object> _send;
    private readonly object _gate = new();

    private Incoming? _current;
    private int _nextId;

    public UploadService(Func<AppSettings> settings, Action<object> send)
    {
        _settings = settings;
        _send = send;
    }

    public static bool Handles(string command) => command is "files" or "put" or "putstop";

    public bool Handle(string command, JsonElement root)
    {
        switch (command)
        {
            case "files": Files(); return true;
            case "put": Put(Text(root, "n"), Number(root, "s") ?? -1, Text(root, "p")); return true;
            case "putstop": Abandon("stopped by the phone"); return true;
            default: return false;
        }
    }

    // ---------------------------------------------------------------- Auflisten

    private void Files()
    {
        var vault = FileVault.From(_settings());

        if (!vault.ReadEnabled)
        {
            Refuse("files", FileVault.Explain(vault.Folder.Length == 0
                ? VaultRefusal.NoFolder
                : VaultRefusal.Disabled));

            return;
        }

        _send(new
        {
            t = "files",
            ok = true,
            folder = vault.Folder,
            write = vault.WriteEnabled,
            items = vault.List().Select(file => new
            {
                n = file.Name,
                s = file.Bytes,
                m = file.ModifiedUtc.ToString("o"),
            }).ToList(),
        });
    }

    // ---------------------------------------------------------------- Annehmen

    /// <param name="folder">
    /// Wohin. Leer heisst: in den Austauschordner.
    ///
    /// Ein Ordner aus der freigegebenen Bibliothek geht auch - wer eine Szene an die
    /// Stelle legen will, an der ihre Bilder liegen, soll das koennen. Erreichbar
    /// muss er ohnehin sein: Was das Handy nicht sehen darf, kann es auch nicht
    /// bestuecken.
    /// </param>
    private void Put(string? name, long size, string? folder)
    {
        var settings = _settings();
        var vault = FileVault.From(settings);

        string? target;
        VaultRefusal refusal;

        if (string.IsNullOrWhiteSpace(folder))
        {
            refusal = vault.CanWrite(name, size, out target);
        }
        else
        {
            refusal = CanWriteInto(folder!, name, size, out target);
        }

        if (refusal != VaultRefusal.None || target is null)
        {
            Refuse("put", FileVault.Explain(refusal));
            return;
        }

        lock (_gate)
        {
            if (_current is not null)
            {
                Refuse("put", "Another file is already coming in.");
                return;
            }

            string partial = FileVault.PartialPath(target);

            FileStream sink;

            try
            {
                sink = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
            }
            catch (Exception)
            {
                Refuse("put", "The file could not be created.");
                return;
            }

            _current = new Incoming
            {
                Id = ++_nextId,
                Name = Path.GetFileName(target),
                Target = target,
                Partial = partial,
                Bytes = size,
                Sink = sink,
            };

            // Der Name, unter dem sie WIRKLICH landet - mit Markierung und
            // gegebenenfalls durchnummeriert. Das Handy soll ihn sehen, bevor die
            // Datei fliesst, und nicht raten muessen, was daraus geworden ist.
            _send(new
            {
                t = "put",
                ok = true,
                id = _current.Id,
                n = _current.Name,
                folder = Path.GetDirectoryName(target),
                c = Envelope.ChunkBytes,
            });
        }
    }

    /// <summary>
    /// In einen bestehenden Ordner ablegen.
    ///
    /// Dieselben Regeln wie im Austauschordner - nur .blend, kein Pfad im Namen,
    /// nichts wird ueberschrieben -, aber der Ordner darf jeder sein, den das Handy
    /// ohnehin sehen darf. Die Markierung im Namen macht das vertretbar: Was hier
    /// ankommt, verdraengt nichts und ist als Zugang von aussen erkennbar.
    /// </summary>
    private VaultRefusal CanWriteInto(string folder, string? name, long size, out string? target)
    {
        target = null;

        var settings = _settings();

        if (!settings.FileAccessEnabled) return VaultRefusal.Disabled;
        if (!settings.FilePushEnabled) return VaultRefusal.WriteDisabled;
        if (size < 0 || size > FileVault.MaxBytes) return VaultRefusal.TooBig;
        if (!FileVault.IsAllowedName(name)) return VaultRefusal.BadName;

        var known = Projects.ProjectLibrary.Load();
        var library = new LibraryVault(settings.LibraryAccessEnabled, known.Folders, known.RenderOutputs);

        string? resolved = library.ResolveFolder(folder);

        if (resolved is null) return VaultRefusal.BadName;

        target = Path.Combine(resolved, FileVault.FreeName(resolved, name!, File.Exists));

        return VaultRefusal.None;
    }

    /// <summary>
    /// Ein Stueck. Wird von der Leitung gerufen, sobald ein Datenpaket ankommt.
    ///
    /// Ein Stueck mit der falschen Nummer wird verworfen und NICHT quittiert: Sonst
    /// entstuende eine Datei, in der ein Stueck fehlt oder doppelt steht, und das
    /// faellt erst in Blender auf.
    /// </summary>
    public void OnChunk(int transfer, int index, bool last, byte[] data)
    {
        Incoming? incoming;

        lock (_gate) incoming = _current;

        if (incoming is null || incoming.Id != transfer || incoming.Expected != index) return;

        try
        {
            incoming.Sink.Write(data, 0, data.Length);

            incoming.Written += data.Length;
            incoming.Expected++;

            // Mehr, als angekuendigt war: Das ist kein Ueberlauf, sondern ein
            // Grund, aufzuhoeren - der Platz war nach der Ankuendigung bemessen.
            if (incoming.Bytes >= 0 && incoming.Written > incoming.Bytes + Envelope.ChunkBytes)
            {
                Abandon("more data than announced");
                return;
            }

            if (!last)
            {
                _send(new { t = "ack", id = transfer, i = index });
                return;
            }

            Finish(incoming);
        }
        catch (Exception)
        {
            Abandon("writing failed");
        }
    }

    private void Finish(Incoming incoming)
    {
        lock (_gate)
        {
            incoming.Sink.Flush();
            incoming.Dispose();

            try
            {
                // Erst jetzt bekommt sie ihren richtigen Namen. Bis hierher war sie
                // eine .teil-Datei, die niemand fuer eine fertige haelt.
                File.Move(incoming.Partial, incoming.Target, overwrite: false);
            }
            catch (Exception)
            {
                try { File.Delete(incoming.Partial); } catch (Exception) { }

                _current = null;
                Refuse("stored", "The finished file could not be put in place.");
                return;
            }

            _current = null;
        }

        _send(new { t = "stored", ok = true, n = incoming.Name, s = incoming.Written });
    }

    /// <summary>Aufgeben und aufraeumen. Eine halbe Datei bleibt nirgends liegen.</summary>
    private void Abandon(string why)
    {
        Incoming? incoming;

        lock (_gate)
        {
            incoming = _current;
            _current = null;
        }

        if (incoming is null) return;

        incoming.Dispose();

        try { File.Delete(incoming.Partial); } catch (Exception) { }

        Refuse("stored", "The transfer was abandoned: " + why + ".");
    }

    private void Refuse(string topic, string why) => _send(new { t = topic, ok = false, why });

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long? Number(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long number) ? number : null;

    public void Dispose() => Abandon("the connection ended");
}
