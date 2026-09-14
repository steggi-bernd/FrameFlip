using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using Brush = System.Windows.Media.Brush;
using Path = System.IO.Path;

namespace FrameFlip.Views;

/// <summary>
/// Ein einzelner Frame, gross und schwebend - wie die Vorschau im Explorer.
///
/// Zweck ist das Nachsehen, nicht das Abspielen: Ist das Bild fertig? Stimmt der
/// Ausschnitt? Ist da Rauschen? Genau dafuer reicht ein Fenster mit einem Bild und
/// zwei Pfeilen, und mit den Pfeiltasten blaettert man durch, ohne die Maus wieder
/// anzufassen.
///
/// Der einzige Weg weiter ist "Zusammenfuegen": Aus den Einzelbildern des Ordners
/// wird die richtige Vorschau mit Wiedergabe, Zeitleiste und allem, was FrameFlip
/// sonst noch kann. Alles andere waere hier doppelt gebaut.
/// </summary>
public partial class FramePreviewWindow : Window
{
    private readonly List<string> _frames;
    private readonly Action<string> _combine;

    private int _index;

    public FramePreviewWindow(string path, IReadOnlyList<string> frames, Action<string> combine)
    {
        _combine = combine;

        InitializeComponent();

        _frames = frames.Count > 0 ? frames.ToList() : new List<string> { path };

        int start = _frames.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        _index = start < 0 ? 0 : start;

        // Ein einzelnes Bild ist nichts zum Blaettern und nichts zum Zusammenfuegen.
        bool many = _frames.Count > 1;

        Previous.IsEnabled = many;
        Next.IsEnabled = many;
        Combine.IsEnabled = many;
        PositionPill.Visibility = many ? Visibility.Visible : Visibility.Collapsed;

        Hint.Text = Localization.Strings.T(many ? "S_PreviewHintMany" : "S_PreviewHintOne");

        KeyDown += OnKey;

        // Auch dieses Fenster kann auf einem Bildschirm landen, den es nicht mehr
        // gibt - siehe WindowPlacer.
        SourceInitialized += (_, _) =>
        {
            WindowPlacer.EnsureVisible(this);

            // Dieselbe Behandlung wie das Hauptfenster: eigene Leiste, aber echtes
            // Fensterverhalten beim Ziehen und Andocken.
            ShellChrome.Attach(this);
        };

        Load();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnPrevious(object sender, RoutedEventArgs e) => Step(-1);

    private void OnNext(object sender, RoutedEventArgs e) => Step(1);

    private void OnCombine(object sender, RoutedEventArgs e)
    {
        if (_frames.Count > 0) _combine(_frames[_index]);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left or Key.Up: Step(-1); break;
            case Key.Right or Key.Down: Step(1); break;

            // Erst der Zoom zurueck, dann schliessen. Wer hineingezoomt hat und Esc
            // drueckt, will fast immer das eine und nicht das andere.
            case Key.Escape when !Lupe.Matrix.IsIdentity: Anpassen(); break;
            case Key.Escape: Close(); break;

            case Key.D0 or Key.NumPad0: Anpassen(); break;
            case Key.Add or Key.OemPlus: Zoomen(1.25, Mitte()); break;
            case Key.Subtract or Key.OemMinus: Zoomen(1 / 1.25, Mitte()); break;

            case Key.Enter or Key.Space when _frames.Count > 1: _combine(_frames[_index]); break;
        }
    }

    /* ------------------------------------------------------------------- Lupe

       Zoom auf den Zeiger und Schieben - dasselbe, was die Zuschauerseite im
       Browser kann. Ohne das war die Vorschau im Projektbereich eine Ansicht, in
       der man nichts nachsehen konnte: Ein Rendersfehler ist oft zwanzig Pixel
       gross, und die sieht man in einer eingepassten Ansicht nicht.

       Gerechnet wird ueber eine Matrix und nicht ueber Skalierung plus Rand. Der
       Unterschied ist der ganze Punkt: Beim Zoomen soll die Stelle UNTER DEM ZEIGER
       dort bleiben, wo der Zeiger ist. Mit einer Matrix ist das eine Zeile -
       verschieben, skalieren, zurueckverschieben. Mit getrennten Werten wird es eine
       Rechnung, die man beim zweiten Zoomschritt falsch hat. */

