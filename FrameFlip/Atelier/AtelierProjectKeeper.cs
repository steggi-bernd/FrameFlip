using System.IO;
using FrameFlip.Configuration;

namespace FrameFlip.Atelier;

/// <summary>Wie es um das Speichern des offenen Projekts steht - fuer die Anzeige.</summary>
internal enum AtelierSaveState
{
    /// <summary>Kein Projekt offen.</summary>
    None,

    /// <summary>Geaendert, noch nicht geschrieben.</summary>
    Unsaved,

    /// <summary>Geschrieben, seitdem nichts geaendert.</summary>
    Saved,

    /// <summary>Weder am Quellordner noch unter den Einstellungen liess sich schreiben.</summary>
    Failed,
}

/// <summary>
/// Haelt das Projekt der offenen Folge (docs/Projekte-und-Masken.md, Punkt 9).
///
/// Oeffnet das Atelier ein Bild einer anderen Folge, wird das bisherige Projekt
/// geschrieben und das neue gelesen - oder, gibt es keines, frisch begonnen. Ein Bild
/// derselben Folge wechselt kein Projekt, es wird nur als zuletzt gezeigtes gemerkt.
///
/// Geschrieben wird nach einer Ruhepause von <see cref="AutosaveDelay"/> nach der letzten
/// Aenderung, auf Knopfdruck, beim Wechsel und beim Ende. Der Abdruck entsteht auf dem
/// Oberflaechenfaden (das Rezept gehoert ihm), das Schreiben laeuft im Hintergrund - ein
/// Autosave mitten in einem Pinselstrich soll ihn nicht stocken lassen. Schreibvorgaenge
/// laufen der Reihe nach; das Ende wartet auf den letzten.
///
/// Einmalige Uebernahme: Das erste Projekt, das ohne eigene Datei geoeffnet wird, bekommt
/// das Rezept aus den Einstellungen - das bisherige, globale. Danach beginnt jede neue
/// Folge mit ihrem eigenen.
/// </summary>
internal sealed class AtelierProjectKeeper
{
    /// <summary>Wie lange nach der letzten Aenderung geschrieben wird.</summary>
    public static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(2);

    private readonly AtelierEditingSession _session;
    private readonly AtelierProjectStore _store;
    private readonly AppSettings _settings;
    private readonly Func<TimeSpan, Action, Action> _later;
    private readonly Action<Action> _dispatch;

    private Action? _cancelAutosave;
    private string? _frame;
    private bool _frameChanged;

    /// <param name="later">Ruft eine Aktion nach einer Weile auf dem Oberflaechenfaden; liefert, was sie absagt.</param>
    /// <param name="dispatch">Bringt das Ende eines Schreibvorgangs auf den Oberflaechenfaden - ohne darauf zu warten.</param>
    internal AtelierProjectKeeper(AtelierEditingSession session, AtelierProjectStore store, AppSettings settings,
                                  Func<TimeSpan, Action, Action> later, Action<Action> dispatch)
    {
        _session = session;
        _store = store;
        _settings = settings;
        _later = later;
        _dispatch = dispatch;

        _session.Changed += OnChanged;
    }

    /// <summary>Die Folge des offenen Projekts - null, solange keines offen ist.</summary>
    public SequenceKey? Current { get; private set; }

    public AtelierProjectStore Store => _store;

    /// <summary>Wann zuletzt geschrieben wurde - Ortszeit.</summary>
    public DateTime? SavedAt { get; private set; }

    public AtelierSaveState State { get; private set; }

