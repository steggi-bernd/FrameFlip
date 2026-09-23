using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Der Ebenenstapel als Bedienelement.
///
/// Oben die Liste, darunter die Regler der gewaehlten Ebene - dieselbe Anordnung
/// wie in jedem Bildprogramm, und aus demselben Grund: Erst die Ebene, dann was
/// damit geschieht. Umgekehrt stellte man etwas ein und suchte danach, woran.
///
/// Die Liste zeigt oben, was im Bild oben liegt. Der Stapel selbst laeuft
/// andersherum - Index 0 ist unten, weil von unten nach oben gerechnet wird. Die
/// Umkehrung steht hier und nicht im Modell: eine rueckwaerts gelesene Schleife im
/// Rechenweg waere eine Fehlerquelle, eine rueckwaerts gefuellte Liste in der
/// Oberflaeche ist keine.
/// </summary>
public partial class LayerPanel : UserControl
{
    private IReadOnlyList<ExrPass> _passes = Array.Empty<ExrPass>();
    private ImageLayer? _selected;
    private bool _filling;

    /// <summary>
    /// Die vollen Passnamen hinter den Eintraegen in <c>MaskSourceBox</c>.
    ///
    /// Angezeigt wird der Kurzname, gespeichert der volle - in einer 300 Punkte
    /// breiten Spalte stuende sonst vierzehnmal "ViewLayer." davor.
    /// </summary>
    private readonly List<string> _maskSources = new();

    /// <summary>Die Kryptomatten, die die Datei fuehrt. Meist zwei: Objekt und Material.</summary>
    private IReadOnlyList<CryptomatteSet> _cryptomattes = Array.Empty<CryptomatteSet>();

    /// <summary>
    /// Die Maskenarten in der Reihenfolge der Auswahl.
    ///
    /// Eine Tabelle statt der blossen Aufzaehlung, weil nicht jede Art schon
    /// bedienbar ist: Die Kryptomatte steht im Modell, hat aber noch keine Auswahl
    /// von Objekten - sie hier anzubieten hiesse, einen Eintrag zu zeigen, der nichts
    /// tut.
    /// </summary>
    private static readonly (MaskKind Kind, string Key)[] AllMaskKinds =
    {
        (MaskKind.None, "S_MaskNone"),
        (MaskKind.Luminance, "S_MaskLuminance"),
        (MaskKind.Underlying, "S_MaskUnderlying"),
        (MaskKind.Pass, "S_MaskPass"),
        (MaskKind.Colour, "S_MaskColour"),
        (MaskKind.Gradient, "S_MaskGradient"),
        (MaskKind.Cryptomatte, "S_MaskCryptomatte"),
        (MaskKind.Painted, "S_MaskPainted"),
    };

    /// <summary>
    /// Die Arten, die diese Datei hergibt. Die Kryptomatte faellt heraus, wenn keine
    /// in der Datei steht - ein Eintrag, der nach einer Auswahl verlangt, die es
    /// nicht gibt, ist schlimmer als keiner.
    /// </summary>
    private (MaskKind Kind, string Key)[] MaskKinds
        => _cryptomattes.Count > 0
            ? AllMaskKinds
            : AllMaskKinds.Where(m => m.Kind != MaskKind.Cryptomatte).ToArray();

    /// <summary>
    /// Wie weit der Rand des Farbrades traegt. 0,5 heisst: ein Kanal reicht von der
    /// Haelfte bis zum Anderthalbfachen - genug fuer eine deutliche Einfaerbung und
    /// wenig genug, dass ein Kanal nie auf null faellt.
    /// </summary>
    private const float TintScale = 0.5f;

    public LayerPanel()
    {
        InitializeComponent();

        foreach (var (_, key) in Blending.All) ModeBox.Items.Add(Strings.T(key));
        ModeBox.SelectedIndex = 0;

        FillMaskKinds();

        TintWheel.Changed += OnTintChanged;
        TintWheel.Released += () => Raise(interim: false);

        Rebuild();
    }

    /// <summary>
    /// Etwas hat sich geaendert. <c>interim</c> heisst: es wird gerade gezogen, eine
    /// grobe Vorschau reicht.
    /// </summary>
    public event Action<bool>? Changed;

    /// <summary>Der Stapel. Wird an Ort und Stelle veraendert, nie ersetzt.</summary>
    public LayerStack Stack { get; } = new();

    /// <summary>
    /// Woher die Miniaturen kommen. Null heisst: keine.
    ///
    /// Der Streifen kennt keine Bilddaten - er kennt Namen. Wer ihn einhaengt, weiss,
    /// welcher Pass gerade gelesen ist, und liefert dazu ein kleines Bild. So bleibt
    /// der Streifen frei von Dateien und der Seite die Hoheit ueber den Speicher.
    /// </summary>
    public Func<ImageLayer, System.Windows.Media.ImageSource?>? Thumbnail { get; set; }

