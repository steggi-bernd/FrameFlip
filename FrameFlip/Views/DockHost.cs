using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameFlip.Localization;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using TextElement = System.Windows.Documents.TextElement;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace FrameFlip.Views;

/// <summary>
/// Die Andockflaeche des Ateliers: das Bild in der Mitte, und links, rechts und darunter
/// Zonen, in die sich die Felder ziehen lassen.
///
/// Andocken an FESTE Zonen und nicht frei. Freies Andocken mit schwebenden Fenstern
/// ist in WPF ohne Fremdpakete ein Andockrahmen von Wochen und eine dauerhafte Quelle
/// seltsamer Fehler - mehrere Fenster, ihre Reihenfolge, ihre Aufloesung auf einem
/// zweiten Bildschirm. Drei Zonen, in denen Felder als Reiter oder uebereinander
/// liegen, decken ab, was "einen Reiter woanders ranschnappen" tatsaechlich heisst.
///
/// Was wo steht, entscheidet <see cref="DockLayout"/>. Diese Klasse zeichnet nur,
/// was dort steht, und uebersetzt einen Zug mit der Maus in einen Aufruf von
/// <see cref="DockLayout.Move"/>. Damit liegt die ganze Logik, an der etwas kaputtgehen
/// kann, in einem Modell, das sich ohne Fenster pruefen laesst.
///
/// Bedienung: einen Reiter greifen und ziehen. Ueber einer Gruppe landet er dort als
/// Reiter; an einem Rand - oben oder unten in einer Seitenzone, links oder rechts in
/// der unteren, oder am Bildrand, wo noch keine Zone ist - wird er eine eigene Gruppe.
/// Rechtsklick oder Escape bricht ab. Ein Klick holt ein Feld nach vorn, ein zweiter
/// klappt seine Gruppe ein.
/// </summary>
[ContentProperty(nameof(Panels))]
public sealed class DockHost : Border
{
    // ------------------------------------------------------------ Kennzeichen

    /// <summary>Unter welchem Namen ein Feld in der Anordnung steht.</summary>
    public static readonly DependencyProperty PanelIdProperty =
        DependencyProperty.RegisterAttached("PanelId", typeof(string), typeof(DockHost),
                                            new PropertyMetadata(""));

    public static void SetPanelId(DependencyObject at, string value) => at.SetValue(PanelIdProperty, value);
    public static string GetPanelId(DependencyObject at) => (string)at.GetValue(PanelIdProperty);

    /// <summary>
    /// Der Schluessel des Namens auf dem Reiter.
    ///
    /// Ein Schluessel und kein fertiger Text: Die Felder stehen beim Laden noch in
    /// keinem Baum, und ein Verweis auf eine Ressource findet dann nichts.
    /// </summary>
    public static readonly DependencyProperty TitleKeyProperty =
        DependencyProperty.RegisterAttached("TitleKey", typeof(string), typeof(DockHost),
                                            new PropertyMetadata(""));

    public static void SetTitleKey(DependencyObject at, string value) => at.SetValue(TitleKeyProperty, value);
    public static string GetTitleKey(DependencyObject at) => (string)at.GetValue(TitleKeyProperty);

    /// <summary>
    /// Ein Feld, das so gross ist wie sein Inhalt - etwa die Ausgabe mit ihren zwei Zeilen
    /// Knoepfe. Liegen in einer Gruppe nur solche Felder, bekommt sie genau ihre Hoehe
    /// (nebeneinander: ihre Breite), und der Rest der Zone gehoert den anderen. Ein Anteil
    /// wie bei den uebrigen Gruppen schnitte sie in einem kleinen Fenster ab und liesse
    /// in einem grossen leere Flaeche stehen.
    /// </summary>
    public static readonly DependencyProperty FitsContentProperty =
        DependencyProperty.RegisterAttached("FitsContent", typeof(bool), typeof(DockHost),
                                            new PropertyMetadata(false));

    public static void SetFitsContent(DependencyObject at, bool value) => at.SetValue(FitsContentProperty, value);
    public static bool GetFitsContent(DependencyObject at) => (bool)at.GetValue(FitsContentProperty);

    /// <summary>Die Felder, die sich andocken lassen.</summary>
    public Collection<FrameworkElement> Panels { get; } = new();

    /// <summary>Was in der Mitte steht - das Bild. Es wird nie verschoben.</summary>
    public FrameworkElement? Center { get; set; }

    /// <summary>Die Anordnung, wie sie gerade steht.</summary>
    public DockLayout Layout { get; private set; } = DockLayout.Default();

    /// <summary>Die Anordnung hat sich geaendert - durch einen Zug oder einen Griff.</summary>
    public event Action<DockLayout>? LayoutChanged;

    private readonly Dictionary<string, string> _hints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _badges = new(StringComparer.Ordinal);

    private FrameworkElement? PanelOf(string id)
        => Panels.FirstOrDefault(p => GetPanelId(p) == id);

    private IReadOnlyCollection<string> Ids => Panels.Select(GetPanelId).ToArray();

    /// <summary>Ob eine Gruppe nur aus Feldern besteht, die so gross sind wie ihr Inhalt.</summary>
    private bool FitsContent(DockGroup group)
        => group.Panels.Count > 0 && group.Panels.All(id => PanelOf(id) is { } panel && GetFitsContent(panel));

    // ------------------------------------------------------------ von aussen

    /// <summary>Uebernimmt eine gespeicherte Anordnung - repariert, falls noetig.</summary>
    public void Load(DockLayout? layout)
    {
        Layout = (layout ?? DockLayout.Default()).Clone();
        Layout.Normalise(Ids);

        Rebuild();
    }

