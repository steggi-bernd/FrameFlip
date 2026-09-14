using System.IO;
using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Eine Nachschlagetabelle aus einer .cube-Datei, mit Staerkeregler.
///
/// Das Werkzeug, mit dem FrameFlip an eine Pipeline anschliesst, von der es nichts
/// weiss: Wer eine Show-LUT oder einen fertigen Look hat, bringt ihn mit, statt ihn
/// hier nachzustellen. Dieselben Tabellen, die auch die Sichtumwandlung benutzt -
/// der Leser dafuer ist schon da.
///
/// **Die Tabelle wird nicht mitgespeichert, nur ihr Pfad.** Eine 57er-Tabelle sind
/// 2,2 MB, und die gehoeren nicht in eine Konfigurationsdatei. Liegt die Datei beim
/// naechsten Start nicht mehr da, faellt das Werkzeug still auf neutral zurueck -
/// ein Rezept, das sich nicht mehr oeffnen laesst, weil eine Datei fehlt, waere die
/// schlechtere Antwort.
/// </summary>
public sealed class LutTool : IGradingTool
{
    public const string KindName = "lut";

    public string Kind => KindName;

    /// <summary>
    /// Nach der Sichtumwandlung. Ein Look erwartet Anzeigewerte von 0 bis 1 -
    /// davor bekaeme er Szenenlicht ohne obere Grenze und wuesste damit nichts
    /// anzufangen.
    /// </summary>
    public GradingStage Stage => GradingStage.Display;

    private string _path = string.Empty;
    private float _strength = 1f;
    private CubeLut? _lut;
    private string? _loadedFrom;

    /// <summary>Pfad der .cube-Datei. Leer heisst: keine Tabelle.</summary>
    public string Path
    {
        get => _path;
        set
        {
            string next = value?.Trim() ?? string.Empty;
            if (string.Equals(next, _path, StringComparison.OrdinalIgnoreCase)) return;

            _path = next;
            _lut = null;
            _loadedFrom = null;
        }
    }

    /// <summary>0 bis 1. Mischt zwischen dem Bild und dem, was die Tabelle daraus macht.</summary>
    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Was beim Laden schiefging, fuer die Anzeige. Null heisst: alles gut.</summary>
    [JsonIgnore]
    public string? Error { get; private set; }

    [JsonIgnore]
    public bool IsNeutral => _path.Length == 0 || _strength < 0.001f;

    public void Prepare()
    {
        if (IsNeutral)
        {
            _lut = null;
            return;
        }

        // Nur neu lesen, wenn sich der Pfad geaendert hat: 185 000 Zeilen Text bei
        // jedem Reglerzug einzulesen waere das Ende der Bedienbarkeit.
        if (_lut is not null && string.Equals(_loadedFrom, _path, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            _lut = CubeLut.Load(_path);
            _loadedFrom = _path;
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _lut = null;
            _loadedFrom = null;
            Error = ex.Message;
        }
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        var lut = _lut;
        if (lut is null) return;

        float nr = r, ng = g, nb = b;
        lut.Apply(ref nr, ref ng, ref nb);

        if (_strength >= 0.999f)
        {
            r = nr;
            g = ng;
            b = nb;
            return;
        }

        // Gemischt wird auf der Anzeigeseite, wo beide Seiten dasselbe meinen -
        // ein Look ist ohnehin keine physikalische Groesse, die man in linearem
        // Licht mitteln muesste.
        r += (nr - r) * _strength;
        g += (ng - g) * _strength;
        b += (nb - b) * _strength;
    }

    public LutTool Clone() => new() { Path = _path, Strength = _strength };
}
