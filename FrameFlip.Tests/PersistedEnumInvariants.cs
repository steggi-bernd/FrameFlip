using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using FrameFlip.Configuration;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Was <see cref="SettingsStore"/> als Zahl schreibt, muss als dieselbe Zahl wieder
/// gelesen werden - auch nach dem naechsten neuen Wert in einer Aufzaehlung.
///
/// Der Anlass (Refactoring-Studio, S0, Befund 1): Die Einstellungen schreiben
/// Aufzaehlungen als Zahlen. Am 20. September kam die Farbbereichsmaske VOR Kryptomatte
/// und Gemalt in <see cref="MaskKind"/>, und beide rueckten um eins. Ein Rezept aus der
/// Zeit davor hiesse seither etwas anderes. Veroeffentlicht war die alte Zaehlung nie -
/// das Atelier kam erst am 24. September nach main -, aber derselbe Griff an einer
/// anderen Aufzaehlung trifft dann echte Dateien.
/// </summary>
public static class PersistedEnumInvariants
{
    public static void Run()
    {
        TheNumbersStay();
        AnOldRecipeMeansTheSame();
    }

    /// <summary>
    /// Jede Aufzaehlung, die in den Einstellungen steht, mit ihren Zahlen. Wer einen Wert
    /// einfuegt statt anhaengt, sieht hier, welche Zahl sich verschoben haette.
    /// </summary>
    private const string Pinned = """
        BlendMode: Normal=0 Add=1 Multiply=2 Screen=3 Darken=4 Lighten=5 Difference=6 Overlay=7 SoftLight=8 HardLight=9 ColourDodge=10 ColourBurn=11 LinearBurn=12 LinearLight=13 VividLight=14 PinLight=15 Exclusion=16 Subtract=17 Divide=18 Hue=19 Saturation=20 Colour=21 Luminosity=22
        ChannelView: All=0 Red=1 Green=2 Blue=3 Alpha=4 Luminance=5
        DataStage: Motion=0 Focus=1
        DiffusionKernel: FloydSteinberg=0 Atkinson=1 JarvisJudiceNinke=2 Stucki=3 Burkes=4 Sierra=5
        DisplaceFrom: Normal=0 Motion=1 Screen=2
        DitherPattern: Ordered=0 Noise=1 Lines=2
        GradingStage: SceneLinear=0 Display=1
        LayerContent: Pass=0 Adjustment=1 Image=2 Group=3
        LocalStage: Haze=0 Light=1 Denoise=2 Contrast=3 Texture=4 Detail=5
        MaskKind: None=0 Luminance=1 Underlying=2 Pass=3 Gradient=4 Colour=5 Cryptomatte=6 Painted=7
        MaskScope: Visibility=0 Colour=1
        OpticsStage: Lens=0 Film=1
        PassNeed: Depth=0 Motion=1 Normal=2 None=3
        SortInterval: Threshold=0 Edges=1 Random=2
        SortKey: Brightness=0 Hue=1 Saturation=2 Intensity=3 Minimum=4
        WaveShape: Sine=0 Triangle=1 Square=2 Saw=3 Slices=4 Blocks=5
        """;

    private static void TheNumbersStay()
    {
        Check.Group("Gespeicherte Aufzaehlungen: die Zahlen bleiben");

        string now = Snapshot();

        var want = Pinned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var have = now.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string line in want)
        {
            string name = line[..line.IndexOf(':')];
            string? current = have.FirstOrDefault(h => h.StartsWith(name + ":", StringComparison.Ordinal));

            // Angehaengte Werte sind erlaubt - die bisherigen muessen vorn unveraendert stehen.
            Check.That(current is not null && (current == line || current.StartsWith(line + " ", StringComparison.Ordinal)),
                       $"{name}: gespeicherte Zahlen unveraendert", current ?? "fehlt");
        }
    }

    private static void AnOldRecipeMeansTheSame()
    {
        Check.Group("Gespeicherte Aufzaehlungen: eine Altdatei");

        // Ein Rezept, wie FrameFlip es seit dem 20. September schreibt - mit Zahlen.
        const string old = """
        {
          "Layers": {
            "Layers": [
              { "Content": 0, "Source": "", "Mode": 0 },
              { "Content": 1, "Name": "Objekt", "Mode": 0, "Mask": { "Kind": 6, "Source": "CryptoObject" } },
              { "Content": 1, "Name": "Gemalt", "Mode": 0, "Mask": { "Kind": 7, "PaintLocked": true } },
              { "Content": 1, "Name": "Farbe", "Mode": 0, "Mask": { "Kind": 5 } }
            ]
          }
        }
        """;

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-enums-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");

        try
        {
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));
            File.WriteAllText(SettingsStore.FilePath, old);

            var layers = SettingsStore.Load().Layers?.Layers;

            Check.That(layers is { Count: 4 } && layers[1].Mask.Kind == MaskKind.Cryptomatte &&
                       layers[2].Mask.Kind == MaskKind.Painted && layers[3].Mask.Kind == MaskKind.Colour,
                       "6 ist Kryptomatte, 7 gemalt, 5 Farbbereich - wie beim Schreiben",
                       layers is null ? "keine Ebenen" : string.Join(", ", layers.Select(l => l.Mask.Kind)));

            // Und was heute geschrieben wird, liest sich wieder so.
            SettingsStore.Save(SettingsStore.Load());
            var again = SettingsStore.Load().Layers?.Layers;

            Check.That(again is { Count: 4 } && again.Select(l => l.Mask.Kind).SequenceEqual(layers!.Select(l => l.Mask.Kind)),
                       "Speichern und Laden aendert keine Maskenart");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Alle Aufzaehlungen, die von <see cref="AppSettings"/> aus erreichbar gespeichert
    /// werden - ueber Eigenschaften, Listen, Woerterbuecher und die abgeleiteten Typen
    /// polymorpher Werkzeuge -, je eine Zeile "Name: Wert=Zahl ...".
    /// </summary>
    internal static string Snapshot()
    {
        var enums = new SortedDictionary<string, Type>(StringComparer.Ordinal);
        var seen = new HashSet<Type>();

        void Walk(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (!seen.Add(type)) return;

            if (type.IsEnum)
            {
                enums[type.Name] = type;
                return;
            }

            if (type.IsArray)
            {
                Walk(type.GetElementType()!);
                return;
            }

            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments()) Walk(argument);

            if (type.Namespace is null || !type.Namespace.StartsWith("FrameFlip", StringComparison.Ordinal)) return;

            foreach (var derived in type.GetCustomAttributes<JsonDerivedTypeAttribute>()) Walk(derived.DerivedType);

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;

                Walk(property.PropertyType);
            }
        }

        Walk(typeof(AppSettings));

        var text = new StringBuilder();

        foreach (var (name, type) in enums)
        {
            text.Append(name).Append(':');

            foreach (var value in Enum.GetValues(type))
                text.Append(' ').Append(value).Append('=').Append(Convert.ToInt64(value));

            text.Append('\n');
        }

        return text.ToString();
    }
}
