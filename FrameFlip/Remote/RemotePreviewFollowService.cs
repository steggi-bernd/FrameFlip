using System.Text.Json;
using FrameFlip.Bridge;

namespace FrameFlip.Remote;

/// <summary>
/// Beantwortet Vorschauen und folgt auf Wunsch jedem neuen Renderframe.
///
/// Der Dienst kennt keine Befehlsleitung und keine Services fuer Dateien oder
/// Render. Er bekommt nur den aktuellen Auftrag und einen Weg nach draussen.
/// Damit bleiben die beiden zeitkritischen Regeln an einer Stelle: sofort ein Bild
/// beim Einschalten und danach hoechstens eines alle zwei Sekunden.
/// </summary>
internal sealed class RemotePreviewFollowService
{
    private readonly Func<RenderJob?> _job;
    private readonly Action<byte[]> _send;
    private readonly Func<string?, int, byte[]?> _encode;
    private readonly Func<DateTime> _utcNow;
    private readonly object _followGate = new();

    /// <summary>
    /// Ob das Handy jedem neuen Frame folgen will.
    ///
    /// Vorher fragte es von sich aus alle paar Sekunden nach - und lag damit
    /// zwangslaeufig daneben: Wer im falschen Moment fragt, bekommt das vorige Bild,
    /// und wer oft fragt, verbraucht Daten fuer Bilder, die es noch gar nicht gibt.
    /// Der Rechner weiss dagegen genau, wann eines fertig ist.
    /// </summary>
    private volatile bool _follow;

    /// <summary>Breite der Bilder, denen gefolgt wird. Klein - es ist eine Kachel, kein Vollbild.</summary>
    private int _followWidth = 480;

    private DateTime _lastFollow = DateTime.MinValue;

    internal RemotePreviewFollowService(Func<RenderJob?> job, Action<byte[]> send)
        : this(job, send, PreviewEncoder.Encode, () => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Der technische Rand fuer Tests: Kodierer und Uhr sind austauschbar, der
    /// Produktpfad benutzt immer die oeffentliche Vorschaukodierung und UTC.
    /// </summary>
    internal RemotePreviewFollowService(
        Func<RenderJob?> job,
        Action<byte[]> send,
        Func<string?, int, byte[]?> encode,
        Func<DateTime> utcNow)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _encode = encode ?? throw new ArgumentNullException(nameof(encode));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    /// <summary>
    /// Behandelt die beiden Befehle, die direkt zur laufenden Vorschau gehoeren.
    /// Andere Befehle bleiben folgenlos; der Router hat sie bereits bei den
    /// zustaendigen Diensten vorbeigefuehrt.
    /// </summary>
    internal void Handle(string name, JsonElement root)
    {
        // Dem Render folgen: Ab jetzt schickt der Rechner jedes fertige Bild von
        // selbst. Das ist der Unterschied zwischen "alle sechs Sekunden fragen"
        // und "da ist es".
        if (name == "follow")
        {
            _follow = !root.TryGetProperty("on", out JsonElement on) || on.GetBoolean();

            if (root.TryGetProperty("w", out JsonElement followWidth)
                && followWidth.TryGetInt32(out int wanted))
            {
                _followWidth = Math.Clamp(wanted, 240, 1920);
            }

            // Sofort eines schicken, damit nicht bis zum naechsten Frame ein
            // leeres Feld dasteht.
            if (_follow) Task.Run(() => SendPreview(_followWidth));

            return;
        }

        if (name != "preview") return;

        // Die gewuenschte Breite. Ohne Angabe die volle - so verhaelt sich eine
        // aeltere App wie bisher.
        int width = root.TryGetProperty("w", out JsonElement requestedWidth) && requestedWidth.TryGetInt32(out int value)
            ? value
            : PreviewEncoder.Width;

        Task.Run(() => SendPreview(width));
    }

    /// <summary>
    /// Die Vorschau beantworten - immer, auch wenn es keine gibt.
    ///
    /// Vorher wurde in diesem Fall einfach nichts geschickt, und in der App stand
    /// dauerhaft "Bild wird geholt". Eine Anfrage ohne Antwort ist die schlechteste
    /// Art zu scheitern: Der Fragende wartet, und niemand sagt ihm, worauf.
    ///
    /// Die haeufigsten Gruende sind harmlos und sollen genau so dastehen - ein
    /// Render, der gerade erst angelaufen ist, hat schlicht noch keinen Frame
    /// geschrieben.
    /// </summary>
    private void SendPreview(int width)
    {
        try
        {
            RenderJob? job = _job();

            string? why = job is null
                ? "No render is running on the machine."
                : string.IsNullOrEmpty(job.LatestFrameFile)
                    ? "No frame written yet."
                    : null;

            if (why is null)
            {
                byte[]? jpeg = _encode(job!.LatestFrameFile, width);

                if (jpeg is not null)
                {
                    _send(Envelope.Preview(job.CurrentFrame, jpeg));
                    return;
                }

                why = "The image could not be read.";
            }

            _send(Envelope.Json(
                $$"""{"t":"preview","ok":false,"why":{{JsonSerializer.Serialize(why)}}}"""));
        }
        catch (Exception)
        {
            // Siehe oben: Diese Kette haengt an der Vorschau und darf nichts werfen.
        }
    }

    /// <summary>
    /// Ein Frame ist auf der Platte - und das Handy will ihn sehen.
    ///
    /// Gedrosselt, weil ein schneller Render mehrere Bilder je Sekunde schreiben
    /// kann und jedes ein paar hundert Kilobyte kostet. Zwei Sekunden sind fuer das
    /// Auge fluessig genug und fuer ein Mobilnetz vertretbar.
    /// </summary>
    internal void OnFrameWritten(string path)
    {
        if (!_follow) return;

        var now = _utcNow();

        lock (_followGate)
        {
            if (now - _lastFollow < TimeSpan.FromSeconds(2)) return;

            _lastFollow = now;
        }

        // Der Ereignispfad sagt, dass ein Frame da ist; als Quelle dient trotzdem
        // der zuletzt sicher am Auftrag festgehaltene Pfad.
        Task.Run(() => SendPreview(_followWidth));
    }
}
