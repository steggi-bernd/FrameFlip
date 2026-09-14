using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Localization;

using PixelFormats = System.Windows.Media.PixelFormats;

namespace FrameFlip.Views;

/// <summary>
/// Belichtung, Gamma, Kontrast, Saettigung, Schwarz- und Weisspunkt fuer das eine
/// Bild, das gerade offen ist.
///
/// Dieselben Regler wie im Viewer, aber nicht dessen ganzer Bereich: Vorlagen,
/// Vergleichsbild und Waveform gehoeren zum Beurteilen einer SEQUENZ. Hier geht es
/// um ein einzelnes Bild, das jemand gerade angeklickt hat.
///
/// Gerechnet wird mit <see cref="FrameProcessor"/> - demselben Code, der die
/// Wiedergabe und den Export korrigiert. Eine zweite Tonwertkurve waere die Art
/// Doppelung, die ein Jahr spaeter auseinanderlaeuft, und dann sieht dasselbe Bild
/// hier anders aus als im Viewer.
///
/// Die Korrektur liegt in der Anzeige, nicht in der Datei. Das ist keine Feinheit,
/// sondern der Grund, warum man sie ueberhaupt gefahrlos anfassen kann.
/// </summary>
public partial class FramePreviewWindow
{
    private ImageAdjustments _korrektur = ImageAdjustments.Neutral;

    /// <summary>Die Pixel des offenen Bildes, unveraendert. Quelle jeder Korrektur.</summary>
    private byte[]? _pixel;
    private int _breite, _hoehe, _schritt;

    /// <summary>Worauf gezeichnet wird. Wird nur bei neuer Groesse neu angelegt.</summary>
    private WriteableBitmap? _flaeche;

    private bool _ruhig;

    /// <summary>
    /// Erst nach InitializeComponent horchen.
    ///
    /// Ein Slider meldet seinen Anfangswert schon WAEHREND des Ladens - und dann
    /// existieren die Geschwister noch nicht. Der erste Wächter fragte
    /// ausgerechnet den Regler ab, der zu dem Zeitpunkt bereits da war, und lief
    /// ins Leere, sobald er den naechsten anfasste.
    /// </summary>
    private bool _bereit;

    /// <summary>
    /// Das geladene Bild fuer die Korrektur vorbereiten.
    ///
    /// Ohne Korrektur bleibt die Anzeige beim urspruenglichen Bild: Ein zusaetzlicher
    /// Kopiervorgang je Frame waere Aufwand fuer einen Fall, den niemand bemerkt.
    /// </summary>
    private void KorrekturQuelle(BitmapSource? bild)
    {
        _pixel = null;
        _flaeche = null;

        if (bild is null) { AdjustToggle.IsEnabled = false; return; }

        AdjustToggle.IsEnabled = true;

        try
        {
            var bgra = bild.Format == PixelFormats.Bgra32 ? bild : new FormatConvertedBitmap(bild, PixelFormats.Bgra32, null, 0);

            _breite = bgra.PixelWidth;
            _hoehe = bgra.PixelHeight;
            _schritt = _breite * 4;
            _pixel = new byte[_schritt * _hoehe];

            bgra.CopyPixels(_pixel, _schritt, 0);
        }
        catch (Exception)
        {
            // Ein Format, das sich nicht in Bgra32 bringen laesst, bleibt eben
            // unkorrigierbar. Angezeigt wird es trotzdem.
            _pixel = null;
            AdjustToggle.IsEnabled = false;
        }
    }

    /// <summary>Die Korrektur auf das Bild rechnen - oder das Original zeigen.</summary>
    private void Anwenden()
    {
        if (_pixel is null || _bild is null) return;

        if (_korrektur.IsNeutral)
        {
            Picture.Source = _bild;
            return;
        }

        _flaeche ??= new WriteableBitmap(_breite, _hoehe, 96, 96, PixelFormats.Bgra32, null);

        _flaeche.Lock();

        try
        {
            FrameProcessor.Apply(_pixel, _breite, _hoehe, _schritt,
                                 _flaeche.BackBuffer, _flaeche.BackBufferStride, _korrektur);

            _flaeche.AddDirtyRect(new Int32Rect(0, 0, _breite, _hoehe));
        }
        finally
        {
            _flaeche.Unlock();
        }

        Picture.Source = _flaeche;
    }

