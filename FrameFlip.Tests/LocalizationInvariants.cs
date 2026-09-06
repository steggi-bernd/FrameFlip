using System.IO;
using System.Text.RegularExpressions;

namespace FrameFlip.Tests;

/// <summary>
/// Die Sprachwoerterbuecher.
///
/// Der eine Fehler, der hier zaehlt: ein Schluessel, den nur eine Sprache kennt.
/// Er faellt zur Laufzeit nicht auf - die Oberflaeche zeigt dann einfach "S_Export"
/// statt "Export", und zwar nur in der einen Sprache und nur an der einen Stelle.
/// Ein Test findet das in Millisekunden, ein Mensch beim Durchklicken vielleicht nie.
/// </summary>
public static class LocalizationInvariants
{
    public static void Run()
    {
        Check.Group("Sprachen - beide Woerterbuecher passen zusammen");

        var folder = FindLocalizationFolder();

        if (folder is null)
        {
            Check.That(false, "die Woerterbuecher wurden gefunden", "Ordner nicht gefunden");
            return;
        }

        var german = Read(Path.Combine(folder, "Strings.de.xaml"));
        var english = Read(Path.Combine(folder, "Strings.en.xaml"));

        Check.That(german.Count > 100, "Deutsch enthaelt die Texte", $"{german.Count} Schluessel");
        Check.That(english.Count > 100, "Englisch enthaelt die Texte", $"{english.Count} Schluessel");

        var onlyGerman = german.Keys.Except(english.Keys).OrderBy(k => k).ToList();
        var onlyEnglish = english.Keys.Except(german.Keys).OrderBy(k => k).ToList();

        Check.That(onlyGerman.Count == 0,
            "kein Schluessel fehlt im Englischen", Join(onlyGerman));
        Check.That(onlyEnglish.Count == 0,
            "kein Schluessel fehlt im Deutschen", Join(onlyEnglish));

        // Ein leerer Text ist schlimmer als ein fehlender: Er faellt beim Ansehen
        // nicht auf, sondern hinterlaesst nur eine Luecke.
        var emptyGerman = german.Where(pair => pair.Value.Length == 0).Select(p => p.Key).ToList();
        var emptyEnglish = english.Where(pair => pair.Value.Length == 0).Select(p => p.Key).ToList();

        Check.That(emptyGerman.Count == 0, "kein leerer deutscher Text", Join(emptyGerman));
        Check.That(emptyEnglish.Count == 0, "kein leerer englischer Text", Join(emptyEnglish));

        // Platzhalter muessen auf beiden Seiten dieselben sein - sonst wirft
        // string.Format in der einen Sprache und in der anderen nicht.
        var mismatched = new List<string>();

        foreach (var (key, value) in german)
        {
            if (!english.TryGetValue(key, out var other)) continue;
            if (Placeholders(value) != Placeholders(other)) mismatched.Add(key);
        }

        Check.That(mismatched.Count == 0,
            "gleiche Platzhalter in beiden Sprachen", Join(mismatched));

        // Deutsche Umlaute im englischen Woerterbuch deuten auf vergessene Texte.
        var untranslated = english
            .Where(pair => pair.Value.Any(c => c is 'ä' or 'ö' or 'ü' or 'ß' or 'Ä' or 'Ö' or 'Ü'))
            .Select(pair => pair.Key)
            .ToList();

        Check.That(untranslated.Count == 0,
            "im Englischen steht kein deutscher Text mehr", Join(untranslated));

        // Und die andere Richtung: ein Schluessel, den der Code benutzt, den es aber
        // nicht gibt. Zur Laufzeit steht dann "S_TrayOpen" im Menue - kein Absturz,
        // keine Meldung, nur ein Wort, das niemand geschrieben hat. Beim Umbenennen
        // eines Schluessels passiert genau das.
        var source = Directory.GetParent(folder)?.FullName;

        if (source is null)
        {
            Check.That(false, "der Quellordner wurde gefunden");
            return;
        }

        var used = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".xaml")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            string text = File.ReadAllText(file);

            // Nur Schluessel mit dem S_-Praefix: DynamicResource traegt auch Pinsel
            // und Vorlagen, und die stehen nicht im Woerterbuch.
            foreach (Match match in Regex.Matches(text, @"Strings\.T\(""(?<key>S_[A-Za-z0-9_]+)"""))
                used.Add(match.Groups["key"].Value);

            foreach (Match match in Regex.Matches(text, @"(?:Dynamic|Static)Resource\s+(?<key>S_[A-Za-z0-9_]+)\s*\}"))
                used.Add(match.Groups["key"].Value);
        }

        Check.That(used.Count > 100, "die benutzten Schluessel wurden gefunden", $"{used.Count} Fundstellen");

        var missing = used.Where(key => !german.ContainsKey(key)).ToList();

        Check.That(missing.Count == 0, "jeder benutzte Schluessel steht im Woerterbuch", Join(missing));
    }

    private static string Join(List<string> keys)
        => keys.Count == 0 ? "" : string.Join(", ", keys.Take(6)) + (keys.Count > 6 ? " …" : "");

    /// <summary>Wie viele und welche Platzhalter der Text benutzt.</summary>
    private static string Placeholders(string text)
        => string.Concat(Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(v => v));

    private static Dictionary<string, string> Read(string path)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return found;

        foreach (Match match in Regex.Matches(File.ReadAllText(path),
                     @"x:Key=""(?<key>[^""]+)""\s*>(?<value>.*?)</sys:String>",
                     RegexOptions.Singleline))
        {
            found[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        return found;
    }

    /// <summary>
    /// Sucht den Quellordner vom Ausfuehrungsverzeichnis aus aufwaerts. Der Test
    /// laeuft aus bin/, die Woerterbuecher liegen im Projekt - ein fester relativer
    /// Pfad haette sich beim naechsten Umbau der Ausgabe verabschiedet.
    /// </summary>
    private static string? FindLocalizationFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "FrameFlip", "Localization");
            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
