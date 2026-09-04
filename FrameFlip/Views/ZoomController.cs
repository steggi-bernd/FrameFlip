using System.Windows;
using System.Windows.Media;
// UseWindowsForms zieht System.Drawing mit ein; dort heissen zwei Typen genauso.
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Zoom und Verschieben als reine Abbildung Inhalt -> Anzeigeflaeche.
///
/// Der Regler kennt drei Groessen und haelt sie strikt auseinander:
///
///   Native Groesse   Aufloesung der Bilddatei, aendert sich nie.
///   Inhaltsgroesse   Pixelmass des aktuell dekodierten Puffers. Aendert sich beim
///                    Nachschaerfen und beim Wechsel der Dekodier-Aufloesung.
///   Anzeigeflaeche   Groesse des Viewports in DIP. Aendert sich nur mit dem Fenster.
///
/// Der Zoom liegt ausschliesslich in der Matrix. Kein Codepfad hier veraendert eine
/// Puffergroesse, eine Bitmapgroesse oder eine Layoutgroesse - deshalb kann der
/// schwarze Rand aus der alten Fassung strukturell nicht wiederkehren.
///
/// Bewusst ohne Bezug auf ein WPF-Element: der Regler haelt nur eine Matrix und
/// meldet Aenderungen. Dadurch ist die gesamte Zoom-Mathematik ohne Fenster testbar.
/// </summary>
public sealed class ZoomController
{
    /// <summary>Obergrenze laut Vorgabe: 800 %.</summary>
    public const double MaxZoom = 8.0;

    private Matrix _matrix = Matrix.Identity;
    private Size _content;
    private Size _viewport;
    private double _dpi = 1.0;
    private double _nativeWidth = 1;
    private double _nativeHeight = 1;
    private bool _fit = true;

    /// <summary>Feuert, wenn sich die Matrix geaendert hat.</summary>
    public event Action? Changed;

    public Matrix Matrix => _matrix;

    public Size Content => _content;

    public Size Viewport => _viewport;

    /// <summary>True, solange der Massstab exakt der Einpassung entspricht.</summary>
    public bool IsFit => _fit;

    /// <summary>
    /// Absoluter Massstab: Bildpixel des Originals je Geraetepixel. 1,0 heisst 100 %.
    /// Bewusst unabhaengig von der Puffergroesse - genau das ist die Invariante, die
    /// das Nachschaerfen sprungfrei macht.
    /// </summary>
    /// Solange noch kein Puffer da ist, gilt der Massstab, der gleich eingestellt
    /// wird - sonst laedt der erste Dekodiervorgang in voller Aufloesung.
    public double Zoom => _content.Width <= 0 || _nativeWidth <= 0
        ? FitZoom
        : _matrix.M11 * (_content.Width / _nativeWidth) * _dpi;

    /// <summary>Massstab, bei dem das Bild vollstaendig hineinpasst. Nie ueber 100 %.</summary>
    public double FitZoom
    {
        get
        {
            if (_viewport.Width <= 0 || _viewport.Height <= 0) return 1.0;
            return Math.Min(1.0, Math.Min(_viewport.Width * _dpi / _nativeWidth,
                                          _viewport.Height * _dpi / _nativeHeight));
        }
    }

    /// <summary>Untergrenze: weiter als bis zur Einpassung wird nicht herausgezoomt.</summary>
    public double MinZoom => Math.Min(FitZoom, 1.0);

    /// <summary>Nur wenn der Inhalt groesser als die Anzeigeflaeche ist, ist Verschieben sinnvoll.</summary>
    public bool CanPan => DisplayWidth > _viewport.Width + 0.5 || DisplayHeight > _viewport.Height + 0.5;

    private double DisplayWidth => _content.Width * _matrix.M11;

    private double DisplayHeight => _content.Height * _matrix.M22;

    // ---------------------------------------------------------------- Eingangsgroessen

    public void SetNativeSize(double width, double height)
    {
        _nativeWidth = Math.Max(1, width);
        _nativeHeight = Math.Max(1, height);
    }

    public void SetDpi(double dpi) => _dpi = dpi > 0 ? dpi : 1.0;

    /// <summary>Neue Fenstergroesse. Im Einpassmodus wird neu eingepasst, sonst nur nachgefuehrt.</summary>
    public void SetViewport(Size viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        if (Math.Abs(viewport.Width - _viewport.Width) < 0.01 &&
            Math.Abs(viewport.Height - _viewport.Height) < 0.01) return;

        _viewport = viewport;

        if (_fit) FitToViewport();
        else Commit(ClampTranslation(_matrix));
    }