    /// <summary>Der Stand hat sich geaendert: ungespeichert, geschrieben, fehlgeschlagen.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Ein Bild wird im Atelier gezeigt. True, wenn damit ein anderes Projekt offen ist -
    /// dann traegt die Sitzung dessen Rezept, und die Seite muss es neu anzeigen.
    /// </summary>
    public bool Enter(string imagePath)
    {
        if (SequenceKey.Of(imagePath) is not { } key) return false;

        string frame = Path.GetFileName(imagePath);

        if (key.Equals(Current))
        {
            if (frame != _frame)
            {
                _frame = frame;
                _frameChanged = true;
                ScheduleAutosave();
            }

            return false;
        }

        // Das bisherige Projekt wird geschrieben, bevor das neue sein Rezept bekommt.
        if (Current is not null && (_session.Dirty || _frameChanged)) Save();

        ProjectRecipeStore recipe;
        bool moved = false;

        if (_store.Load(key) is { } project)
        {
            recipe = ProjectRecipeStore.From(project);
        }
        else if (!_settings.AtelierRecipeMoved)
        {
            recipe = new ProjectRecipeStore
            {
                Adjustments = _settings.Adjustments,
                Grading = _settings.Grading,
                Layers = _settings.Layers,
                Nodes = _settings.AtelierNodes,
            };

            _settings.AtelierRecipeMoved = true;
            moved = true;
        }
        else
        {
            recipe = new ProjectRecipeStore();
        }

        Current = key;
        _frame = frame;
        _frameChanged = false;

        _session.Switch(recipe);

        // Uebernommen gehoert es gleich in eine Datei - sonst stuende es nur im Speicher,
        // und die Einstellungen hielten es fuer schon uebernommen.
        if (moved) Save();
        else Report(AtelierSaveState.Saved);

        return true;
    }

    /// <summary>Welches Bild in einem Projekt zuletzt offen war - aus seiner Datei, oder null.</summary>
    public string? RememberedFrame(SequenceKey key)
    {
        if (key.Equals(Current)) return _frame is null ? null : Path.Combine(key.Folder, _frame);

        return _store.Load(key)?.Frame is { Length: > 0 } frame ? Path.Combine(key.Folder, frame) : null;
    }

    /// <summary>
    /// Schreibt das offene Projekt jetzt - auf Knopfdruck, beim Wechsel, nach der Ruhepause.
    /// <paramref name="wait"/>: erst zurueck, wenn geschrieben ist (beim Ende).
    /// </summary>
    public void Save(bool wait = false)
    {
        CancelAutosave();

        if (Current is not { } key || _session.Store is not ProjectRecipeStore recipe)
        {
            if (wait) Wait();
            return;
        }

        long revision = _session.Revision;
        var project = recipe.ToProject(_frame);
        _frameChanged = false;

        _store.Enqueue(key, project, written =>
        {
            _dispatch(() =>
            {
                if (written)
                {
                    // Kam waehrend des Schreibens etwas dazu, bleibt es ungespeichert.
                    _session.MarkKept(revision);
                    SavedAt = DateTime.Now;
                }

                Report(!written ? AtelierSaveState.Failed
                       : _session.Dirty ? AtelierSaveState.Unsaved
                       : AtelierSaveState.Saved);
            });
        });

        if (wait) Wait();
    }

    /// <summary>Schreibt, was noch nicht geschrieben ist - ohne darauf zu warten. Wenn die Seite geht.</summary>
    public void Settle()
    {
        if (Current is not null && (_session.Dirty || _frameChanged)) Save();
    }

    /// <summary>Schreibt, was noch nicht geschrieben ist, und wartet darauf - beim Ende.</summary>
    public void Flush()
    {
        if (Current is not null && (_session.Dirty || _frameChanged)) Save(wait: true);
        else Wait();
    }

    private static void Wait() => AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));

    private void OnChanged()
    {
        if (Current is null) return;

        Report(AtelierSaveState.Unsaved);
        ScheduleAutosave();
    }

    private void ScheduleAutosave()
    {
        CancelAutosave();
        _cancelAutosave = _later(AutosaveDelay, () =>
        {
            _cancelAutosave = null;
            if (_session.Dirty || _frameChanged) Save();
        });
    }

    private void CancelAutosave()
    {
        _cancelAutosave?.Invoke();
        _cancelAutosave = null;
    }

    private void Report(AtelierSaveState state)
    {
        if (State == state && state != AtelierSaveState.Saved) return;

        State = state;
        StateChanged?.Invoke();
    }
}