    /// <summary>
    /// Holt ein Feld in seiner Gruppe nach vorn.
    ///
    /// Nur die eine Gruppe wird neu gebaut. Ein Klick auf einen Reiter soll nicht das
    /// Bild und die anderen Felder aus- und wieder einhaengen - das hiesse jedes Mal
    /// alles neu vermessen, fuer einen Wechsel, der nur eine Gruppe betrifft.
    /// </summary>
    public void Activate(string panel)
    {
        if (Layout.Find(panel) is not { } at) return;

        var group = Layout.Zone(at.Zone)[at.Group];

        if (group.Active == panel && !group.Collapsed) return;

        // Aufklappen aendert die Form der Zone, nicht nur die Gruppe - dann alles.
        bool reshape = group.Collapsed;

        group.Active = panel;
        group.Collapsed = false;

        if (reshape) Rebuild();
        else RebuildGroup(at.Zone, at.Group);

        LayoutChanged?.Invoke(Layout);
    }

    /// <summary>
    /// Ein Klick auf einen Reiter: das Feld nach vorn - oder, wenn es schon vorn und
    /// offen liegt, seine Gruppe einklappen. Siehe <see cref="DockLayout.Toggle"/>.
    /// </summary>
    public void Toggle(string panel)
    {
        if (Layout.Find(panel) is not { } at) return;

        var group = Layout.Zone(at.Zone)[at.Group];
        bool was = group.Collapsed;

        Layout.Toggle(panel);

        if (group.Collapsed != was) Rebuild();
        else RebuildGroup(at.Zone, at.Group);

        LayoutChanged?.Invoke(Layout);
    }

    /// <summary>
    /// Verschiebt ein Feld - derselbe Weg, den ein Zug mit der Maus nimmt.
    /// Oeffentlich, damit die Probe ihn gehen kann, ohne eine Maus vorzutaeuschen.
    /// </summary>
    public void MovePanel(string panel, DockZone zone, int group, bool asTab)
    {
        Layout.Move(panel, zone, group, asTab);
        Layout.Normalise(Ids);

        Rebuild();
        LayoutChanged?.Invoke(Layout);
    }

    /// <summary>Ein einzelnes Feld zurueck an seinen Platz - die anderen bleiben, wo sie sind.</summary>
    public void HomePanel(string panel)
    {
        Layout.Home(panel);
        Layout.Normalise(Ids);

        Rebuild();
        LayoutChanged?.Invoke(Layout);
    }

    /// <summary>Zurueck zur Grundanordnung - fuer den, der sich verzogen hat.</summary>
    public void ResetLayout()
    {
        Layout = DockLayout.Default();
        Layout.Normalise(Ids);

        Rebuild();
        LayoutChanged?.Invoke(Layout);
    }

    /// <summary>
    /// Ob ein Feld gerade etwas zeigen kann. Wenn nicht, steht an seiner Stelle der
    /// Satz, der sagt, warum - statt einer leeren Flaeche.
    /// </summary>
    public void SetAvailable(string panel, bool available, string hint)
    {
        bool was = !_hints.ContainsKey(panel);

        if (available) _hints.Remove(panel);
        else _hints[panel] = hint;

        if (was == available) return;

        if (Layout.Find(panel) is { } at) RebuildGroup(at.Zone, at.Group);
    }

    /// <summary>
    /// Laesst ein Feld an einer Stelle fallen, als waere es dorthin gezogen worden -
    /// mit derselben Zielsuche wie beim Ziehen. Wahr, wenn dort ein Ziel war.
    ///
    /// Oeffentlich fuer die Probe: Die Geometrie der Ziele ist das, was beim Ziehen
    /// schiefgehen kann, und eine Maus laesst sich im Test nicht vortaeuschen.
    /// </summary>
    public bool DropAt(string panel, Point at)
    {
        _targets = Targets(panel);

        var target = TargetAt(at);

        _targets = new();

        if (target is not { } t) return false;

        MovePanel(panel, t.Zone, t.Group, t.AsTab);
        return true;
    }

    /// <summary>Eine kleine Zahl neben dem Namen auf dem Reiter.</summary>
    public void SetBadge(string panel, string text)
    {
        if (_badges.TryGetValue(panel, out var old) && old == text) return;

        _badges[panel] = text;

        if (_badgeBlocks.TryGetValue(panel, out var block)) block.Text = text;
    }

    /// <summary>Ob ein Feld gerade etwas zu zeigen hat - oder nur seinen Hinweis.</summary>
    public bool IsAvailable(string panel) => !_hints.ContainsKey(panel);

    /// <summary>Ob ein Feld gerade vorn liegt und zu sehen ist.</summary>
    public bool IsShown(string panel)
        => PanelOf(panel) is { } element && element.Parent is not null && !_hints.ContainsKey(panel) &&
           Layout.Find(panel) is { } at && Layout.Zone(at.Zone)[at.Group] is { Collapsed: false } group &&
           group.Active == panel;

    // ------------------------------------------------------------ Aufbau

    private Grid? _root;
    private ColumnDefinition? _leftColumn, _rightColumn;
    private RowDefinition? _bottomRow, _topRow;
    private Canvas? _overlay;
    private Rectangle? _mark;
    private TextBlock? _markText;

    private readonly List<(DockZone Zone, int Index, FrameworkElement View)> _groupViews = new();
    private readonly Dictionary<DockZone, FrameworkElement> _zoneViews = new();
    private readonly Dictionary<string, TextBlock> _badgeBlocks = new(StringComparer.Ordinal);