    private const double Kleinste = 1, Groesste = 24;

    private System.Windows.Point _griff;
    private bool _schiebt;

    private System.Windows.Point Mitte() => new(Stage.ActualWidth / 2, Stage.ActualHeight / 2);

    /// <summary>Zurueck auf eingepasst. Auch nach jedem Bildwechsel.</summary>
    private void Anpassen()
    {
        Lupe.Matrix = System.Windows.Media.Matrix.Identity;
        Stage.Cursor = null;
    }

    private void Zoomen(double faktor, System.Windows.Point um)
    {
        var m = Lupe.Matrix;

        double jetzt = m.M11;
        double ziel = Math.Clamp(jetzt * faktor, Kleinste, Groesste);

        if (Math.Abs(ziel - jetzt) < 0.0001) return;

        // Um den Punkt skalieren: hin, skalieren, zurueck.
        m.ScaleAt(ziel / jetzt, ziel / jetzt, um.X, um.Y);

        Lupe.Matrix = m;

        Stage.Cursor = ziel > 1 ? Cursors.SizeAll : null;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        Zoomen(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(Stage));
        e.Handled = true;
    }

    private void OnGrab(object sender, MouseButtonEventArgs e)
    {
        // Ein Doppelklick passt wieder ein - der kuerzeste Weg zurueck.
        if (e.ClickCount == 2) { Anpassen(); return; }

        // Bei eingepasster Ansicht gibt es nichts zu schieben.
        if (Lupe.Matrix.M11 <= 1) return;

        _griff = e.GetPosition(Stage);
        _schiebt = true;
        Stage.CaptureMouse();
    }

    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (!_schiebt) return;

        var jetzt = e.GetPosition(Stage);
        var m = Lupe.Matrix;

        m.Translate(jetzt.X - _griff.X, jetzt.Y - _griff.Y);

        Lupe.Matrix = m;
        _griff = jetzt;
    }

    private void OnRelease(object sender, MouseEventArgs e)
    {
        if (!_schiebt) return;

        _schiebt = false;
        Stage.ReleaseMouseCapture();
    }

    private void Step(int direction)
    {
        if (_frames.Count < 2) return;

        _index = (_index + direction + _frames.Count) % _frames.Count;
        Load();
    }

    private void Load()
    {
        string path = _frames[_index];

        // Beim Bildwechsel wieder einpassen. Den Zoom mitzunehmen klingt bequem und
        // ist es nicht: Zwei Frames derselben Sequenz sind gleich gross, aber der
        // Ausschnitt, den man am einen pruefen wollte, ist am naechsten selten der
        // gesuchte - und man landet blind in einer Ecke.
        Anpassen();

        Caption.Text = Path.GetFileName(path);
        Trouble.Visibility = Visibility.Collapsed;

        var image = Decode(path);

        Picture.Source = image;

        if (image is null)
        {
            Trouble.Text = Localization.Strings.T("S_CannotShow", Path.GetExtension(path).ToUpperInvariant());

            Trouble.Visibility = Visibility.Visible;
        }

        var parts = new List<string>();

        if (image is not null) parts.Add($"{image.PixelWidth}×{image.PixelHeight}");

        try
        {
            var info = new System.IO.FileInfo(path);
            if (info.Exists) parts.Add(Size(info.Length));
        }
        catch (Exception)
        {
        }

        Detail.Text = string.Join("   ·   ", parts);

        // Die Stelle in der Reihe steht rechts, nicht mitten in den Bildangaben:
        // Sie aendert sich beim Blaettern, die anderen bleiben meist gleich.
        Position.Text = Localization.Strings.T("S_OfCount", _index + 1, _frames.Count);
    }

    /// <summary>
    /// Das Bild in voller Groesse laden - hier soll man ja etwas erkennen.
    ///
    /// OnLoad, damit die Datei nicht offen bleibt: Waehrend eines Renders schreibt
    /// Blender im selben Ordner weiter, und ein gesperrter Frame waere das Letzte,
    /// was ein Vorschaufenster anrichten darf.
    /// </summary>
    private static BitmapSource? Decode(string path)
    {
        try
        {
            var image = new BitmapImage();

            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} kB",
        _ => $"{bytes} B",
    };
}
