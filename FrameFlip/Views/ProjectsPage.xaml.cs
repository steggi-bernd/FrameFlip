using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Configuration;
using FrameFlip.Projects;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Directory = System.IO.Directory;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using WinForms = System.Windows.Forms;

namespace FrameFlip.Views;

/// <summary>
/// Der Projektbrowser.
///
/// Auf dem Handy genuegt eine Liste dessen, was zuletzt offen war. Am Rechner liegen
/// die Dateien, und da ist die Frage eine andere: Was habe ich eigentlich, und welche
/// Fassung davon ist die aktuelle? Deshalb steht hier das Blender-Dokument im
/// Mittelpunkt und nicht der Bildordner - kitchen.blend, kitchen_v2.blend und die
/// Sicherungen sind EIN Projekt mit mehreren Fassungen, so wie man es auch denkt.
///
/// Darunter geht es wie im Explorer weiter: die Ordner neben der .blend-Datei als
/// Kacheln (cam1, cam2, final), darin die Frames, und ein Frame angeklickt schwebt
/// gross ueber der Seite. Von dort fuehrt genau ein Knopf weiter - "Zusammenfuegen"
/// macht aus den Einzelbildern die Vorschau, die FrameFlip ohnehin am besten kann.
///
/// Die Zuordnung zu einem Projekt ist geraten (siehe <see cref="BlendProjects"/>),
/// und Geratenes muss man korrigieren koennen: Eine Kachel auf eine andere gezogen
/// legt beide zusammen, und eine Fassung laesst sich aus ihrem Projekt wieder
/// herausloesen. Ohne diesen Weg waere die Heuristik eine Behauptung.
/// </summary>
public partial class ProjectsPage : UserControl
{
    /// <summary>Eigenes Format fuer das Ziehen zwischen zwei Kacheln.</summary>
    private const string ProjectFormat = "FrameFlip.Project";

    /// <summary>Soviele Frames werden gezeigt. Darueber steht, wieviele es sind.</summary>
    private const int FrameLimit = 240;

    /// <summary>Soviele Fassungen als Zeile - der Rest sind ohnehin Autosicherungen.</summary>
    private const int VersionLimit = 40;

    private readonly Action<string> _openSequence;
    private readonly ProjectScanService _scans;
    private readonly ProjectThumbnailService _thumbnails;
    private CancellationTokenSource _libraryScan = new();
    private CancellationTokenSource _contentScan = new();
    private Task _libraryTask = Task.CompletedTask;
    private Task _contentTask = Task.CompletedTask;
    private TextBlock? _libraryError;

    private readonly ProjectNavigation _navigation = new();

    /// <summary>
    /// Laufende Nummer der Ansicht. Ein Vorschaubild, das aus dem Hintergrund
    /// zurueckkommt, nachdem jemand weitergeklickt hat, gehoert nirgendwohin mehr.
    /// </summary>
    private int _generation;

    private Window? _preview;
    private Point _pressed;
    private bool _dragging;

    public ProjectsPage(Action<string> openSequence)
        : this(openSequence, new ProjectScanService())
    {
    }

    /// <summary>Die Datenquellen sind austauschbar; Navigationstests lesen keine persoenliche Bibliothek.</summary>
    internal ProjectsPage(Action<string> openSequence, Func<List<BlendProject>> scan,
                          Func<List<RecentSequence>> recent)
        : this(openSequence, new ProjectScanService(scan, recent))
    {
    }

    internal ProjectsPage(Action<string> openSequence, ProjectScanService scans,
                          ProjectThumbnailService? thumbnails = null)
    {
        _openSequence = openSequence;
        _scans = scans;
        _thumbnails = thumbnails ?? ProjectThumbnailService.Shared;

        InitializeComponent();

        AllowDrop = true;
        DragOver += (_, e) => e.Effects = Dropped(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        Drop += OnDropOnPage;

        Click(AddFolder, PickFolder);
        Click(Rescan, () => Reload());

        // Zurueck gehoert an die Tasten und an die Maus, nicht nur an die Krumen.
        // Angehaengt wird es am Fenster, weil ein UserControl keine Tasten bekommt,
        // solange nichts darin den Fokus hat - und in einer Kachelansicht hat nie
        // etwas den Fokus.
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is not Window window) return;

            window.PreviewMouseDown += OnWindowMouse;
            window.PreviewKeyDown += OnWindowKey;

            Unloaded += (_, _) =>
            {
                window.PreviewMouseDown -= OnWindowMouse;
                window.PreviewKeyDown -= OnWindowKey;
            };
        };

