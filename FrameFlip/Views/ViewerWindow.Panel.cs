using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FrameFlip.Caching;
using FrameFlip.Configuration;
using FrameFlip.Imaging;
using FrameFlip.Interop;
using Brush = System.Windows.Media.Brush;

namespace FrameFlip.Views;

/// <summary>
/// Das Seitenpanel: Anzeigekorrektur, Verteilung, A/B-Vergleich und Vorlagen.
///
/// Bewusst in einer eigenen Datei - das Fenster ist ohnehin schon gross genug, und
/// diese Funktionen haengen untereinander eng, mit der Wiedergabe dagegen kaum
/// zusammen.
/// </summary>
public partial class ViewerWindow
{
    /// <summary>Nur jedes vierte Pixel messen. Ein Sechzehntel der Arbeit, dieselbe Verteilung.</summary>
    private const int HistogramStep = 4;

    private bool _panelOpen;
    private bool _suppressAdjustment;

    private ImageAdjustments _adjustments = ImageAdjustments.Neutral;
    private readonly Histogram _histogram = new();
    private readonly Stopwatch _histogramDue = Stopwatch.StartNew();

    /// <summary>Gemerkter Frame fuer den A/B-Vergleich - eine echte Kopie.</summary>
    private byte[]? _referencePixels;
    private int _referenceWidth, _referenceHeight, _referenceStride, _referenceNumber;
    private bool _captureRequested;
    private bool _showingReference;

    // ---------------------------------------------------------------- Aufklappen

    private void InitialisePanel()
    {
        ChannelBox.ItemsSource = new[]
        {
            new ChannelOption("Alle Kanäle", ChannelView.All),
            new ChannelOption("Rot", ChannelView.Red),
            new ChannelOption("Grün", ChannelView.Green),
            new ChannelOption("Blau", ChannelView.Blue),
            new ChannelOption("Alpha", ChannelView.Alpha),
            new ChannelOption("Helligkeit", ChannelView.Luminance),
        };
        ChannelBox.SelectedIndex = 0;

        _adjustments = _settings.Adjustments?.Clamped() ?? ImageAdjustments.Neutral;
        PushAdjustmentsToControls();
        RefreshPresetList();

        if (_settings.PanelOpen) SetPanelOpen(true, resizeWindow: false);
    }

    private sealed record ChannelOption(string Name, ChannelView Value)
    {
        public override string ToString() => Name;
    }

    private void TogglePanel() => SetPanelOpen(!_panelOpen, resizeWindow: true);

    /// <summary>
    /// Klappt das Panel auf oder zu. Das Fenster waechst dabei nach rechts, solange
    /// der Bildschirm es hergibt - sonst schrumpft der Bildbereich. Andernfalls
    /// waere das Panel auf einem kleinen Fenster nur ein Dieb von Bildflaeche.
    /// </summary>
    private void SetPanelOpen(bool open, bool resizeWindow)
    {
        if (open == _panelOpen && SidePanel.Visibility == (open ? Visibility.Visible : Visibility.Collapsed))
            return;

        _panelOpen = open;
        SidePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        _settings.PanelOpen = open;
        _persist(_settings);

        if (resizeWindow) ResizeForPanel(open, SidePanel.Width + 1);

        if (open)
        {
            _histogramDue.Restart();
            UpdateHistogramFromCurrentFrame();
        }
        else
        {
            HistogramView.Clear();
        }

        ShowBar();
    }

    /// <param name="panel">
    /// Breite der Spalte samt Rahmen. Beide Spalten - Korrektur und Metriken -
    /// benutzen dieselbe Mechanik; nur so verhalten sie sich fuer den Nutzer gleich.
    /// </param>
    private void ResizeForPanel(bool open, double panel)
    {
        if (!open)
        {
            Width = Math.Max(MinWidth, Width - panel);
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var (work, scale) = NativeMethods.GetWorkArea(handle);

        double available = work.Width / (scale > 0 ? scale : 1.0);
        double wanted = Width + panel;

        // Passt es nicht mehr auf den Bildschirm, bleibt die Fensterbreite stehen und
        // der Bildbereich gibt den Platz ab.
        if (wanted <= available)
        {
            Width = wanted;

            // Nach rechts ueber den Rand hinausgewachsen? Dann zurueckschieben.
            double right = work.X / scale + available;
            if (Left + Width > right) Left = Math.Max(work.X / scale, right - Width);
        }
    }

    // ---------------------------------------------------------------- Korrektur

    private void OnAdjustmentChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressAdjustment || !IsLoaded) return;

        _adjustments = new ImageAdjustments
        {
            Exposure = ExposureSlider.Value,
            Gamma = GammaSlider.Value,
            Contrast = ContrastSlider.Value,
            Saturation = SaturationSlider.Value,
            BlackPoint = BlackSlider.Value,
            WhitePoint = WhiteSlider.Value,
            Channel = CurrentChannel,
        }.Clamped();