    /// <summary>
    /// Quellen, die sich nicht lesen liessen - gesetzt von der Seite.
    ///
    /// Eine Datei, die es gibt und die sich trotzdem nicht oeffnen laesst, war bisher
    /// nicht von einer Ebene zu unterscheiden, die nichts beitraegt: Der Leseversuch
    /// gab null zurueck, und niemand sagte etwas. Im Bild sah das aus wie "das
    /// Einblenden tut nichts", und danach sucht man an der falschen Stelle.
    /// </summary>
    public IReadOnlyCollection<string> Unreadable { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Eine andere Ebene ist gewaehlt. Null heisst: keine Einstellungsebene, die
    /// Werkzeuge gehoeren wieder dem ganzen Bild.
    ///
    /// Der Streifen entscheidet nicht, was damit geschieht - er sagt nur, worauf die
    /// Wahl gefallen ist. Was die Werkzeuge daraufhin bearbeiten, ist Sache dessen,
    /// der beide einhaengt.
    /// </summary>
    public event Action<ImageLayer?>? Editing;

    /// <summary>
    /// Ob die Farbwerkzeuge der GEWAEHLTEN EBENE gelten statt dem ganzen Bild.
    ///
    /// Eine Einstellungsebene besteht aus ihrer Korrektur - bei ihr gilt es immer.
    /// Bei allen anderen ist es eine Wahl, und sie wird im Farbstreifen getroffen.
    /// </summary>
    public bool OnLayer { get; set; }

    /// <summary>
    /// Die Ebene, der die Farbwerkzeuge gerade gehoeren. Null heisst: das ganze Bild.
    ///
    /// Dass hier lange nur Einstellungsebenen standen, war der Grund, warum eine
    /// Maske auf einem Bild nichts bewirkte: Eine Bildebene konnte gar keine eigenen
    /// Werkzeuge bekommen. Man stellte etwas ein, es landete in der Korrektur des
    /// ganzen Bildes - die laeuft NACH dem Zusammensetzen -, und keine Maske der Welt
    /// haette sie noch begrenzen koennen.
    /// </summary>
    public ImageLayer? EditedLayer
        => _selected is not null && (OnLayer || _selected.Content == LayerContent.Adjustment)
            ? _selected
            : null;

    /// <summary>Was der Farbstreifen ueber sein Ziel wissen muss.</summary>
    public (string? Layer, bool OnIt, bool Locked) TargetState
        => (_selected?.Name,
            EditedLayer is not null,
            _selected?.Content == LayerContent.Adjustment);

    /// <summary>
    /// Die gewaehlte Ebene, gleich welcher Art. Null, wenn keine gewaehlt ist.
    ///
    /// Gebraucht vom Greifrahmen im Bild: Er zeigt, was hier ausgewaehlt ist, und
    /// eine zweite Auswahl daneben waere eine, die auseinanderlaufen kann.
    /// </summary>
    public ImageLayer? Selection => _selected;

    /// <summary>
    /// Die Platzierung wurde von aussen veraendert - vom Greifrahmen im Bild.
    ///
    /// Die Regler muessen dann nachziehen: Wer im Bild schiebt und danach am Regler
    /// weiterdreht, spraenge sonst zurueck auf den Wert, der dort noch steht.
    /// </summary>
    public void PlaceMovedOutside(bool interim)
    {
        _filling = true;

        try { PushPlaceToControls(); }
        finally { _filling = false; }

        Raise(interim);
    }

    /// <summary>
    /// Nimmt eine Datei entgegen: welche Passe sie fuehrt, und welcher Stapel
    /// darauf gelten soll.
    ///
    /// Ein leerer Stapel bekommt eine Ebene auf dem Hauptbild. Das ist dieselbe
    /// Ansicht wie ohne Ebenen ueberhaupt - nur steht sie jetzt in der Liste, und
    /// damit ist zu sehen, dass sich daran etwas machen laesst.
    /// </summary>
    public void Load(IReadOnlyList<ExrPass> passes, LayerStack? stack)
        => Load(passes, Array.Empty<CryptomatteSet>(), stack);

    /// <inheritdoc cref="Load(IReadOnlyList{ExrPass}, LayerStack?)"/>
    /// <param name="cryptomattes">
    /// Was die Datei an Kryptomatten fuehrt. Leer heisst: keine - dann steht die Art
    /// gar nicht erst zur Wahl.
    /// </param>
    public void Load(IReadOnlyList<ExrPass> passes, IReadOnlyList<CryptomatteSet> cryptomattes,
                     LayerStack? stack)
    {
        _passes = passes;
        _cryptomattes = cryptomattes;

        FillMaskKinds();
        FillMaskSources();

        Stack.Layers.Clear();
        if (stack is not null) Stack.Layers.AddRange(stack.Layers.Select(l => l.Clone()));

        if (Stack.Layers.Count == 0) Stack.Layers.Add(BaseLayer());

        _selected = Stack.Layers[^1];
        Rebuild();

        // Eingeklappt, wenn es nichts zu waehlen und nichts zu sehen gibt: Bei einem
        // PNG ist die Liste eine Zeile lang, und zweihundert Punkte Hoehe dafuer
        // waeren im schmalen Streifen der teuerste Platz, den es gibt. Die
        // Ueberschrift bleibt stehen - wer schichten will, findet sie.
        // Immer offen. Frueher klappte der Streifen bei einer einzigen Ebene zu, um
        // der Farbe keinen Platz zu nehmen - jetzt steht er in einem eigenen Reiter
        // und nimmt niemandem etwas. Zugeklappt liesse er den Reiter leer.
        Fold(open: true);

        Editing?.Invoke(EditedLayer);
    }

    private void Fold(bool open)
    {
        Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FoldButton.Content = open ? "−" : "+";
    }

    /// <summary>
    /// Ob die Datei mehrere Passe zur Wahl stellt.
    ///
    /// Das ist NICHT die Frage, ob sich schichten laesst - die stellt sich nicht.
    /// Ein PNG fuehrt genau ein Bild, aber dasselbe Bild ein zweites Mal und auf
    /// Multiplizieren gestellt ist der Griff, mit dem in Photoshop jeder Kontrast
    /// anfaengt, und der rechnet hier genauso.
    ///
    /// Die Antwort entscheidet nur darueber, ob der Streifen aufgeklappt beginnt.
    /// </summary>
    public bool HasChoice => _passes.Count > 1;

    /// <summary>
    /// Die unterste Ebene: das Bild, wie die Datei es ohnehin hergibt.
    ///
    /// Auf Normal und nicht auf Add, weil sie auf Schwarz liegt und beides dort
    /// dasselbe ergibt - aber wer sie ansieht, soll "das Bild" lesen und nicht
    /// "das Bild, addiert zu etwas".
    /// </summary>
    private static ImageLayer BaseLayer() => new()
    {
        Source = "",
        Name = Strings.T("S_LayerColour"),
        Mode = BlendMode.Normal,
    };

    // ------------------------------------------------------------------- die Liste

    /// <summary>
    /// Zeichnet die Zeilen neu, weil jetzt Miniaturen da sind.
    ///
    /// Die Passe und Bilder einer Ebene werden im Hintergrund gelesen; die Zeile
    /// steht schon, waehrend sie eintreffen. Wer danach nichts mehr anfasst, hat
    /// eine Ebene ohne Bildchen - und das sieht aus, als waere die Datei nicht
    /// angekommen, obwohl sie im Bild steht.
    /// </summary>
    public void ShowThumbnails() => Rebuild();

    private void Rebuild()
    {
        _filling = true;

        try
        {
            LayerList.Items.Clear();
            AddRows(Stack.Layers, depth: 0);

            if (_selected is not null && Owner(_selected) is null)
                _selected = Stack.Layers.Count > 0 ? Stack.Layers[^1] : null;

            foreach (ListBoxItem item in LayerList.Items)
                if (ReferenceEquals(item.Tag, _selected)) item.IsSelected = true;
        }
        finally
        {
            _filling = false;
        }

        PushToControls();
        UpdateButtons();
    }

    /// <summary>
    /// Fuellt die Liste - rueckwaerts, weil oben in der Liste oben im Bild ist, und
    /// eine Gruppe vor ihren Kindern, weil sie sie zusammenhaelt.
    /// </summary>
    private void AddRows(List<ImageLayer> layers, int depth)
    {
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var layer = layers[i];
            LayerList.Items.Add(Row(layer, depth));

            if (layer.Content == LayerContent.Group) AddRows(layer.Children, depth + 1);
        }
    }