        Reload();
    }

    // ---------------------------------------------------------------- Zurueck

    /// <summary>Die vierte Maustaste - auf jeder Maus mit Daumentasten das Zurueck.</summary>
    private void OnWindowMouse(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.XButton1) return;

        e.Handled = Back();
    }

    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        // In einem Textfeld ist die Ruecktaste das Loeschen und sonst nichts.
        if (Keyboard.FocusedElement is TextBox) return;

        bool back = e.Key == Key.Back
                    || (e.Key == Key.Left && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                    || e.SystemKey == Key.Left && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        if (!back) return;

        e.Handled = Back();
    }

    /// <summary>
    /// Eine Ebene zurueck. false heisst: Hier gibt es nichts mehr, wohin.
    ///
    /// Die Reihenfolge ist die, in der man hergekommen ist: erst das schwebende
    /// Bild, dann die Ordner nach oben, zuletzt heraus aus dem Projekt.
    /// </summary>
    public bool Back()
    {
        if (_preview is not null)
        {
            _preview.Close();
            return true;
        }

        if (!_navigation.Back()) return false;
        Render();

        return true;
    }

    // ---------------------------------------------------------------- Laden

    private void Reload()
    {
        if (_libraryError is not null) Body.Children.Remove(_libraryError);
        var token = RenewScan(ref _libraryScan);
        _libraryTask = ApplyScanAsync(_scans.LibraryAsync(token), token, Apply,
            () => Body.Children.Insert(0, _libraryError = ScanNotice(T("S_ProjectsReadFailed"))));
    }

    private void Apply(List<BlendProject> projects)
    {
        _navigation.Apply(projects);
        Render();
    }

    // ---------------------------------------------------------------- Zeichnen

    private void Render()
    {
        _generation++;
        RenewScan(ref _contentScan);

        Body.Children.Clear();
        Crumbs.Children.Clear();
        StackBar.Content = null;
        Note.Visibility = Visibility.Collapsed;

        if (_navigation.Project is null) Overview();
        else Inside();
    }

    private static CancellationToken RenewScan(ref CancellationTokenSource source)
    {
        source.Cancel();
        source.Dispose();
        source = new CancellationTokenSource();
        return source.Token;
    }

    private void LoadContent<TResult>(Func<CancellationToken, Task<TResult>> read, Action<TResult> show)
    {
        var token = _contentScan.Token;
        var notice = ScanNotice(T("S_ProjectsLoading"));
        Body.Children.Add(notice);
        _contentTask = ApplyScanAsync(read(token), token, result =>
        {
            Body.Children.Remove(notice);
            show(result);
        }, () => notice.Text = T("S_ProjectsReadFailed"));
    }

    /// <summary>
    /// Immer ueber den Dispatcher dieser Seite zurueck, auch wenn sie vor der
    /// Dispatcher-Schleife konstruiert wurde. Ein inzwischen abgeloester Scan
    /// darf weder alte Kacheln noch einen alten Fehler in die neue Ansicht setzen.
    /// </summary>
    private async Task ApplyScanAsync<T>(Task<T> scan, CancellationToken token, Action<T> apply, Action failed)
    {
        Action update;
        try
        {
            var result = await scan.ConfigureAwait(false);
            update = () => apply(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception) { update = failed; }

        if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested) update();
            });
        }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
    }

    private TextBlock ScanNotice(string text) => new()
    {
        Text = text,
        Margin = new Thickness(2, 6, 0, 12),
        TextWrapping = TextWrapping.Wrap,
        FontFamily = (FontFamily)FindResource("BodyFont"),
        FontSize = 12.5,
        Foreground = (Brush)FindResource("FaintBrush"),
    };

    private void Overview()
    {
        Heading.Text = T("S_ProjectsTitle");

        Crumbs.Children.Add(new TextBlock
        {
            Text = T("S_ProjectsIntro"),
            MaxWidth = 640,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)FindResource("BodyFont"),
            FontSize = 12.5,
            Foreground = (Brush)FindResource("MutedBrush"),
        });

        LoadContent(token => _scans.OverviewAsync(_navigation.Projects, token), ShowOverviewContents);
    }

    private void ShowOverviewContents(ProjectOverviewScan scan)
    {
        if (scan.Projects.Count == 0)
        {
            Note.Text = T("S_ProjectsEmpty");

            Note.Visibility = Visibility.Visible;
        }
        else
        {
            Body.Children.Add(Section(T("S_BlenderProjects", scan.Projects.Count)));

            var tiles = Wrap();

            foreach (var project in scan.Projects) tiles.Children.Add(ProjectTile(project.Project, project.Thumbnail));

            Body.Children.Add(tiles);
        }

        // Was FrameFlip schon einmal geoeffnet hat. Das sind Bildordner, keine
        // Projekte - deshalb eine eigene Reihe und nicht dazwischengemischt.
        var recent = scan.Recent;

        if (recent.Count > 0)
        {
            Body.Children.Add(Section(T("S_RecentlyOpened")));

            var tiles = Wrap();

            foreach (var entry in recent) tiles.Children.Add(RecentTile(entry.Sequence, entry.Exists));

            Body.Children.Add(tiles);
        }
    }

    private void Inside()
    {
        var project = _navigation.Project!;
        string root = _navigation.Root;
        string folder = _navigation.CurrentFolder;

        Heading.Text = project.Name.ToUpperInvariant();

        Crumbs.Children.Add(Crumb("PROJEKTE", () => { _navigation.ShowOverview(); Render(); }));
        Crumbs.Children.Add(Crumb(project.Name, () => { _navigation.OpenFolder(root); Render(); }));

        // Der Weg von der .blend-Datei bis hierher, Ordner fuer Ordner.
        foreach (var crumb in _navigation.Breadcrumbs())
        {
            Crumbs.Children.Add(Crumb(crumb.Name, () => { _navigation.OpenFolder(crumb.Path); Render(); }));
        }

        // Die .blend-Dateien gehoeren in die Kopfzeile, nicht in den Inhalt: Sie sind
        // die Ueberschrift des Projekts, nicht sein Gegenstand. Was man sehen will,
        // sind die Bilder - und die standen vorher hinter zweiundvierzig Zeilen
        // Dateinamen. Aufgeklappt wird auf Wunsch, nicht von selbst.
        if (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase) && project.Versions.Count > 0)
        {
            StackBar.Content = VersionStack(project);

            if (_navigation.VersionsOpen)
            {
                Body.Children.Add(Section(T("S_VersionsTitle")));

                foreach (var version in project.Versions.Take(VersionLimit))
                    Body.Children.Add(VersionRow(project, version));

                // Autosicherungen stehen hinten und sind schnell zu Hunderten da. Sie
                // alle als Zeile zu zeichnen kostet Zeit und sagt nichts.
                if (project.Versions.Count > VersionLimit)
                {
                    Body.Children.Add(new TextBlock
                    {
                        Margin = new Thickness(2, 2, 0, 12),
                        Text = T("S_MoreVersions", project.Versions.Count - VersionLimit),
                        FontFamily = (FontFamily)FindResource("BodyFont"),
                        FontSize = 11.5,
                        Foreground = (Brush)FindResource("FaintBrush"),
                    });
                }
            }
        }

        LoadContent(token => _scans.FolderAsync(folder, token), ShowFolderContents);
    }

    private void ShowFolderContents(ProjectFolderScan scan)
    {
        var folders = scan.Folders;

        if (folders.Count > 0)
        {
            Body.Children.Add(Section(T("S_FoldersTitle")));

            var tiles = Wrap();

            foreach (var tile in folders) tiles.Children.Add(FolderTileView(tile));

            Body.Children.Add(tiles);
        }

        var frames = scan.Frames;

        if (frames.Count > 0)
        {
            Body.Children.Add(Section(T("S_FramesTitle", frames.Count)));

            var tiles = Wrap();

            foreach (string frame in frames.Take(FrameLimit)) tiles.Children.Add(FrameTile(frame, frames));

            Body.Children.Add(tiles);

            if (frames.Count > FrameLimit)
            {
                Body.Children.Add(new TextBlock
                {
                    Margin = new Thickness(2, 2, 0, 12),
                    Text = T("S_MoreFrames", frames.Count - FrameLimit),
                    FontFamily = (FontFamily)FindResource("BodyFont"),
                    FontSize = 11.5,
                    Foreground = (Brush)FindResource("FaintBrush"),
                });
            }
        }

        if (folders.Count == 0 && frames.Count == 0)
        {
            Body.Children.Add(new TextBlock
            {
                Margin = new Thickness(2, 6, 0, 0),
                MaxWidth = 620,
                TextWrapping = TextWrapping.Wrap,
                Text = T("S_EmptyFolder"),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = 12.5,
                Foreground = (Brush)FindResource("FaintBrush"),
            });
        }
    }

    // ---------------------------------------------------------------- Kacheln

    private Border ProjectTile(BlendProject project, string? thumbnail)
    {
        string root = ProjectNavigation.RootOf(project);

        // Ein Projekt ohne echte Fassung gibt es wirklich: Die .blend wurde geloescht,
        // Blenders Sicherung liegt noch da. "0 Fassungen" waere die schlechtere
        // Auskunft als der Grund dafuer.
        string count = project.VersionCount switch
        {
            0 => T("S_OnlyBackups"),
            1 => T("S_OneVersion"),
            int many => T("S_ManyVersions", many),
        };

        string detail = $"{count} · {Ago(project.TouchedUtc)}";

        var tile = Tile(project.Name, detail, thumbnail, 480, 320, 180,
                        () => { _navigation.OpenProject(project); Render(); });

        tile.ToolTip = project.Newest?.Path ?? root;

        // Ziehen: eine Kachel auf eine andere legt beide zusammen.
        tile.MouseLeftButtonDown += (_, e) => _pressed = e.GetPosition(this);
        tile.MouseMove += (_, e) => MaybeDrag(tile, project, e);

        tile.AllowDrop = true;
        tile.DragOver += (_, e) => e.Effects = Accepts(e, project) ? DragDropEffects.Move : DragDropEffects.None;
        tile.Drop += (_, e) => OnDropOnProject(project, e);

        return tile;
    }

    private Border FolderTileView(FolderTile tile)
    {
        string detail = tile.Images > 0
            ? tile.Images == 1 ? T("S_OneImage") : T("S_ManyImages", tile.Images)
            : tile.Folders > 0
                ? tile.Folders == 1 ? T("S_OneFolder") : T("S_ManyFolders", tile.Folders)
                : T("S_EmptyShort");

        var view = Tile(tile.Name, detail, tile.Thumbnail, 480, 320, 180,
                        () => { _navigation.OpenFolder(tile.Path); Render(); });

        view.ToolTip = tile.Path;

        return view;
    }

    private Border FrameTile(string path, List<string> frames)
    {
        var tile = Tile(Path.GetFileName(path), string.Empty, path, 320, 224, 126,
                        () => ShowPreview(path, frames));

        tile.ToolTip = path;

        return tile;
    }

    private Border RecentTile(RecentSequence entry, bool alive)
    {
        string detail = alive
            ? (entry.Missing > 0 ? T("S_MissingCount", entry.Missing) : T("S_FramesOf", entry.Count))
              + " · " + Ago(entry.OpenedUtc)
            : T("S_FolderGone");

        var tile = Tile(entry.Name, detail, alive ? entry.Seed : null, 480, 320, 180,
                        alive ? () => _openSequence(entry.Seed) : null);

        tile.ToolTip = entry.Folder;

        if (entry.Missing > 0 && alive) tile.BorderBrush = (Brush)FindResource("AppWarn");

        return tile;
    }

    /// <summary>
    /// Eine Kachel: das Bild fuellt sie ganz aus, Name und Angaben stehen unten
    /// darauf.
    ///
    /// Ein Streifen ueber dem Bild statt einer Zeile darunter - so bleibt bei
    /// gleicher Kachelgroesse mehr Bild uebrig, und das Bild ist hier die Auskunft.
    /// Der Streifen ist dunkel und fast deckend, weil Text auf einem hellen Frame
    /// sonst nicht zu lesen waere.
    ///
    /// Alle Kacheln einer Reihe sind gleich gross - eine, die aus der Reihe faellt,
    /// weil sie mehr zu sagen hat, macht das Raster unruhig.
    /// </summary>
    private Border Tile(string title, string detail, string? image, int decodeWidth,
                        double width, double height, Action? click)
    {
        var picture = new Rectangle
        {
            RadiusX = 13,
            RadiusY = 13,
            Fill = (Brush)FindResource("AppBg2"),
        };

        if (image is not null) LoadThumb(picture, image, decodeWidth);

        var name = new TextBlock
        {
            Text = title,
            FontFamily = (FontFamily)FindResource("HeadFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource(click is null ? "DisabledBrush" : "ForegroundBrush"),
        };

        var caption = new StackPanel();
        caption.Children.Add(name);

        if (detail.Length > 0)
        {
            caption.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 3, 0, 0),
                Text = detail,
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)FindResource("MutedBrush"),
            });
        }

        // Ein Verlauf statt eines Blocks: Der Text braucht einen dunklen Grund, das
        // Bild darueber nicht. Eine harte Kante quer durch die Kachel sieht aus wie
        // ein Balken, der etwas verdeckt - der Verlauf legt sich darueber, ohne dass
        // man ihn als Flaeche wahrnimmt.
        var shade = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, 0x05, 0x05, 0x0B), 0.0),
                new GradientStop(Color.FromArgb(0x99, 0x05, 0x05, 0x0B), 0.45),
                new GradientStop(Color.FromArgb(0xEE, 0x05, 0x05, 0x0B), 1.0),
            },
        };

        var band = new Border
        {
            // Oben viel Luft, damit der Verlauf Platz zum Ausklingen hat.
            Padding = new Thickness(12, 34, 12, 10),
            CornerRadius = new CornerRadius(0, 0, 13, 13),
            Background = shade,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = caption,
        };

        var layers = new Grid();
        layers.Children.Add(picture);
        layers.Children.Add(band);

        var card = new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("AppSurface"),
            BorderBrush = (Brush)FindResource("PanelBorder"),
            BorderThickness = new Thickness(1),
            Cursor = click is null ? Cursors.Arrow : Cursors.Hand,
            Child = layers,
        };

        if (click is not null)
        {
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (_dragging) return;

                click();
            };

            card.MouseEnter += (_, _) => card.BorderBrush = (Brush)FindResource("AccentBrush");
            card.MouseLeave += (_, _) => card.BorderBrush = (Brush)FindResource("PanelBorder");
        }

        return card;
    }

    /// <summary>
    /// Der Stapel der .blend-Dateien, zu einer Zeile zusammengelegt.
    ///
    /// Sichtbar bleibt, was man wissen will: welche Datei die aktuelle ist und
    /// wieviele es sonst noch gibt. Der Rest steht einen Klick daneben. Vorher war
    /// es umgekehrt - achtundzwanzig Zeilen Dateinamen, und die Bilder fingen
    /// darunter an.
    /// </summary>
    private Border VersionStack(BlendProject project)
    {
        var parts = new List<string>();

        if (project.VersionCount > 0) parts.Add(T("S_ManyVersions", project.VersionCount));

        int backups = project.Versions.Count(v => v.IsBackup && !v.IsAutosave);

        if (backups > 0) parts.Add(T("S_ManyBackups", backups));
        if (project.AutosaveCount > 0) parts.Add(T("S_ManyAutosaves", project.AutosaveCount));

        var label = new TextBlock
        {
            Text = T("S_BlenderFile"),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)FindResource("HeadFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 9,
            Foreground = (Brush)FindResource("AccentBrush"),
        };

        var name = new TextBlock
        {
            Margin = new Thickness(14, 0, 0, 0),
            Text = project.Newest?.FileName ?? T("S_NoBlend"),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("ForegroundBrush"),
        };

        var detail = new TextBlock
        {
            Margin = new Thickness(14, 0, 0, 0),
            Text = string.Join(" · ", parts),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 10.5,
            Foreground = (Brush)FindResource("MutedBrush"),
        };

        // Der Pfeil zeigt, dass hier etwas aufgeht - ohne ihn sieht die Zeile aus
        // wie eine Beschriftung und niemand klickt darauf.
        var arrow = new TextBlock
        {
            Margin = new Thickness(14, 0, 0, 0),
            Text = _navigation.VersionsOpen ? "▴" : "▾",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(name);
        row.Children.Add(detail);
        row.Children.Add(arrow);

        var bar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 9, 16, 10),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("AppSurface"),

            // Eine kräftige Kante links statt eines Rahmens ringsum: Sie macht die
            // Zeile zu einer Überschrift und nicht zu einer weiteren Kachel.
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = row,
            ToolTip = project.Newest?.Path,
        };

        bar.MouseLeftButtonUp += (_, _) =>
        {
            _navigation.ToggleVersions();
            Render();
        };

        bar.MouseEnter += (_, _) => bar.Background = (Brush)FindResource("SurfaceBrush");
        bar.MouseLeave += (_, _) => bar.Background = (Brush)FindResource("AppSurface");

        return bar;
    }

    /// <summary>Eine Fassung als Zeile - Marke, Datum, Groesse, Dateiname.</summary>
    private Border VersionRow(BlendProject project, BlendVersion version)
    {
        bool newest = ReferenceEquals(version, project.Newest);

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Fill = (Brush)FindResource(newest ? "AppCyan" : version.IsReal ? "MutedBrush" : "DisabledBrush"),
        };

        var label = new TextBlock
        {
            Width = 128,
            VerticalAlignment = VerticalAlignment.Center,
            Text = version.Label,
            FontFamily = (FontFamily)FindResource("HeadFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Foreground = (Brush)FindResource(version.IsReal ? "ForegroundBrush" : "DisabledBrush"),
        };

        var detail = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = $"{version.FileName}   ·   {Size(version.Bytes)}   ·   {Ago(version.ModifiedUtc)}",
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("MutedBrush"),
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        Grid.SetColumn(dot, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(detail, 2);

        row.Children.Add(dot);
        row.Children.Add(label);
        row.Children.Add(detail);

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource("AppSurface"),
            BorderBrush = (Brush)FindResource(newest ? "AccentBrush" : "PanelBorder"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = row,
            ToolTip = version.Path,
        };

        var menu = new ContextMenu();

        var reveal = new MenuItem { Header = T("S_ShowInExplorer") };
        reveal.Click += (_, _) => Reveal(version.Path);

        // Der Gegenweg zum Zusammenlegen: Was hier faelschlich einsortiert wurde,
        // muss auch wieder heraus koennen.
        var detach = new MenuItem { Header = T("S_DetachVersion") };
        detach.Click += (_, _) =>
        {
            ProjectLibrary.Assign(version.Path, Path.GetFileNameWithoutExtension(version.FileName));
            _navigation.ShowOverview();
            Reload();
        };

        menu.Items.Add(reveal);
        menu.Items.Add(detach);
        card.ContextMenu = menu;

        card.MouseLeftButtonUp += (_, _) => Reveal(version.Path);

        return card;
    }

    // ---------------------------------------------------------------- Vorschau

    private void ShowPreview(string path, List<string> frames)
    {
        _preview?.Close();

        var window = new FramePreviewWindow(path, frames, seed =>
        {
            _preview?.Close();
            _openSequence(seed);
        });

        window.Owner = Window.GetWindow(this);
        window.Closed += (_, _) => _preview = null;

        _preview = window;
        window.Show();
    }

    // ---------------------------------------------------------------- Ziehen und Fallenlassen

    private void MaybeDrag(Border tile, BlendProject project, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragging) return;

        var now = e.GetPosition(this);

        if (Math.Abs(now.X - _pressed.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - _pressed.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragging = true;

        try
        {
            DragDrop.DoDragDrop(tile, new DataObject(ProjectFormat, project.Key), DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
        }
    }

    private bool Accepts(DragEventArgs e, BlendProject target)
    {
        if (e.Data.GetDataPresent(ProjectFormat))
            return e.Data.GetData(ProjectFormat) as string != target.Key;

        return Dropped(e).Count > 0;
    }

    private void OnDropOnProject(BlendProject target, DragEventArgs e)
    {
        e.Handled = true;

        // Ein ganzes Projekt: alle seine Fassungen wandern hinueber.
        if (e.Data.GetDataPresent(ProjectFormat)
            && e.Data.GetData(ProjectFormat) is string key
            && _navigation.Projects.FirstOrDefault(p => p.Key == key) is BlendProject source
            && source.Key != target.Key)
        {
            foreach (var version in source.Versions) ProjectLibrary.Assign(version.Path, target.Key);

            Reload();
            return;
        }

        var files = Dropped(e);
        if (files.Count == 0) return;

        foreach (string path in files)
        {
            if (BlendProjects.IsBlendFile(path)) ProjectLibrary.Assign(path, target.Key);
            else if (Directory.Exists(path)) ProjectLibrary.AddFolder(path);
        }

        Reload();
    }

    private void OnDropOnPage(object sender, DragEventArgs e)
    {
        if (e.Handled) return;

        var files = Dropped(e);
        if (files.Count == 0) return;

        foreach (string path in files)
        {
            if (Directory.Exists(path)) ProjectLibrary.AddFolder(path);
            else if (BlendProjects.IsBlendFile(path)) ProjectLibrary.Note(path);
        }

        Reload();
    }

    /// <summary>Was in einem Ziehvorgang steckt: Ordner und .blend-Dateien, sonst nichts.</summary>
    private static List<string> Dropped(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return new List<string>();

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return new List<string>();

        return paths.Where(p => Directory.Exists(p) || BlendProjects.IsBlendFile(p)).ToList();
    }

    private void PickFolder()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = T("S_PickProjectFolder"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        ProjectLibrary.AddFolder(dialog.SelectedPath);
        Reload();
    }

    // ---------------------------------------------------------------- Kleinteile

    private StackPanel Section(string title)
    {
        var text = new TextBlock
        {
            Text = title,
            Margin = new Thickness(2, 6, 0, 10),
            FontFamily = (FontFamily)FindResource("HeadFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
        };

        var holder = new StackPanel();
        holder.Children.Add(text);

        return holder;
    }

    private static WrapPanel Wrap() => new() { Margin = new Thickness(0, 0, 0, 10) };

    private Border Crumb(string text, Action click)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("HeadFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
        };

        var chip = new Border
        {
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource("AppSurface"),
            BorderBrush = (Brush)FindResource("PanelBorder"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = label,
        };

        chip.MouseLeftButtonUp += (_, _) => click();
        chip.MouseEnter += (_, _) => label.Foreground = (Brush)FindResource("ForegroundBrush");
        chip.MouseLeave += (_, _) => label.Foreground = (Brush)FindResource("MutedBrush");

        return chip;
    }

    private void Click(Border button, Action action)
    {
        button.MouseLeftButtonUp += (_, _) => action();
        button.MouseEnter += (_, _) => button.Background = (Brush)FindResource("SurfaceBrush");
        button.MouseLeave += (_, _) => button.Background = (Brush)FindResource("AppSurface");
    }

    private static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Wer keinen Explorer hat, bekommt hier eben nichts. Kein Grund fuer mehr.
        }
    }

    private async void LoadThumb(Rectangle target, string path, int width)
    {
        int generation = _generation;
        var token = _contentScan.Token;
        try
        {
            var image = await _thumbnails.LoadAsync(path, width, token).ConfigureAwait(false);
            if (image is null || token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;

            void Apply()
            {
                if (generation == _generation) Paint(target, image);
            }

            // Cache-Treffer bleiben sofort sichtbar. Worker-Ergebnisse gehen
            // auch ohne SynchronizationContext an den Dispatcher dieser Seite.
            if (Dispatcher.CheckAccess()) Apply();
            else await Dispatcher.InvokeAsync(Apply);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) { }
    }

    private static void Paint(Rectangle target, BitmapSource image)
        => target.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };

    /// <summary>Kurz fuer Localization.Strings.T - der Name steht hier oft genug.</summary>
    private static string T(string key) => Localization.Strings.T(key);

    private static string T(string key, params object?[] values) => Localization.Strings.T(key, values);

    private static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} kB",
        _ => $"{bytes} B",
    };

    /// <summary>"vor zwei Tagen" statt eines Zeitstempels - so erinnert man sich.</summary>
    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;

        return span switch
        {
            { TotalMinutes: < 2 } => T("S_JustNow"),
            { TotalMinutes: < 60 } => T("S_MinutesAgo", (int)span.TotalMinutes),
            { TotalHours: < 24 } => T("S_HoursAgo", (int)span.TotalHours),
            { TotalDays: < 2 } => T("S_Yesterday"),
            { TotalDays: < 14 } => T("S_DaysAgo", (int)span.TotalDays),
            _ => utc.ToLocalTime().ToString("d"),
        };
    }
}
