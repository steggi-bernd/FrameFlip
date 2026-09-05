namespace FrameFlip.Remote;

/// <summary>
/// base64url ohne Fuellzeichen (RFC 4648 §5).
///
/// Das gewoehnliche Base64 taugt hier nicht: '+' und '/' muessten in einer URL
/// maskiert werden, und der QR-Code traegt eine URL.
/// </summary>
internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? text, out byte[]? data)
    {
        data = null;
        if (string.IsNullOrEmpty(text)) return false;

        string padded = text.Replace('-', '+').Replace('_', '/');

        // Convert.FromBase64String verlangt eine Laenge, die durch vier teilbar ist.
        // Die Fuellzeichen sind in der Textform weggelassen und kommen hier zurueck.
        switch (padded.Length % 4)
        {
            case 1: return false;
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        try
        {
            data = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
