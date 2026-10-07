using System.Windows;
using System.Windows.Controls;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Zwei Dinge, die zum Beurteilen gehoeren und beim Bauen leicht vergessen werden:
/// das Original daneben und ein Blick aus der Naehe.
/// </summary>
public partial class AtelierPage
{
    /// <summary>True, solange das Original gezeigt wird.</summary>
    private bool _showingOriginal;

    /// <summary>0 heisst eingepasst; sonst der Massstab in Punkten je Bildpunkt.</summary>
    private double _zoom;

    /// <summary>Ob das Original stehen bleibt - nach einem kurzen Klick oder mit der Taste O.</summary>
    private bool _compareLatched;

    /// <summary>Wann der Knopf gedrueckt wurde: kurz heisst umschalten, lang heisst nur hinsehen.</summary>
    private DateTime _comparePressed;

    /// <summary>Ob das Original gerade stehen bleibt - fuer die Probe.</summary>
    internal bool CompareLatched => _compareLatched;

    /// <summary>
    /// Zeigt das Bild ohne jede Korrektur - beim Druecken sofort.
    ///
    /// Gedrueckt halten war lange der einzige Weg, und er wurde nicht gefunden: Ein Klick zeigte
    /// das Original nur fuer die Dauer des Klicks, und es sah aus, als taete der Knopf nichts. Jetzt
    /// schaltet ein kurzer Klick um und laesst das Original stehen; gehalten bleibt es ein Blick.
    /// Die Sorge von frueher - wer umschaltet, vergisst zurueckzuschalten und beurteilt minutenlang
    /// das falsche Bild - loest das Abzeichen am Bild, und jede Aenderung schaltet von selbst zurueck.
    /// </summary>
    private void OnCompareDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_frame is null) return;

        _comparePressed = DateTime.UtcNow;
        if (_compareLatched || _showingOriginal) return;

        _showingOriginal = true;
        Render();
        ShowCompare();
    }

    private void OnCompareUp(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.RoutedEvent == System.Windows.Input.Mouse.MouseLeaveEvent)
        {
            // Nur ein gehaltener Blick endet beim Verlassen; ein eingerastetes Original bleibt.
            if (_compareLatched || !_showingOriginal) return;

            _showingOriginal = false;
            Render();
            ShowCompare();
            return;
        }

        if (_compareLatched)
        {
            EndCompare();
            return;
        }

        if (!_showingOriginal) return;

        if ((DateTime.UtcNow - _comparePressed).TotalMilliseconds < 350)
        {
            _compareLatched = true;
            ShowCompare();
            return;
        }

        _showingOriginal = false;
        Render();
        ShowCompare();
    }

    /// <summary>
    /// Der Klick ohne Maus - Leertaste, Eingabe, Bedienhilfen. Mit der Maus haben Druecken und
    /// Loslassen schon entschieden; dann kommt der Klick direkt danach und wird uebergangen.
    /// </summary>
    private void OnCompareClicked(object sender, RoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _comparePressed).TotalMilliseconds < 600) return;

        ToggleCompare();
    }

    /// <summary>Vorher/Nachher umschalten - die Taste O und der Klick ohne Maus.</summary>
    internal void ToggleCompare()
    {
        if (_frame is null) return;

        if (_compareLatched)
        {
            EndCompare();
            return;
        }

        _compareLatched = true;
        _showingOriginal = true;
        Render();
        ShowCompare();
    }

    private void EndCompare()
    {
        _compareLatched = false;

        if (_showingOriginal)
        {
            _showingOriginal = false;
            Render();
        }

        ShowCompare();
    }

    /// <summary>Jede Aenderung am Bild zeigt wieder das Ergebnis - sonst dreht man an einem Regler, und nichts passiert.</summary>
    private void DropCompare()
    {
        if (!_compareLatched) return;

        _compareLatched = false;
        _showingOriginal = false;
        ShowCompare();
    }

    private void ShowCompare()
    {
        CompareBadge.Visibility = _showingOriginal ? Visibility.Visible : Visibility.Collapsed;
        CompareBadgeText.SetResourceReference(TextBlock.TextProperty, _compareLatched ? "S_BeforeBadgeLatched" : "S_BeforeBadge");
    }

    /// <summary>
    /// Zwischen Einpassen und 100 % umschalten.
    ///
    /// Zweistufig wie im Vorschaufenster, und mit derselben Beschriftung - wer den
    /// Knopf dort kennt, soll hier nicht raten muessen, was eine dritte Stufe
    /// bedeutet.
    ///
    /// 100 % ist dabei die eigentliche Stufe: Rauschen, Stufen in einem Verlauf und
    /// die Wirkung einer steilen Kurve lassen sich nur dort beurteilen, wo ein
    /// Bildpunkt auch ein Bildpunkt ist. Eingepasst sieht alles glatt aus, was die
    /// Skalierung glattgerechnet hat.
    /// </summary>
    private void OnZoomClicked(object sender, RoutedEventArgs e)
    {
        _zoom = _zoom == 0 ? 1.0 : 0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        var frame = _frame;

        if (frame is null || _zoom == 0)
        {
            Display.Stretch = System.Windows.Media.Stretch.Uniform;
            Display.Width = double.NaN;
            Display.Height = double.NaN;
            ZoomText.Text = Localization.Strings.T("S_ZoomFit");

            // Eingepasst gibt es nichts zu schieben; die Balken verschwinden.
            ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            ShowPlacement();
            return;
        }

        // Eingepasst gestreckt und nicht "None": Bei "None" zeichnet das Bildelement die
        // Bitmap in ihrer eigenen Groesse, egal wie gross das Element ist - das ging nur,
        // solange es neben dem Einpassen allein 100 % gab. Das Element ist genau so gross
        // wie das Bild im Massstab, also gibt es keine Raender, und die Umrechnung in
        // ImageHit liefert den Massstab aus der Elementgroesse.
        Display.Stretch = System.Windows.Media.Stretch.Uniform;
        Display.Width = frame.Width * _zoom;
        Display.Height = frame.Height * _zoom;
        ZoomText.Text = $"{_zoom * 100:0} %";

        ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        // Bei 100 % und darueber soll ein Bildpunkt scharf stehen; die weiche
        // Skalierung wuerde genau das verwischen, was man sehen will.
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(
            Display, _zoom >= 1.0
                ? System.Windows.Media.BitmapScalingMode.NearestNeighbor
                : System.Windows.Media.BitmapScalingMode.HighQuality);

        // Der Greifrahmen rechnet in Punkten auf dem Element - beim Massstabwechsel
        // stimmt seine Umrechnung nicht mehr.
        ShowPlacement();
    }

    private void SetUpView() => ImageScroll.PreviewMouseWheel += OnViewportWheel;

    /// <summary>
    /// Das Mausrad zoomt - beim Verschieben und beim Pinsel, sonst nicht.
    ///
    /// Nur dort, weil es sonst anderem gehoert: ueber dem Graphen zoomt es den Graphen,
    /// und beim Zuschneiden, Waehlen und bei der Pipette rollt es wie bisher den
    /// Ausschnitt. Beim Pinsel stellt Strg + Rad den Abstand der Tupfer.
    /// </summary>
    private void OnViewportWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_frame is null || _tool is not (AtelierTool.Move or AtelierTool.Brush)) return;

        e.Handled = true;

        if (_tool == AtelierTool.Brush &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            StepBrushSpacing(e.Delta / 120.0);
            return;
        }

        // Umschalt und Rad beim Pinsel: die Spitze drehen.
        if (_tool == AtelierTool.Brush &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0)
        {
            Placement.StepAngle(e.Delta / 120.0);
            return;
        }

        WheelZoom(e.GetPosition(Display), e.GetPosition(ImageScroll), e.Delta / 120.0);
    }

    /// <summary>
    /// Zoomt um den Punkt unter dem Zeiger: Der Bildpunkt, auf den er zeigt, steht nach
    /// dem Schritt wieder unter ihm.
    ///
    /// Beim Herauszoomen rastet es an der Einpassung ein und zeigt das Bild wieder
    /// eingepasst und mittig - dieselbe Ansicht wie nach dem Oeffnen, nicht ein Bild,
    /// das zufaellig fast so gross ist und irgendwo liegt.
    /// </summary>
    /// <param name="onDisplay">Der Zeiger auf dem Bildelement.</param>
    /// <param name="inView">Derselbe Zeiger im sichtbaren Ausschnitt.</param>
    internal void WheelZoom(System.Windows.Point onDisplay, System.Windows.Point inView, double notches)
    {
        var frame = _frame;
        if (frame is null) return;

        double fit = FitScale();
        if (fit <= 0) return;

        double current = _zoom == 0 ? fit : _zoom;
        double next = ZoomSteps.Next(current, notches, fit);

        if (next <= fit * (1 + 1e-9))
        {
            if (_zoom == 0) return;

            _zoom = 0;
            ApplyZoom();
            return;
        }

        if (Math.Abs(next - current) < 1e-9) return;

        // Der Bildpunkt unter dem Zeiger, ungerundet - gerundet wanderte das Bild bei
        // starkem Zoom um bis zu einen Bildpunkt je Schritt.
        ImageHit.Exact(onDisplay.X, onDisplay.Y, Display.ActualWidth, Display.ActualHeight,
                       frame.Width, frame.Height, Display.Stretch == System.Windows.Media.Stretch.Uniform,
                       out double u, out double v);

        _zoom = next;
        ApplyZoom();

        ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(u * next - inView.X);
        ImageScroll.ScrollToVerticalOffset(v * next - inView.Y);
    }

    /// <summary>Der Massstab des eingepassten Bildes - so wie das Bildelement es beim Einpassen zeigt.</summary>
    private double FitScale()
    {
        var frame = _frame;
        if (frame is null || frame.Width <= 0 || frame.Height <= 0) return 0;

        return Math.Min(ImageScroll.ActualWidth / frame.Width, ImageScroll.ActualHeight / frame.Height);
    }

    /// <summary>
    /// Was gerechnet werden soll: beim Vergleich nichts, sonst das Eingestellte.
    ///
    /// Das Original heisst hier wirklich ohne alles - auch ohne die Grundregler.
    /// Ein Vergleich, der die Haelfte der Korrektur stehenlaesst, beantwortet keine
    /// Frage, die jemand gestellt haette.
    /// </summary>
    private (ImageAdjustments Adjustments, PreparedGrading Grading) Current()
        => _showingOriginal
            ? (ImageAdjustments.Neutral, PreparedGrading.None)
            : (_finalAdjustments, _finalGrading);

    /// <summary>
    /// Welches Bild gezeichnet wird: das zusammengesetzte, oder beim Vergleich das Original -
    /// dieselben Ebenen ohne eigene Korrekturen (<see cref="Untouched"/>).
    ///
    /// Frueher fiel beim Vergleich auch die Schichtung weg, und es stand das Bild der Datei da.
    /// Wer sein Bild aus Passen zusammensetzt, sah dann ein anderes Bild mit anderer Farbe -
    /// nicht sein Bild ohne Korrektur. Wie die Ebenen eingemischt werden, gehoert zum Aufbau.
    /// </summary>
    private FloatFrame? Shown() => _showingOriginal ? OriginalFrame() : _soloFrame ?? _frame;

    /// <summary>Das Original im Stapel - in einem eigenen Frame, damit das Ergebnis bleibt.</summary>
    private FloatFrame? _original;

    /// <summary>Was beim Original obenauf liegt: die Wasserzeichen, ebenfalls ohne Korrektur.</summary>
    private OverlayPlan[] _originalOverlays = Overlays.None;

    /// <summary>
    /// Setzt den Stapel ohne eigene Korrekturen zusammen. Im Knotenmodus gibt das Bild der
    /// Datei nur die Leinwand vor - gerechnet wird beim Zeichnen, siehe RenderUntouchedNodes.
    /// </summary>
    private FloatFrame? OriginalFrame()
    {
        if (_base is null) return _frame;

        _originalOverlays = Overlays.None;
        if (InNodes) return _base;

        var plain = Untouched.Of(Layers.Stack);

        _original = LayerComposer.Compose(plain, _sources, _original, _coarse ? CoarseStep : 1, _number);
        var shown = _original ?? _base;

        _originalOverlays = Overlays.Prepare(plain, _sources, shown.Width, shown.Height);

        // Hat der Composer eine Quelle durchgereicht, gehoert sie ihm nicht - wie beim Ergebnis.
        if (_original is not null && !Owned(_original)) _original = null;

        return shown;
    }
}
