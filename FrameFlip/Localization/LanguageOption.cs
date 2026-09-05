namespace FrameFlip.Localization;

/// <summary>
/// Ein Eintrag der Sprachauswahl.
///
/// Der Name steht bewusst in der jeweiligen Sprache selbst - "Deutsch" und
/// "English", nicht uebersetzt. Wer die Oberflaeche gerade nicht lesen kann, weil
/// sie in der falschen Sprache steht, findet den eigenen Sprachnamen trotzdem.
/// </summary>
public sealed record LanguageOption(Language Value, string Name)
{
    public static readonly LanguageOption[] All =
    {
        new(Language.German, "Deutsch"),
        new(Language.English, "English"),
    };

    public static LanguageOption For(Language language)
        => All.FirstOrDefault(option => option.Value == language) ?? All[0];

    public override string ToString() => Name;
}
