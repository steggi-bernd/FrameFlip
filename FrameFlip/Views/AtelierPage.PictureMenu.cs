using System.Windows;
using System.Windows.Input;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Rechtsklick auf das Bild: die Farbe an der Stelle aufnehmen, das Objekt dort als Maske
/// nehmen, mit dem Original vergleichen, zwischen 100 % und Einpassen wechseln.
///
/// Nicht beim Pinsel - dort nimmt die rechte Taste weg - und nicht, solange der Graph
/// ueber dem Bild liegt: Der hat seinen eigenen Rechtsklick.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Das zuletzt geoeffnete Menue des Bildes - fuer die Probe.</summary>
    internal FlipMenu? PictureMenu { get; private set; }

    private void SetUpPictureMenu() => ImageScroll.PreviewMouseRightButtonUp += OnPictureRightUp;

    private void OnPictureRightUp(object sender, MouseButtonEventArgs e)
    {
        if (_frame is null || _tool == AtelierTool.Brush) return;
        if (!PixelAt(e.GetPosition(Display), out int x, out int y)) return;

        e.Handled = true;
        ShowPictureMenu(x, y);
    }

    /// <summary>Das Menue fuer einen Bildpunkt.</summary>
    internal void ShowPictureMenu(int x, int y)
    {
        var menu = new FlipMenu(ImageScroll)
            .Item("⊙", Strings.T("S_PicMenuPick"), () =>
            {
                MouseTools.Select(AtelierTool.Pick);
                OnToolChanged(AtelierTool.Pick);
                ReadAt(x, y);
            }, "I");

        // Je Kryptomatte der Datei eine Zeile - meist Objekt und Material.
        foreach (var set in _cryptomattes)
        {
            menu.Item("⬢", Strings.T("S_PicMenuObjectMask", set.ShortName), () => MaskObjectAt(set, x, y));

            // Im Knotenmodus auch als eigene Ebene: das Objekt ausgeschnitten, obendrauf.
            if (InNodes)
                menu.Item("✂", Strings.T("S_PicMenuObjectLayer", set.ShortName), () => ObjectAsLayerAt(set, x, y));
        }

        menu.Separator()
            .Item("◑", Strings.T("S_PicMenuCompare"), ShowOriginalBriefly)
            .Item("⊡", Strings.T(_zoom == 0 ? "S_PicMenuFull" : "S_PicMenuFit"), () => ZoomAt(x, y));

        PictureMenu = menu;
        menu.Open();
    }

    /// <summary>
    /// Das Objekt an einem Bildpunkt als Maske: eine Maskenebene - eine Einstellungsebene mit
    /// einer Kryptomatte, die genau dieses Objekt waehlt, benannt nach ihm. Dieselbe Art
    /// Ebene, die der Pinsel beim ersten Strich anlegt. False, wenn dort nichts steht.
    /// </summary>
    internal bool MaskObjectAt(CryptomatteSet set, int x, int y)
    {
        if (_path is null) return false;

        var levels = Cryptomatte.Levels(_passes, set.Prefix).ToList();
        if (levels.Count == 0) return false;

        // Die erste Stufe traegt das Objekt mit dem groessten Anteil - gelesen wie beim
        // Waehlen, und wenn sie noch nicht im Vorrat liegt, jetzt, ungeschmaelert.
        var frame = _sources.TryGetValue(levels[0], out var known) ? known : FloatFrame.FromExrPass(_path, levels[0]);
        if (frame is null || x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return false;

        float id = frame.R[y * frame.Width + x];
        if (id == 0f) return false;

        string name = set.NameOf(id) ?? Strings.T("S_MaskLayerName");
        var mask = new LayerMask { Kind = MaskKind.Cryptomatte, Source = set.Prefix, Levels = levels };

        if (InNodes)
        {
            mask.TogglePick(name, id);
            return AddNodeMaskLayer(mask, name) is not null;
        }

        Layers.AddAdjustment();

        if (Layers.Selection is not { } made) return false;

        made.Name = name;
        made.Mask = mask;
        Layers.AddPick(name, id);

        return true;
    }

    /// <summary>
    /// Das Original, bis zum naechsten Klick oder Tastendruck. Kein Schalter, der stehen
    /// bleibt: Der Vergleich ist ein Blick und kein Zustand - siehe OnCompareDown.
    /// </summary>
    private void ShowOriginalBriefly()
    {
        if (_frame is null || _showingOriginal) return;

        _showingOriginal = true;
        Render();

        void Back()
        {
            PreviewMouseDown -= OnMouse;
            PreviewKeyDown -= OnKey;

            if (!_showingOriginal) return;

            _showingOriginal = false;
            Render();
        }

        void OnMouse(object sender, MouseButtonEventArgs e) => Back();
        void OnKey(object sender, KeyEventArgs e) => Back();

        PreviewMouseDown += OnMouse;
        PreviewKeyDown += OnKey;
    }

    /// <summary>Zwischen Einpassen und 100 % - bei 100 % mit dem geklickten Punkt in der Mitte.</summary>
    private void ZoomAt(int x, int y)
    {
        _zoom = _zoom == 0 ? 1.0 : 0;
        ApplyZoom();

        if (_zoom == 0) return;

        ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(Math.Max(0, x * _zoom - ImageScroll.ViewportWidth / 2));
        ImageScroll.ScrollToVerticalOffset(Math.Max(0, y * _zoom - ImageScroll.ViewportHeight / 2));
    }

    /// <summary>Ob gerade das Original gezeigt wird - fuer die Probe.</summary>
    internal bool ShowingOriginal => _showingOriginal;

    /// <summary>Der Massstab: 0 eingepasst, sonst Bildpunkte je Punkt - fuer die Probe.</summary>
    internal double ZoomLevel => _zoom;
}