    /// <summary>
    /// Die Liste, in der diese Ebene steht - der Stapel selbst oder die Kinder einer
    /// Gruppe. Null, wenn sie nirgends mehr steht.
    /// </summary>
    private List<ImageLayer>? Owner(ImageLayer layer, List<ImageLayer>? within = null)
    {
        within ??= Stack.Layers;
        if (within.Contains(layer)) return within;

        foreach (var candidate in within)
        {
            if (candidate.Content != LayerContent.Group) continue;

            var found = Owner(layer, candidate.Children);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>Die Gruppe, in der diese Liste steckt. Null auf oberster Ebene.</summary>
    private ImageLayer? GroupOf(List<ImageLayer> children)
        => ReferenceEquals(children, Stack.Layers)
            ? null
            : Stack.All().FirstOrDefault(l => ReferenceEquals(l.Children, children));

    /// <summary>
    /// Das Zeichen vor dem Namen. Eine Liste aus Passen, Korrekturen, Bildern und
    /// Gruppen ist ohne sie nicht zu lesen - man suchte bei einer Korrektur nach
    /// ihrem Pass.
    /// </summary>
    private static string Marker(ImageLayer layer) => layer.Content switch
    {
        LayerContent.Adjustment => "≡ ",
        LayerContent.Group => "▼ ",
        LayerContent.Image => "▣ ",
        _ => "",
    };

    /// <summary>Der Dateiname einer Bildebene - der ganze Pfad passt nicht in die Spalte.</summary>
    private static string Short(string source)
    {
        if (source.Length == 0) return "";

        try { return System.IO.Path.GetFileName(source) is { Length: > 0 } name ? name : source; }
        catch (ArgumentException) { return source; }
    }

    private ListBoxItem Row(ImageLayer layer, int depth)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Punkt
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Miniatur
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var eye = new ToggleButton
        {
            Style = (Style)FindResource("LayerEye"),
            IsChecked = layer.Visible,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = layer,
        };

        eye.Click += OnVisibilityClicked;
        Grid.SetColumn(eye, 0);
        grid.Children.Add(eye);

        // Eine Miniatur sagt in einem Blick, was ein Name nicht sagt: ob der Pass
        // ueberhaupt etwas enthaelt. Ein leerer Glanzpass sieht schwarz aus, und das
        // ist eine Antwort - "GlossDir" ist keine.
        var preview = Thumbnail?.Invoke(layer);

        if (preview is not null)
        {
            // Groesser als frueher, und auf Karo.
            //
            // Dreissig mal achtzehn Punkte reichten, um zu sehen, ob ein Pass
            // ueberhaupt etwas enthaelt - mehr nicht. Eine Ebene wiederzuerkennen
            // braucht mehr Flaeche, und das Karo dahinter beantwortet die Frage, die
            // man an eine Miniatur in einer Liste aus Freistellungen hat: Auf einem
            // einfarbigen Grund ist "durchsichtig" von "genau dieser Farbe" nicht zu
            // unterscheiden.
            var thumb = new Border
            {
                Width = 64,
                Height = 38,
                Background = (System.Windows.Media.Brush)FindResource("CheckerBrush"),
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                BorderBrush = (System.Windows.Media.Brush)FindResource("PanelBorder"),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = layer.Visible ? 1.0 : 0.4,
                Child = new System.Windows.Controls.Image
                {
                    Source = preview,

                    // Ganz hinein statt fuellend: Eine Ebene mit anderem
                    // Seitenverhaeltnis soll als solche zu erkennen sein, und
                    // angeschnitten sieht jede Miniatur gleich aus.
                    Stretch = System.Windows.Media.Stretch.Uniform,
                },
            };

            Grid.SetColumn(thumb, 1);
            grid.Children.Add(thumb);
        }

        bool missing = layer.Content switch
        {
            LayerContent.Pass => layer.Source.Length > 0 &&
                                 ExrPasses.Find(_passes, layer.Source) is null,
            LayerContent.Image => layer.Source.Length == 0 ||
                                  !System.IO.File.Exists(layer.Source) ||
                                  Unreadable.Contains(layer.Source),
            _ => false,
        };

        // Eine angeschnittene Ebene rueckt ein und bekommt einen Pfeil davor -
        // dieselbe Schreibweise wie in Photoshop, und sie sagt in einem Zeichen,
        // was sonst ein Satz waere: "das hier gilt nur fuer die Zeile darunter".
        bool clipped = layer.Clipped && (Owner(layer)?.IndexOf(layer) ?? 0) > 0;

        var name = new TextBlock
        {
            // Eingerueckt nach Tiefe: Wer in einer Gruppe steckt, steht weiter
            // rechts. Ohne das waere eine Gruppe nur eine Zeile mehr, und niemand
            // saehe, was zu ihr gehoert.
            Margin = new Thickness(depth * 11 + (clipped ? 12 : 0), 0, 0, 0),
            // Ein Zeichen vor dem Namen: Eine Korrektur bringt kein Bild mit, und in
            // einer Liste aus Passen muss das auf den ersten Blick zu sehen sein -
            // sonst sucht man ihren Pass.
            Text = (clipped ? "↳ " : "") + Marker(layer) +
                   (layer.Name.Length > 0 ? layer.Name : Short(layer.Source)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = layer.Visible ? 1.0 : 0.45,
            Foreground = (System.Windows.Media.Brush)FindResource(missing ? "GapBrush" : "ForegroundBrush"),
            ToolTip = missing
                ? layer.Source + " — " + Strings.T(layer.Content == LayerContent.Image
                                                   ? "S_ImageMissing" : "S_PassMissing")
                : layer.Source,
        };

        Grid.SetColumn(name, 2);
        grid.Children.Add(name);

        // Die Mischung steht in der Zeile, nicht nur im Auswahlfeld darunter: Ein
        // Stapel, in dem eine Ebene multipliziert und der Rest addiert, erklaert
        // sich damit auf einen Blick.
        // Ein Punkt vor der Mischung, wenn die Ebene maskiert ist. Ohne ihn waere
        // eine Ebene, die nur an einer Stelle wirkt, in der Liste nicht von einer zu
        // unterscheiden, die ueberall wirkt - und man suchte den Grund woanders.
        var mode = new TextBlock
        {
            Text = (layer.Mask.IsNeutral ? "" : "\u25D0 ") +
                   Strings.T(Blending.All.First(m => m.Mode == layer.Mode).Key),
            FontSize = 9,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        };

        Grid.SetColumn(mode, 3);
        grid.Children.Add(mode);

        return new ListBoxItem { Content = grid, Tag = layer };
    }

    /// <summary>
    /// Eine Datei auf die Liste gezogen wird eine Bildebene.
    ///
    /// Der kuerzeste Weg fuer den haeufigsten Fall - ein Wasserzeichen oder eine
    /// zweite Fassung liegt im Explorer, und der Umweg ueber Menue und Dateidialog
    /// ist drei Klicks fuer etwas, das eine Geste ist.
    /// </summary>
    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        // Der eigene Zug zuerst: Eine Zeile, die auf die Liste faellt, ist ein
        // Umsortieren und keine Datei.
        if (e.Data.GetDataPresent(RowFormat))
        {
            DropRow(e);
            e.Handled = true;

            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        e.Handled = true;

        foreach (string file in files.Where(Readable)) AddImage(file);
    }

    private void OnFilesDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(RowFormat))
        {
            e.Effects = ShowDropLine(e) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;

            return;
        }

        bool welcome = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(Readable);

        e.Effects = welcome ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Ob FrameFlip diese Datei als Bild lesen kann.
    ///
    /// Nach der Endung und nicht nach dem Inhalt: Beim Ueberfahren muss die Antwort
    /// sofort da sein, und eine Datei zu oeffnen, waehrend die Maus darueber
    /// schwebt, ist das nicht.
    /// </summary>
    private static bool Readable(string path)
    {
        string extension = System.IO.Path.GetExtension(path);

        return extension.ToLowerInvariant() is ".exr" or ".png" or ".jpg" or ".jpeg"
                                            or ".tif" or ".tiff" or ".bmp" or ".webp";
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        if (LayerList.SelectedItem is not ListBoxItem item || item.Tag is not ImageLayer layer) return;

        _selected = layer;
        PushToControls();
        UpdateButtons();
        Editing?.Invoke(EditedLayer);
    }

    /// <summary>
    /// Die Sperre der gemalten Maske wurde umgelegt.
    ///
    /// Beim Entsperren wird der bisherige Anstrich dem AKTUELLEN Bild zugeschlagen,
    /// statt ihn wegzuwerfen: Wer entsperrt, will meistens von dem weitermalen, was
    /// schon da ist, und nicht bei null anfangen.
    /// </summary>
    private void OnMaskLockChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected is null) return;

        var mask = _selected.Mask;
        bool locked = MaskLockButton.IsChecked == true;

        if (mask.PaintLocked && !locked && mask.Paint is { } had)
        {
            mask.PaintFrames[Number] = had;
            mask.Paint = null;
        }
        else if (!mask.PaintLocked && locked)
        {
            mask.Paint = mask.PaintFor(Number)?.Clone();
            mask.PaintFrames.Clear();
        }

        mask.PaintLocked = locked;