    /// <summary>
    /// Baut die ganze Flaeche aus der Anordnung neu.
    ///
    /// Neu statt nachgefuehrt: Ein Zug kann eine Zone leeren, eine neue anlegen und
    /// eine Gruppe verschwinden lassen - das nachzufuehren waere eine eigene
    /// Fehlerquelle. Die Felder selbst werden dabei nur umgehaengt, nicht neu
    /// angelegt; alles, was sie wissen, bleibt.
    /// </summary>
    private void Rebuild()
    {
        foreach (var panel in Panels) Detach(panel);
        if (Center is not null) Detach(Center);

        _groupViews.Clear();
        _zoneViews.Clear();
        _badgeBlocks.Clear();

        var root = new Grid();

        bool left = Layout.Left.Count > 0;
        bool right = Layout.Right.Count > 0;
        bool bottom = Layout.Bottom.Count > 0;
        bool top = Layout.Top.Count > 0;

        // Eine ganz eingeklappte Zone ist nur so breit (oder hoch) wie ihr Streifen.
        // Ihre Groesse bleibt in der Anordnung stehen und gilt wieder beim Aufklappen.
        bool leftFolded = Layout.IsFolded(DockZone.Left);
        bool rightFolded = Layout.IsFolded(DockZone.Right);
        bool bottomFolded = Layout.IsFolded(DockZone.Bottom);

        // Unten wie oben: Liegen dort nur Felder, die so hoch sind wie ihr Inhalt, ist die Zone
        // genau so hoch - statt einer festen Hoehe, in der die Werkzeugeinstellungen verloren
        // in viel leerer Flaeche standen.
        bool bottomFits = bottom && Layout.Bottom.All(g => g.Collapsed || FitsContent(g));
        bool topFolded = Layout.IsFolded(DockZone.Top);

        // Oben liegen in der Grundanordnung nur die Werkzeugeinstellungen - so hoch wie ihr
        // Inhalt. Eine feste Hoehe bekommt die Zone erst mit einem Feld, das keine eigene hat.
        bool topFits = top && Layout.Top.All(g => g.Collapsed || FitsContent(g));

        _leftColumn = new ColumnDefinition { Width = Size(left, leftFolded, Layout.LeftWidth) };
        _rightColumn = new ColumnDefinition { Width = Size(right, rightFolded, Layout.RightWidth) };
        _bottomRow = new RowDefinition { Height = Size(bottom, bottomFolded || bottomFits, Layout.BottomHeight) };
        _topRow = new RowDefinition { Height = Size(top, topFolded || topFits, Layout.TopHeight) };

        if (left && !leftFolded) _leftColumn.MinWidth = 180;
        if (right && !rightFolded) _rightColumn.MinWidth = 200;
        if (bottom && !bottomFolded && !bottomFits) _bottomRow.MinHeight = 120;
        if (top && !topFolded && !topFits) _topRow.MinHeight = 80;

        root.ColumnDefinitions.Add(_leftColumn);
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap(left, leftFolded)) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap(right, rightFolded)) });
        root.ColumnDefinitions.Add(_rightColumn);

        // Zeilen: oben, Fuge, Bild, Fuge, unten. Die Seiten reichen vom Bild bis ganz nach
        // unten; oben liegt ueber allem, ueber die ganze Breite.
        root.RowDefinitions.Add(_topRow);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(top ? (topFolded || topFits ? 8 : 12) : 0) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 200 });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(bottomFits && !bottomFolded ? 8 : Gap(bottom, bottomFolded)) });
        root.RowDefinitions.Add(_bottomRow);

        static GridLength Size(bool there, bool folded, double size)
            => !there ? new GridLength(0) : folded ? GridLength.Auto : new GridLength(size);

        static double Gap(bool there, bool folded) => !there ? 0 : folded ? 6 : 12;

        if (Center is not null)
        {
            Grid.SetColumn(Center, 2);
            Grid.SetRow(Center, 2);
            root.Children.Add(Center);
        }

        if (left)
        {
            var zone = leftFolded ? FoldedStrip(DockZone.Left, Layout.Left)
                                  : ZoneView(DockZone.Left, Layout.Left, vertical: true);
            Grid.SetColumn(zone, 0);
            Grid.SetRow(zone, 2);
            Grid.SetRowSpan(zone, 3);
            root.Children.Add(zone);

            if (!leftFolded)
            {
                root.Children.Add(Splitter(column: 1, row: 2, rowSpan: 3, columns: true, () =>
                {
                    Layout.LeftWidth = _leftColumn.ActualWidth;
                    LayoutChanged?.Invoke(Layout);
                }));
            }
        }

        if (right)
        {
            var zone = rightFolded ? FoldedStrip(DockZone.Right, Layout.Right)
                                   : ZoneView(DockZone.Right, Layout.Right, vertical: true);
            Grid.SetColumn(zone, 4);
            Grid.SetRow(zone, 2);
            Grid.SetRowSpan(zone, 3);
            root.Children.Add(zone);

            if (!rightFolded)
            {
                root.Children.Add(Splitter(column: 3, row: 2, rowSpan: 3, columns: true, () =>
                {
                    Layout.RightWidth = _rightColumn.ActualWidth;
                    LayoutChanged?.Invoke(Layout);
                }));
            }
        }

        if (bottom)
        {
            // Unten gibt es keinen eigenen Streifen: Eingeklappt stehen die
            // Reiterleisten nebeneinander, und die Zeile ist so hoch wie sie.
            var zone = ZoneView(DockZone.Bottom, Layout.Bottom, vertical: false);
            Grid.SetColumn(zone, 2);
            Grid.SetRow(zone, 4);
            root.Children.Add(zone);

            if (!bottomFolded && !bottomFits)
            {
                root.Children.Add(Splitter(column: 2, row: 3, rowSpan: 1, columns: false, () =>
                {
                    Layout.BottomHeight = _bottomRow.ActualHeight;
                    LayoutChanged?.Invoke(Layout);
                }));
            }
        }

        if (top)
        {
            // Oben ueber die ganze Breite - wie die Leiste, die dort vorher fest stand. Die
            // Reiter stehen hier senkrecht links: Eine Reiterzeile ueber der Leiste kostete
            // genau die Hoehe, die sie sparen soll.
            var zone = ZoneView(DockZone.Top, Layout.Top, vertical: false);
            Grid.SetColumnSpan(zone, 5);
            Grid.SetRow(zone, 0);
            root.Children.Add(zone);

            if (!topFolded && !topFits)
            {
                var splitter = Splitter(column: 0, row: 1, rowSpan: 1, columns: false, () =>
                {
                    Layout.TopHeight = _topRow.ActualHeight;
                    LayoutChanged?.Invoke(Layout);
                });

                Grid.SetColumnSpan(splitter, 5);
                root.Children.Add(splitter);
            }
        }

        // Die Markierung beim Ziehen - ueber allem, und ohne selbst die Maus zu fangen.
        _overlay = new Canvas { IsHitTestVisible = false };
        Grid.SetColumnSpan(_overlay, 5);
        Grid.SetRowSpan(_overlay, 5);
        Panel.SetZIndex(_overlay, 100);

        _mark = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xA4, 0x7B, 0xF0)),
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2,
            RadiusX = 4,
            RadiusY = 4,
            Visibility = Visibility.Collapsed,
        };

        _markText = new TextBlock
        {
            Foreground = (Brush)FindResource("ForegroundBrush"),
            Background = (Brush)FindResource("PanelBackground"),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11,
            Visibility = Visibility.Collapsed,
        };

        _overlay.Children.Add(_mark);
        _overlay.Children.Add(_markText);
        root.Children.Add(_overlay);

        _root = root;
        Child = root;
    }

    /// <summary>Baut eine Gruppe neu, an ihrer Stelle - der Rest bleibt, wie er ist.</summary>
    private void RebuildGroup(DockZone zone, int index)
    {
        int slot = _groupViews.FindIndex(g => g.Zone == zone && g.Index == index);

        if (slot < 0 || _groupViews[slot].View.Parent is not Grid grid)
        {
            Rebuild();
            return;
        }

        var old = _groupViews[slot].View;
        var group = Layout.Zone(zone)[index];

        foreach (string id in group.Panels)
            if (PanelOf(id) is { } element) Detach(element);

        var view = GroupView(zone, index, group);

        Grid.SetRow(view, Grid.GetRow(old));
        Grid.SetColumn(view, Grid.GetColumn(old));

        int at = grid.Children.IndexOf(old);

        grid.Children.RemoveAt(at);
        grid.Children.Insert(at, view);

        _groupViews[slot] = (zone, index, view);
    }

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
    }

    private GridSplitter Splitter(int column, int row, int rowSpan, bool columns, Action done)
    {
        var splitter = new GridSplitter
        {
            Style = (Style)FindResource("DashboardSplitter"),
            ResizeDirection = columns ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = columns ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = columns ? VerticalAlignment.Stretch : VerticalAlignment.Center,
            Width = columns ? 12 : double.NaN,
            Height = columns ? double.NaN : 12,
            Focusable = false,
        };

        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, row);
        Grid.SetRowSpan(splitter, rowSpan);

        splitter.DragCompleted += (_, _) => done();

        return splitter;
    }

    /// <summary>Eine Zone: ihre Gruppen untereinander (oder nebeneinander), mit Griffen dazwischen.</summary>
    private FrameworkElement ZoneView(DockZone zone, List<DockGroup> groups, bool vertical)
    {
        var grid = vertical ? new ZoneGrid() : new Grid();

        // Die Zeilen der Gruppen, die so hoch sind wie ihr Inhalt - in einer Seitenzone.
        var fits = new List<RowDefinition>();

        for (int i = 0; i < groups.Count; i++)
        {
            if (i > 0)
            {
                if (vertical) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
                else grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });

                // Ein Griff nur zwischen zwei Gruppen, die ihre Groesse teilen. Eine
                // eingeklappte ist so gross wie ihre Leiste; sie groesser zu ziehen hiesse,
                // sie aufzuklappen, ohne dass das Modell davon weiss. Eine, die so gross
                // ist wie ihr Inhalt, hat ebenso nichts abzugeben.
                if (Shares(groups[i - 1], vertical) && Shares(groups[i], vertical))
                    grid.Children.Add(GroupSplitter(grid, groups, vertical));
            }

            bool fixedSize = !Shares(groups[i], vertical);

            var size = fixedSize
                ? GridLength.Auto
                : new GridLength(Math.Max(0.01, groups[i].Weight), GridUnitType.Star);

            if (vertical) grid.RowDefinitions.Add(new RowDefinition { Height = size, MinHeight = fixedSize ? 0 : 90 });
            else grid.ColumnDefinitions.Add(new ColumnDefinition { Width = size, MinWidth = fixedSize ? 0 : 160 });

            var view = GroupView(zone, i, groups[i]);

            if (vertical && fixedSize && !groups[i].Collapsed) fits.Add(grid.RowDefinitions[^1]);

            if (vertical) Grid.SetRow(view, grid.RowDefinitions.Count - 1);
            else Grid.SetColumn(view, grid.ColumnDefinitions.Count - 1);

            grid.Children.Add(view);
            _groupViews.Add((zone, i, view));
        }

        if (vertical && fits.Count > 0) KeepInside((ZoneGrid)grid, fits);

        _zoneViews[zone] = grid;

        return grid;
    }

    /// <summary>
    /// Haelt eine Seitenzone in ihrer Hoehe. Gruppen, die so hoch sind wie ihr Inhalt, bekommen
    /// hoechstens den Platz, den die anderen lassen, und ihr Inhalt rollt (GroupView). Sonst
    /// schnitt ein niedriges Fenster die unteren Gruppen ab - samt ihrem Reiter, dem einzigen
    /// Griff, und nur das Zuruecksetzen der ganzen Anordnung holte sie zurueck.
    ///
    /// Stehen in der Zone nur solche Gruppen, fuellt die letzte die Spalte: Ein Kasten, der
    /// oben in einer sonst leeren Spalte haengt, gehoert nicht erkennbar dazu.
    /// </summary>
    private static void KeepInside(ZoneGrid grid, List<RowDefinition> fits)
    {
        if (!grid.RowDefinitions.Any(r => r.Height.IsStar))
        {
            fits[^1].Height = new GridLength(1, GridUnitType.Star);
            fits.RemoveAt(fits.Count - 1);
        }

        grid.Fits.AddRange(fits);
    }

    /// <summary>
    /// Das Raster einer Seitenzone. Es kuerzt beim Messen die Zeilen der Gruppen, die so hoch
    /// sind wie ihr Inhalt, auf den Platz, den die Zone hat - dort ist die verfuegbare Hoehe
    /// bekannt. Danach gefragt, wie hoch das Raster geworden ist, antwortete WPF mit der Hoehe
    /// samt dem, was schon abgeschnitten war.
    /// </summary>
    private sealed class ZoneGrid : Grid
    {
        public List<RowDefinition> Fits { get; } = new();

        /// <summary>Wie hoch eine eingeklappte Gruppe ist - ihre Reiterleiste.</summary>
        private const double Folded = 40, Least = 72;

        protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
        {
            if (Fits.Count > 0 && !double.IsInfinity(constraint.Height))
            {
                double taken = RowDefinitions
                    .Where(r => !Fits.Contains(r))
                    .Sum(r => r.Height.IsAbsolute ? r.Height.Value : r.Height.IsStar ? r.MinHeight : Folded);

                double each = Math.Max(Least, (constraint.Height - taken) / Fits.Count);

                foreach (var row in Fits)
                    if (Math.Abs(row.MaxHeight - each) > 0.5) row.MaxHeight = each;
            }

            return base.MeasureOverride(constraint);
        }
    }

    /// <summary>
    /// Ob eine Gruppe ihre Groesse mit den anderen teilt - offen und nicht so gross wie ihr
    /// Inhalt. Nur solche Gruppen haben einen Griff und ein Gewicht, das zaehlt.
    ///
    /// Nebeneinander (oben, unten) zaehlt "so gross wie der Inhalt" nur fuer die Hoehe der
    /// Zone: In der Breite teilen sich alle offenen Gruppen den Platz - eine Leiste, die so
    /// breit waere wie ihr Inhalt in einer Zeile, ragte aus dem Fenster.
    /// </summary>
    private bool Shares(DockGroup group, bool vertical = true) => !group.Collapsed && (!vertical || !FitsContent(group));

    /// <summary>Der Griff zwischen zwei offenen Gruppen - am Ende der Definitionen, die es bis hierher gibt.</summary>
    private GridSplitter GroupSplitter(Grid grid, List<DockGroup> groups, bool vertical)
    {
        var splitter = new GridSplitter
        {
            Style = (Style)FindResource("DashboardSplitter"),
            ResizeDirection = vertical ? GridResizeDirection.Rows : GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Stretch,
            Width = vertical ? double.NaN : 10,
            Height = vertical ? 10 : double.NaN,
            Focusable = false,
        };

        if (vertical) Grid.SetRow(splitter, grid.RowDefinitions.Count - 1);
        else Grid.SetColumn(splitter, grid.ColumnDefinitions.Count - 1);

        // Nach dem Loslassen die Anteile aus den tatsaechlichen Groessen lesen.
        // Pixel als Gewichte: Das Verhaeltnis ist, was zaehlt, und es bleibt
        // richtig, wenn das Fenster spaeter eine andere Hoehe hat. Eingeklappte
        // Gruppen behalten ihr Gewicht - ihre Leistenhoehe ist keine Wahl.
        splitter.DragCompleted += (_, _) =>
        {
            int g = 0;

            for (int d = 0; d < (vertical ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count); d += 2)
            {
                double size = vertical ? grid.RowDefinitions[d].ActualHeight
                                       : grid.ColumnDefinitions[d].ActualWidth;

                if (g < groups.Count && size > 0 && Shares(groups[g], vertical)) groups[g].Weight = size;
                g++;
            }

            LayoutChanged?.Invoke(Layout);
        };

        return splitter;
    }

    /// <summary>
    /// Eine Seitenzone, in der alles eingeklappt ist: ein schmaler Streifen mit
    /// senkrechten Reitern, wie die Seitenleisten in Blender. Ein Klick auf einen
    /// holt das Feld zurueck, und die Zone bekommt ihre alte Breite wieder.
    /// </summary>
    private FrameworkElement FoldedStrip(DockZone zone, List<DockGroup> groups)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

        for (int i = 0; i < groups.Count; i++)
        {
            if (i > 0)
            {
                stack.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(6, 4, 6, 4),
                    Background = (Brush)FindResource("PanelBorder"),
                });
            }

            foreach (string id in groups[i].Panels)
            {
                var tab = Tab(id, active: false);

                // Vor dem Drehen: Die Breite wird zur Hoehe.
                tab.MinWidth = 72;
                tab.LayoutTransform = new RotateTransform(zone == DockZone.Left ? -90 : 90);

                stack.Children.Add(tab);
            }
        }

        var view = new Border
        {
            Background = (Brush)FindResource("PanelBackground"),
            BorderBrush = (Brush)FindResource("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = stack,
            ContextMenu = LayoutMenu(),
        };

        _zoneViews[zone] = view;

        return view;
    }

    /// <summary>
    /// Eine Gruppe: die Reiter oben, das vordere Feld darunter. Eingeklappt nur die
    /// Reiter - ohne Linie, denn vorn liegt dann nichts.
    /// </summary>
    private FrameworkElement GroupView(DockZone zone, int index, DockGroup group)
    {
        // Oben ist jeder Reiter ein schmaler Griff links - ein gedrehter Name haette mit seiner
        // Laenge die Hoehe der Leiste bestimmt. Ueberall sonst waagerecht ueber dem Feld.
        bool side = zone == DockZone.Top;

        var tabs = side ? new UniformGrid { Columns = 1 } : new UniformGrid { Rows = 1 };

        foreach (string id in group.Panels)
        {
            bool active = !group.Collapsed && group.Active == id;
            tabs.Children.Add(side ? Grip(id, active) : Tab(id, active));
        }

        double line = group.Collapsed ? 0 : 1;

        var strip = new Border
        {
            BorderBrush = (Brush)FindResource("PanelBorder"),
            BorderThickness = side ? new Thickness(0, 0, line, 0) : new Thickness(0, 0, 0, line),
            Child = tabs,
            ContextMenu = LayoutMenu(),
        };

        if (group.Collapsed)
        {
            return new Border
            {
                Background = (Brush)FindResource("PanelBackground"),
                BorderBrush = (Brush)FindResource("PanelBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = side ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
                Child = strip,
                Tag = (zone, index),
            };
        }

        var host = new Border();

        if (group.Active is { } activeId && PanelOf(activeId) is { } panel)
        {
            host.Child = _hints.TryGetValue(activeId, out var hint)
                ? new TextBlock
                {
                    Text = hint,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16),
                    FontSize = 11,
                    Foreground = (Brush)FindResource("MutedBrush"),
                }
                : Scrolling(zone, group, panel);
        }

        var grid = new Grid();

        if (side)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(host, 1);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(host, 1);
        }

        grid.Children.Add(strip);
        grid.Children.Add(host);

        return new Border
        {
            Background = (Brush)FindResource("PanelBackground"),
            BorderBrush = (Brush)FindResource("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid,
            Tag = (zone, index),
        };
    }

    /// <summary>
    /// Ein Reiter als Griff: sechs Punkte, der Name im Hinweis - fuer die Leiste oben. Er
    /// tut, was ein Reiter tut: ziehen, anklicken, Rechtsklick fuer die Anordnung.
    /// </summary>
    private FrameworkElement Grip(string id, bool active)
    {
        var element = PanelOf(id);
        string title = element is null ? id : Strings.T(GetTitleKey(element));

        var dots = Dots((Brush)FindResource(active ? "AccentBrush" : "MutedBrush"), 12);

        var grip = new Grid
        {
            Width = 18,
            MinHeight = 24,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Tag = id,
            ToolTip = title + " – " + Strings.T("S_DockTabHint"),
            ContextMenu = PanelMenu(id),
        };

        grip.Children.Add(dots);

        grip.MouseLeftButtonDown += OnTabDown;
        grip.MouseMove += OnTabMove;
        grip.MouseLeftButtonUp += OnTabUp;
        grip.MouseRightButtonDown += OnTabRight;
        grip.LostMouseCapture += OnTabLost;

        return grip;
    }

    /// <summary>
    /// In einer Seitenzone rollt der Inhalt einer Gruppe, die so hoch ist wie er - falls
    /// KeepInside sie kuerzen musste. Oben und unten nie: Dort ist die Zone so hoch wie er.
    /// </summary>
    private FrameworkElement Scrolling(DockZone zone, DockGroup group, FrameworkElement panel)
    {
        if (DockLayout.IsHorizontal(zone) || !FitsContent(group)) return panel;

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
    }

    /// <summary>
    /// Das Menue am Griff eines Feldes: dieses eine Feld an seinen Platz zurueck - oder die ganze
    /// Anordnung. Am Rechtsklick auf Reiter und Punktegriff, ueberall gleich.
    /// </summary>
    private ContextMenu PanelMenu(string id)
    {
        var menu = new ContextMenu();

        var home = new MenuItem { Header = Strings.T("S_DockPanelHome") };
        home.Click += (_, _) => HomePanel(id);
        menu.Items.Add(home);

        menu.Items.Add(new Separator());

        var reset = new MenuItem { Header = Strings.T("S_DockReset") };
        reset.Click += (_, _) => ResetLayout();
        menu.Items.Add(reset);

        return menu;
    }

    /// <summary>Sechs Punkte - das Zeichen fuer "hier anfassen", auf jedem Reiter und am Griff oben.</summary>
    private static System.Windows.Shapes.Path Dots(Brush brush, double size)
    {
        double k = size / 12;

        return new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M{1 * k},{1 * k} h0.01 M{6 * k},{1 * k} h0.01 M{1 * k},{6 * k} h0.01 M{6 * k},{6 * k} h0.01 M{1 * k},{11 * k} h0.01 M{6 * k},{11 * k} h0.01")),
            Stroke = brush,
            StrokeThickness = 2.4 * Math.Max(0.75, k),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Ein Reiter: Name, Zaehler, und die Linie unter dem vorderen.</summary>
    private FrameworkElement Tab(string id, bool active)
    {
        var element = PanelOf(id);
        string title = element is null ? id : Strings.T(GetTitleKey(element));

        var label = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };

        // Die Punkte sagen: Das hier laesst sich greifen und woanders hinziehen.
        var grip = Dots((Brush)FindResource("MutedBrush"), 9);
        grip.Margin = new Thickness(0, 0, 7, 0);
        grip.Opacity = 0.7;
        label.Children.Add(grip);

        label.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var badge = new TextBlock
        {
            Text = _badges.TryGetValue(id, out var b) ? b : "",
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = (Brush)FindResource("FaintBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        label.Children.Add(badge);
        _badgeBlocks[id] = badge;

        var line = new Border
        {
            Height = 2,
            Margin = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (Brush)FindResource("AccentBrush"),
            Visibility = active ? Visibility.Visible : Visibility.Hidden,
        };

        var tab = new Grid
        {
            Height = 34,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = id,
            ToolTip = Strings.T("S_DockTabHint"),
            ContextMenu = PanelMenu(id),
        };

        TextElement.SetFontSize(tab, 12);
        TextElement.SetFontWeight(tab, FontWeights.SemiBold);
        TextElement.SetForeground(tab, (Brush)FindResource(active ? "ForegroundBrush" : "MutedBrush"));

        tab.Children.Add(label);
        tab.Children.Add(line);

        tab.MouseLeftButtonDown += OnTabDown;
        tab.MouseMove += OnTabMove;
        tab.MouseLeftButtonUp += OnTabUp;
        tab.MouseRightButtonDown += OnTabRight;
        tab.LostMouseCapture += OnTabLost;

        return tab;
    }

    private ContextMenu LayoutMenu()
    {
        var menu = new ContextMenu();
        var reset = new MenuItem { Header = Strings.T("S_DockReset") };

        reset.Click += (_, _) => ResetLayout();
        menu.Items.Add(reset);

        return menu;
    }

    // ------------------------------------------------------------ Ziehen

    /// <summary>
    /// Ein Ort, an dem ein gezogenes Feld landen kann: wo die Maus sein muss, und
    /// was die Markierung dabei zeigt - die Flaeche, die das Feld dann einnaehme.
    /// </summary>
    private readonly record struct Drop(DockZone Zone, int Group, bool AsTab, Rect Hit, Rect Show);

    private FrameworkElement? _pressed;
    private string? _dragPanel;
    private Point _start;
    private bool _dragging;
    private bool _swallowRightUp;
    private Window? _keys;
    private Drop? _target;
    private List<Drop> _targets = new();

    private void OnTabDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } tab) return;

        _pressed = tab;
        _dragPanel = id;
        _start = e.GetPosition(this);
        _dragging = false;

        tab.CaptureMouse();
        e.Handled = true;
    }

    private void OnTabMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || _dragPanel is null || !ReferenceEquals(sender, _pressed)) return;

        var at = e.GetPosition(this);

        // Erst ab ein paar Punkten Weg ist es ein Zug - sonst waere jedes Anklicken
        // eines Reiters mit einer zittrigen Hand ein Verschieben.
        if (!_dragging)
        {
            if (Math.Abs(at.X - _start.X) < 6 && Math.Abs(at.Y - _start.Y) < 6) return;

            _dragging = true;
            _targets = Targets(_dragPanel);

            // Escape am Fenster abgreifen: Die Tastatur steht selten auf der
            // Andockflaeche, sondern da, wo zuletzt geklickt wurde.
            _keys = Window.GetWindow(this);
            if (_keys is not null) _keys.PreviewKeyDown += OnDragKey;
        }

        _target = TargetAt(at);
        ShowMark();
    }

    private void OnTabUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed is null || !ReferenceEquals(sender, _pressed)) return;

        var panel = _dragPanel;
        var target = _target;
        bool dragged = _dragging;

        EndDrag();

        if (panel is null) return;

        if (!dragged)
        {
            Toggle(panel);
            return;
        }

        if (target is { } t) MovePanel(panel, t.Zone, t.Group, t.AsTab);
    }

    /// <summary>Rechts bricht ab - wie beim Ziehen an einem Regler.</summary>
    private void OnTabRight(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        EndDrag();

        _swallowRightUp = true;
        e.Handled = true;
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        // Ein neuer Rechtsklick - was vom letzten Abbruch noch offen war, gilt nicht mehr.
        _swallowRightUp = false;

        base.OnPreviewMouseRightButtonDown(e);
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);

        // Der Abbruch war das Druecken. Das Loslassen gehoert noch dazu und darf
        // kein Kontextmenue mehr oeffnen.
        if (!_swallowRightUp) return;

        _swallowRightUp = false;
        e.Handled = true;
    }

    private void OnTabLost(object sender, MouseEventArgs e)
    {
        if (_pressed is not null && ReferenceEquals(sender, _pressed)) EndDrag();
    }

    private void OnDragKey(object sender, KeyEventArgs e)
    {
        if (!_dragging || e.Key != Key.Escape) return;

        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        var pressed = _pressed;

        _pressed = null;
        _dragPanel = null;
        _dragging = false;
        _target = null;
        _targets = new();

        if (_keys is not null)
        {
            _keys.PreviewKeyDown -= OnDragKey;
            _keys = null;
        }

        if (pressed is not null && pressed.IsMouseCaptured) pressed.ReleaseMouseCapture();

        ShowMark();
    }

    /// <summary>
    /// Wohin sich ein Feld ziehen laesst - in Koordinaten dieser Flaeche.
    ///
    /// Jede Gruppe teilt sich in drei. Auf die Reiterleiste oder in die Mitte
    /// gezogen, wird das Feld ein weiterer Reiter - die Leiste zaehlt immer als
    /// Reiter, dorthin zieht man, was dazugehoeren soll, wie in jedem Browser. An den
    /// oberen oder unteren Rand (unter dem Bild: links oder rechts) wird es eine
    /// eigene Gruppe davor oder dahinter. So laesst sich auch eine Gruppe teilen:
    /// einen ihrer Reiter an ihren eigenen Rand ziehen.
    ///
    /// Eine leere Zone ist ein Streifen am Bildrand, dort, wo sie entstehen wuerde.
    ///
    /// Die eigene Gruppe bietet sich nicht als Reiterziel an: Dorthin zurueckgezogen
    /// heisst "doch nicht". Steht das Feld allein darin, auch nicht als Rand - jeder
    /// Ort an ihr ist dann derselbe Ort.
    /// </summary>
    private List<Drop> Targets(string dragged)
    {
        var found = new List<Drop>();

        var home = Layout.Find(dragged);
        bool alone = home is { } h && Layout.Zone(h.Zone)[h.Group].Panels.Count == 1;

        // Die Reiterleiste: 34 Punkte Reiter und ihre Linie darunter, samt Rahmen.
        const double bar = 36;

        foreach (var (zone, index, view) in _groupViews)
        {
            var area = Bounds(view);
            if (area.IsEmpty) continue;

            bool own = home is { } at && at.Zone == zone && at.Group == index;

            if (own && alone) continue;

            // Oben stehen die Griffe links (GroupView) - dort ist ihre Leiste, samt etwas Rand.
            var tabs = zone == DockZone.Top
                ? new Rect(area.Left, area.Top, Math.Min(bar, area.Width), area.Height)
                : new Rect(area.Left, area.Top, area.Width, Math.Min(bar, area.Height));
            var body = zone == DockZone.Top
                ? new Rect(tabs.Right, area.Top, Math.Max(0, area.Width - tabs.Width), area.Height)
                : new Rect(area.Left, tabs.Bottom, area.Width, Math.Max(0, area.Height - tabs.Height));

            if (!own) found.Add(new Drop(zone, index, true, tabs, area));

            if (DockLayout.IsHorizontal(zone))
            {
                double part = Math.Min(110, body.Width / 4);
                double half = area.Width / 2;

                found.Add(new Drop(zone, index, false,
                                   new Rect(body.Left, body.Top, part, body.Height),
                                   new Rect(area.Left, area.Top, half, area.Height)));
                found.Add(new Drop(zone, index + 1, false,
                                   new Rect(body.Right - part, body.Top, part, body.Height),
                                   new Rect(area.Left + half, area.Top, half, area.Height)));
            }
            else
            {
                double part = Math.Min(90, body.Height / 4);
                double half = area.Height / 2;

                found.Add(new Drop(zone, index, false,
                                   new Rect(body.Left, body.Top, body.Width, part),
                                   new Rect(area.Left, area.Top, area.Width, half)));
                found.Add(new Drop(zone, index + 1, false,
                                   new Rect(body.Left, body.Bottom - part, body.Width, part),
                                   new Rect(area.Left, area.Top + half, area.Width, half)));
            }

            if (!own) found.Add(new Drop(zone, index, true, area, area));
        }

        // Der Streifen einer ganz eingeklappten Seitenzone - samt einem Rand ins Bild
        // hinein, denn 34 Punkte trifft man beim Ziehen nicht auf Anhieb. Das Feld
        // wird dort eine eigene, offene Gruppe, und die Zone geht damit wieder auf.
        foreach (var zone in new[] { DockZone.Left, DockZone.Right })
        {
            if (!Layout.IsFolded(zone) || !_zoneViews.TryGetValue(zone, out var folded)) continue;

            var area = Bounds(folded);
            if (area.IsEmpty) continue;

            double wide = zone == DockZone.Left ? Layout.LeftWidth : Layout.RightWidth;
            const double reach = 52;

            var (hit, show) = zone == DockZone.Left
                ? (new Rect(area.Left, area.Top, area.Width + reach, area.Height),
                   new Rect(area.Left, area.Top, wide, area.Height))
                : (new Rect(area.Left - reach, area.Top, area.Width + reach, area.Height),
                   new Rect(area.Right - wide, area.Top, wide, area.Height));

            found.Add(new Drop(zone, 0, false, hit, show));
        }

        Rect centre = Center is not null && Center.IsLoaded ? Bounds(Center) : Rect.Empty;

        if (!centre.IsEmpty)
        {
            const double edge = 66;

            foreach (var (zone, groups) in Layout.Zones())
            {
                if (groups.Count > 0) continue;

                // Gezeigt wird, was entstuende, und wie gross - damit man vorher sieht,
                // wie viel Bild es kostet.
                double wide = Math.Min(zone == DockZone.Left ? Layout.LeftWidth : Layout.RightWidth, centre.Width / 2);
                double high = Math.Min(Layout.BottomHeight, centre.Height / 2);

                // Oben: ein Feld mit eigener Hoehe wird eine Leiste, sonst so hoch wie die Zone.
                double over = PanelOf(dragged) is { } moved && GetFitsContent(moved)
                    ? Math.Min(64, centre.Height / 3)
                    : Math.Min(Layout.TopHeight, centre.Height / 2);

                var (hit, show) = zone switch
                {
                    DockZone.Left => (new Rect(centre.Left, centre.Top, edge, centre.Height),
                                      new Rect(centre.Left, centre.Top, wide, centre.Height)),
                    DockZone.Right => (new Rect(centre.Right - edge, centre.Top, edge, centre.Height),
                                       new Rect(centre.Right - wide, centre.Top, wide, centre.Height)),
                    // Nach oben zieht man gern ueber den oberen Rand hinaus, auf die Werkzeugleiste
                    // - auch dort landet es oben. Ohne das traf man nur einen schmalen Streifen im Bild.
                    DockZone.Top => (new Rect(centre.Left, centre.Top - 160, centre.Width, edge + 160),
                                     new Rect(centre.Left, centre.Top, centre.Width, over)),
                    _ => (new Rect(centre.Left, centre.Bottom - edge, centre.Width, edge),
                          new Rect(centre.Left, centre.Bottom - high, centre.Width, high)),
                };

                found.Add(new Drop(zone, 0, false, hit, show));
            }
        }

        return found;
    }

    private Rect Bounds(FrameworkElement element)
    {
        try
        {
            return element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    private Drop? TargetAt(Point at)
    {
        foreach (var target in _targets)
            if (target.Hit.Contains(at)) return target;

        return null;
    }

    /// <summary>Zeigt, wo das Feld landen wuerde - und als was.</summary>
    private void ShowMark()
    {
        if (_mark is null || _markText is null) return;

        if (!_dragging || _target is not { } target)
        {
            _mark.Visibility = Visibility.Collapsed;
            _markText.Visibility = Visibility.Collapsed;
            return;
        }

        var area = target.Show;

        Canvas.SetLeft(_mark, area.Left + 2);
        Canvas.SetTop(_mark, area.Top + 2);
        _mark.Width = Math.Max(0, area.Width - 4);
        _mark.Height = Math.Max(0, area.Height - 4);
        _mark.Visibility = Visibility.Visible;

        _markText.Text = Strings.T(target.AsTab ? "S_DockAsTab" : "S_DockAsGroup");
        _markText.Visibility = Visibility.Visible;

        Canvas.SetLeft(_markText, area.Left + 10);
        Canvas.SetTop(_markText, area.Top + 10);
    }
}
