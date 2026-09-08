using System.Text.Json;
using FrameFlip.Bridge;

namespace FrameFlip.Remote;

/// <summary>
/// Verbindet den Renderfortschritt mit der Leitung zum Handy.
///
/// Sie behaelt die stabile Fassade fuer die App, waehrend die ausgehende
/// Status-/Telemetrie-Seite intern beim <see cref="RemoteStatusPublisher"/> liegt.
/// Befehle, Vorschauen und Dateiuebertragungen bleiben hier koordiniert.
/// </summary>
public sealed class RemoteLink : IAsyncDisposable
{
    private readonly RenderMonitor _monitor;
    private readonly RelayClient _client;
    private readonly RemoteStatusPublisher _status;
    private readonly RemoteCommandRouter _commands;

    /// <summary>Blaettern, Ansehen und Holen in der Bibliothek. Antwortet selbst.</summary>
    private readonly BrowseService _browse;

    /// <summary>Rendern auf Zuruf. Nur mit Erlaubnis, und nur mit freigegebenen Dateien.</summary>
    private readonly RenderService _render;

    /// <summary>Dateien entgegennehmen. Die einzige Stelle, an der etwas hereinkommt.</summary>
    private readonly UploadService _upload;

    public RemoteLink(PairingInvite invite, RenderMonitor monitor, Func<Diagnostics.LoadSnapshot?>? load = null,
                      Func<Configuration.AppSettings>? settings = null)
    {
        _monitor = monitor;
        _client = new RelayClient(invite);

        // Ohne Einstellungen bleibt die Bibliothek zu: Was hier nicht durchgereicht
        // wird, kann auch niemand freigeschaltet haben.
        var read = settings ?? (() => new Configuration.AppSettings());

        _browse = new BrowseService(read, payload => _client.Send(payload));

        void Answer(object payload) => _client.Send(Envelope.Json(JsonSerializer.Serialize(payload)));

        _render = new RenderService(read, Answer, new Rendering.RenderRunner(read, monitor.Feed));
        _upload = new UploadService(read, Answer);

        _commands = new RemoteCommandRouter(() => _monitor.Job, _browse, _render, _upload,
                                             payload => _client.Send(payload));

        _monitor.FrameWritten += _commands.OnFrameWritten;
        _client.PayloadReceived += _commands.OnPayload;
        _status = new RemoteStatusPublisher(_monitor, payload => _client.Send(payload), load ?? (() => null));
    }

    public RelayState State => _client.State;

    public event Action<RelayState>? StateChanged
    {
        add => _client.StateChanged += value;
        remove => _client.StateChanged -= value;
    }

    public void Start() => _client.Start();

    /// <summary>
    /// Die Fassade behaelt die bisherige Serialisierungs-API, obwohl die
    /// ausgehende Verantwortung jetzt beim Status-Publisher liegt.
    /// </summary>
    public static string Describe(
        RenderJob? job,
        Diagnostics.LoadSnapshot? load = null,
        Diagnostics.GpuReading gpu = default)
        => RemoteStatusPublisher.Describe(job, load, gpu);

    public async ValueTask DisposeAsync()
    {
        _monitor.FrameWritten -= _commands.OnFrameWritten;
        _client.PayloadReceived -= _commands.OnPayload;
        await _status.DisposeAsync();
        _browse.Dispose();
        _upload.Dispose();
        await _client.DisposeAsync();
    }
}
