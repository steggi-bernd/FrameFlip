using System.Text.Json;
using FrameFlip.Bridge;

namespace FrameFlip.Remote;

/// <summary>
/// Ordnet eingehende Handy-Nachrichten ihren Diensten zu.
///
/// Das ist die eingehende Seite von <see cref="RemoteLink"/>. Sie kennt weder
/// QR-Kopplung noch Relay-Zustand; die Fassade verdrahtet sie nur mit der Leitung.
/// Vorschau und Follow werden dabei an ihren eigenen, zeitgedrosselten Dienst
/// weitergegeben.
/// </summary>
internal sealed class RemoteCommandRouter
{
    private readonly BrowseService _browse;
    private readonly RenderService _render;
    private readonly UploadService _upload;
    private readonly RemotePreviewFollowService _preview;

    internal RemoteCommandRouter(
        Func<RenderJob?> job,
        BrowseService browse,
        RenderService render,
        UploadService upload,
        Action<byte[]> send)
    {
        _browse = browse ?? throw new ArgumentNullException(nameof(browse));
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _upload = upload ?? throw new ArgumentNullException(nameof(upload));
        _preview = new RemotePreviewFollowService(job, send);
    }

    /// <summary>
    /// Eine Nutzlast vom Handy.
    ///
    /// Stuecke werden synchron weitergereicht: Der Upload erwartet ihre Reihenfolge
    /// und quittiert sie sofort. Die datei- und prozesslastigen JSON-Befehle bleiben
    /// dagegen vom Netzwerkthread fern.
    /// </summary>
    internal void OnPayload(byte[] payload)
    {
        try
        {
            if (!Envelope.TryRead(payload, out PayloadKind kind, out byte[] body)) return;

            // Ein Stueck einer Datei, die hereinkommt. Es traegt keinen Befehl - die
            // Zuordnung steht in seiner Vorgangsnummer.
            if (kind == PayloadKind.Chunk)
            {
                if (Envelope.TryReadChunk(body, out int transfer, out int index, out bool last, out byte[] data))
                    _upload.OnChunk(transfer, index, last, data);

                return;
            }

            if (kind != PayloadKind.Json) return;

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("c", out JsonElement command)) return;

            string? name = command.GetString();

            if (name is null) return;

            // Alles rund um die Bibliothek beantwortet der Browser-Dienst selbst -
            // samt Ablehnung, falls sie gar nicht freigegeben ist.
            //
            // Auf dem Threadpool, und mit einer losgeloesten Kopie des Befehls: Das
            // Blaettern geht auf die Platte, und diese Kette haengt am Netzwerk-Thread
            // der Verbindung. Ohne Clone() zeigte die Kopie in ein JsonDocument, das
            // beim Verlassen dieser Methode schon weg ist.
            if (BrowseService.Handles(name))
            {
                JsonElement copy = document.RootElement.Clone();

                Task.Run(() => _browse.Handle(name, copy));
                return;
            }

            // Der Render geht denselben Weg: eigener Thread, losgeloeste Kopie. Das
            // Nachsehen in einer .blend-Datei startet Blender und dauert Sekunden.
            if (RenderService.Handles(name))
            {
                JsonElement copy = document.RootElement.Clone();

                Task.Run(() => _render.Handle(name, copy));
                return;
            }

            if (UploadService.Handles(name))
            {
                JsonElement copy = document.RootElement.Clone();

                Task.Run(() => _upload.Handle(name, copy));
                return;
            }

            _preview.Handle(name, document.RootElement);
        }
        catch (Exception)
        {
            // Was hereinkommt, ist zwar entschluesselt und damit echt - aber echt
            // heisst nicht wohlgeformt. Eine aeltere oder neuere App darf hier
            // nichts umwerfen.
        }
    }

    internal void OnFrameWritten(string path)
        => _preview.OnFrameWritten(path);
}
