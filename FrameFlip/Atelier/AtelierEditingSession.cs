using FrameFlip.Configuration;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Atelier;

/// <summary>
/// Wo das Rezept des Ateliers liegt: Grundregler und Werkzeuge des fertigen Bildes, der
/// Ebenenstapel und der Graph des Knotenmodus.
///
/// Heute sind das Felder der Einstellungen (<see cref="SettingsRecipeStore"/>). Die Ablage
/// je Sequenz aus docs/Projekte-und-Masken.md (Phase C) ist eine zweite Ablage hinter
/// derselben Schnittstelle - die Seite merkt davon nichts.
/// </summary>
internal interface IAtelierRecipeStore
{
    ImageAdjustments? Adjustments { get; set; }

    GradingStack? Grading { get; set; }

    LayerStack? Layers { get; set; }

    /// <summary>Der Graph als Text - null, solange das Atelier mit dem Stapel rechnet.</summary>
    string? Nodes { get; set; }
}

/// <summary>
/// Das Rezept in den Einstellungen - unter denselben Feldnamen wie bisher, damit eine
/// vorhandene config.json unveraendert gelesen und geschrieben wird.
///
/// Die Grundregler (<see cref="AppSettings.Adjustments"/>) teilt sich das Atelier dabei mit
/// dem Vorschaufenster. Das war schon so und bleibt es hier; ob sie je Projekt gelten
/// sollen, entscheidet die Projektablage.
/// </summary>
internal sealed class SettingsRecipeStore(AppSettings settings) : IAtelierRecipeStore
{
    public ImageAdjustments? Adjustments
    {
        get => settings.Adjustments;
        set => settings.Adjustments = value;
    }

    public GradingStack? Grading
    {
        get => settings.Grading;
        set => settings.Grading = value;
    }

    public LayerStack? Layers
    {
        get => settings.Layers;
        set => settings.Layers = value;
    }

    public string? Nodes
    {
        get => settings.AtelierNodes;
        set => settings.AtelierNodes = value;
    }
}

/// <summary>
/// Besitzt das Rezept des Ateliers und meldet jede Aenderung daran.
///
/// Die Seite liest und schreibt das Rezept nur noch hier, nicht mehr an den
/// Einstellungen vorbei. Damit gibt es eine Stelle, an der feststeht, ob seit dem letzten
/// Festhalten etwas geaendert wurde (<see cref="Dirty"/>), und eine, an der sich die
/// Ablage austauschen laesst.
///
/// Was gezeigt wird - die Werkzeuge im Farbstreifen, der Stapel im Ebenenstreifen, der
/// Graph im Editor -, bleibt bei der Seite und ihren Panels (Refactoring-Studio, S2).
/// </summary>
internal sealed class AtelierEditingSession
{
    private IAtelierRecipeStore _store;

    internal AtelierEditingSession(IAtelierRecipeStore store) => _store = store;

    /// <summary>Die Ablage, hinter der das Rezept gerade liegt.</summary>
    internal IAtelierRecipeStore Store => _store;

    /// <summary>
    /// Tauscht die Ablage - ein anderes Projekt. Was die alte hielt, muss vorher
    /// festgehalten sein; die neue gilt als festgehalten, so wie sie gelesen wurde.
    /// </summary>
    public void Switch(IAtelierRecipeStore store)
    {
        _store = store;
        Revision++;
        Dirty = false;
    }

    /// <summary>Die Grundregler des fertigen Bildes.</summary>
    public ImageAdjustments? Adjustments
    {
        get => _store.Adjustments;
        set
        {
            _store.Adjustments = value;
            Touch();
        }
    }

    /// <summary>Die Werkzeuge des fertigen Bildes - als eigene Kopie, nicht der Stapel des Farbstreifens.</summary>
    public GradingStack? Grading
    {
        get => _store.Grading;
        set
        {
            _store.Grading = value;
            Touch();
        }
    }

    /// <summary>Der Ebenenstapel.</summary>
    public LayerStack? Layers
    {
        get => _store.Layers;
        set
        {
            _store.Layers = value;
            Touch();
        }
    }

    /// <summary>Der Graph als Text - null im Stapelmodus.</summary>
    public string? Nodes
    {
        get => _store.Nodes;
        set
        {
            _store.Nodes = value;
            Touch();
        }
    }

    /// <summary>Ob seit dem letzten <see cref="MarkKept"/> etwas geaendert wurde.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Zaehlt jede Aenderung - eine Speicherung, die mit einer aelteren Nummer endet, laesst Dirty stehen.</summary>
    public long Revision { get; private set; }

    /// <summary>Etwas am Rezept hat sich geaendert.</summary>
    public event Action? Changed;

    /// <summary>Der Stand mit dieser Nummer ist festgehalten. Kam seither etwas dazu, bleibt Dirty.</summary>
    public void MarkKept(long revision)
    {
        if (revision == Revision) Dirty = false;
    }

    private void Touch()
    {
        Dirty = true;
        Revision++;
        Changed?.Invoke();
    }
}
