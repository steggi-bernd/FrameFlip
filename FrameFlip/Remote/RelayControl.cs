using System.Text.Json;

namespace FrameFlip.Remote;

/// <summary>Was der Relay von sich aus sagt. Alles andere ist Nutzlast.</summary>
public enum RelayMessage
{
    /// <summary>Unlesbar oder unbekannt - wird verworfen.</summary>
    Unknown,

    /// <summary>Wir sind im Raum, die Gegenseite noch nicht.</summary>
    Waiting,

    /// <summary>Die Gegenseite ist da.</summary>
    PeerUp,

    /// <summary>Die Gegenseite ist weg.</summary>
    PeerDown,

    /// <summary>Abgewiesen. Die Verbindung schliesst gleich danach.</summary>
    Error
}

/// <summary>
/// Liest die Textframes des Relays.
///
/// Der Relay schickt seine eigenen Meldungen als Text und reicht Nutzlast als
/// Binaerframe weiter - die Trennung liegt also im Frametyp, nicht im Inhalt. Diese
/// Klasse bekommt deshalb nur zu sehen, was ohnehin schon als Text angekommen ist,
/// und muss nie raten.
///
/// Unbekanntes wird verworfen, nicht als Fehler behandelt. Eine spaetere Fassung des
/// Relays darf Meldungen hinzufuegen, ohne diese hier zu brechen.
/// </summary>
public static class RelayControl
{
    public static RelayMessage Parse(string? text, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(text)) return RelayMessage.Unknown;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return RelayMessage.Unknown;
            if (!root.TryGetProperty("t", out JsonElement kind) || kind.ValueKind != JsonValueKind.String)
                return RelayMessage.Unknown;

            switch (kind.GetString())
            {
                case "waiting":
                    return RelayMessage.Waiting;

                case "peer":
                    if (!root.TryGetProperty("up", out JsonElement up)) return RelayMessage.Unknown;

                    return up.ValueKind switch
                    {
                        JsonValueKind.True => RelayMessage.PeerUp,
                        JsonValueKind.False => RelayMessage.PeerDown,
                        _ => RelayMessage.Unknown
                    };

                case "error":
                    if (root.TryGetProperty("why", out JsonElement why) && why.ValueKind == JsonValueKind.String)
                        reason = why.GetString();

                    return RelayMessage.Error;

                default:
                    return RelayMessage.Unknown;
            }
        }
        catch (JsonException)
        {
            // Kein JSON. Kommt von einem Zwischenstueck, das sich einmischt - etwa
            // einer Anmeldeseite in einem Gaeste-WLAN.
            return RelayMessage.Unknown;
        }
    }
}
