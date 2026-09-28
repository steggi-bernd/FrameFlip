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

    /// <summary>Die Pipette am Weissabgleich (C4c): neutral, oder angleichen an eine gemerkte Farbe.</summary>
    private (WhiteBalanceTool Tool, bool Match)? _wbPick;

    /// <summary>Die gemerkte Farbe, an die angeglichen wird - die neueste, bis ein Feld gewaehlt wird.</summary>
    private SavedColour? _wbReference;

    /// <summary>Das Werkzeug der Maus vor der Pipette - danach geht es dorthin zurueck.</summary>
    private AtelierTool _beforeColourPick = AtelierTool.Move;

    private void SetUpColourPick()
    {
        Tools.ZonePickWanted += (tool, zone) =>
        {
            if (zone is { } chosen) StartColourPick(ColourPickKind.Zone, () => _zonePick = (tool, chosen));
            else EndColourPick();
        };

        Layers.TintPickWanted += layer =>
        {
            if (layer is not null) StartColourPick(ColourPickKind.Tint, () => _tintPick = layer);
            else EndColourPick();
        };

        Tools.WhiteBalancePickWanted += (tool, match) =>
        {
            if (match is { } chosen)
                StartColourPick(ColourPickKind.WhiteBalance, () =>
                {
                    _wbPick = (tool, chosen);
                    _wbReference = chosen ? _settings.AtelierColours.FirstOrDefault() : null;
                });
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
    internal bool ColourPicking => _zonePick is not null || _tintPick is not null || _wbPick is not null;

    /// <summary>Welche Art Pipette wartet - innerhalb einer Art schaltet ihr Feld selbst um.</summary>
    private enum ColourPickKind { Zone, Tint, WhiteBalance }

    private void StartColourPick(ColourPickKind kind, Action arm)
    {
        if (!ColourPicking && _levelsPick is null) _beforeColourPick = _tool == AtelierTool.Pick ? AtelierTool.Move : _tool;

        // Immer nur eine wartende Pipette: die andere geht aus - mit ihrem Knopf. Der eben
        // gedrueckte bleibt an; innerhalb eines Feldes schaltet das Feld selbst um.
        if (kind != ColourPickKind.Zone && _zonePick is not null) Tools.EndZonePick();
        if (kind != ColourPickKind.WhiteBalance && _wbPick is not null) Tools.EndWhiteBalancePick();
        if (kind != ColourPickKind.Tint && _tintPick is not null) Layers.EndTintPick();

        _zonePick = null;
        _tintPick = null;
        _wbPick = null;
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
        _wbPick = null;

        Tools.EndZonePick();
        Tools.EndWhiteBalancePick();
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

        if (_wbPick is { } wb)
        {
            if (!wb.Match) return Strings.T("S_PickForWbNeutral");

            return _wbReference is { } reference
                ? Strings.T("S_PickForWbMatch", reference.From is { } from ? $"{reference.Hex} ({from})" : reference.Hex)
                : Strings.T("S_PickForWbNoReference");
        }

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

        if (_wbPick is { } wb)
        {
            // Angleichen ohne gemerkte Farbe: nichts, woran - die Pipette endet und liest nur ab.
            if (wb.Match && _wbReference is null)
            {
                LeaveColourPick();
                return false;
            }

            if (_frame is not { } frame || WhiteBalanceInputAt(wb.Tool, x, y) is not var (r, g, b)) return false;

            var view = ViewFor(frame);

            (float, float, float) Shown((float R, float G, float B) c)
            {
                var (sr, sg, sb) = c;
                view.Apply(ref sr, ref sg, ref sb);
                return (sr, sg, sb);
            }

            (float, float, float)? target = wb.Match && _wbReference is { } reference
                ? (reference.ShownR / 255f, reference.ShownG / 255f, reference.ShownB / 255f)
                : null;

            var (kelvin, tint) = ColourSolve.WhiteBalance(r, g, b, Shown, target);

            Tools.SetWhiteBalance(kelvin, tint);
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
        // Angleichen wartet: Das Feld wird die Vorlage - der naechste Klick ins Bild gleicht an.
        if (_wbPick is { Match: true })
        {
            _wbReference = colour;
            return true;
        }

        if (_tintPick is not null && ReferenceEquals(Layers.Selection, _tintPick))
        {
            Layers.TakeTint(colour.R, colour.G, colour.B);
            LeaveColourPick();
            return true;
        }

        Properties.Read(colour.X, colour.Y, colour.ShownR, colour.ShownG, colour.ShownB, colour.R, colour.G, colour.B, null);
        return false;
    }

    /// <summary>
    /// Das Licht, wie es beim Weissabgleich ankommt. Am Weissabgleich des ganzen Bildes im Stapel:
    /// das zusammengesetzte Bild mit Belichtung, Saettigung und den linearen Werkzeugen davor -
    /// derselbe Weg wie die Anzeige. An einer Ebene oder im Knotenmodus: das Bild, wie die Datei
    /// es hergibt - dort liegt vor dem Werkzeug meist nichts anderes.
    /// </summary>
    private (float R, float G, float B)? WhiteBalanceInputAt(IGradingTool tool, int x, int y)
    {
        bool whole = !InNodes && ToolsLayer is null && Tools.Stack.Tools.Contains(tool);
        var frame = whole ? _frame : _base ?? _frame;

        if (frame is null || x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return null;

        int i = y * frame.Width + x;
        float r = frame.R[i], g = frame.G[i], b = frame.B[i];

        if (!whole) return (r, g, b);

        FloatFrameProcessor.Light((float)Math.Pow(2.0, _finalAdjustments.Exposure), (float)_finalAdjustments.Saturation,
                                  ref r, ref g, ref b);

        foreach (var before in _finalGrading.SceneLinear)
        {
            if (ReferenceEquals(before, tool)) break;
            before.Apply(ref r, ref g, ref b);
        }

        return (r, g, b);
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
