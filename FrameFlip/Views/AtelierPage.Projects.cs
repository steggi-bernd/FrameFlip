using System.Windows;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Das Projekt der offenen Folge: Jede Sequenz hat ihr eigenes Rezept in einer
/// Projektdatei neben den Bildern, gespeichert von selbst und auf Knopfdruck. Siehe
/// <see cref="AtelierProjectKeeper"/> und docs/Projekte-und-Masken.md, Punkt 9.
/// </summary>
public partial class AtelierPage
{
    private AtelierProjectKeeper _projects = null!;

    /// <summary>Das Projekt der offenen Folge - fuer die Probe.</summary>
    internal AtelierProjectKeeper Projects => _projects;

    /// <summary>Das Rezept - fuer die Probe.</summary>
    internal AtelierEditingSession Recipe => _recipe;

    /// <summary>
    /// Die Folge, an der das Atelier arbeitet - oder, solange noch kein Bild steht, die des
    /// Bildes, das es beim ersten Anzeigen oeffnen wird. Null: keine.
    /// </summary>
    internal SequenceKey? ProjectKey
    {
        get
        {
            if (_projects.Current is { } open) return open;

            string? path = _path ?? _settings.AtelierImage;
            return path is { Length: > 0 } ? SequenceKey.Of(path) : null;
        }
    }

    /// <summary>Ein Bild steht im Atelier - fuer die Uebersicht, die seine Folge zeigen soll.</summary>
    public event Action<string>? ImageShown;

    private void SetUpProjects()
    {
        _projects = new AtelierProjectKeeper(_recipe, new AtelierProjectStore(), _settings, Later,
                                             action => Dispatcher.BeginInvoke(action));

        _projects.StateChanged += ShowSaveState;

        // Die Seite geht - ein anderer Reiter, ein anderes Fenster: Was noch nicht
        // geschrieben ist, wird jetzt geschrieben, ohne darauf zu warten.
        Unloaded += (_, _) =>
        {
            if (InNodes) KeepNodes();
            _projects.Settle();
        };

        // Beim Ende des Programms schreiben, was noch nicht geschrieben ist - auch wenn
        // das Fenster dabei nicht mehr eigens geschlossen wird.
        Dispatcher.ShutdownStarted += (_, _) => Flush();

        ShowSaveState();
    }

    /// <summary>Eine Aktion nach einer Weile - absagbar.</summary>
    private Action Later(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };

        timer.Start();
        return timer.Stop;
    }

    /// <summary>
    /// Schreibt das offene Projekt und wartet darauf - beim Schliessen des Fensters und beim
    /// Ende des Programms.
    /// </summary>
    public void Flush()
    {
        // Ein Zug, der gerade laeuft, steht im Graphen und noch nicht im Rezept.
        if (InNodes) KeepNodes();

        _projects.Flush();
    }

    /// <summary>Speichern auf Knopfdruck - oder mit Strg+S.</summary>
    internal void SaveProject()
    {
        if (InNodes) KeepNodes();

        _projects.Save();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e) => SaveProject();

    /// <summary>Was die Kopfzeile zum Speichern sagt.</summary>
    private void ShowSaveState()
    {
        SaveButton.IsEnabled = _projects.Current is not null;

        SaveText.Text = _projects.State switch
        {
            AtelierSaveState.Saved when _projects.SavedAt is { } at => Strings.T("S_ProjectSavedAt", at.ToString("HH:mm")),
            AtelierSaveState.Saved => Strings.T("S_ProjectOpen"),
            AtelierSaveState.Unsaved => Strings.T("S_ProjectUnsaved"),
            AtelierSaveState.Failed => Strings.T("S_ProjectSaveFailed"),
            _ => "",
        };

        SaveText.ToolTip = _projects.Store.LastWritten ??
                           (_projects.Current is { } key ? AtelierProjectStore.PrimaryPath(key) : null);
    }

    /// <summary>
    /// Ein anderes Projekt ist offen - sein Rezept wird angezeigt.
    ///
    /// Der Verlauf von Rueckgaengig gehoert zum Projekt, das man verlaesst: Ein Schritt
    /// zurueck darf nicht das Rezept einer anderen Folge hervorholen. Der Knotenmodus
    /// richtet sich nach dem Projekt - er war eine Einbahnstrasse, solange es ein Rezept
    /// fuer alles gab; mit einem je Folge kann die naechste wieder im Stapel rechnen.
    /// </summary>
    private void ApplyProject()
    {
        _undo.Clear();
        _redo.Clear();
        _valueEditOpen = false;

        if (_recipe.Nodes is { Length: > 0 } json && NodeGraph.Load(json) is { } graph && graph.Problems().Count == 0)
        {
            _graph = graph;
            _cache.Clear();
            _viewer = null;

            EnterNodes();
            NodeView.Frame();
        }
        else if (InNodes)
        {
            LeaveNodes();
        }

        // Im Stapel: was das fertige Bild rechnet, kommt aus dem neuen Rezept. Den
        // Farbstreifen bindet der Ebenenstreifen gleich selbst neu, wenn er den Stapel
        // laedt - an das Bild oder an die gewaehlte Ebene. Hier an das Bild zu binden,
        // hiesse, dass der Streifen seinen Stand meldet, bevor der neue Stapel steht:
        // Der alte Stapel des Ebenenstreifens landete dann im Rezept des neuen Projekts.
        if (!InNodes)
        {
            _finalAdjustments = _recipe.Adjustments ?? Imaging.ImageAdjustments.Neutral;
            _finalGrading = (_recipe.Grading ?? new Imaging.Grading.GradingStack()).Prepare();
        }
    }

    /// <summary>
    /// Zurueck in den Stapel - fuer ein Projekt, das keinen Graphen hat. Das Gegenstueck zu
    /// <see cref="EnterNodes"/>.
    /// </summary>
    private void LeaveNodes()
    {
        _graph = null;
        _viewer = null;
        _cache.Clear();

        NodeView.Graph = null;
        NodeLayers.Visibility = Visibility.Collapsed;
        Layers.Visibility = Visibility.Visible;

        Tools.LeaveNodes();

        if (_tool == AtelierTool.Nodes) MouseTools.Select(AtelierTool.Move, notify: true);

        ShowNodeMode();
        ShowLayerCount();
        ShowPlacement();
    }
}
