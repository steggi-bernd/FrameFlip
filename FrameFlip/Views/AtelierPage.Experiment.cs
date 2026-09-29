using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Versuch (docs/Atelier-UX-Versuch.md): die neu geordnete Bedienung. Was schon da ist, wird
/// umgehaengt statt nachgebaut - der Bild/Folge-Schalter, der Vergleich, die Einstellungen der
/// Ebene, die Ausgabe. Ihre Logik bleibt, wo sie war.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Die Vorgabe der Favoriten: Tonwert, Kurven, Weissabgleich.</summary>
    private static readonly string[] DefaultFavourites = { "S_Levels", "S_Curves", "S_WhiteBalance" };

    /// <summary>Wann das Aufklappfeld der Ausgabe zuletzt zuging - ein Klick auf den Knopf schliesst es sonst und oeffnet es gleich wieder.</summary>
    private DateTime _exportClosedAt;

    private void SetUpExperiment()
    {
        // Bild/Folge aus dem Band in die Folgenleiste.
        if (ToolBand.FrameSwitch.Parent is Panel band) band.Children.Remove(ToolBand.FrameSwitch);
        FrameModeHost.Content = ToolBand.FrameSwitch;

        // Der Vergleich in den Streifen ueber dem Bild.
        if (CompareButton.Parent is Panel controls) controls.Children.Remove(CompareButton);
        CompareHost.Content = CompareButton;

        // Die Einstellungen der Ebene aus der Liste in die Einstellungen.
        if (Layers.LayerTools.Parent is Panel list) list.Children.Remove(Layers.LayerTools);
        Tools.LayerToolsHost.Child = Layers.LayerTools;
        Layers.OpenMaskSection();

        // Die Ausgabe: im Aufklappfeld, ihr Stand in der Folgenleiste.
        FrameExportBar.SetBinding(RangeBase.ValueProperty, new Binding("Value") { Source = BatchProgress });
        FrameExportProgress.SetBinding(VisibilityProperty, new Binding("Visibility") { Source = BatchProgress });
        FrameExportText.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = BatchStatus });
        FormatWarning.SetBinding(VisibilityProperty, new Binding("Visibility") { Source = EightBitNote });

        DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(Border))
            .AddValueChanged(ExportBar, (_, _) => ExportOpenButton.IsEnabled = ExportBar.Visibility == Visibility.Visible);

        FormatBox.SelectionChanged += (_, _) => ShowFormat();
        ShowFormat();

        ExportPopup.Closed += (_, _) => _exportClosedAt = DateTime.UtcNow;

        // Der Katalog.
        Catalog.Entries = CatalogEntries;
        Catalog.StateOf = CatalogStateOf;
        Catalog.Favourites = () => Favourites;
        Catalog.Chosen += entry => UseTool(entry);
        Catalog.ChosenOnWhole += entry =>
        {
            if (!InNodes) Layers.SelectWhole();
            UseTool(entry);
        };
        Catalog.FavouriteToggled += ToggleFavourite;
        Catalog.WholeWanted += () =>
        {
            Layers.SelectWhole();
            Catalog.Retarget(CatalogTarget(), onLayer: false);
        };

        ToolBand.SearchWanted += OpenCatalog;
        Tools.AddEffectWanted += OpenCatalog;
        Tools.FavouriteWanted += key =>
        {
            if (ToolCatalog.All.FirstOrDefault(e => e.Key == key) is { } entry) UseTool(entry);
        };

        Layers.Editing += _ => AfterTargetChanged();
        ShowFavourites();
    }

    /// <summary>Nach jedem Wechsel des Ziels: was zu ihm gehoert.</summary>
    private void AfterTargetChanged()
    {
        Tools.ShowLayerTools(!InNodes && Layers.EditedLayer is not null);
        ShowSoloSwitch();
        ShowFavourites();
    }

    private void ShowFormat()
        => FormatText.Text = FormatBox.SelectedItem is ComboBoxItem { Content: var content } ? content?.ToString() : FormatBox.SelectedItem?.ToString();

    private void OnExportOpenClicked(object sender, RoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _exportClosedAt).TotalMilliseconds < 250) return;

        ExportPopup.IsOpen = !ExportPopup.IsOpen;
    }

    // ---------------------------------------------------------------- Katalog

    /// <summary>Was in Frage kommt: im Stapel ohne das, was es nur als Knoten gibt.</summary>
    private IEnumerable<ToolEntry> CatalogEntries()
        => ToolCatalog.All.Where(e => InNodes || !e.NodesOnly);

    private string CatalogTarget()
    {
        if (InNodes) return SelectedNode is { } node ? NodeTitles.For(node) : Strings.T("S_NodeCrumb");

        return Layers.EditedLayer is { } layer
            ? Strings.T("S_CatalogTargetLayer", layer.Name)
            : Strings.T("S_WholeRow");
    }

    private void OpenCatalog()
    {
        if (_frame is null) return;

        Catalog.Open(CatalogTarget(), onLayer: !InNodes && Layers.EditedLayer is not null);
    }

    /// <summary>Was ein Eintrag am aktuellen Ziel tut - vor dem Klick.</summary>
    internal CatalogState CatalogStateOf(ToolEntry entry)
    {
        string category = Strings.T(entry.Category);

        if (InNodes)
            return new CatalogState(Strings.T("S_CatalogAdd"), true, category + " · " + Strings.T("S_CatalogAsNode"));

        switch (entry.Action)
        {
            case ToolAction.Brush:
                return new CatalogState(Strings.T("S_CatalogUse"), true, category);

            case ToolAction.Effect when entry.Section is { } section:
                if (Layers.EditedLayer is not null && Tools.PictureOnlySection(section))
                    return new CatalogState(Strings.T("S_CatalogAdd"), false, Strings.T("S_CatalogPictureOnly"), Strings.T("S_CatalogToWhole"));

                if (Tools.MissingPass(section) is { } pass)
                    return new CatalogState(Strings.T("S_CatalogAdd"), false, Strings.T("S_CatalogNeedsPass", pass));

                string target = Layers.EditedLayer?.Name ?? Strings.T("S_WholeRow");

                return Tools.SectionInStack(section)
                    ? new CatalogState(Strings.T("S_CatalogEdit"), true, Strings.T("S_CatalogThere", target))
                    : new CatalogState(Strings.T("S_CatalogAdd"), true, category);

            default:
                return new CatalogState(Strings.T("S_CatalogAdd"), false, Strings.T("S_CatalogNodesOnly"));
        }
    }

    // ---------------------------------------------------------------- Favoriten

    private List<string> Favourites => _settings.AtelierFavourites ??= DefaultFavourites.ToList();

    private void ToggleFavourite(string key)
    {
        if (!Favourites.Remove(key)) Favourites.Add(key);

        _persist(_settings);
        ShowFavourites();
    }

    private void ShowFavourites()
    {
        var entries = CatalogEntries().ToList();

        Tools.ShowFavourites(Favourites
            .Select(key => entries.FirstOrDefault(e => e.Key == key))
            .OfType<ToolEntry>()
            .Select(entry =>
            {
                var state = _frame is null ? new CatalogState("", true, "") : CatalogStateOf(entry);
                return new FavouriteItem(entry.Key, entry.Glyph, Strings.T(entry.TitleKey), state.Enabled,
                                         state.Enabled ? null : state.Subtitle);
            }));
    }

    // ---------------------------------------------------------------- Ergebnis, Maske, Ueberlagerung

    private void ShowSoloSwitch()
    {
        var layer = InNodes ? null : Layers.EditedLayer;
        bool masked = layer is { Mask.Kind: not MaskKind.None };

        SoloSwitch.Visibility = masked ? Visibility.Visible : Visibility.Collapsed;
        if (!masked) return;

        var view = _stackSolo is { } solo && ReferenceEquals(solo.Layer, layer) ? solo.View : (SoloView?)null;

        SoloResultButton.IsChecked = view is null;
        SoloMaskButton.IsChecked = view == SoloView.Mask;
        SoloVeilButton.IsChecked = view == SoloView.Veil;
    }

    private void OnStripSolo(object sender, RoutedEventArgs e)
    {
        if (Layers.EditedLayer is not { } layer) return;

        var current = _stackSolo is { } solo && ReferenceEquals(solo.Layer, layer) ? solo.View : (SoloView?)null;

        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "Mask" when current != SoloView.Mask:
                IsolateStack(layer, SoloView.Mask);
                break;

            case "Veil" when current != SoloView.Veil:
                IsolateStack(layer, SoloView.Veil);
                break;

            case "Result":
                EndStackSolo(render: true);
                ShowViewer();
                break;
        }

        ShowSoloSwitch();
    }

    // ---------------------------------------------------------------- Vorher/Nachher

    /// <summary>Ob das Original stehen bleibt - nach einem kurzen Klick oder mit der Taste O.</summary>
    private bool _compareLatched;

    /// <summary>Wann der Knopf gedrueckt wurde: kurz heisst umschalten, lang heisst nur hinsehen.</summary>
    private DateTime _comparePressed;

    /// <summary>Ob das Original gerade stehen bleibt - fuer die Probe.</summary>
    internal bool CompareLatched => _compareLatched;

    /// <summary>
    /// Der Klick ohne Maus - Leertaste, Eingabe, Bedienhilfen. Mit der Maus haben Druecken und
    /// Loslassen schon entschieden; dann kommt der Klick direkt danach und wird uebergangen.
    /// </summary>
    private void OnCompareClicked(object sender, RoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _comparePressed).TotalMilliseconds < 600) return;

        ToggleCompare();
    }

    /// <summary>Vorher/Nachher umschalten - der Klick auf den Knopf und die Taste O.</summary>
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
        if (!_compareLatched && !_showingOriginal) return;

        _compareLatched = false;
        _showingOriginal = false;
        ShowCompare();
    }

    private void ShowCompare()
    {
        CompareBadge.Visibility = _showingOriginal ? Visibility.Visible : Visibility.Collapsed;
        CompareBadgeText.Text = Strings.T(_compareLatched ? "S_BeforeBadgeLatched" : "S_BeforeBadge");
    }
}
