using System.Text;
using System.Text.Json;

namespace FrameFlip.Decoding.Exr;

/// <summary>
/// Ein Kryptomatten-Satz einer Datei: wie er heisst, und welcher Name welche Kennung
/// hat.
/// </summary>
/// <param name="Prefix">
/// Der Name, unter dem die Stufen stehen: "ViewLayer.CryptoObject". Die Stufen
/// heissen dann "...00", "...01" und so fort.
/// </param>
/// <param name="Names">Name zu Kennung. Die Kennung ist ein Gleitkommawert, siehe unten.</param>
public sealed record CryptomatteSet(string Prefix, IReadOnlyDictionary<string, float> Names)
{
    /// <summary>Der Name ohne den Vorsatz der Ansichtsebene: "CryptoObject".</summary>
    public string ShortName
    {
        get
        {
            int dot = Prefix.LastIndexOf('.');
            return dot >= 0 && dot < Prefix.Length - 1 ? Prefix[(dot + 1)..] : Prefix;
        }
    }

    /// <summary>Zu welchem Namen eine Kennung gehoert. Null, wenn keiner passt.</summary>
    public string? NameOf(float id)
    {
        foreach (var (name, value) in Names)
            if (value.Equals(id)) return name;

        return null;
    }
}

/// <summary>
/// Kryptomatten: die Auswahl von Objekten und Materialien, die eine ganze Sequenz
/// ueberdauert.
///
/// Das ist der Unterschied zu einer gemalten Maske. Eine gemalte Maske gilt fuer ein
/// Bild; sobald sich etwas bewegt, sitzt sie falsch. Eine Kryptomatte steht in jedem
/// Bild neu und kennt das Objekt - wer die Kugel auswaehlt, hat sie in Bild 1 und in
/// Bild 300, auch wenn sie inzwischen durchs Bild gelaufen ist.
///
/// Was in der Datei steht, ist keine Maske, sondern eine Nachschlagetabelle je
/// Bildpunkt. Jede Stufe traegt in ihren vier Kanaelen ZWEI Paare aus Kennung und
/// Deckung:
///
///     r = Kennung 0, g = Deckung 0, b = Kennung 1, a = Deckung 1
///
/// Drei Stufen sind damit sechs Objekte je Bildpunkt - genug fuer Haare vor einer
/// Glasscheibe. Die Maske eines Objekts entsteht, indem ueber alle Stufen die
/// Deckung derjenigen Paare summiert wird, deren Kennung passt. Wer eine Stufe als
/// Bild ansieht, sieht farbiges Rauschen, und das ist richtig so: Es sind Hashwerte,
/// keine Farben.
///
/// Die Kennung ist der 32-Bit-MurmurHash3 des Namens, als Gleitkomma umgedeutet -
/// derselbe Name ergibt damit ueber Dateien und Blender-Fassungen hinweg dieselbe
/// Kennung. Verglichen wird deshalb auf GENAUE Gleichheit; ein Toleranzband waere
/// hier kein Entgegenkommen, sondern eine Verwechslung zweier Objekte.
/// </summary>
public static class Cryptomatte
{
    private const string Prefix = "cryptomatte/";