    // ------------------------------------------------------------------ Bedienung

    private void OnAdjustToggle(object sender, RoutedEventArgs e)
    {
        bool offen = AdjustToggle.IsChecked == true;

        AdjustPanel.Visibility = offen ? Visibility.Visible : Visibility.Collapsed;

        // Die Spalte wird erst beim Aufklappen breit. Eine feste Breite haette die
        // Buehne auch dann verkleinert, wenn niemand korrigiert.
        PanelColumn.Width = offen ? new GridLength(268) : new GridLength(0);
    }

    private void OnAdjustChanged(object sender, RoutedEventArgs e)
    {
        if (!_bereit || _ruhig) return;

        _korrektur = new ImageAdjustments
        {
            Exposure = ExposureSlider.Value,
            Gamma = GammaSlider.Value,
            Contrast = ContrastSlider.Value,
            Saturation = SaturationSlider.Value,
            BlackPoint = BlackSlider.Value,
            WhitePoint = WhiteSlider.Value,
            Channel = (ChannelView)Math.Max(0, ChannelBox.SelectedIndex),
        }.Clamped();

        Beschriften();
        Anwenden();
    }

    private void OnAdjustReset(object sender, RoutedEventArgs e) => Zuruecksetzen();

    /// <summary>
    /// Ein Doppelklick auf einen Regler setzt ihn auf seinen Ruhewert.
    ///
    /// Der steht im Tag und nicht in einer Tabelle daneben: So sieht man beim Lesen
    /// des Markups, worauf ein Regler zurueckfaellt, statt es an zwei Stellen
    /// gleichhalten zu muessen.
    /// </summary>
    private void OnSliderReset(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Slider regler || regler.Tag is not string ruhe) return;

        if (double.TryParse(ruhe, System.Globalization.CultureInfo.InvariantCulture, out double wert))
            regler.Value = wert;
    }

    private void Zuruecksetzen()
    {
        _ruhig = true;

        ExposureSlider.Value = 0;
        GammaSlider.Value = 1;
        ContrastSlider.Value = 1;
        SaturationSlider.Value = 1;
        BlackSlider.Value = 0;
        WhiteSlider.Value = 1;
        ChannelBox.SelectedIndex = 0;

        _ruhig = false;

        _korrektur = ImageAdjustments.Neutral;

        Beschriften();
        Anwenden();
    }

    private void Beschriften()
    {
        var kultur = System.Globalization.CultureInfo.CurrentCulture;

        ExposureValue.Text = _korrektur.Exposure.ToString("+0.00;-0.00;0.00", kultur);
        GammaValue.Text = _korrektur.Gamma.ToString("0.00", kultur);
        ContrastValue.Text = _korrektur.Contrast.ToString("0.00", kultur);
        SaturationValue.Text = _korrektur.Saturation.ToString("0.00", kultur);
        BlackValue.Text = _korrektur.BlackPoint.ToString("0.00", kultur);
        WhiteValue.Text = _korrektur.WhitePoint.ToString("0.00", kultur);

        AdjustNote.Visibility = _korrektur.IsNeutral ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Die Kanaele in der Reihenfolge des Aufzaehlungstyps - sonst waehlt man Alpha und bekommt Rot.</summary>
    private void KanaeleFuellen()
    {
        ChannelBox.Items.Clear();

        foreach (string key in new[] { "S_ChannelAll", "S_ChannelRed", "S_ChannelGreen",
                                       "S_ChannelBlue", "S_ChannelAlpha", "S_ChannelLuminance" })
        {
            ChannelBox.Items.Add(Strings.T(key));
        }

        ChannelBox.SelectedIndex = 0;
    }
}
