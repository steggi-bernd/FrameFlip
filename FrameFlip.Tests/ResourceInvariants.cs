using System.IO;
using System.Text.RegularExpressions;

namespace FrameFlip.Tests;

/// <summary>
/// Die Ressourcenschluessel der Oberflaeche.
///
/// FindResource wirft, wenn der Schluessel nicht existiert - und zwar erst dann,
/// wenn jemand die Seite oeffnet. Ein vertippter Pinselname faellt beim Uebersetzen
/// nicht auf, sondern beim Benutzen, als leeres Fenster mit einer Ausnahme dahinter.
/// Genau die Sorte Fehler, die eine in C# gebaute Oberflaeche einlaedt: Die Schluessel
/// sind Zeichenketten, und Zeichenketten prueft niemand.
///
/// Der Test liest die Quellen, nicht die gebaute Anwendung. Was hier steht, gilt
/// deshalb auch fuer Seiten, die in keinem Testlauf jemals gezeichnet werden.
/// </summary>
public static class ResourceInvariants
{
    /// <summary>Schluessel mit S_ stehen in den Woerterbuechern - die pruefen die Sprachtests.</summary>
    private static bool IsText(string key) => key.StartsWith("S_", StringComparison.Ordinal);

    public static void Run()
    {
        Check.Group("Oberflaeche - jeder Ressourcenschluessel existiert");

        string? source = FindSource();

        if (source is null)
        {
            Check.That(false, "der Quellordner wurde gefunden", "nicht gefunden");
            return;
        }

        var defined = new HashSet<string>(StringComparer.Ordinal);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string file in Files(source))
        {
            string text = File.ReadAllText(file);
            string name = Path.GetFileName(file);

            foreach (Match match in Regex.Matches(text, @"x:Key=""(?<key>[^""]+)"""))
                defined.Add(match.Groups["key"].Value);

            if (Path.GetExtension(file).Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in Regex.Matches(
                             text, @"\{(?:Static|Dynamic)Resource\s+(?<key>[A-Za-z][A-Za-z0-9_]*)\s*\}"))
                {
                    used.TryAdd(match.Groups["key"].Value, name);
                }
            }
            else
            {
                // Auch die Form mit Fallunterscheidung: FindResource(x ? "A" : "B").
                foreach (Match call in Regex.Matches(text, @"FindResource\((?<args>[^)]*)\)"))
                {
                    foreach (Match literal in Regex.Matches(call.Groups["args"].Value,
                                                            @"""(?<key>[A-Za-z][A-Za-z0-9_]*)"""))
                    {
                        used.TryAdd(literal.Groups["key"].Value, name);
                    }
                }
            }
        }

        Check.That(defined.Count > 30, "die Ressourcen wurden gefunden", $"{defined.Count} Schluessel");
        Check.That(used.Count > 30, "die Fundstellen wurden gefunden", $"{used.Count} Verwendungen");

        var missing = used.Where(pair => !IsText(pair.Key) && !defined.Contains(pair.Key))
                          .Select(pair => $"{pair.Key} ({pair.Value})")
                          .OrderBy(entry => entry, StringComparer.Ordinal)
                          .ToList();

        Check.That(missing.Count == 0, "jeder benutzte Schluessel ist auch definiert",
                   missing.Count == 0 ? null : string.Join(", ", missing.Take(6)));
    }

    private static IEnumerable<string> Files(string source)
        => Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
                    .Where(file => Path.GetExtension(file) is ".cs" or ".xaml")
                    .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                   && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string? FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory.FullName, "FrameFlip", "Views");
            if (Directory.Exists(candidate)) return Path.Combine(directory.FullName, "FrameFlip");

            directory = directory.Parent;
        }

        return null;
    }
}
