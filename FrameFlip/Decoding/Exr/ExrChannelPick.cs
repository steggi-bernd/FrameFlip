namespace FrameFlip.Decoding.Exr;

/// <summary>Welche Kanaele einer Datei das sichtbare Bild ergeben.</summary>
/// <param name="Layer">Die Ebene, aus der sie stammen. Null bei einer schmucklosen Datei.</param>
public readonly record struct ExrColourChannels(string? Red, string? Green, string? Blue, string? Alpha, string? Layer)
{
    public bool IsComplete => Red is not null && Green is not null && Blue is not null;

    public bool HasAlpha => Alpha is not null;
}

/// <summary>
/// Sucht die Farbkanaele einer EXR.
///
/// Eine einfache Datei hat "R", "G", "B". Ein Multilayer-Render aus Blender hat
/// stattdessen "ViewLayer.Combined.R" und zwanzig weitere - und wie dieser Name
/// gebildet wird, haengt an der Blender-Fassung und am Namen der Ansichtsebene, den
/// der Anwender frei vergibt. Auf eine Schreibweise zu pruefen waere deshalb eine
/// Wette, die sich mit der naechsten Fassung verliert.
///
/// Gesucht wird stattdessen der Reihe nach: erst die schmucklosen Namen, dann eine
/// Ebene, deren Name auf "Combined" endet, dann irgendeine mit vollstaendigem Tripel.
/// </summary>
public static class ExrChannelPick
{
    public static ExrColourChannels Colour(ExrHeader header) => Colour(header.Channels.Select(c => c.Name));

    public static ExrColourChannels Colour(IEnumerable<string> channelNames)
    {
        var names = channelNames as IList<string> ?? channelNames.ToList();

        if (names.Contains("R") && names.Contains("G") && names.Contains("B"))
            return new ExrColourChannels("R", "G", "B", names.Contains("A") ? "A" : null, null);

        // Nach Ebene gruppieren: alles vor dem letzten Punkt. Der Punkt trennt den
        // Kanalbuchstaben ab, nicht die Ebenen untereinander - "ViewLayer.Combined"
        // bleibt als Ganzes stehen.
        var layers = names
            .Where(n => n.Contains('.'))
            .Select(n => n[..n.LastIndexOf('.')])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        ExrColourChannels? Build(string layer)
        {
            string? Find(string leaf)
                => names.FirstOrDefault(n => n.Equals($"{layer}.{leaf}", StringComparison.Ordinal));

            string? red = Find("R");
            string? green = Find("G");
            string? blue = Find("B");

            return red is null || green is null || blue is null
                ? null
                : new ExrColourChannels(red, green, blue, Find("A"), layer);
        }

        string? combined = layers.FirstOrDefault(
            l => l.EndsWith("Combined", StringComparison.OrdinalIgnoreCase));

        if (combined is not null && Build(combined) is { } fromCombined) return fromCombined;

        foreach (string layer in layers)
        {
            if (Build(layer) is { } any) return any;
        }

        // Nur ein einziger Kanal - eine reine Tiefen- oder Maskendatei. Sie als
        // Graustufenbild zu zeigen ist die richtige Antwort auf die Frage, was
        // darin steht.
        if (names.Count == 1)
            return new ExrColourChannels(names[0], names[0], names[0], null, null);

        return default;
    }
}
