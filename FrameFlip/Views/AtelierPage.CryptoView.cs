using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Rueckmeldung beim Waehlen von Kryptomatte-Objekten (docs/Atelier-Arbeitsablauf.md, C3). Vorher
/// nahm ein Klick ein Objekt auf, ohne dass vorher oder nachher etwas davon zu sehen war.
///
/// Mit dem Werkzeug "Auswaehlen": der Name des Objekts am Zeiger, das Objekt darunter hell
/// ueberlagert, die gewaehlten umrandet, und in den Werkzeugeinstellungen die Auswahl als Chips.
/// Gewaehlt wird an der Kryptomatte des Bearbeitungsziels - im Stapel die Maske der gewaehlten
/// Ebene, im Graphen der gewaehlte Maskenknoten. Ohne ein solches Ziel werden die Objekte der
/// Objekt-Kryptomatte nur angezeigt.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Jeder wievielte Bildpunkt fuer die Hervorhebung unter dem Zeiger - sie folgt der Maus.</summary>
    private const int HoverStep = 4;

    /// <summary>Jeder wievielte fuer den Umriss der Auswahl - er entsteht nur, wenn sich die Auswahl aendert.</summary>
    private const int OutlineStep = 2;

    /// <summary>Gebaute Hervorhebungen: Datei, Kryptomatte, Kennung, Bild.</summary>
    private readonly Dictionary<(string Path, string Set, float Id, int Number), WriteableBitmap> _hoverCovers = new();

    private float? _hoverId;
    private string? _hoverName;

    /// <summary>
    /// Stufen, deren Lesen gerade laeuft - nicht zweimal zugleich. Danach wieder frei: Der Vorrat
    /// wird neu aufgebaut, wenn sich der Stapel aendert, und dann muessen sie noch einmal kommen.
    /// </summary>
    private readonly HashSet<string> _cryptoFetching = new(StringComparer.Ordinal);

    private void SetUpCryptoView()
    {
        Properties.PickRemoveWanted += RemovePick;
        _recipe.TargetChanged += ShowCryptoView;
    }

    /// <summary>
    /// Die Kryptomatte, an der gerade gewaehlt wird: die Maske des Ziels und ihre Kryptomatte in der
    /// Datei. Null, wenn das Ziel keine Kryptomatte-Maske hat.
    /// </summary>
    private (LayerMask Mask, CryptomatteSet Set)? CryptoTarget()
    {
        // Eine vorlaeufige Auswahl (C3b): wie eine Maske, die es noch nicht gibt.
        if (_recipe.Target is Atelier.EditingTarget.CryptoSelection selection)
        {
            var chosen = _cryptomattes.FirstOrDefault(s => s.Prefix == selection.Set);

            return chosen is null ? null : (new LayerMask
            {
                Kind = MaskKind.Cryptomatte,
                Source = selection.Set,
                Levels = Cryptomatte.Levels(_passes, selection.Set).ToList(),
                Picks = selection.Picks.ToList(),
            }, chosen);
        }

        var mask = InNodes
            ? (SelectedNode as MaskNode)?.Mask
            : Layers.Selection?.Mask;

        if (mask is not { Kind: MaskKind.Cryptomatte }) return null;

        var set = _cryptomattes.FirstOrDefault(s => s.Prefix == mask.Source)
                  ?? _cryptomattes.FirstOrDefault(s => mask.Levels.Count > 0 && Cryptomatte.Levels(_passes, s.Prefix).Contains(mask.Levels[0]));

        return set is null ? null : (mask, set);
    }

    /// <summary>
    /// Die Stufen einer Kryptomatte aus dem Vorrat. Fehlen welche, wird ihr Lesen angestossen und
    /// null geliefert - die Anzeige kommt, sobald sie da sind.
    /// </summary>
    private List<FloatFrame>? LevelsOf(CryptomatteSet set)
    {
        if (_path is not { } path) return null;

        var names = Cryptomatte.Levels(_passes, set.Prefix).ToList();
        if (names.Count == 0) return null;

        var missing = names.Where(name => !_sources.ContainsKey(name)).ToList();

        if (missing.Count == 0) return names.Select(name => _sources[name]).ToList();

        string key = path + "|" + set.Prefix;

        if (_cryptoFetching.Add(key))
        {
            Fetch(path, missing.Select(name => new LayerRead(name, LayerContent.Pass, false)).ToList(), Array.Empty<string>(),
                  _ =>
                  {
                      _cryptoFetching.Remove(key);
                      ShowCryptoView();
                  });
        }

        return null;
    }

    /// <summary>
    /// Zeichnet den Umriss der Auswahl und traegt sie in die Werkzeugeinstellungen ein - nach
    /// einem Wechsel von Werkzeug, Ziel, Auswahl oder Bild.
    /// </summary>
    private void ShowCryptoView()
    {
        bool picking = _tool == AtelierTool.Select && _frame is not null;

        if (!picking)
        {
            CryptoSelection.Source = null;
            HideCryptoHover();
            return;
        }

        var target = CryptoTarget();
        var set = target?.Set ?? ObjectSet();

        Properties.ShowSelection(set?.ShortName, target?.Mask.Picks, _hoverName);

        if (target is not var (mask, targetSet) || mask.Picks.Count == 0 || LevelsOf(targetSet) is not { } levels)
        {
            CryptoSelection.Source = null;
            return;
        }

        var ids = mask.Picks.Select(p => p.Id).ToArray();
        var cover = CryptoCoverage.Build(levels, ids, OutlineStep, out int cols, out int rows);
        var edge = CryptoCoverage.Outline(cover, cols, rows);

        // Die Auswahl leise gefuellt und kraeftig umrandet: Man sieht, WAS gewaehlt ist, und das
        // Bild darunter bleibt lesbar.
        CryptoSelection.Source = Overlay(cols, rows, i => edge[i] > 0 ? 1f : cover[i] / 255f * 0.18f);
    }

    /// <summary>Die Maus ueber dem Bild, mit dem Werkzeug "Auswaehlen": Name und Hervorhebung des Objekts darunter.</summary>
    private void OnCryptoHover(object sender, MouseEventArgs e)
    {
        if (_tool != AtelierTool.Select || _frame is null) return;

        if (!PixelAt(e.GetPosition(Display), out int x, out int y))
        {
            HideCryptoHover();
            return;
        }

        HoverAt(x, y, e.GetPosition(CryptoHoverLayer));
    }

    /// <summary>
    /// Der Zeiger steht ueber einem Bildpunkt: Name und Hervorhebung des Objekts dort. Getrennt
    /// von der Maus, damit die Probe denselben Weg gehen kann. False, wenn dort nichts steht.
    /// </summary>
    internal bool HoverAt(int x, int y, Point at)
    {
        if (_tool != AtelierTool.Select || _frame is null || _path is null) return false;

        var set = CryptoTarget()?.Set ?? ObjectSet();

        if (set is null || LevelsOf(set) is not { } levels || x < 0 || y < 0 ||
            x >= levels[0].Width || y >= levels[0].Height)
        {
            HideCryptoHover();
            return false;
        }

        float id = levels[0].R[y * levels[0].Width + x];

        if (id == 0f)
        {
            HideCryptoHover();
            return false;
        }

        if (_hoverId != id)
        {
            _hoverId = id;
            _hoverName = HoverName(id, x, y);

            var key = (_path, set.Prefix, id, _number);

            if (!_hoverCovers.TryGetValue(key, out var bitmap))
            {
                var cover = CryptoCoverage.Build(levels, new[] { id }, HoverStep, out int cols, out int rows);
                bitmap = Overlay(cols, rows, i => cover[i] / 255f * 0.42f);

                // Wer ueber eine Szene faehrt, trifft ein paar Dutzend Objekte - der Vorrat bleibt klein.
                if (_hoverCovers.Count > 32) _hoverCovers.Clear();
                _hoverCovers[key] = bitmap;
            }

            CryptoHover.Source = bitmap;
            CryptoHoverText.Text = _hoverName;
            Properties.ShowSelection(set.ShortName, CryptoTarget()?.Mask.Picks, _hoverName);
        }

        CryptoHoverName.Visibility = string.IsNullOrEmpty(_hoverName) ? Visibility.Collapsed : Visibility.Visible;

        // Unten rechts neben dem Zeiger, im Bereich des Bildes gehalten.
        CryptoHoverName.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double left = Math.Min(at.X + 16, Math.Max(0, CryptoHoverLayer.ActualWidth - CryptoHoverName.DesiredSize.Width - 4));
        double top = Math.Min(at.Y + 18, Math.Max(0, CryptoHoverLayer.ActualHeight - CryptoHoverName.DesiredSize.Height - 4));

        Canvas.SetLeft(CryptoHoverName, left);
        Canvas.SetTop(CryptoHoverName, top);

        return true;
    }

    /// <summary>Der Name, der gerade am Zeiger steht - fuer die Probe.</summary>
    internal string? HoverText => CryptoHoverName.Visibility == Visibility.Visible ? CryptoHoverText.Text : null;

    private void OnCryptoHoverLeft(object sender, MouseEventArgs e) => HideCryptoHover();

    private void HideCryptoHover()
    {
        _hoverId = null;
        _hoverName = null;
        CryptoHover.Source = null;
        CryptoHoverName.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Der Name unter dem Zeiger: das Objekt - und, wenn die Datei eine weitere Kryptomatte fuehrt
    /// und ihre Stufen schon da sind, was dort steht, etwa das Material.
    /// </summary>
    private string HoverName(float id, int x, int y)
    {
        var names = new List<string>();

        foreach (var set in _cryptomattes)
        {
            if (set.NameOf(id) is { } own)
            {
                names.Insert(0, own);
                continue;
            }

            var first = Cryptomatte.Levels(_passes, set.Prefix).FirstOrDefault();
            if (first is not null && _sources.TryGetValue(first, out var level) && x < level.Width && y < level.Height &&
                set.NameOf(level.R[y * level.Width + x]) is { } other)
            {
                names.Add(other);
            }
        }

        return string.Join(" · ", names.Distinct());
    }

    /// <summary>Ein kleines Bild in der Akzentfarbe, die Deckung je Feld als Deckkraft.</summary>
    private WriteableBitmap Overlay(int cols, int rows, Func<int, float> alpha)
    {
        var colour = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0xA7, 0x8B, 0xFA);
        var pixels = new byte[cols * rows * 4];

        for (int i = 0; i < cols * rows; i++)
        {
            float a = Math.Clamp(alpha(i), 0f, 1f);

            // Vormultipliziert, wie Pbgra32 es erwartet.
            pixels[i * 4] = (byte)(colour.B * a);
            pixels[i * 4 + 1] = (byte)(colour.G * a);
            pixels[i * 4 + 2] = (byte)(colour.R * a);
            pixels[i * 4 + 3] = (byte)(255 * a);
        }

        var bitmap = new WriteableBitmap(cols, rows, 96, 96, PixelFormats.Pbgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, cols, rows), pixels, cols * 4, 0);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>Das Kreuz an einem Chip: das Objekt aus der Auswahl nehmen.</summary>
    private void RemovePick(CryptoPick pick)
    {
        if (_recipe.Target is Atelier.EditingTarget.CryptoSelection selection)
        {
            var rest = selection.Picks.Where(p => p.Id != pick.Id).ToList();
            _recipe.Focus(rest.Count == 0 ? Atelier.EditingTarget.Picture : selection with { Picks = rest });
            ShowCryptoView();
            return;
        }

        if (CryptoTarget() is not var (mask, _)) return;

        if (InNodes)
        {
            RememberNodes();
            mask.TogglePick(pick.Name, pick.Id);
            AfterNodeEdit();
        }
        else
        {
            Layers.AddPick(pick.Name, pick.Id);
        }

        ShowCryptoView();
    }

    /// <summary>Ein anderes Bild: Was fuer das alte gebaut war, gilt nicht mehr.</summary>
    private void ForgetCryptoView()
    {
        _hoverCovers.Clear();
        _cryptoFetching.Clear();
        HideCryptoHover();
        CryptoSelection.Source = null;
    }
}
