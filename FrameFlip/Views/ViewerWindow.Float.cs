using System.IO;
using System.Windows.Threading;
using FrameFlip.Decoding;
using FrameFlip.Imaging;

namespace FrameFlip.Views;

/// <summary>
/// Der zweite Weg fuer das angehaltene Bild: die Korrektur auf den Gleitkommawerten
/// der Datei statt auf den acht Bit im Ringpuffer.
///
/// Warum nur im Halt: gemessen kostet ein 1080p-Bild mit AgX rund 92 ms, bei 4K das
/// Vierfache. Bei 24 fps stehen 41,7 ms zwischen zwei Bildern - fuer die Wiedergabe
/// reicht das nicht, und sie braucht es auch nicht. Steht das Bild dagegen, ist Zeit,
/// und dann macht es den Unterschied: Belichtung holt dort Zeichnung aus der
/// Ueberstrahlung, wo auf acht Bit nur noch Weiss steht.
/// </summary>
public sealed partial class ViewerWindow
{
    /// <summary>Die Gleitkommawerte des stehenden Bildes. Null, solange keine vorliegen.</summary>
    private FloatFrame? _floatFrame;

    /// <summary>Zu welchem Frame sie gehoeren. -1 heisst: zu keinem.</summary>
    private int _floatIndex = -1;

    /// <summary>
    /// Zaehlt die Ladeauftraege. Trifft eine Antwort ein, deren Nummer nicht mehr die
    /// aktuelle ist, wird sie verworfen - beim Durchblaettern mit den Pfeiltasten
    /// laufen sonst mehrere Ladevorgaenge gegeneinander und der letzte gewinnt, nicht
    /// der richtige.
    /// </summary>
    private int _floatGeneration;

    /// <summary>Was die Datei mitbringt, fuer die Anzeige im Panel.</summary>
    private bool _floatAvailable;

    /// <summary>
    /// Sorgt dafuer, dass zum stehenden Bild die Gleitkommawerte vorliegen.
    ///
    /// Gelesen wird auf einem Hintergrundthread: eine 4K-EXR von der Platte zu holen
    /// und auszupacken dauert laenger, als ein Fenster stehenbleiben darf.
    /// </summary>
    private void EnsureFloatFrame(int index, int width, int height)
    {
        if (_closing || _playback.IsPlaying || _playback.IsBuffering) return;
        if (index < 0 || index >= _sequence.Count) return;
        if (_floatIndex == index && _floatFrame is not null &&
            _floatFrame.Width == width && _floatFrame.Height == height) return;

        string path = _sequence.Frames[index].Path;
        if (!IsFloatFormat(path))
        {
            DropFloatFrame();
            return;
        }

        int generation = ++_floatGeneration;

        Task.Run(() =>
        {
            var frame = FloatFrame.FromExr(path, width, height);
            if (frame is null) return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                // Inzwischen weitergeblaettert, angefangen zu spielen oder das Fenster
                // geschlossen: das Ergebnis ist dann wertlos, nicht falsch.
                if (_closing || generation != _floatGeneration) return;
                if (_playback.IsPlaying || _playback.IsBuffering) return;

                _floatFrame = frame;
                _floatIndex = index;
                _floatAvailable = true;

                // Jetzt erst neu zeichnen - vorher stand das Bild aus dem Ringpuffer,
                // und das war nicht falsch, nur acht Bit.
                RedrawCurrentFrame();
                UpdateHistogramFromCurrentFrame();
                UpdateFloatBadge();
            }));
        });
    }

    /// <summary>
    /// Vergisst die Gleitkommawerte. Beim Abspielen muessen sie weg sein, sonst
    /// zeigte jedes Bild der Sequenz die Werte desjenigen, bei dem zuletzt angehalten
    /// wurde.
    /// </summary>
    private void DropFloatFrame()
    {
        _floatGeneration++;
        _floatFrame = null;
        _floatIndex = -1;
        _floatAvailable = false;
        UpdateFloatBadge();
    }

    /// <summary>Ob fuer den gezeigten Frame Gleitkommawerte bereitstehen.</summary>
    private bool HasFloatFor(int index, int width, int height)
        => _floatFrame is not null && _floatIndex == index &&
           _floatFrame.Width == width && _floatFrame.Height == height &&
           !_playback.IsPlaying && !_playback.IsBuffering;

    /// <summary>
    /// Die Sichtumwandlung fuer den Gleitkommaweg. Dieselbe, die auch der Decoder
    /// benutzt - sonst saehe das angehaltene Bild anders aus als das laufende.
    /// </summary>
    private IViewTransform FloatView
        => _decoders.For(".exr") is ExrFrameDecoder exr ? exr.View : new StandardViewTransform();

    private static bool IsFloatFormat(string path)
        => Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Zeigt an, dass die Korrektur gerade auf den echten Werten rechnet. Ohne den
    /// Hinweis waere der Unterschied nur an der Wirkung der Regler zu merken - und
    /// das ist genau der Moment, in dem man ihn wissen will.
    /// </summary>
    private void UpdateFloatBadge()
    {
        if (FloatBadge is null) return;

        FloatBadge.Visibility = _floatAvailable
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

        UpdateToolAvailability();
    }

    /// <summary>
    /// Die Werkzeuge abschalten, solange sie nichts ausrichten koennen.
    ///
    /// Sie rechnen auf den Gleitkommawerten und wirken damit nur auf einem
    /// angehaltenen EXR. Ohne diesen Schalter staenden dreissig Regler da, die sich
    /// bedienen lassen und nichts tun - und der Anwender suchte den Fehler bei sich.
    /// </summary>
    private void UpdateToolAvailability()
    {
        if (ToolsBody is null) return;

        ToolsBody.IsEnabled = _floatAvailable;
        ToolsHint.Visibility = _floatAvailable
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    }
}
