using System.Windows;

namespace FrameFlip.Localization;

public enum Language
{
    German,
    English,
}

/// <summary>
/// Sprachumschaltung zur Laufzeit.
///
/// Die Texte liegen als ResourceDictionary je Sprache. Beim Wechsel wird das eine
/// gegen das andere getauscht - jede Stelle, die im XAML <c>{DynamicResource …}</c>
/// benutzt, aktualisiert sich dadurch von selbst. Mit <c>StaticResource</c> waere
/// dafuer ein Neustart noetig.
///
/// Bewusst ohne .resx und ohne Satelliten-Assemblies: Die brauchen einen
/// Kulturwechsel je Thread, greifen erst beim naechsten Fensteraufbau und liessen
/// sich nicht so einfach im laufenden Betrieb umstellen. Zwei XAML-Woerterbuecher
/// sind hier das kleinere und das ehrlichere Mittel.
/// </summary>
public static class Strings
{
    private static ResourceDictionary? _active;

    /// <summary>Wird nach einem Sprachwechsel ausgeloest - fuer Texte, die Code setzt.</summary>
    public static event Action? Changed;

    public static Language Current { get; private set; } = Language.German;

    public static void Apply(Language language)
    {
        var application = Application.Current;
        if (application is null) return;

        var next = new ResourceDictionary { Source = SourceFor(language) };

        // Erst anhaengen, dann das alte entfernen: Dazwischen darf keine Anfrage
        // ins Leere laufen, sonst wirft FindResource mitten im Zeichnen.
        application.Resources.MergedDictionaries.Add(next);

        if (_active is not null)
            application.Resources.MergedDictionaries.Remove(_active);

        _active = next;
        Current = language;

        try { Changed?.Invoke(); }
        catch (Exception) { /* ein Empfaenger darf den Wechsel nicht verhindern */ }
    }

    private static Uri SourceFor(Language language) => new(
        language == Language.English
            ? "Localization/Strings.en.xaml"
            : "Localization/Strings.de.xaml",
        UriKind.Relative);

    /// <summary>
    /// Ein Text fuer den Code. Fehlt der Schluessel, kommt er selbst zurueck - das
    /// faellt beim Ansehen sofort auf, statt eine leere Zeile zu hinterlassen.
    /// </summary>
    public static string T(string key)
    {
        try
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }
        catch (Exception)
        {
            return key;
        }
    }

    /// <summary>Wie <see cref="T"/>, mit Platzhaltern.</summary>
    public static string T(string key, params object?[] values)
    {
        try { return string.Format(T(key), values); }
        catch (FormatException) { return T(key); }
    }

    public static Language Parse(string? value)
        => string.Equals(value, "en", StringComparison.OrdinalIgnoreCase)
            ? Language.English
            : Language.German;

    public static string ToCode(Language language)
        => language == Language.English ? "en" : "de";
}
