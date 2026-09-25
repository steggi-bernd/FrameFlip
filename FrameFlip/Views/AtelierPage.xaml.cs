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
using CompositionTarget = System.Windows.Media.CompositionTarget;
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

    /// <summary>
    /// Der Frame, in den zusammengesetzt wird - Eigentum der Seite.
    ///
    /// Er wird wiederverwendet, statt bei jedem Reglerzug neu angelegt zu werden: Bei
    /// 4K sind das rund 130 Megabyte je Durchgang, und gemessen war das Anlegen
    /// teurer als das Rechnen.
    /// </summary>
    private FloatFrame? _composed;

    /// <summary>Die Ebenen, die ueber allem liegen - Wasserzeichen und dergleichen.</summary>
    private OverlayPlan[] _overlays = Overlays.None;

    /// <summary>
    /// Quellen, deren Leseversuch nichts ergeben hat.
    ///
    /// Sie werden beim naechsten Anlass wieder versucht - ein Lesefehler kann
    /// voruebergehend sein, etwa weil die Datei gerade geschrieben wurde. Was sich
    /// aendert, ist nur, dass es SICHTBAR wird: Die Zeile im Streifen zeigt sich als
    /// fehlend, statt dass eine eingeblendete Ebene stumm nichts tut.
    /// </summary>
    private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);

    /// <summary>Die gelesenen Passe, nach Quellnamen. Leerer Name ist das Bild selbst.</summary>
    private readonly Dictionary<string, FloatFrame> _sources = new(StringComparer.Ordinal);

    /// <summary>Was die Datei anbietet - die Auswahl im Plusknopf.</summary>
    private IReadOnlyList<ExrPass> _passes = Array.Empty<ExrPass>();

    /// <summary>
    /// Die Passe, die die Werkzeuge brauchen - aus dem, was schon gelesen ist.
    ///
    /// Hier wird nichts von der Platte geholt: Das laeuft ueber denselben Weg wie die
    /// Passe der Ebenen, und zwar vorher. Ein Lesezugriff im Zeichnen waere bei jedem
    /// Reglerzug eine Datei im Weg.
    /// </summary>
    private FloatFrame?[] Renderdata(PreparedGrading grading)
        => FramePasses.Resolve(grading.Data, _passes,
                               name => _sources.TryGetValue(name, out var found) ? found : null);

    private WriteableBitmap? _surface;
    private string? _path;
    private int _number;

    /// <summary>
    /// Die Ebene, deren Werkzeuge der Streifen gerade zeigt. Null heisst: das
    /// fertige Bild.
    /// </summary>
    private ImageLayer? _editing;

    /// <summary>
    /// Die Korrektur des fertigen Bildes - unabhaengig davon, was der Streifen
    /// gerade zeigt.
    ///
    /// Getrennt gefuehrt, weil der Streifen sich umhaengt: Waehrend er die Werkzeuge
    /// einer Ebene zeigt, darf das fertige Bild nicht ploetzlich mit deren Kurve
    /// gerechnet werden.
    /// </summary>
    private ImageAdjustments _finalAdjustments = ImageAdjustments.Neutral;
    private PreparedGrading _finalGrading = PreparedGrading.None;

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

        SetUpDock();

        Tools.Changed += OnToolsChanged;
        Tools.ToolsEnabled = false;

        Layers.Changed += OnLayersChanged;
        Layers.PickMode += OnPickModeChanged;
        MouseTools.ToolChanged += OnToolChanged;
        Properties.FocusWanted += OnFocusWanted;
        Properties.Show(AtelierTool.Move);
        Layers.Editing += Bind;
        Tools.TargetChanged += OnTargetChosen;
        Layers.Thumbnail = Thumbnail;

        Placement.Changed += OnPlacementDragged;
        Placement.Painted += OnPainted;
        Placement.MaskWanted = MakeMaskLayer;
        Properties.BrushChanged += UseBrushSettings;
        Placement.BrushAdjusted += () => Properties.SetBrush(Placement.BrushRadius, Placement.BrushHardness, Placement.BrushSpacing);

        Bind(null);

        NodeView.SocketTitle = NodeTitles.Socket;
        NodeView.SelectionChanged += _ => OnNodeSelected();
        NodeView.LayoutChanged += SaveNodes;
        NodeView.GraphChanged += OnGraphChanged;
        SetUpNodeEditing();

        // Der Knotenmodus kommt zurueck, wenn er beim letzten Mal an war - vor dem
        // ersten Bild, damit dieses gleich mit dem Graphen gerechnet wird.
        RestoreNodes();

        // Die Sitzung kommt zurueck, sobald jemand das Atelier zum ersten Mal
        // ansieht - und nicht beim Start des Programms. Wer FrameFlip oeffnet, um
        // eine Sequenz durchzusehen, soll nicht auf eine 4K-Datei warten, die er
        // vielleicht gar nicht mehr braucht.
        IsVisibleChanged += OnFirstShown;

        _settle = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };

        SetUpBatch();
        SetUpView();

        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            if (!_coarse) return;

            // Erst die Schrittweite zuruecksetzen, dann zusammensetzen, dann
            // zeichnen. Waere das Zusammensetzen noch grob, blieben die Werte
            // zwischen den Gitterpunkten stehen - und der volle Durchgang zeichnete
            // sie mit, als waeren sie gerechnet.
            _coarse = false;

            Recompose();
            Render();
            Measure();

            // Ein Zug ist zu Ende - jetzt gehoert der Stand des Graphen in die
            // Einstellungen. Waehrend des Zuges waere das bei jedem Bild ein Durchgang
            // durch den ganzen Graphen.
            if (InNodes) KeepNodes();
        };
    }

    /// <summary>
    /// Holt beim ersten Ansehen zurueck, was zuletzt offen war.
    ///
    /// Vorher blieb das Atelier nach einem Neustart leer, obwohl der Stapel
    /// gespeichert war - und wer dann irgendein Bild oeffnete, bekam das alte Rezept
    /// auf die neue Leinwand. Das sah aus, als waere beim Speichern etwas
    /// verrutscht, und war nur ein Rezept ohne sein Bild.
    /// </summary>
    private void OnFirstShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) return;

        IsVisibleChanged -= OnFirstShown;

        string? last = _settings.AtelierImage;

        // Eine Datei, die es nicht mehr gibt, ist kein Fehler - sie ist weg. Das
        // leere Atelier ist dann die richtige Antwort und nicht eine Meldung.
        if (_path is not null || string.IsNullOrWhiteSpace(last) || !File.Exists(last)) return;

        Open(last);
    }

    /// <summary>Oeffnet ein Bild - der Weg, den auch die Projektseite nehmen kann.</summary>
    public void Open(string path)
    {
        _path = path;

        // Die Bildnummer aus dem Dateinamen. Sie ist der Wurf fuer das Filmkorn,
        // und sie kommt aus dem Namen und nicht aus der Stelle im Lauf: Bild 47 soll
        // immer dasselbe Korn bekommen - in der Vorschau, im Export und auch dann,
        // wenn jemand spaeter nur einen Ausschnitt nachexportiert.
        _number = SequenceLink.NumberOf(path) ?? 0;

        // Der Streifen braucht sie fuer die Sperre der gemalten Maske - er kennt
        // keine Dateien und keine Sequenz, nur diese Zahl.
        Layers.Number = _number;
        FileText.Text = Path.GetFileName(path);
        BusyBadge.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;

        // Lesen und Auspacken dauert bei 4K spuerbar lange; auf dem Oberflaechenfaden
        // staende dabei das ganze Fenster.
        Task.Run(() => Load(path)).ContinueWith(task =>
        {
            var loaded = task.IsCompletedSuccessfully
                ? task.Result
                : (null, Array.Empty<ExrPass>(), Array.Empty<CryptomatteSet>());

            Dispatcher.Invoke(() => Show(path, loaded.Frame, loaded.Passes, loaded.Cryptomattes));
        });
    }

    private void Show(string path, FloatFrame? loaded, IReadOnlyList<ExrPass> passes,
                      IReadOnlyList<CryptomatteSet> cryptomattes)
    {
        BusyBadge.Visibility = Visibility.Collapsed;

        // Ein Klickmodus, der eine Datei ueberdauert, waere ein stiller Zustand: Der
        // naechste Klick ins neue Bild taete etwas, womit niemand rechnet.
        Layers.StopPicking();

        _sources.Clear();
        _unreadable.Clear();
        _pool.Clear();
        _regionPool.Clear();
        _cache.Clear();
        _sourceThumbs.Clear();
        _composed = null;
        _passes = passes;

        // Welche Renderdaten diese Datei fuehrt. Der Streifen sagt es an den
        // Werkzeugen, die sie brauchen - sonst zieht man an einem Regler, es
        // passiert nichts, und man sucht den Fehler im Programm statt in der Datei.
        Tools.ShowPasses(FramePasses.NameFor(PassNeed.Depth, passes) is not null,
                         FramePasses.NameFor(PassNeed.Motion, passes) is not null,
                         FramePasses.NameFor(PassNeed.Normal, passes) is not null);
        _cryptomattes = cryptomattes;

        // Die Miniaturen liegen unter dem Namen der Quelle - und derselbe Name meint
        // in einer anderen Datei etwas anderes.
        ForgetThumbnails();

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

        // Erst jetzt gemerkt, nicht beim Oeffnen: Eine Datei, die sich nicht lesen
        // laesst, soll beim naechsten Start nicht wieder versucht werden.
        //
        // Und gleich geschrieben. Das Merken im Speicher haette dem Rezept nichts
        // genuetzt: Es wird beim Beenden gespeichert, das Bild dazu waere es nicht,
        // und die beiden duerfen nicht auseinanderfallen.
        _settings.AtelierImage = path;
        _persist(_settings);
        _surface = null;
        Tools.ToolsEnabled = true;

        // Der gespeicherte Stapel gilt nur, soweit diese Datei die Passe auch
        // fuehrt. Zwanzig ausgegraute Zeilen nach dem Wechsel auf ein PNG waeren
        // kein Hinweis, sondern ein Raetsel.
        Layers.Load(passes, cryptomattes, Prune(_settings.Layers, passes));

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

        // Erst jetzt steht der Stapel da - und damit, was einem alten Graphen an
        // ausgeblendeten Ebenen fehlt.
        if (InNodes) ShowMissingLayers();

        // Braucht der Stapel Passe, die noch nicht gelesen sind, kommen sie
        // nachtraeglich - das Bild steht schon, waehrend sie eintreffen. Im
        // Knotenmodus fragt der Graph, was er braucht.
        if (InNodes) FetchNodeSources();
        else if (!Layers.Stack.IsPassThrough) OnLayersChanged(interim: false);

        ShowNodeMode();
    }

    private (FloatFrame? Frame, IReadOnlyList<ExrPass> Passes,
             IReadOnlyList<CryptomatteSet> Cryptomattes) Load(string path)
    {
        // EXR bringt die Werte selbst mit. Alles andere geht ueber den vorhandenen
        // Decoder und wird aus den acht Bit zurueckgerechnet.
        if (Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase))
            return (FloatFrame.FromExr(path), ExrPasses.Of(path), Cryptomatte.Of(path));

        var decoder = _decoders.For(Path.GetExtension(path));
        if (decoder is null) return (null, Array.Empty<ExrPass>(), Array.Empty<CryptomatteSet>());

        var frame = decoder.TryDecode(path, 16384, 16384, n => new byte[n], out var decoded)
            ? FloatFrame.FromBgra32(decoded.Pixels, decoded.Width, decoded.Height, decoded.Stride)
            : null;

        return (frame, Array.Empty<ExrPass>(), Array.Empty<CryptomatteSet>());
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

    /// <summary>
    /// Haengt den Werkzeugstreifen an ein Ziel: an eine Einstellungsebene oder an
    /// das fertige Bild.
    ///
    /// Der heikle Teil ist, dass beide danach DIESELBEN Werkzeugobjekte halten
    /// muessen. Der Streifen legt beim Laden an, was fehlt, und traegt es in seinen
    /// eigenen Stapel ein; wer das nicht zurueckschreibt, verliert ein frisch
    /// angelegtes Werkzeug beim naechsten Umschalten. Und andersherum darf der
    /// Streifen nicht einfach seinen Stapel weiterreichen - beim naechsten Laden
    /// leert er ihn, und die Ebene stuende ohne da.
    /// </summary>
    private void Bind(ImageLayer? layer)
    {
        // Im Knotenmodus gehoert der Streifen dem gewaehlten Knoten - auch dann, wenn
        // der Ebenenstreifen beim Oeffnen einer Datei eine Ebene waehlt.
        if (InNodes)
        {
            _editing = null;
            ShowNodeSettings();
            return;
        }

        Tools.LeaveNodes();

        _editing = layer;
        ShowPlacement();

        if (layer is null)
        {
            Tools.Load(_settings.Adjustments, _settings.Grading);
            _settings.Grading = Snapshot();
            _settings.Adjustments = Tools.Adjustments;

            _finalAdjustments = Tools.Adjustments;
            _finalGrading = Tools.Prepared;

            ShowTools();
            return;
        }

        Tools.Load(layer.Adjustments, layer.Tools);
        layer.Tools = Snapshot();
        layer.Adjustments = Tools.Adjustments;

        ShowTools();
    }

    /// <summary>Sagt dem Farbstreifen, worauf er gerade wirkt.</summary>
    private void ShowTools()
    {
        var (name, onIt, locked) = Layers.TargetState;

        Tools.ShowTarget(name, onIt, locked);
    }

    /// <summary>
    /// Im Farbstreifen wurde ein anderes Ziel gewaehlt.
    ///
    /// Neu gebunden und neu gerechnet: Was vorher am Bild stand, bleibt am Bild, und
    /// was an der Ebene steht, kommt jetzt zum Vorschein. Die Werte wandern NICHT mit
    /// - sonst haette ein Umschalten still eine Korrektur verschoben.
    /// </summary>
    private void OnTargetChosen(bool onLayer)
    {
        Layers.OnLayer = onLayer;

        Bind(Layers.EditedLayer);
        Refresh(interim: false, recompose: true);
    }

    /// <summary>
    /// Ein eigener Stapel mit denselben Werkzeugen, die der Streifen gerade haelt.
    ///
    /// Die Liste ist neu, die Werkzeuge darin sind dieselben Objekte - wer am Regler
    /// zieht, aendert sie fuer beide. Genau das ist gewollt; nur der Behaelter darf
    /// nicht geteilt sein.
    /// </summary>
    /// <summary>
    /// Der Stapel, wie er gerade steht - VOLLSTAENDIG.
    ///
    /// Er zaehlte frueher zwei seiner sechs Listen auf, und das war ein Fehler mit
    /// langem Nachhall: Optik, Geometrie, Renderdaten und die Durchgaenge ueber den
    /// Rahmen fielen weg. Weil diese Aufnahme sofort in die Einstellungen
    /// zurueckgeschrieben wird und der naechste Ladevorgang daraus liest, wischte
    /// jeder Wechsel der gewaehlten Ebene die Vignette, das Korn, das Raster, die
    /// Verzeichnung, die Tiefenschaerfe und die Bewegungsunschaerfe wieder aus.
    ///
    /// Im Fenster sah das aus, als taete der Regler nichts - er tat etwas, und kurz
    /// darauf nahm es ihm jemand wieder ab.
    ///
    /// Jetzt wird kopiert statt aufgezaehlt. Eine Aufzaehlung ist eine Liste, die man
    /// pflegen muss; sie war schon beim Schreiben unvollstaendig und wurde es mit
    /// jeder neuen Passart mehr.
    /// </summary>
    private GradingStack Snapshot() => Tools.Stack.Clone();

    private void OnToolsChanged(bool interim)
    {
        if (InNodes)
        {
            OnNodeSettingsChanged(interim);
            return;
        }

        bool layer = _editing is not null;

        if (layer)
        {
            _editing!.Adjustments = Tools.Adjustments;

            // Und der Stapel dazu - genau wie beim ganzen Bild eine Zeile tiefer.
            //
            // Hier stand frueher, der Streifen habe die Werkzeuge beim Binden
            // "hineingelegt und aendere dasselbe Objekt weiter". Das war falsch:
            // Snapshot() ist ein Clone, und GradingStack.Clone() kopiert auch die
            // Werkzeuge selbst. Die Ebene bekam beim Binden EIGENE Objekte, und was
            // danach am Regler gezogen wurde, erreichte sie nie mehr.
            //
            // Sichtbar war das auf die verwirrendste Art, die es gibt: Saettigung und
            // Belichtung wirkten, Farbbereiche und Zonen nicht. Die einen stehen in
            // Adjustments - das wurde geschrieben -, die anderen im Stapel.
            _editing.Tools = Snapshot();

            _settings.Layers = Layers.Stack;
        }
        else
        {
            _settings.Adjustments = Tools.Adjustments;

            // Und der Stapel dazu. Ihn hier zu vergessen war die zweite Haelfte eines
            // langen Fehlers: Die Aenderung kam im BILD an - _finalGrading steht ja
            // gleich darunter -, aber nicht in den Einstellungen. Sobald die Auswahl
            // auf eine Ebene wechselte, las der Streifen deren Werkzeuge ein und
            // leerte dabei seine Listen; beim Zurueckwechseln stellte er den Stand aus
            // den Einstellungen wieder her - also den von VOR der Aenderung.
            //
            // Im Fenster sah das aus, als taete der Regler nichts. Er tat etwas, und
            // der naechste Klick woanders nahm es ihm wieder ab.
            _settings.Grading = Snapshot();

            _finalAdjustments = Tools.Adjustments;
            _finalGrading = Tools.Prepared;
        }

        // Ein Werkzeug mit Renderdaten kann einen Pass verlangen, den noch niemand
        // gelesen hat - und dann tut es nichts, ohne das zu sagen.
        //
        // Das war ein alter Fehler und nicht nur einer der Verschiebung: Gelesen
        // wurde bisher NUR ueber den Ebenenstreifen. Wer die Tiefenschaerfe
        // aufdrehte, nachdem die Datei offen war, bekam sie deshalb erst zu sehen,
        // wenn er nebenbei eine Ebene anfasste - vorher lag der Tiefenpass nicht im
        // Vorrat, und ein Werkzeug ohne seinen Pass ruht.
        //
        // Geprueft wird erst beim Loslassen: Waehrend des Zuges darf keine Datei im
        // Weg liegen, und die Frage kostet einen Durchgang durch die Werkzeugliste.
        if (!interim && !layer && DataPasses().Any(name => !_sources.ContainsKey(name)))
        {
            OnLayersChanged(interim: false);
            return;
        }

        // Eine Einstellungsebene sitzt IM Stapel - was sie aendert, aendert das
        // zusammengesetzte Bild und nicht erst die Korrektur am Ende.
        Refresh(interim, recompose: layer);
    }

    /// <summary>
    /// Zeichnet neu, und wenn noetig setzt es vorher zusammen.
    ///
    /// Die Reihenfolge ist der ganze Inhalt dieser Methode, und sie ist nicht
    /// beliebig: ERST steht fest, wie grob gerechnet wird, DANN wird zusammengesetzt,
    /// DANN gezeichnet. Beim Ziehen rechnet der Composer nur die Gitterpunkte, die
    /// die Anzeige danach liest - stuende die Schrittweite noch auf dem Wert des
    /// vorigen Schritts, bliebe beim Loslassen ein grob zusammengesetztes Bild
    /// stehen, das der volle Durchgang dann als fertig zeichnet.
    ///
    /// Das faellt beim Ausprobieren kaum auf: Man sieht ein Bild, das nach dem
    /// Loslassen etwas zu weich aussieht, und haelt es fuer die Vorschau.
    /// </summary>
    private void Refresh(bool interim, bool recompose)
    {
        if (interim)
        {
            _coarse = true;
            _settle.Stop();
            _settle.Start();

            // Je BILD rechnen, nicht je Meldung - und deshalb steht das hier und
            // nicht an den Reglern.
            //
            // Windows liefert Regler- und Mausmeldungen so schnell, wie das Programm
            // sie abholt. Wer in jeder davon ein Bild zusammensetzt, staut die
            // Warteschlange, sobald ein Durchgang laenger dauert als der Abstand
            // zweier Meldungen. Der Regler zieht dann nicht nur nach, er ZUCKT: Der
            // Griff steht an der Stelle einer alten Meldung, waehrend die Maus schon
            // woanders ist, und springt beim naechsten Durchgang vor.
            //
            // Einmal hier gefasst gilt es fuer alles, was zwischendurch meldet -
            // jeden Regler, jedes Ziehen, jeden Pinselstrich. An den Reglern selbst
            // gaebe es das nur dort, wo jemand daran gedacht hat.
            _pendingCompose |= recompose;

            if (_frameHooked) return;

            _frameHooked = true;
            CompositionTarget.Rendering += OnRefreshFrame;

            return;
        }

        StopRefreshFrames();

        _settle.Stop();
        _coarse = false;

        Draw(recompose);
        Measure();
    }

    /// <summary>Ein Durchgang je Bildwiederholung - mehr sieht ohnehin niemand.</summary>
    private void OnRefreshFrame(object? sender, EventArgs e)
    {
        bool recompose = _pendingCompose;

        // ERST abhaengen: Was jetzt gezeichnet wird, darf sich fuer das naechste Bild
        // sofort wieder anmelden. Andersherum bliebe der Haken haengen, wenn niemand
        // mehr etwas will, und liefe sechzig Mal in der Sekunde ins Leere.
        StopRefreshFrames();

        Draw(recompose);
    }

    private void StopRefreshFrames()
    {
        _pendingCompose = false;

        if (!_frameHooked) return;

        _frameHooked = false;
        CompositionTarget.Rendering -= OnRefreshFrame;
    }

    /// <summary>Zusammensetzen, zeichnen, die Anfasser nachziehen - in dieser Reihenfolge.</summary>
    private void Draw(bool recompose)
    {
        if (recompose) Recompose();

        Render();
        ShowPlacement();
    }

    private bool _frameHooked;
    private bool _pendingCompose;

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

            // Im Knotenmodus rechnet der Graph - ausser beim Vergleich mit dem
            // Original, das ist in beiden Modi das Bild der Datei ohne alles.
            if (InNodes && !_showingOriginal)
            {
                // Ergibt der Graph kein Bild - die Ausgabe haengt an nichts -, bleibt die
                // Flaeche leer. Auf den Stapel zurueckzufallen hiesse, ein Bild zu zeigen,
                // das niemand mehr eingestellt hat; der Editor sagt, was fehlt.
                bool done = RenderNodes(_surface.BackBuffer, _surface.BackBufferStride);

                if (!done)
                {
                    unsafe
                    {
                        new Span<byte>((void*)_surface.BackBuffer,
                                       _surface.BackBufferStride * _surface.PixelHeight).Clear();
                    }
                }

                // Ein ganzes, scharfes Bild der Ausgabe - darauf darf der Pinsel Ausschnitte setzen.
                _wholeShown = done && !_coarse && _viewer is null;

                _surface.AddDirtyRect(new Int32Rect(0, 0, frame.Width, frame.Height));
                return;
            }

            _wholeShown = false;

            FloatFrameProcessor.Apply(frame, adjustments, ViewFor(frame), grading,
                                      _surface.BackBuffer, _surface.BackBufferStride,
                                      _coarse ? CoarseStep : 1,
                                      _showingOriginal ? Overlays.None : _overlays, _number,
                                      Renderdata(grading));

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

        if (InNodes)
        {
            MeasureNodes();
            return;
        }

        var histogram = new Histogram();

        // Jedes vierte Pixel in beiden Richtungen: ein Sechzehntel der Arbeit, und
        // die Verteilung stimmt trotzdem.
        // Gemessen wird das FERTIGE Bild, nicht das, was der Streifen gerade zeigt.
        // Ein Histogramm, das sich beim Anklicken einer Ebene aendert, beantwortet
        // eine Frage, die niemand gestellt hat.
        FloatFrameProcessor.Measure(frame, _finalAdjustments, ViewFor(frame), _finalGrading,
                                    histogram, step: 4, _number);

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