    /// <summary>
    /// Liest die Kryptomatten aus dem Dateikopf. Leer, wenn keine darin stehen.
    ///
    /// Der Kopf fuehrt je Satz vier Eintraege unter einer Kennung, die aus dem Namen
    /// gebildet ist: name, hash, conversion und manifest. Gebraucht werden hier zwei
    /// - der Name, unter dem die Stufen stehen, und die Zuordnung von Namen zu
    /// Kennungen.
    /// </summary>
    public static IReadOnlyList<CryptomatteSet> Read(ExrHeader header)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in header.RawAttributes)
        {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal)) continue;

            int slash = key.IndexOf('/', Prefix.Length);
            if (slash < 0) continue;

            string id = key[Prefix.Length..slash];
            string field = key[(slash + 1)..];

            if (field.Equals("name", StringComparison.Ordinal)) names[id] = Text(value);
            else if (field.Equals("manifest", StringComparison.Ordinal)) manifests[id] = Text(value);
        }

        var sets = new List<CryptomatteSet>();

        foreach (var (id, name) in names)
        {
            if (name.Length == 0) continue;

            manifests.TryGetValue(id, out string? manifest);
            sets.Add(new CryptomatteSet(name, ParseManifest(manifest)));
        }

        // Nach Namen sortiert, damit die Reihenfolge nicht davon abhaengt, wie ein
        // Woerterbuch gerade laeuft - in einer Auswahlliste waere das sonst mal so
        // und mal anders herum.
        sets.Sort((a, b) => string.CompareOrdinal(a.Prefix, b.Prefix));

        return sets;
    }

    /// <summary>Wie <see cref="Read(ExrHeader)"/>, aber fuer eine Datei.</summary>
    public static IReadOnlyList<CryptomatteSet> Of(string path)
    {
        try
        {
            using var stream = ExrReader.Open(path);
            return Read(ExrHeaderReader.Read(stream));
        }
        catch (Exception)
        {
            return Array.Empty<CryptomatteSet>();
        }
    }

    /// <summary>
    /// Die Stufen eines Satzes, in der Reihenfolge 00, 01, 02 - so viele, wie die
    /// Datei fuehrt.
    /// </summary>
    public static IReadOnlyList<string> Levels(IReadOnlyList<ExrPass> passes, string prefix)
    {
        var found = new List<string>();

        for (int level = 0; level < MaxLevels; level++)
        {
            string wanted = prefix + level.ToString("00");

            // Ueber Find und nicht ueber den blossen Vergleich, damit ein Rezept
            // eine umbenannte Ansichtsebene uebersteht.
            if (ExrPasses.Find(passes, wanted) is not { } pass) break;

            found.Add(pass.Name);
        }

        return found;
    }

    /// <summary>
    /// Wie viele Stufen hoechstens gesucht werden.
    ///
    /// Blender laesst bis zu 16 Rangstufen zu, und zwei davon passen in eine Stufe -
    /// acht ist also die Obergrenze und nicht eine gegriffene Zahl. Der uebliche Fall
    /// sind drei.
    /// </summary>
    public const int MaxLevels = 8;

    /// <summary>
    /// Ob ein Pass eine Kryptomattenstufe ist. Solche Passe gehoeren nicht in einen
    /// Bildstapel - als Bild angesehen sind es Hashwerte, kein Licht.
    /// </summary>
    public static bool IsLevel(string shortName)
        => shortName.Length > 2 &&
           shortName.StartsWith("Crypto", StringComparison.OrdinalIgnoreCase) &&
           char.IsDigit(shortName[^1]) && char.IsDigit(shortName[^2]);

    /// <summary>
    /// Die Kennung aus einem Hexwert des Manifests.
    ///
    /// "532d6818" ist der Hash als 32-Bit-Zahl; in der Datei steht dasselbe Bitmuster
    /// als Gleitkomma. Umgedeutet statt umgerechnet - eine Umrechnung ergaebe eine
    /// voellig andere Zahl und damit eine Maske, die nie etwas trifft.
    /// </summary>
    public static float IdFromHex(string hex)
    {
        uint bits = Convert.ToUInt32(hex, 16);
        return BitConverter.UInt32BitsToSingle(bits);
    }

    private static IReadOnlyDictionary<string, float> ParseManifest(string? json)
    {
        var map = new Dictionary<string, float>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return map;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return map;

            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String) continue;

                string? hex = entry.Value.GetString();
                if (string.IsNullOrEmpty(hex)) continue;

                try { map[entry.Name] = IdFromHex(hex); }
                catch (Exception) { /* ein einzelner kaputter Eintrag kostet nicht das ganze Manifest */ }
            }
        }
        catch (JsonException)
        {
            // Ein unlesbares Manifest heisst: keine Namen. Auswaehlen laesst sich
            // trotzdem, nur steht dann die Kennung statt des Namens da - das ist
            // brauchbarer als gar keine Kryptomatte.
        }

        return map;
    }

    private static string Text(byte[] value)
    {
        // EXR schreibt ein string-Attribut ohne Abschluss; die Laenge steht im Kopf.
        // Ein nachlaufendes Nullbyte kommt trotzdem vor, und es gehoert nicht zum Namen.
        int length = value.Length;
        while (length > 0 && value[length - 1] == 0) length--;

        return Encoding.UTF8.GetString(value, 0, length);
    }
}
