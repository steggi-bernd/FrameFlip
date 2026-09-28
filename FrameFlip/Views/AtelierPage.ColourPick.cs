using System.IO;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Farbspeicher und Farbraeder (docs/Atelier-Arbeitsablauf.md, C4b).
///
/// Jede Farbe, die die Pipette liest, wird gemerkt - ueber das Bild hinaus, damit sie in einem
/// anderen Bild weiterverwendet werden kann. Und die Raeder bekommen eine Pipette: Lift, Gamma
/// und Gain neutralisieren die Stelle, auf die man klickt; die Toenung einer Ebene uebernimmt
/// die Farbe - aus dem Bild oder aus dem Speicher.
///
/// Dasselbe Muster wie die Pipetten des Tonwerts: Ein Knopf am Rad macht die Maus zur Pipette,
/// der naechste Klick ins Bild stellt ein, und die Maus geht zu ihrem vorigen Werkzeug zurueck.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Die Pipette an einem Rad von Lift, Gamma und Gain, die auf einen Klick wartet.</summary>
    private (LiftGammaGainTool Tool, ZoneKind Zone)? _zonePick;

    /// <summary>Die Ebene, deren Toenung auf eine Farbe wartet.</summary>
    private ImageLayer? _tintPick;

    /// <summary>Das Werkzeug der Maus vor der Pipette - danach geht es dorthin zurueck.</summary>
    private AtelierTool _beforeColourPick = AtelierTool.Move;

    private void SetUpColourPick()
    {
        Tools.ZonePickWanted += (tool, zone) =>
        {
            if (zone is { } chosen) StartColourPick(() => _zonePick = (tool, chosen));
            else EndColourPick();
        };

        Layers.TintPickWanted += layer =>
        {
            if (layer is not null) StartColourPick(() => _tintPick = layer);
            else EndColourPick();
        };

        Properties.StoredColourChosen += colour => TakeStoredColour(colour);
        Properties.StoredColourRemoved += colour =>
        {
            _settings.AtelierColours.Remove(colour);
            _persist(_settings);
            Properties.ShowStore(_settings.AtelierColours);
        };

        Properties.ShowStore(_settings.AtelierColours);
    }

    /// <summary>Ob eine Pipette an einem Rad oder an der Toenung wartet - fuer die Probe.</summary>
    internal bool ColourPicking => _zonePick is not null || _tintPick is not null;

    private void StartColourPick(Action arm)
    {
        if (!ColourPicking && _levelsPick is null) _beforeColourPick = _tool == AtelierTool.Pick ? AtelierTool.Move : _tool;

        // Immer nur eine wartende Pipette: die andere geht aus.
        _zonePick = null;
        _tintPick = null;
        LeaveLevelsPickQuietly();

        arm();

        MouseTools.Select(AtelierTool.Pick);
        OnToolChanged(AtelierTool.Pick);
    }

    /// <summary>Beendet eine wartende Pipette an Rad oder Toenung - ihre Knoepfe gehen aus.</summary>
    private void EndColourPick()
    {
        if (!ColourPicking) return;

        _zonePick = null;
        _tintPick = null;

        Tools.EndZonePick();
        Layers.EndTintPick();
    }

    /// <summary>Beendet sie und gibt der Maus ihr voriges Werkzeug zurueck.</summary>
    private void LeaveColourPick()
    {
        if (!ColourPicking) return;

        var back = _beforeColourPick;
        EndColourPick();

        MouseTools.Select(back);
        OnToolChanged(back);
    }

    /// <summary>Eine wartende Tonwert-Pipette weicht, ohne die Maus zurueckzustellen.</summary>
    private void LeaveLevelsPickQuietly() => EndLevelsPick();

    /// <summary>Was die Lupe ueber die wartende Pipette sagt - oder null, wenn keine wartet.</summary>
    private string? ColourPickPurpose()
    {
        if (_zonePick is { } zone)
            return Strings.T("S_PickForZone", Strings.T(zone.Zone switch
            {
                ZoneKind.Lift => "S_ZoneLift",
                ZoneKind.Gamma => "S_ZoneGamma",
                _ => "S_ZoneGain",
            }));

        if (_tintPick is { } layer) return Strings.T("S_PickForTint", layer.Name);

        return null;
    }

    /// <summary>
    /// Ein Klick ins Bild mit einer wartenden Pipette an Rad oder Toenung. Falsch, wenn keine
    /// wartet - dann liest die Pipette nur ab.
    /// </summary>
    internal bool ColourPickAt(int x, int y)
    {
        if (_zonePick is { } zone)
        {
            // Der Ton, wie er bei Lift, Gamma und Gain ankommt - dieselbe Frage wie beim Tonwert.
            if (LevelsInputAt(zone.Tool, x, y) is not var (r, g, b)) return false;

            Tools.NeutraliseZone(zone.Zone, r, g, b);
            RememberColour(x, y);
            LeaveColourPick();
            return true;
        }

        if (_tintPick is not null)
        {
            // Inzwischen eine andere Ebene gewaehlt: Die Toenung gehoerte der alten - die Pipette endet.
            if (!ReferenceEquals(Layers.Selection, _tintPick))
            {
                LeaveColourPick();
                return false;
            }

            if (LinearAt(x, y) is not var (r, g, b)) return false;

            Layers.TakeTint(r, g, b);
            RememberColour(x, y);
            LeaveColourPick();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ein Feld aus dem Farbspeicher: in die wartende Toenung - die Farbe eines anderen Bildes
    /// in diesem. Ohne wartende Toenung zeigt es nur seine Werte.
    /// </summary>
    internal bool TakeStoredColour(SavedColour colour)
    {
        if (_tintPick is not null && ReferenceEquals(Layers.Selection, _tintPick))
        {
            Layers.TakeTint(colour.R, colour.G, colour.B);
            LeaveColourPick();
            return true;
        }

        Properties.Read(colour.X, colour.Y, colour.ShownR, colour.ShownG, colour.ShownB, colour.R, colour.G, colour.B, null);
        return false;
    }

    /// <summary>Merkt die Farbe an einer Stelle im Farbspeicher - wie angezeigt und als Licht der Quelle.</summary>
    private void RememberColour(int x, int y)
    {
        if (ShownAt(x, y) is not var (r, g, b) || LinearAt(x, y) is not var (lr, lg, lb)) return;

        var colour = new SavedColour
        {
            R = lr, G = lg, B = lb,
            ShownR = r, ShownG = g, ShownB = b,
            From = _path is null ? null : Path.GetFileName(_path),
            X = x, Y = y,
        };

        if (!ColourStore.Add(_settings.AtelierColours, colour)) return;

        _persist(_settings);
        Properties.ShowStore(_settings.AtelierColours);
    }
}