        AfterAdjustmentChanged();
    }

    private void OnChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAdjustment || !IsLoaded) return;

        _adjustments = _adjustments with { Channel = CurrentChannel };
        AfterAdjustmentChanged();
    }

    private ChannelView CurrentChannel
        => ChannelBox.SelectedItem is ChannelOption option ? option.Value : ChannelView.All;

    /// <summary>Doppelklick auf einen Regler setzt ihn auf seinen Ausgangswert.</summary>
    private void OnSliderReset(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        if (!double.TryParse(slider.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double neutral))
            return;

        e.Handled = true;
        slider.Value = neutral;
    }

    private void OnResetAdjustClicked(object sender, RoutedEventArgs e)
    {
        _adjustments = ImageAdjustments.Neutral;
        PushAdjustmentsToControls();
        AfterAdjustmentChanged();
    }

    /// <summary>
    /// Nach jeder Aenderung: Werte anzeigen, Bild neu zeichnen, Zustand sichern.
    ///
    /// Das Neuzeichnen laeuft ueber den normalen Praesentationsweg. Im pausierten
    /// Zustand ist das ein einzelner Frame, waehrend der Wiedergabe passiert es
    /// ohnehin beim naechsten Bild.
    /// </summary>
    private void AfterAdjustmentChanged()
    {
        UpdateAdjustmentReadouts();

        _settings.Adjustments = _adjustments;
        _persist(_settings);

        if (!_playing) RedrawCurrentFrame();
        ShowBar();
    }

    private void UpdateAdjustmentReadouts()
    {
        ExposureValue.Text = _adjustments.Exposure == 0 ? "0" : $"{_adjustments.Exposure:+0.00;-0.00} EV";
        GammaValue.Text = $"{_adjustments.Gamma:0.00}";
        ContrastValue.Text = $"{_adjustments.Contrast:0.00}";
        SaturationValue.Text = $"{_adjustments.Saturation:0.00}";
        BlackValue.Text = $"{_adjustments.BlackPoint:0.00}";
        WhiteValue.Text = $"{_adjustments.WhitePoint:0.00}";

        // Am Titel ablesbar, dass die Anzeige nicht dem Original entspricht - sonst
        // beurteilt man irgendwann einen Render nach einer vergessenen Korrektur.
        AdjustBadge.Visibility = _adjustments.IsNeutral ? Visibility.Collapsed : Visibility.Visible;
        AdjustBadge.Text = _adjustments.Describe();
    }

    private void PushAdjustmentsToControls()
    {
        _suppressAdjustment = true;
        try
        {
            ExposureSlider.Value = _adjustments.Exposure;
            GammaSlider.Value = _adjustments.Gamma;
            ContrastSlider.Value = _adjustments.Contrast;
            SaturationSlider.Value = _adjustments.Saturation;
            BlackSlider.Value = _adjustments.BlackPoint;
            WhiteSlider.Value = _adjustments.WhitePoint;

            foreach (var item in ChannelBox.Items)
                if (item is ChannelOption option && option.Value == _adjustments.Channel)
                    ChannelBox.SelectedItem = item;
        }
        finally
        {
            _suppressAdjustment = false;
        }

        UpdateAdjustmentReadouts();
    }

    /// <summary>Zeichnet den stehenden Frame neu, ohne die Position zu verändern.</summary>
    private void RedrawCurrentFrame()
    {
        if (_closing) return;

        int index = _shownIndex >= 0 ? _shownIndex : _index;
        if (_showingReference) { ShowReferenceFrame(); return; }

        _cache?.TryPresent(index, Blit);
    }

    // ---------------------------------------------------------------- Verteilung

    private void OnHistogramModeClicked(object sender, RoutedEventArgs e)
    {
        HistogramView.ShowChannels = !HistogramView.ShowChannels;
        HistogramModeButton.Content = HistogramView.ShowChannels ? "RGB" : "Luma";
        HistogramView.InvalidateVisual();
        ShowBar();
    }

    private void UpdateHistogramFromCurrentFrame()
    {
        if (!_panelOpen) return;

        // Auf dem gemerkten Frame messen, wenn gerade der gezeigt wird.
        if (_showingReference && _referencePixels is not null)
        {
            FrameProcessor.Measure(_referencePixels, _referenceWidth, _referenceHeight,
                                   _referenceStride, _histogram, HistogramStep, _adjustments);
            HistogramView.Update(_histogram);
            UpdateClipText();
            return;
        }

        int index = _shownIndex >= 0 ? _shownIndex : _index;

        _cache?.TryPresent(index, frame =>
        {
            FrameProcessor.Measure(frame.Pixels, frame.Width, frame.Height, frame.Stride,
                                   _histogram, HistogramStep, _adjustments);
        });

        HistogramView.Update(_histogram);
        UpdateClipText();
    }

    private void UpdateClipText()
    {
        var parts = new List<string>();

        if (_histogram.ClippedLow > HistogramView.ClipThreshold)
            parts.Add($"{_histogram.ClippedLow * 100:0.#} % zugelaufen");

        if (_histogram.ClippedHigh > HistogramView.ClipThreshold)
            parts.Add($"{_histogram.ClippedHigh * 100:0.#} % ausgebrannt");

        ClipText.Text = parts.Count == 0 ? "Keine anliegenden Ränder." : string.Join("   ", parts);
        ClipText.Foreground = (Brush)FindResource(parts.Count == 0 ? "MutedBrush" : "GapBrush");
    }

    // ---------------------------------------------------------------- A/B

    /// <summary>
    /// Merkt sich den aktuellen Frame. Hier wird ausdruecklich KOPIERT: der Puffer
    /// gehoert dem Ringpuffer und wird gleich nach der Anzeige an den naechsten
    /// Frame weitergereicht. Eine Referenz aufzuheben zeigte spaeter irgendein Bild.
    /// </summary>
    private void OnMarkAClicked(object sender, RoutedEventArgs e) => MarkReference();

    private void MarkReference()
    {
        if (_showingReference) return;   // sonst merkt man sich den gemerkten Frame

        _captureRequested = true;

        // Im Stillstand kommt kein neuer Frame - also einen zeichnen lassen.
        if (!_playing) RedrawCurrentFrame();
    }

    private void CaptureReference(FrameBuffer buffer)
    {
        int bytes = buffer.Stride * buffer.Height;

        if (_referencePixels is null || _referencePixels.Length < bytes)
            _referencePixels = new byte[bytes];

        Array.Copy(buffer.Pixels, _referencePixels, bytes);

        _referenceWidth = buffer.Width;
        _referenceHeight = buffer.Height;
        _referenceStride = buffer.Stride;
        _referenceNumber = _sequence.Frames[Math.Clamp(buffer.Index, 0, _sequence.Count - 1)].Number;

        CompareButton.IsEnabled = true;
        CompareText.Text = $"Gemerkt: Frame {_referenceNumber.ToString(_numberFormat)}. " +
                           "Mit A/B oder Taste C umschalten.";
    }

    private void OnCompareToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetShowingReference(CompareButton.IsChecked == true);
    }

    private void ToggleCompare()
    {
        if (_referencePixels is null) { MarkReference(); return; }
        CompareButton.IsChecked = !(CompareButton.IsChecked == true);
    }

    private void SetShowingReference(bool show)
    {
        if (show && _referencePixels is null) return;

        // Der Vergleich ist eine Standaufnahme - bei laufender Wiedergabe waere das
        // gemerkte Bild nach einem Bildwechsel sofort wieder weg.
        if (show && (_playing || _buffering)) Pause();

        _showingReference = show;

        if (show) ShowReferenceFrame();
        else RedrawCurrentFrame();

        UpdateHistogramFromCurrentFrame();

        FileNameText.Text = show
            ? $"A · Frame {_referenceNumber.ToString(_numberFormat)}"
            : _sequence.Frames[Math.Clamp(_shownIndex >= 0 ? _shownIndex : _index, 0, _sequence.Count - 1)].FileName;
    }

    private void ShowReferenceFrame()
    {
        if (_referencePixels is null) return;

        // Denselben Weg wie ein normaler Frame nehmen, damit Korrektur, Zoom und
        // Bitmapgroesse identisch behandelt werden.
        Blit(new FrameBuffer(_referencePixels, _referenceWidth, _referenceHeight,
                             _referenceStride, _shownIndex));
    }

    // ---------------------------------------------------------------- Vorlagen

    private void RefreshPresetList()
    {
        _suppressAdjustment = true;
        try
        {
            PresetBox.ItemsSource = null;
            PresetBox.ItemsSource = _settings.AdjustmentPresets;
            PresetBox.SelectedIndex = -1;
        }
        finally
        {
            _suppressAdjustment = false;
        }

        DeletePresetButton.IsEnabled = _settings.AdjustmentPresets.Count > 0;
    }

    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAdjustment || !IsLoaded) return;
        if (PresetBox.SelectedItem is not AdjustmentPreset preset) return;

        _adjustments = preset.Adjustments.Clamped();
        PresetNameBox.Text = preset.Name;

        PushAdjustmentsToControls();
        AfterAdjustmentChanged();
        UpdateHistogramFromCurrentFrame();

        ShowPanelStatus($"Vorlage „{preset.Name}“ übernommen.");
    }

    private void OnSavePresetClicked(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowPanelStatus("Bitte einen Namen für die Vorlage angeben.");
            return;
        }

        var existing = _settings.AdjustmentPresets.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));

        if (existing is not null) _settings.AdjustmentPresets.Remove(existing);

        _settings.AdjustmentPresets.Add(new AdjustmentPreset { Name = name, Adjustments = _adjustments });
        _settings.AdjustmentPresets.Sort(static (a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        _persist(_settings);
        RefreshPresetList();

        ShowPanelStatus(existing is null
            ? $"Vorlage „{name}“ gesichert."
            : $"Vorlage „{name}“ ersetzt.");
    }

    private void OnDeletePresetClicked(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not AdjustmentPreset preset)
        {
            ShowPanelStatus("Erst eine Vorlage auswählen.");
            return;
        }

        _settings.AdjustmentPresets.Remove(preset);
        _persist(_settings);
        RefreshPresetList();

        ShowPanelStatus($"Vorlage „{preset.Name}“ gelöscht.");
    }

    private void ShowPanelStatus(string message)
    {
        PanelStatus.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