        ShowMaskLock();
        Raise(interim: false);
    }

    /// <summary>
    /// Die Bildnummer, fuer die gerade gemalt wird - von der Seite gesetzt.
    ///
    /// Der Streifen kennt keine Dateien und keine Sequenz; er kennt eine Zahl. Das
    /// reicht fuer die Sperre und haelt ihn frei von allem anderen.
    /// </summary>
    public int Number { get; set; }

    /// <summary>Zeigt oder versteckt die Sperre, je nach Maskenart.</summary>
    private void ShowMaskLock()
    {
        bool painted = _selected?.Mask.Kind == MaskKind.Painted;

        MaskLockButton.Visibility = painted ? Visibility.Visible : Visibility.Collapsed;
        MaskLockNote.Visibility = painted ? Visibility.Visible : Visibility.Collapsed;

        if (!painted || _selected is null) return;

        bool locked = _selected.Mask.PaintLocked;

        MaskLockButton.IsChecked = locked;

        MaskLockNote.Text = Strings.T(locked ? "S_MaskLockedNote" : "S_MaskLooseNote");
    }

    private void OnVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not ImageLayer layer) return;

        SetVisible(layer, button.IsChecked == true);
    }

    /// <summary>
    /// Blendet eine Ebene ein oder aus - derselbe Weg, den auch das Auge in der Zeile
    /// geht.
    ///
    /// Oeffentlich, damit es genau EINEN Weg gibt. Wer von aussen stattdessen
    /// layer.Visible setzt und sich selbst eine Meldung ausdenkt, prueft hinterher
    /// seinen eigenen Nachbau und nicht das Programm.
    /// </summary>
    public void SetVisible(ImageLayer layer, bool on)
    {
        layer.Visible = on;

        // Die AUSWAHL bleibt, wo sie war. Hier stand frueher _selected = layer, und
        // das war ein stiller Nebeneffekt mit weitem Ausschlag: Ein Klick aufs Auge
        // waehlte die Zeile mit aus, und damit sprang der Greifrahmen auf eine andere
        // Ebene - man fasste eine an und bewegte eine andere. Seit die Farbwerkzeuge
        // einer Ebene gehoeren koennen, haette derselbe Klick auch noch das Ziel der
        // Regler verschoben.
        //
        // Ein- und Ausblenden ist eine Aussage ueber die Ebene, keine darueber, womit
        // man weiterarbeiten will. Rebuild haelt die Auswahl von sich aus fest,
        // solange die Ebene noch im Stapel steht.

        // Ein neuer Pass kann dadurch gebraucht werden, der noch nicht gelesen ist -
        // deshalb die vollstaendige Meldung und nicht die vorlaeufige.
        Rebuild();
        Raise(interim: false);
    }

    // ------------------------------------------------------------------- Bedienung

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddButton, Placement = PlacementMode.Bottom };

        var adjustment = new MenuItem { Header = Strings.T("S_AddAdjustment") };
        adjustment.Click += (_, _) => AddAdjustment();
        menu.Items.Add(adjustment);

        var image = new MenuItem { Header = Strings.T("S_AddImage") };
        image.Click += (_, _) => AddImage();
        menu.Items.Add(image);

        var group = new MenuItem { Header = Strings.T("S_AddGroup") };
        group.Click += (_, _) => AddGroup();
        menu.Items.Add(group);

        menu.Items.Add(new Separator());

        if (_passes.Count > 1)
        {
            // Ein Eintrag, der den ganzen Stapel aufbaut. Zwanzig Passe einzeln
            // anzuklicken ist die Art Arbeit, die niemand zweimal macht.
            var all = new MenuItem { Header = Strings.T("S_AddAllPasses") };
            all.Click += (_, _) => RebuildFromPasses();
            menu.Items.Add(all);
            menu.Items.Add(new Separator());
        }

        foreach (var pass in _passes)
        {
            var captured = pass;
            var item = new MenuItem { Header = pass.ShortName };
            item.Click += (_, _) => Add(captured);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            // Kein Pass zur Wahl heisst nicht "nichts geht": Dasselbe Bild ein
            // zweites Mal ist der Griff, mit dem in Photoshop jeder Kontrast anfaengt.
            var again = new MenuItem { Header = Strings.T("S_LayerColour") };
            again.Click += (_, _) => Add(null);
            menu.Items.Add(again);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// Legt eine Einstellungsebene an.
    ///
    /// Auf Normal und nicht auf Addieren: Eine Korrektur ERSETZT, was unter ihr
    /// liegt, durch das korrigierte Ergebnis. Auf Addieren kaeme das Bild ein
    /// zweites Mal dazu, und es waere doppelt so hell - richtig gerechnet und
    /// niemals gemeint.
    ///
    /// Oeffentlich aus demselben Grund wie <see cref="RebuildFromPasses"/>: Der
    /// Menuepunkt ist ein Weg hierher und nicht der einzige, und ein Menuepunkt
    /// laesst sich nicht pruefen.
    /// </summary>
    public void AddAdjustment()
    {
        var layer = new ImageLayer
        {
            Content = LayerContent.Adjustment,
            Name = Strings.T("S_AdjustmentLayer"),
            Mode = BlendMode.Normal,
            Adjustments = ImageAdjustments.Neutral,
            Tools = new GradingStack(),
        };

        Stack.Layers.Add(layer);
        _selected = layer;

        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    /// <summary>
    /// Legt eine Bildebene an - nach der Wahl einer Datei.
    ///
    /// Ohne Datei keine Ebene: Eine Bildebene, die auf nichts zeigt, waere eine
    /// Zeile, die nichts tut und nach einer Erklaerung verlangt.
    /// </summary>
    public void AddImage(string? path = null)
    {
        path ??= AskForImage();
        if (path is null) return;

        Place(new ImageLayer
        {
            Content = LayerContent.Image,
            Source = path,
            Name = Short(path),
            Mode = BlendMode.Normal,
        });
    }

    /// <summary>
    /// Legt eine Gruppe an - mit der gewaehlten Ebene darin, wenn es eine gibt.
    ///
    /// Eine leere Gruppe anzulegen und danach Ebenen hineinzuschieben waere zwei
    /// Schritte fuer das, was man ohnehin meint.
    /// </summary>
    public void AddGroup()
    {
        var group = new ImageLayer
        {
            Content = LayerContent.Group,
            Name = Strings.T("S_GroupLayer"),
            Mode = BlendMode.Normal,
        };

        var owner = _selected is null ? null : Owner(_selected);

        if (owner is not null && _selected is not null)
        {
            int at = owner.IndexOf(_selected);
            owner.RemoveAt(at);
            group.Children.Add(_selected);
            owner.Insert(at, group);
        }
        else
        {
            Stack.Layers.Add(group);
        }

        _selected = group;

        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    /// <summary>Haengt eine neue Ebene neben die gewaehlte - in dieselbe Liste.</summary>
    private void Place(ImageLayer layer)
    {
        var owner = _selected is null ? Stack.Layers : Owner(_selected) ?? Stack.Layers;

        if (_selected is not null && owner.Contains(_selected))
            owner.Insert(owner.IndexOf(_selected) + 1, layer);
        else
            owner.Add(layer);

        _selected = layer;

        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    private string? AskForImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("S_PickImage"),
            Filter = "Bilder|*.exr;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.webp|Alle Dateien|*.*",
            CheckFileExists = true,
        };

        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    private void OnPickImageClicked(object sender, RoutedEventArgs e)
    {
        if (_selected?.Content != LayerContent.Image) return;

        string? path = AskForImage();
        if (path is null) return;

        _selected.Source = path;
        _selected.Name = Short(path);

        Rebuild();
        Raise(interim: false);
    }

    private void OnFollowChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected?.Content != LayerContent.Image) return;

        _selected.FollowSequence = FollowButton.IsChecked == true;
        Raise(interim: false);
    }

    private void Add(ExrPass? pass)
    {
        var layer = new ImageLayer
        {
            Source = pass?.Name ?? "",
            Name = pass?.ShortName ?? Strings.T("S_LayerColour"),
            Mode = Stack.Layers.Count == 0 ? BlendMode.Normal : BlendMode.Add,
        };

        Stack.Layers.Add(layer);
        _selected = layer;

        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    /// <summary>
    /// Baut den Stapel, der die Zerlegung wieder zusammensetzt.
    ///
    /// Wie das geht, steht in <see cref="PassStack"/> - es ist nicht "alle
    /// addieren", sondern Licht mal Farbe. Fuehrt die Datei keine Lichtpasse, bleibt
    /// es beim Bild selbst.
    ///
    /// Oeffentlich, weil der Eintrag im Menue nur ein Weg hierher ist und nicht der
    /// einzige: Ein Menuepunkt laesst sich nicht pruefen, dieser Aufruf schon.
    /// </summary>
    public void RebuildFromPasses()
    {
        var built = PassStack.Rebuild(_passes);
        if (built.Layers.Count == 0) built.Layers.Add(BaseLayer());

        Stack.Layers.Clear();
        Stack.Layers.AddRange(built.Layers);

        _selected = Stack.Layers[^1];
        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    private void OnDuplicateClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var copy = _selected.Clone();
        copy.Name = _selected.Name + " ·";

        var owner = Owner(_selected);
        if (owner is null) return;

        owner.Insert(owner.IndexOf(_selected) + 1, copy);
        _selected = copy;

        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        // Die letzte Ebene bleibt stehen. Ein leerer Stapel ergaebe ein schwarzes
        // Bild, und das sieht aus wie ein Fehler statt wie eine Einstellung.
        if (_selected is null || Stack.All().Count() <= 1) return;

        var owner = Owner(_selected);
        if (owner is null) return;

        int at = owner.IndexOf(_selected);
        owner.RemoveAt(at);

        // Eine Gruppe nimmt ihre Kinder mit. Sie stattdessen nach aussen zu setzen
        // waere die freundlichere Geste und die verwirrendere: Man loescht eine
        // Zeile, und es erscheinen vier neue.
        _selected = owner.Count > 0
            ? owner[Math.Clamp(at, 0, owner.Count - 1)]
            : GroupOf(owner) ?? (Stack.Layers.Count > 0 ? Stack.Layers[^1] : null);

        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    /// <summary>
    /// Schaltet die Schnittmaske der gewaehlten Ebene um.
    ///
    /// Die unterste Ebene kann sich an nichts anschneiden - dort tut der Knopf
    /// nichts, und er sagt es auch, indem er gesperrt bleibt.
    /// </summary>
    /// <summary>
    /// Die Schnittmaske: Diese Ebene gilt nur fuer die EINE Zeile darunter.
    ///
    /// Ein Schalter und kein Knopf, weil sie eine Eigenschaft der Ebene ist und kein
    /// Befehl. Ein Knopf, der sie umlegt, ohne zu zeigen, wie sie steht, laesst einen
    /// jedes Mal im Bild nachsehen - und das Bild sagt es nur, wenn die Ebene gerade
    /// etwas tut.
    ///
    /// Der Pfeil zeigt nach UNTEN, weil genau das die Aussage ist: nach unten
    /// gebunden, und zwar an eine einzige Ebene. Dieselbe Schreibweise wie in
    /// Photoshop, und sie sagt in einem Zeichen, was sonst ein Satz waere.
    /// </summary>
    private void OnClipChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected is null) return;
        if (Stack.Layers.IndexOf(_selected) <= 0) return;

        _selected.Clipped = ClipButton.IsChecked == true;

        Rebuild();
        Raise(interim: false);
    }

    private void OnUpClicked(object sender, RoutedEventArgs e) => Move(+1);

    private void OnDownClicked(object sender, RoutedEventArgs e) => Move(-1);

    private void Move(int direction)
    {
        if (_selected is null) return;

        var owner = Owner(_selected);
        if (owner is null) return;

        int at = owner.IndexOf(_selected);
        int to = at + direction;

        // Innerhalb des eigenen Zweigs. An seiner Grenze tut der Knopf nichts - wer
        // die Gruppe wechseln will, benutzt die Pfeile daneben. Zwei klare Griffe
        // statt eines, der raet.
        if (to < 0 || to >= owner.Count) return;

        owner.RemoveAt(at);
        owner.Insert(to, _selected);

        Rebuild();

        // Die Reihenfolge aendert das Bild nur, wenn die Mischungen nicht alle
        // vertauschbar sind - bei Add ist sie es. Trotzdem wird gerechnet: zu
        // pruefen, ob es sich lohnt, kostet mehr Gedanken als der Durchgang.
        Raise(interim: false);
    }

    /// <summary>
    /// Schiebt die gewaehlte Ebene in die Gruppe, die in der Liste UEBER ihr steht.
    ///
    /// Im Stapel ist das der naechsthoehere Platz, in der Liste die Zeile darueber -
    /// die beiden Richtungen sind umgekehrt, und genau daran ist der erste Versuch
    /// gescheitert. Die Liste zaehlt: Man schiebt eine Zeile unter die Ueberschrift,
    /// die man darueber sieht.
    ///
    /// Hineingelegt wird UNTEN in die Gruppe, also dort, wo die Ebene ohnehin schon
    /// stand. So springt beim Einruecken nichts - es rueckt nur ein.
    /// </summary>
    private void OnIndentClicked(object sender, RoutedEventArgs e)
    {
        if (TargetGroup() is not { } group || _selected is null) return;

        Owner(_selected)!.Remove(_selected);
        group.Children.Insert(0, _selected);

        Rebuild();
        Raise(interim: false);
    }

    private void OnOutdentClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var owner = Owner(_selected);
        if (owner is null || ReferenceEquals(owner, Stack.Layers)) return;

        var group = GroupOf(owner);
        if (group is null) return;

        var outer = Owner(group);
        if (outer is null) return;

        owner.Remove(_selected);

        // Direkt unter die Gruppe - der Umkehrschritt zum Einruecken. Darueber
        // gesetzt spraenge die Zeile ueber den ganzen Gruppenblock hinweg, und man
        // suchte sie.
        outer.Insert(outer.IndexOf(group), _selected);

        Rebuild();
        Raise(interim: false);
    }

    /// <summary>Die Gruppe, in die das Einruecken fuehren wuerde. Null, wenn keine da ist.</summary>
    private ImageLayer? TargetGroup()
    {
        if (_selected is null) return null;

        var owner = Owner(_selected);
        if (owner is null) return null;

        int at = owner.IndexOf(_selected);
        if (at < 0 || at + 1 >= owner.Count) return null;

        var above = owner[at + 1];
        return above.Content == LayerContent.Group ? above : null;
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        Stack.Layers.Clear();
        Stack.Layers.Add(BaseLayer());
        _selected = Stack.Layers[0];

        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    private void OnFoldClicked(object sender, RoutedEventArgs e)
    {
        bool open = Body.Visibility != Visibility.Visible;
        Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FoldButton.Content = open ? "−" : "+";
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is null) return;

        _selected.Mode = Blending.All[Math.Clamp(ModeBox.SelectedIndex, 0, Blending.All.Length - 1)].Mode;

        Rebuild();
        Raise(interim: false);
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Wie im Werkzeugstreifen: Beim Einlesen der XAML setzt jeder Regler erst
        // sein Minimum, und die weiter unten stehenden gibt es dann noch nicht.
        if (_filling || !IsLoaded || _selected is null) return;

        _selected.Opacity = (float)OpacitySlider.Value;
        _selected.MatteFloor = (float)MatteSlider.Value;
        _selected.Reveal = (float)RevealSlider.Value;
        _selected.Exposure = (float)LayerExposureSlider.Value;

        UpdateValues();
        Raise(interim: true);
    }

    private void OnTintChanged()
    {
        if (_filling || _selected is null) return;

        var (r, g, b) = ColourWheelMath.ToChannels(TintWheel.Value, 0f, 1f, TintScale);
        _selected.Tint.R = r;
        _selected.Tint.G = g;
        _selected.Tint.B = b;

        Raise(interim: true);
    }

    private void OnSliderReset(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        if (!double.TryParse(slider.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double neutral))
            return;

        e.Handled = true;
        slider.Value = neutral;
    }

    // ------------------------------------------------------------------- Abgleich

    private void PushToControls()
    {
        _filling = true;

        try
        {
            LayerTools.IsEnabled = _selected is not null;
            if (_selected is null) return;

            int mode = Array.FindIndex(Blending.All, m => m.Mode == _selected.Mode);
            ModeBox.SelectedIndex = Math.Max(0, mode);

            OpacitySlider.Value = Math.Clamp(_selected.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            MatteSlider.Value = Math.Clamp(_selected.MatteFloor,
                                           MatteSlider.Minimum, MatteSlider.Maximum);

            RevealSlider.Value = Math.Clamp(_selected.Reveal, 0, RevealSlider.Maximum);
            BlendSpaceButton.IsChecked = _selected.BlendInDisplay;
            LayerExposureSlider.Value = Math.Clamp(_selected.Exposure,
                                                   LayerExposureSlider.Minimum, LayerExposureSlider.Maximum);

            TintWheel.Value = ColourWheelMath.ToPoint(_selected.Tint.R / TintScale,
                                                      _selected.Tint.G / TintScale,
                                                      _selected.Tint.B / TintScale);

            PushMaskToControls();
            PushPlaceToControls();

            if (_selected?.Content == LayerContent.Image) OnTopButton.IsChecked = _selected.OnTop;
        }
        finally
        {
            _filling = false;
        }

        UpdateValues();
        ShowMaskControls();
    }

    private void UpdateValues()
    {
        OpacityValue.Text = $"{OpacitySlider.Value:0.00}";

        // In Stufen von 255 statt in Anteilen: Wer eine Datei befragt, bekommt die
        // Deckung in Stufen genannt, und die beiden Zahlen sollen dieselben sein.
        MatteValue.Text = $"{MatteSlider.Value * 255:0} / 255";
        RevealValue.Text = $"{RevealSlider.Value * 100:0} %";
        LayerExposureValue.Text = LayerExposureSlider.Value == 0
            ? "0"
            : $"{LayerExposureSlider.Value:+0.00;-0.00}";
    }

    private void UpdateButtons()
    {
        var owner = _selected is null ? null : Owner(_selected);
        int at = owner is null || _selected is null ? -1 : owner.IndexOf(_selected);
        int count = owner?.Count ?? 0;

        // Jede Art zeigt, was zu ihr gehoert, und nur das.
        var content = _selected?.Content ?? LayerContent.Pass;

        AdjustmentHint.Visibility = content == LayerContent.Adjustment
            ? Visibility.Visible : Visibility.Collapsed;

        GroupHint.Visibility = content == LayerContent.Group
            ? Visibility.Visible : Visibility.Collapsed;

        ImageBody.Visibility = content == LayerContent.Image
            ? Visibility.Visible : Visibility.Collapsed;

        // Platziert wird, was ein Bild mitbringt. Eine Korrektur hat keine Flaeche,
        // und eine Gruppe erbt die ihrer Kinder.
        bool placeable = content is LayerContent.Pass or LayerContent.Image;

        PlaceHeader.Visibility = placeable ? Visibility.Visible : Visibility.Collapsed;

        // Die Deckung saeubern kann nur, wer eine mitbringt. Bei einem Pass heisst
        // Alpha "hier wurde nichts getroffen" - dort etwas wegzuschneiden loeschte
        // das Umgebungslicht.
        var matte = content == LayerContent.Image ? Visibility.Visible : Visibility.Collapsed;

        ShowMaskLock();

        MatteGroup.Visibility = matte;
        MatteHeader.Visibility = matte;
        MatteSlider.Visibility = matte;
        RevealHeader.Visibility = matte;
        RevealSlider.Visibility = matte;
        PlaceBody.Visibility = placeable && PlaceFoldButton.Content as string == "−"
            ? Visibility.Visible : Visibility.Collapsed;

        if (content == LayerContent.Image && _selected is not null)
        {
            _filling = true;

            try
            {
                ImagePathText.Text = Short(_selected.Source);
                FollowButton.IsChecked = _selected.FollowSequence;
                FollowButton.IsEnabled = SequenceLink.NumberOf(_selected.Source) is not null;
            }
            finally
            {
                _filling = false;
            }
        }

        DuplicateButton.IsEnabled = at >= 0;

        // Die letzte Ebene des GANZEN Stapels bleibt stehen; eine in einer Gruppe
        // darf verschwinden, denn der Stapel bleibt dann trotzdem bewohnt.
        RemoveButton.IsEnabled = at >= 0 && Stack.All().Count() > 1;

        UpButton.IsEnabled = at >= 0 && at < count - 1;
        DownButton.IsEnabled = at > 0;

        ClipButton.IsEnabled = at > 0 && content != LayerContent.Group;

        // Der Zustand kommt aus der Ebene und nicht aus dem Schalter - sonst zeigte
        // er nach einem Wechsel der Auswahl noch den der vorigen.
        bool clipping = ClipButton.IsEnabled && _selected!.Clipped;

        if (ClipButton.IsChecked != clipping)
        {
            bool was = _filling;

            _filling = true;

            try { ClipButton.IsChecked = clipping; }
            finally { _filling = was; }
        }

        IndentButton.IsEnabled = TargetGroup() is not null;
        OutdentButton.IsEnabled = owner is not null && !ReferenceEquals(owner, Stack.Layers);

        // Der Hinweis sagt in beiden Faellen etwas anderes: bei Passen, wie sie
        // zusammengehoeren; bei einem Einzelbild, dass Schichten trotzdem geht.
        HintText.Text = Strings.T(HasChoice ? "S_LayersHint" : "S_SinglePass");
    }

    private void Raise(bool interim) => Changed?.Invoke(interim);

    // ----------------------------------------------------------- Platzierung

    private void OnPlaceFoldClicked(object sender, RoutedEventArgs e)
    {
        bool open = PlaceBody.Visibility != Visibility.Visible;
        PlaceBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PlaceFoldButton.Content = open ? "−" : "+";
    }

    private void OnPlaceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || !IsLoaded || _selected is null) return;

        var place = _selected.Place;

        place.OffsetX = (float)PlaceXSlider.Value;
        place.OffsetY = (float)PlaceYSlider.Value;
        place.Scale = (float)PlaceScaleSlider.Value;
        place.Rotation = (float)PlaceRotationSlider.Value;

        place.CropLeft = (float)CropLeftSlider.Value;
        place.CropTop = (float)CropTopSlider.Value;
        place.CropRight = (float)CropRightSlider.Value;
        place.CropBottom = (float)CropBottomSlider.Value;

        // Gegenueberliegende Anschnitte duerfen sich nicht ueberholen - sonst bleibt
        // nichts uebrig, und die Ebene sieht aus, als waere sie verschwunden.
        Hold(CropLeftSlider, CropRightSlider, sender);
        Hold(CropTopSlider, CropBottomSlider, sender);

        place.CropLeft = (float)CropLeftSlider.Value;
        place.CropTop = (float)CropTopSlider.Value;
        place.CropRight = (float)CropRightSlider.Value;
        place.CropBottom = (float)CropBottomSlider.Value;

        UpdatePlaceValues();
        Raise(interim: true);
    }

    /// <summary>Haelt zwei gegenueberliegende Anschnitte zusammen unter 95 Prozent.</summary>
    private void Hold(Slider first, Slider second, object moved)
    {
        if (first.Value + second.Value <= 0.95) return;

        _filling = true;

        try
        {
            var other = ReferenceEquals(moved, first) ? second : first;
            var mover = ReferenceEquals(moved, first) ? first : second;

            other.Value = Math.Max(0, 0.95 - mover.Value);
        }
        finally
        {
            _filling = false;
        }
    }

    private void OnResetPlaceClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        _selected.Place = new LayerTransform();

        PushToControls();
        Raise(interim: false);
    }

    /// <summary>
    /// Der Schalter fuer den Raum, in dem diese Ebene mischt.
    ///
    /// Er wirkt sofort und vollstaendig - es gibt nichts daran einzustellen, nur
    /// zwei Antworten auf die Frage "ist diese Ebene Licht oder ein Bild".
    /// </summary>
    private void OnBlendSpaceChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected is null) return;

        _selected.BlendInDisplay = BlendSpaceButton.IsChecked == true;
        Raise(interim: false);
    }

    private void OnOnTopChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected?.Content != LayerContent.Image) return;

        _selected.OnTop = OnTopButton.IsChecked == true;

        Rebuild();
        Raise(interim: false);
    }

    private void PushPlaceToControls()
    {
        var place = _selected?.Place ?? new LayerTransform();

        PlaceXSlider.Value = Math.Clamp(place.OffsetX, PlaceXSlider.Minimum, PlaceXSlider.Maximum);
        PlaceYSlider.Value = Math.Clamp(place.OffsetY, PlaceYSlider.Minimum, PlaceYSlider.Maximum);
        PlaceScaleSlider.Value = Math.Clamp(place.Scale, PlaceScaleSlider.Minimum, PlaceScaleSlider.Maximum);

        // Der Regler laeuft von -180 bis 180, die Drehung selbst darf darueber
        // hinausgehen - beim Ziehen im Bild kommt leicht mehr als eine Umdrehung
        // zusammen. Umgerechnet statt beschnitten, sonst spraenge der Griff.
        PlaceRotationSlider.Value = Wrapped(place.Rotation);

        CropLeftSlider.Value = Math.Clamp(place.CropLeft, 0, CropLeftSlider.Maximum);
        CropTopSlider.Value = Math.Clamp(place.CropTop, 0, CropTopSlider.Maximum);
        CropRightSlider.Value = Math.Clamp(place.CropRight, 0, CropRightSlider.Maximum);
        CropBottomSlider.Value = Math.Clamp(place.CropBottom, 0, CropBottomSlider.Maximum);

        UpdatePlaceValues();
    }

    /// <summary>Einen Winkel auf -180 bis 180 bringen.</summary>
    private static double Wrapped(float degrees)
    {
        double value = degrees % 360.0;

        if (value > 180.0) value -= 360.0;
        if (value < -180.0) value += 360.0;

        return value;
    }

    private void UpdatePlaceValues()
    {
        PlaceXValue.Text = $"{PlaceXSlider.Value:+0.00;-0.00;0.00}";
        PlaceYValue.Text = $"{PlaceYSlider.Value:+0.00;-0.00;0.00}";
        PlaceScaleValue.Text = $"{PlaceScaleSlider.Value:0.00}";
        PlaceRotationValue.Text = $"{PlaceRotationSlider.Value:0} \u00B0";
    }

    // --------------------------------------------------------------------- Masken

    private void FillMaskKinds()
    {
        _filling = true;

        try
        {
            MaskBox.Items.Clear();
            foreach (var (_, key) in MaskKinds) MaskBox.Items.Add(Strings.T(key));
            MaskBox.SelectedIndex = 0;
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>
    /// Fuellt die Quellenauswahl - je nach Art mit Passen oder mit Kryptomatten.
    ///
    /// Dasselbe Feld fuer beides, weil es dieselbe Frage ist: "woraus kommt die
    /// Maske?". Zwei Felder uebereinander, von denen immer eines leer waere, kosteten
    /// im schmalen Streifen Platz und erklaerten nichts.
    /// </summary>
    private void FillMaskSources()
    {
        _filling = true;

        try
        {
            MaskSourceBox.Items.Clear();
            _maskSources.Clear();

            if (_selected?.Mask.Kind == MaskKind.Cryptomatte)
            {
                foreach (var set in _cryptomattes)
                {
                    MaskSourceBox.Items.Add(set.ShortName);
                    _maskSources.Add(set.Prefix);
                }

                return;
            }

            foreach (var pass in _passes)
            {
                // Eine Kryptomattenstufe ist kein Bild - als Maske gelesen waere sie
                // ein Hashwert. Sie gehoert in die Kryptomattenauswahl und nicht hier
                // in die Passliste.
                if (Cryptomatte.IsLevel(pass.ShortName)) continue;

                MaskSourceBox.Items.Add(pass.ShortName);
                _maskSources.Add(pass.Name);
            }
        }
        finally
        {
            _filling = false;
        }
    }

    // ----------------------------------------------------------- Kryptomatten

    /// <summary>
    /// Der Anwender moechte ins Bild klicken, um zu waehlen - oder nicht mehr.
    ///
    /// Der Streifen kann das nicht selbst: Er hat kein Bild. Er sagt nur Bescheid,
    /// und wer das Bild zeigt, holt die Kennung und meldet sie zurueck.
    /// </summary>
    public event Action<bool>? PickMode;

    /// <summary>
    /// Die unterste Stufe der Kryptomatte, aus der die Kennung zu lesen ist. Null,
    /// wenn gerade keine Kryptomattenmaske gewaehlt ist.
    /// </summary>
    public string? PickLevel
        => _selected?.Mask is { Kind: MaskKind.Cryptomatte, Levels.Count: > 0 } mask
            ? mask.Levels[0]
            : null;

    /// <summary>Nimmt ein gewaehltes Objekt auf. Ein zweites Mal nimmt es wieder weg.</summary>
    public void AddPick(string name, float id)
    {
        if (_selected is null || _selected.Mask.Kind != MaskKind.Cryptomatte) return;

        var picks = _selected.Mask.Picks;
        var already = picks.FirstOrDefault(p => p.Id.Equals(id));

        // Noch einmal auf dasselbe Objekt zu klicken nimmt es heraus. Das ist der
        // Griff, den man ohnehin versucht, und er erspart das Zielen auf ein
        // Kreuzchen in einer schmalen Liste.
        if (already is not null) picks.Remove(already);
        else picks.Add(new CryptoPick { Name = name, Id = id });

        ShowPicks();
        Rebuild();
        Raise(interim: false);
    }

    private void OnCryptoPickToggled(object sender, RoutedEventArgs e)
    {
        bool on = CryptoPickButton.IsChecked == true;

        CryptoPickButton.Content = Strings.T(on ? "S_CryptoPickOn" : "S_CryptoPick");
        if (!_filling) PickMode?.Invoke(on);
    }

    /// <summary>Beendet den Klickmodus - etwa, wenn ein anderes Bild geoeffnet wird.</summary>
    public void StopPicking()
    {
        if (CryptoPickButton.IsChecked != true) return;

        _filling = true;

        try
        {
            CryptoPickButton.IsChecked = false;
            CryptoPickButton.Content = Strings.T("S_CryptoPick");
        }
        finally
        {
            _filling = false;
        }
    }

    private void ShowPicks()
    {
        CryptoPicks.Items.Clear();

        var picks = _selected?.Mask.Picks ?? new List<CryptoPick>();

        foreach (var pick in picks)
        {
            var row = new Grid { Margin = new Thickness(8, 2, 4, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = pick.Name.Length > 0 ? pick.Name : Strings.T("S_CryptoUnknown"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var drop = new Button
            {
                Style = (Style)FindResource("LinkButton"),
                Content = "\u2715",
                Tag = pick,
            };

            drop.Click += OnDropPickClicked;
            Grid.SetColumn(drop, 1);
            row.Children.Add(drop);

            CryptoPicks.Items.Add(row);
        }

        CryptoEmpty.Visibility = picks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDropPickClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not CryptoPick pick) return;
        if (_selected is null) return;

        _selected.Mask.Picks.Remove(pick);

        ShowPicks();
        Rebuild();
        Raise(interim: false);
    }

    /// <summary>
    /// Setzt eine Kryptomatte als Quelle: ihren Namen und die Stufen, die dazu in
    /// der Datei stehen.
    /// </summary>
    private void UseCryptomatte(LayerMask mask, string prefix)
    {
        mask.Source = prefix;
        mask.Levels = Cryptomatte.Levels(_passes, prefix).ToList();

        // Die Auswahl gilt nur fuer IHREN Satz: Die Kennungen von Objekten und
        // Materialien sind verschiedene Hashes, und eine mitgenommene Auswahl traefe
        // im anderen Satz nichts - eine Maske, die stillschweigend leer ist.
        mask.Picks.Clear();
    }

    private void OnMaskFoldClicked(object sender, RoutedEventArgs e)
    {
        bool open = MaskBody.Visibility != Visibility.Visible;
        MaskBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MaskFoldButton.Content = open ? "\u2212" : "+";
    }

    private void OnMaskKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is null) return;

        var mask = _selected.Mask;
        var kinds = MaskKinds;
        var was = mask.Kind;

        mask.Kind = kinds[Math.Clamp(MaskBox.SelectedIndex, 0, kinds.Length - 1)].Kind;

        // Eine NEU gewaehlte Maske auf einer Bildebene begrenzt die Korrektur, nicht
        // die Sichtbarkeit - dort ist es fast immer das Gemeinte.
        //
        // Nur beim Wechsel von "keine", und nur hier im Streifen: Ein gespeichertes
        // Rezept mit einem Glanz und einer Verlaufsmaske soll nach dem naechsten
        // Start dasselbe tun wie vorher, und das entscheidet die Grundstellung im
        // Modell. Umstellen kann man es daneben mit zwei Knoepfen.
        if (was == MaskKind.None && mask.Kind != MaskKind.None &&
            _selected.Content != LayerContent.Adjustment)
        {
            mask.Scope = MaskScope.Colour;
        }

        // Beim Umschalten gleich die erste Quelle nehmen. Eine Maskenart ohne Quelle
        // waere eine Einstellung, die stillschweigend nichts tut.
        FillMaskSources();

        if (mask.Kind == MaskKind.Cryptomatte)
        {
            if (_cryptomattes.Count > 0 &&
                !_cryptomattes.Any(s => s.Prefix.Equals(mask.Source, StringComparison.Ordinal)))
            {
                UseCryptomatte(mask, _cryptomattes[0].Prefix);
            }
        }
        else if (mask.Kind == MaskKind.Pass && mask.Source.Length == 0 && _maskSources.Count > 0)
        {
            mask.Source = _maskSources[0];
        }

        ShowMaskControls();
        PushMaskToControls();
        Rebuild();
        Raise(interim: false);
    }

    private void OnMaskSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is null) return;

        int at = MaskSourceBox.SelectedIndex;
        if (at < 0 || at >= _maskSources.Count) return;

        if (_selected.Mask.Kind == MaskKind.Cryptomatte)
        {
            UseCryptomatte(_selected.Mask, _maskSources[at]);
            ShowPicks();
        }
        else
        {
            _selected.Mask.Source = _maskSources[at];
        }

        // Ein neuer Pass muss gelesen werden - deshalb die vollstaendige Meldung.
        Raise(interim: false);
    }

    private void OnMaskInvertChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected is null) return;

        _selected.Mask.Invert = MaskInvertButton.IsChecked == true;
        Raise(interim: false);
    }

    /// <summary>Worauf die Maske wirkt - Sichtbarkeit oder Korrektur.</summary>
    private void OnMaskScopeChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || !IsLoaded || _selected is null) return;

        _selected.Mask.Scope = ReferenceEquals(sender, ScopeColourButton)
            ? MaskScope.Colour
            : MaskScope.Visibility;

        Raise(interim: false);
    }

    private void OnMaskSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || !IsLoaded || _selected is null) return;

        var mask = _selected.Mask;

        mask.Low = (float)MaskLowSlider.Value;
        mask.High = (float)MaskHighSlider.Value;
        mask.Softness = (float)MaskSoftSlider.Value;
        mask.Angle = (float)MaskAngleSlider.Value;
        mask.Centre = (float)MaskCentreSlider.Value;
        mask.Width = (float)MaskWidthSlider.Value;
        mask.Hue = (float)MaskHueSlider.Value;
        mask.Spread = (float)MaskSpreadSlider.Value;

        // Von darf Bis nicht ueberholen - sonst laesst die Maske nichts mehr durch,
        // und das sieht aus, als waere die Ebene verschwunden.
        if (mask.Low > mask.High)
        {
            _filling = true;

            try
            {
                if (ReferenceEquals(sender, MaskLowSlider)) MaskHighSlider.Value = mask.Low;
                else MaskLowSlider.Value = mask.High;
            }
            finally
            {
                _filling = false;
            }

            mask.Low = (float)MaskLowSlider.Value;
            mask.High = (float)MaskHighSlider.Value;
        }

        UpdateMaskValues();
        Raise(interim: true);
    }

    /// <summary>Zeigt die Regler, die zur gewaehlten Art gehoeren - und nur die.</summary>
    private void ShowMaskControls()
    {
        var kind = _selected?.Mask.Kind ?? MaskKind.None;

        bool range = kind is MaskKind.Luminance or MaskKind.Underlying or MaskKind.Pass
                          or MaskKind.Cryptomatte or MaskKind.Colour;
        bool gradient = kind == MaskKind.Gradient;
        bool source = kind is MaskKind.Pass or MaskKind.Cryptomatte;
        bool crypto = kind == MaskKind.Cryptomatte;

        MaskCryptoBody.Visibility = crypto ? Visibility.Visible : Visibility.Collapsed;
        if (!crypto) StopPicking();

        MaskRangeBody.Visibility = range ? Visibility.Visible : Visibility.Collapsed;
        MaskColourBody.Visibility = kind == MaskKind.Colour ? Visibility.Visible : Visibility.Collapsed;

        // Die Frage stellt sich nur dort, wo sie zwei Antworten hat. Eine
        // Einstellungsebene BESTEHT aus ihrer Korrektur - bei ihr heisst "wo sie zu
        // sehen ist" und "wo sie korrigiert" dasselbe.
        MaskScopeRow.Visibility = kind != MaskKind.None &&
                                  _selected?.Content != LayerContent.Adjustment
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Tiefen, Mitten und Lichter nur dort, wo sie etwas heissen: an einer
        // Helligkeit. Auf einem Maskenpass oder einem Farbbereich waeren es drei
        // Knoepfe, die etwas anderes tun, als sie sagen.
        MaskZoneRow.Visibility = kind is MaskKind.Luminance or MaskKind.Underlying
            ? Visibility.Visible
            : Visibility.Collapsed;
        MaskGradientBody.Visibility = gradient ? Visibility.Visible : Visibility.Collapsed;
        MaskSourceBox.Visibility = source ? Visibility.Visible : Visibility.Collapsed;
        MaskInvertButton.IsEnabled = kind != MaskKind.None;

        // Dieselben zwei Regler, aber nicht dieselbe Rechnung - und deshalb auch
        // nicht dieselbe Beschriftung. Bei der Helligkeit sind sie ein Fenster: was
        // dazwischen liegt, wirkt. Auf einem Maskenpass sind sie Schwarz- und
        // Weisspunkt: was darunter liegt, faellt weg, was darueber liegt, wirkt voll.
        // Gleich beschriftet waere das eine Falle.
        // Eine Deckung ist wie ein Maskenpass schon ein Anteil - dort sind es
        // Schwarz- und Weisspunkt, nicht ein Fenster.
        // Ein Farbbereich ist wie ein Maskenpass schon ein Anteil: Die Regler sind
        // dort Schwarz- und Weisspunkt und ziehen an, wie blass eine Farbe noch sein
        // darf, um dazuzugehoeren.
        bool levels = kind is MaskKind.Pass or MaskKind.Cryptomatte or MaskKind.Colour;

        MaskLowLabel.Text = Strings.T(levels ? "S_MaskBlack" : "S_MaskFrom");
        MaskHighLabel.Text = Strings.T(levels ? "S_MaskWhite" : "S_MaskTo");

        // Die Weichheit gehoert zum Fenster. Ein Schwarz- und ein Weisspunkt haben
        // ihre Kante schon im Abstand zueinander.
        // Beim Farbbereich zaehlt sie in Grad um den Farbton herum - gebraucht wird
        // sie also auch dort, obwohl die beiden anderen Regler Punkte sind.
        bool soft = !levels || kind == MaskKind.Colour;

        MaskSoftRow.Visibility = soft ? Visibility.Visible : Visibility.Collapsed;
        MaskSoftSlider.Visibility = soft ? Visibility.Visible : Visibility.Collapsed;

        MaskHint.Text = kind switch
        {
            MaskKind.Pass => Strings.T("S_MaskHintPass"),
            MaskKind.Gradient => Strings.T("S_MaskHintGradient"),
            MaskKind.Luminance or MaskKind.Underlying => Strings.T("S_MaskHintLuma"),
            MaskKind.Colour => Strings.T("S_MaskHintColour"),
            MaskKind.Cryptomatte => Strings.T("S_CryptoHint"),
            _ => "",
        };

        MaskHint.Visibility = MaskHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PushMaskToControls()
    {
        var mask = _selected?.Mask ?? new LayerMask();

        int kind = Array.FindIndex(MaskKinds, m => m.Kind == mask.Kind);
        MaskBox.SelectedIndex = Math.Max(0, kind);

        ShowPicks();

        MaskInvertButton.IsChecked = mask.Invert;

        ScopeColourButton.IsChecked = mask.Scope == MaskScope.Colour;
        ScopeShowButton.IsChecked = mask.Scope != MaskScope.Colour;

        MaskLowSlider.Value = Math.Clamp(mask.Low, MaskLowSlider.Minimum, MaskLowSlider.Maximum);
        MaskHighSlider.Value = Math.Clamp(mask.High, MaskHighSlider.Minimum, MaskHighSlider.Maximum);
        MaskSoftSlider.Value = Math.Clamp(mask.Softness, MaskSoftSlider.Minimum, MaskSoftSlider.Maximum);
        MaskAngleSlider.Value = Math.Clamp(mask.Angle, MaskAngleSlider.Minimum, MaskAngleSlider.Maximum);
        MaskCentreSlider.Value = Math.Clamp(mask.Centre, MaskCentreSlider.Minimum, MaskCentreSlider.Maximum);
        MaskHueSlider.Value = Math.Clamp(mask.Hue, MaskHueSlider.Minimum, MaskHueSlider.Maximum);
        MaskSpreadSlider.Value = Math.Clamp(mask.Spread, MaskSpreadSlider.Minimum, MaskSpreadSlider.Maximum);
        MaskWidthSlider.Value = Math.Clamp(mask.Width, MaskWidthSlider.Minimum, MaskWidthSlider.Maximum);

        int source = _maskSources.FindIndex(s => s.Equals(mask.Source, StringComparison.Ordinal));
        MaskSourceBox.SelectedIndex = source;

        // Die Quellen haengen an der Art - wechselt die Auswahl auf eine Ebene mit
        // Kryptomatte, muss dort auch die Kryptomattenliste stehen.
        if (source < 0 && mask.Source.Length > 0) FillMaskSources();

        UpdateMaskValues();
    }

    private void UpdateMaskValues()
    {
        MaskLowValue.Text = $"{MaskLowSlider.Value:0.00}";
        MaskHighValue.Text = $"{MaskHighSlider.Value:0.00}";
        MaskSoftValue.Text = $"{MaskSoftSlider.Value:0.00}";
        MaskAngleValue.Text = $"{MaskAngleSlider.Value:0} \u00B0";
        MaskCentreValue.Text = $"{MaskCentreSlider.Value:0.00}";
        MaskWidthValue.Text = $"{MaskWidthSlider.Value:0.00}";
        MaskHueValue.Text = $"{MaskHueSlider.Value:0} °";
        MaskSpreadValue.Text = $"±{MaskSpreadSlider.Value:0} °";
    }

    /// <summary>
    /// Tiefen, Mitten, Lichter - drei Stellungen der Regler darueber.
    ///
    /// Sie stellen ein und sperren nicht: Wer danach an den Reglern zieht, verliert
    /// nichts. Die Zahlen sind die ueblichen Drittel mit weicher Kante, gemessen an
    /// der wahrgenommenen Helligkeit - nicht am linearen Licht, in dem mittleres Grau
    /// bei 0,18 laege und "Mitten" damit fast alles waere.
    /// </summary>
    private void OnMaskZoneClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not Button { Tag: string zone }) return;

        var (low, high) = zone switch
        {
            "shadows" => (0f, 0.35f),
            "lights" => (0.65f, 1f),
            _ => (0.25f, 0.75f),
        };

        var mask = _selected.Mask;

        mask.Low = low;
        mask.High = high;
        mask.Softness = 0.15f;

        _filling = true;

        try
        {
            MaskLowSlider.Value = low;
            MaskHighSlider.Value = high;
            MaskSoftSlider.Value = 0.15f;
        }
        finally
        {
            _filling = false;
        }

        UpdateMaskValues();
        Raise(interim: false);
    }
}
