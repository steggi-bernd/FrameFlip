using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;
using PixelFormats = System.Windows.Media.PixelFormats;

namespace FrameFlip.Views;

/// <summary>
/// Das Atelier: ein Bild, alle Werkzeuge, Zeit.
///
/// Der Unterschied zur Vorschau ist nicht der Funktionsumfang, sondern die Haltung.
/// Die Vorschau geht auf Tastendruck auf und bei Escape wieder zu; sie ist zum
/// Beurteilen einer Sequenz da. Hier sitzt man bei einem Bild und feilt daran,
/// und deshalb stehen die Werkzeuge hier und nicht dort.
///
/// Gerechnet wird durchgehend auf Gleitkomma - auch bei einem PNG, das dafuer
/// zurueckgerechnet wird. Damit wirken alle Werkzeuge auf jedem Material; was ein
/// PNG nicht mitbringt, ist die Zeichnung oberhalb von Weiss, und das steht in der
/// Kopfzeile.
/// </summary>
public sealed partial class AtelierPage : UserControl
{
    private readonly FrameDecoderRegistry _decoders;
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _persist;

    /// <summary>Das zusammengesetzte Bild - das, worauf alle Werkzeuge wirken.</summary>
    private FloatFrame? _frame;

    /// <summary>
    /// Das Bild, wie die Datei es hergibt, ohne Ebenen.
    ///
    /// Es wird zweimal gebraucht: als unterste Quelle des Stapels, und fuer den
    /// Vergleich - "Original" heisst auch ohne die Schichtung, sonst beantwortete
    /// der Knopf eine Frage, die niemand gestellt hat.
    /// </summary>
    private FloatFrame? _base;

    /// <summary>Die gelesenen Passe, nach Quellnamen. Leerer Name ist das Bild selbst.</summary>
    private readonly Dictionary<string, FloatFrame> _sources = new(StringComparer.Ordinal);

    /// <summary>Was die Datei anbietet - die Auswahl im Plusknopf.</summary>
    private IReadOnlyList<ExrPass> _passes = Array.Empty<ExrPass>();

    private WriteableBitmap? _surface;
    private string? _path;

    private readonly DispatcherTimer _settle;
    private bool _coarse;

    /// <summary>
    /// Wie grob waehrend eines Reglerzugs gerechnet wird. Vier heisst ein
    /// Sechzehntel der Arbeit - gemessen faellt 1080p damit von 40 auf 4,5 ms, und
    /// 4K von rund 400 auf 45.
    /// </summary>
    private const int CoarseStep = 4;

    public AtelierPage(FrameDecoderRegistry decoders, AppSettings settings, Action<AppSettings> persist)
    {
        _decoders = decoders;
        _settings = settings;
        _persist = persist;

        InitializeComponent();

        Tools.Load(settings.Adjustments, settings.Grading);
        Tools.Changed += OnToolsChanged;
        Tools.ToolsEnabled = false;

        Layers.Changed += OnLayersChanged;

        _settle = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };

        SetUpBatch();

        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            if (!_coarse) return;