    /// <summary>
    /// Neuer Puffer. preserveZoom haelt den optischen Massstab exakt konstant - das
    /// ist die Bedingung dafuer, dass das Nachschaerfen keinen Sprung erzeugt.
    /// </summary>
    public void SetContent(Size content, bool preserveZoom)
    {
        if (content.Width <= 0 || content.Height <= 0) return;

        bool first = _content.Width <= 0;
        var previous = _content;
        _content = content;

        if (first || !preserveZoom || _fit)
        {
            FitToViewport();
            return;
        }

        // Der Inhalt hat jetzt mehr (oder weniger) Pixel. Der Matrixfaktor wird im
        // Kehrverhaeltnis angepasst, damit das Produkt aus Inhaltsbreite und Faktor -
        // also die Anzeigegroesse - unveraendert bleibt. Der Ursprung des Inhalts
        // liegt weiter bei (0,0), deshalb bleiben OffsetX und OffsetY unberuehrt.
        double ratio = previous.Width / content.Width;

        var m = _matrix;
        m.M11 *= ratio;
        m.M22 *= ratio;
        Commit(ClampTranslation(m));
    }

    // ---------------------------------------------------------------- Bedienung

    /// <summary>
    /// Zoomt um den Faktor; der Bildpunkt unter dem Anker bleibt ortsfest.
    /// Der Anker ist eine Viewport-Koordinate in DIP.
    /// </summary>
    public void ZoomBy(Point anchor, double factor) => SetZoom(Snap(Zoom * factor), anchor);

    /// <summary>Setzt den absoluten Massstab und haelt dabei den Anker ortsfest.</summary>
    public void SetZoom(double zoom, Point anchor)
    {
        if (_content.Width <= 0) return;

        double target = Math.Clamp(zoom, MinZoom, MaxZoom);
        double current = Zoom;
        if (current <= 0) return;

        double factor = target / current;
        if (Math.Abs(factor - 1.0) < 1e-6) return;

        var m = _matrix;

        // ScaleAt haengt die Skalierung an die Matrix an. Da die Matrix vom Inhalt in
        // den Viewport abbildet, wirkt der Ankerpunkt im Viewport-Koordinatensystem -
        // genau dort, wo der Mauszeiger steht. Eine eigene Offsetrechnung ist
        // dadurch nicht noetig.
        m.ScaleAt(factor, factor, anchor.X, anchor.Y);

        _fit = Math.Abs(target - FitZoom) < 1e-4;
        Commit(ClampTranslation(m));
    }

    public void Pan(Vector delta)
    {
        if (!CanPan) return;

        var m = _matrix;
        m.Translate(delta.X, delta.Y);
        Commit(ClampTranslation(m));
    }

    /// <summary>Einpassen ohne Hochskalieren - kleine Bilder bleiben klein.</summary>
    public void FitToViewport()
    {
        _fit = true;
        Commit(Centered(MatrixScaleFor(FitZoom)));
    }

    /// <summary>1 Bildpixel auf 1 Geraetepixel.</summary>
    public void ActualSize(Point anchor)
    {
        SetZoom(1.0, anchor);
        _fit = Math.Abs(1.0 - FitZoom) < 1e-4;

        // Passt das Bild bei 100 % vollstaendig hinein, wird zentriert statt am Anker
        // haengen zu bleiben - sonst klebt ein kleines Bild in einer Ecke.
        if (!CanPan) Commit(Centered(_matrix.M11));
    }

    /// <summary>Doppelklick: Wechsel zwischen Einpassung und 100 %.</summary>
    public void ToggleFitAndActual(Point anchor)
    {
        if (_fit && Math.Abs(FitZoom - 1.0) > 1e-4) ActualSize(anchor);
        else FitToViewport();
    }

    // ---------------------------------------------------------------- Rechnen

    /// <summary>Rechnet den absoluten Massstab in den Matrixfaktor Inhalt -> DIP um.</summary>
    public double MatrixScaleFor(double zoom)
        => _content.Width <= 0 ? 1.0 : zoom * _nativeWidth / (_content.Width * _dpi);

    /// <summary>
    /// Rastet nahe 100 % und nahe der Einpassung ein. Ohne das trifft man die beiden
    /// wichtigen Stufen mit dem Mausrad nie genau.
    /// </summary>
    private double Snap(double zoom)
    {
        const double tolerance = 0.08;
        double fit = FitZoom;

        if (Math.Abs(zoom - 1.0) < tolerance) return 1.0;
        if (Math.Abs(zoom - fit) < tolerance * fit) return fit;
        return zoom;
    }

    private Matrix Centered(double scale) => new(
        scale, 0, 0, scale,
        (_viewport.Width - _content.Width * scale) / 2.0,
        (_viewport.Height - _content.Height * scale) / 2.0);

    /// <summary>
    /// Ist der Inhalt kleiner als die Anzeigeflaeche, wird er zentriert. Ist er
    /// groesser, wird der Versatz so begrenzt, dass nie ein leerer Rand entsteht.
    /// </summary>
    private Matrix ClampTranslation(Matrix m)
    {
        double w = _content.Width * m.M11;
        double h = _content.Height * m.M22;

        m.OffsetX = w <= _viewport.Width
            ? (_viewport.Width - w) / 2.0
            : Math.Clamp(m.OffsetX, _viewport.Width - w, 0);

        m.OffsetY = h <= _viewport.Height
            ? (_viewport.Height - h) / 2.0
            : Math.Clamp(m.OffsetY, _viewport.Height - h, 0);

        return m;
    }

    private void Commit(Matrix m)
    {
        if (m.Equals(_matrix)) return;
        _matrix = m;
        Changed?.Invoke();
    }
}
