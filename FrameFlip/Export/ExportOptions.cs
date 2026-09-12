namespace FrameFlip.Export;

/// <summary>
/// Eine Qualitaetsstufe.
///
/// Die Stufe ist keine Zahl, sondern eine Absicht - "so gut wie das Original" oder
/// "klein genug zum Verschicken". Welcher CRF-Wert dahintersteht, haengt vom Codec ab:
/// x265 braucht fuer dieselbe Bildguete rund sechs Stufen mehr als x264, und VP9
/// rechnet noch einmal anders. Deshalb traegt die Stufe einen Versatz zum Vorgabewert
/// des Formats, nicht eine feste Zahl.
/// </summary>
public sealed record ExportQuality(string Name, int Offset, string Hint)
{
    public override string ToString() => Name;

    public static readonly ExportQuality Best = new(
        "Sehr hoch", -4, "Praktisch nicht vom Original zu unterscheiden. Große Dateien.");

    public static readonly ExportQuality High = new(
        "Hoch", 0, "Die Vorgabe des Formats. Für Ansicht und Abnahme.");

    public static readonly ExportQuality Medium = new(
        "Mittel", 4, "Sichtbar kleiner, für ein geübtes Auge erkennbar weicher.");

    public static readonly ExportQuality Small = new(
        "Platzsparend", 8, "Zum Verschicken. Deutlich kleiner, deutlich weicher.");

    public static readonly IReadOnlyList<ExportQuality> All = new[] { Best, High, Medium, Small };
}

/// <summary>
/// Wie gruendlich der Encoder suchen darf.
///
/// Dieselbe Qualitaetsstufe, mehr Rechenzeit, kleinere Datei - das ist der ganze
/// Handel. "Schnell" ist nicht schlechter im Bild, nur groesser in der Datei.
/// </summary>
public sealed record ExportSpeed(string Name, string Value, double Factor, string Hint)
{
    public override string ToString() => Name;

    /// <summary>Wie sich die Dateigroesse gegenueber der mittleren Stufe verhaelt.</summary>
    public double SizeFactor { get; init; } = 1.0;

    public static readonly ExportSpeed Fast = new(
        "Schnell", "veryfast", 3.2, "Kürzeste Wartezeit, größte Datei.") { SizeFactor = 1.35 };

    public static readonly ExportSpeed Balanced = new(
        "Ausgewogen", "medium", 1.0, "Die Vorgabe. Guter Handel aus Zeit und Größe.");

    public static readonly ExportSpeed Compact = new(
        "Klein", "slow", 0.45, "Rund die Hälfte langsamer, dafür kleiner.") { SizeFactor = 0.88 };

    public static readonly IReadOnlyList<ExportSpeed> All = new[] { Fast, Balanced, Compact };
}