            _coarse = false;
            Render();
            Measure();
        };
    }

    /// <summary>Oeffnet ein Bild - der Weg, den auch die Projektseite nehmen kann.</summary>
    public void Open(string path)
    {
        _path = path;
        FileText.Text = Path.GetFileName(path);
        BusyBadge.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;

        // Lesen und Auspacken dauert bei 4K spuerbar lange; auf dem Oberflaechenfaden
        // staende dabei das ganze Fenster.
        Task.Run(() => Load(path)).ContinueWith(task =>
        {
            var loaded = task.IsCompletedSuccessfully ? task.Result : (null, Array.Empty<ExrPass>());

            Dispatcher.Invoke(() => Show(path, loaded.Frame, loaded.Passes));
        });
    }

    private void Show(string path, FloatFrame? loaded, IReadOnlyList<ExrPass> passes)
    {
        BusyBadge.Visibility = Visibility.Collapsed;

        _sources.Clear();
        _passes = passes;

        if (loaded is null)
        {
            FileText.Text = Path.GetFileName(path) + " — " + Strings.T("S_CannotRead");
            EmptyHint.Visibility = Visibility.Visible;
            Tools.ToolsEnabled = false;
            CompareButton.IsEnabled = false;
            _frame = null;
            _base = null;
            ShowLayers(false);
            UpdateBatchBar();
            return;
        }

        _base = loaded;
        _sources[""] = loaded;
        _surface = null;
        Tools.ToolsEnabled = true;

        // Der gespeicherte Stapel gilt nur, soweit diese Datei die Passe auch
        // fuehrt. Zwanzig ausgegraute Zeilen nach dem Wechsel auf ein PNG waeren
        // kein Hinweis, sondern ein Raetsel.
        Layers.Load(passes, Prune(_settings.Layers, passes));

        // Der Streifen gilt fuer jedes Bild, nicht nur fuer eine Multilayer-EXR.
        // Passe braucht das Format, Ebenen nicht: Dasselbe Bild ein zweites Mal und
        // auf Multiplizieren gestellt rechnet auf einem PNG genauso. Ob er
        // aufgeklappt beginnt, entscheidet der Streifen selbst.
        ShowLayers(true);
        _settings.Layers = Layers.Stack;

        _frame = loaded;

        UpdateSourceText();
        FindSequence(path);
        CompareButton.IsEnabled = true;
        ApplyZoom();
        Render();
        Measure();

        // Braucht der Stapel Passe, die noch nicht gelesen sind, kommen sie
        // nachtraeglich - das Bild steht schon, waehrend sie eintreffen.
        if (!Layers.Stack.IsPassThrough) OnLayersChanged(interim: false);
    }

    private (FloatFrame? Frame, IReadOnlyList<ExrPass> Passes) Load(string path)
    {
        // EXR bringt die Werte selbst mit. Alles andere geht ueber den vorhandenen
        // Decoder und wird aus den acht Bit zurueckgerechnet.
        if (Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase))
            return (FloatFrame.FromExr(path), ExrPasses.Of(path));

        var decoder = _decoders.For(Path.GetExtension(path));
        if (decoder is null) return (null, Array.Empty<ExrPass>());

        var frame = decoder.TryDecode(path, 16384, 16384, n => new byte[n], out var decoded)
            ? FloatFrame.FromBgra32(decoded.Pixels, decoded.Width, decoded.Height, decoded.Stride)
            : null;

        return (frame, Array.Empty<ExrPass>());
    }

    /// <summary>
    /// Welche Sichtumwandlung dieses Bild braucht.
    ///
    /// Szenenlicht aus einer EXR bekommt die des Programms - AgX, wenn Blender zu
    /// finden war. Ein zurueckgerechnetes PNG dagegen ist bereits durch eine
    /// Umwandlung gegangen; hier darf nur die einfache stehen, die die Dekodierung
    /// umkehrt. Sonst liefe die Bildwerdung zweimal.
    /// </summary>
    private IViewTransform ViewFor(FloatFrame frame)
        => frame.IsSceneReferred && _decoders.For(".exr") is ExrFrameDecoder exr
            ? exr.View
            : new StandardViewTransform();

    private void OnToolsChanged(bool interim)
    {
        _settings.Adjustments = Tools.Adjustments;
        _settings.Grading = Tools.Stack;

        if (interim)
        {
            _coarse = true;
            _settle.Stop();
            _settle.Start();
        }
        else
        {
            _settle.Stop();
            _coarse = false;
        }

        Render();
        if (!interim) Measure();
    }

    private void Render()
    {
        var frame = Shown();
        if (frame is null) return;

        if (_surface is null || _surface.PixelWidth != frame.Width || _surface.PixelHeight != frame.Height)
        {
            _surface = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            Display.Source = _surface;
        }

        _surface.Lock();

        try
        {
            var (adjustments, grading) = Current();

            FloatFrameProcessor.Apply(frame, adjustments, ViewFor(frame), grading,
                                      _surface.BackBuffer, _surface.BackBufferStride,
                                      _coarse ? CoarseStep : 1);

            _surface.AddDirtyRect(new Int32Rect(0, 0, frame.Width, frame.Height));
        }
        finally
        {
            _surface.Unlock();
        }
    }

    private void Measure()
    {
        var frame = _frame;
        if (frame is null) return;

        var histogram = new Histogram();

        // Jedes vierte Pixel in beiden Richtungen: ein Sechzehntel der Arbeit, und
        // die Verteilung stimmt trotzdem.
        FloatFrameProcessor.Measure(frame, Tools.Adjustments, ViewFor(frame), Tools.Prepared,
                                    histogram, step: 4);

        Tools.ShowHistogram(histogram);
    }

    private void UpdateSourceText()
    {
        var frame = _frame;
        if (frame is null) return;

        SourceText.Text = $"{frame.Width} × {frame.Height}";

        if (frame.IsSceneReferred)
        {
            string layer = frame.Layer is null ? "EXR" : "EXR · " + frame.Layer;
            SourceText.Text += "   " + layer;
        }
        else
        {
            // Bei zurueckgerechnetem Material ist die Reserve oberhalb von Weiss
            // nicht vorhanden - das gehoert gesagt, bevor jemand den
            // Belichtungsregler dafuer verantwortlich macht.
            SourceText.Text += "   " + Strings.T("S_EightBitSource");
        }

        ViewText.Text = ViewFor(frame) is AgxViewTransform ? "AgX" : "Standard";
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("S_OpenImage"),
            Filter = "Bilder|*.exr;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.webp|" +
                     "OpenEXR|*.exr|Alle Dateien|*.*",
            CheckFileExists = true,
        };

        if (_path is not null)
        {
            try { dialog.InitialDirectory = Path.GetDirectoryName(_path); }
            catch (Exception) { /* ein ungueltiger Pfad ist kein Grund, den Dialog nicht zu oeffnen */ }
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        Open(dialog.FileName);
        _persist(_settings);
    }
}
