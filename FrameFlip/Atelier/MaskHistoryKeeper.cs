using System.Globalization;
using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Atelier;

/// <summary>
/// Die Verlaeufe der gemalten Masken einer Folge: gelesen, sobald eine Maske bemalt wird,
/// und geschrieben, sobald ein neuer Stand entsteht - nicht bei jedem Strich.
///
/// Jede Maske hat ihren eigenen Verlauf, an ihrer festen Kennung (<see cref="LayerMask.Id"/>).
/// Eine Maske, die je Bild einen eigenen Anstrich hat, hat je Bild einen Verlauf. Die
/// Dateien liegen neben der Projektdatei, in "verlauf" im Ordner ".ffdata" - dort, wo
/// auch das Projekt liegt, am Quellordner oder unter den Einstellungen.
/// </summary>
internal sealed class MaskHistoryKeeper
{
    private const string Folder = "verlauf";

    private readonly AtelierProjectStore _store;
    private readonly Dictionary<string, MaskHistory> _histories = new(StringComparer.Ordinal);
    private SequenceKey? _key;

    internal MaskHistoryKeeper(AtelierProjectStore store) => _store = store;

    /// <summary>Eine andere Folge: Die Verlaeufe der vorigen sind geschrieben, sie werden vergessen.</summary>
    public void Enter(SequenceKey? key)
    {
        if (Equals(key, _key)) return;

        _key = key;
        _histories.Clear();
    }

    /// <summary>Unter welchem Namen der Verlauf eines Anstrichs steht: Kennung der Maske und das Bild, fuer das er gilt.</summary>
    public static string NameOf(LayerMask mask, int number)
        => mask.EnsureId() + "-" + (mask.PaintLocked ? "alle" : number.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Der Verlauf dieses Anstrichs - gelesen oder neu begonnen - und von jetzt an
    /// beobachtet. Vor dem ersten Strich aufgerufen, kennt er den Stand davor.
    /// </summary>
    public MaskHistory Watch(LayerMask mask, int number, PaintedMask paint)
    {
        string name = NameOf(mask, number);

        if (!_histories.TryGetValue(name, out var history) || !history.Fits(paint))
        {
            history = Load(name) is { } read && read.Fits(paint) ? read : MaskHistory.Start(paint);
            _histories[name] = history;
        }

        history.Follow(paint);
        return history;
    }

    /// <summary>Der Verlauf, falls er schon bekannt oder gespeichert ist - sonst null. Beginnt keinen.</summary>
    public MaskHistory? Find(LayerMask mask, int number, PaintedMask paint)
    {
        string name = NameOf(mask, number);

        if (_histories.TryGetValue(name, out var known) && known.Fits(paint)) return known;

        return Load(name) is { } read && read.Fits(paint) ? _histories[name] = read : null;
    }

    /// <summary>Ein Strich ist zu Ende. True, wenn ein neuer Stand entstanden ist - er wird dann geschrieben.</summary>
    public bool Record(LayerMask mask, int number, PaintedMask paint, PaintStroke? stroke)
    {
        var history = Watch(mask, number, paint);
        if (!history.Record(paint, stroke)) return false;

        Save(NameOf(mask, number), history);
        return true;
    }

    /// <summary>Schreibt den Verlauf - im Hintergrund. Der Abdruck entsteht jetzt, geschrieben wird er spaeter.</summary>
    public void Save(LayerMask mask, int number, MaskHistory history) => Save(NameOf(mask, number), history);

    private void Save(string name, MaskHistory history)
    {
        if (_key is not { } key) return;

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(history, AtelierProjectStore.Options);
        _store.EnqueueSide(key, Path.Combine(Folder, name + ".json"), json);
    }

    private MaskHistory? Load(string name)
    {
        if (_key is not { } key || _store.LoadSide(key, Path.Combine(Folder, name + ".json")) is not { } bytes) return null;

        try
        {
            return JsonSerializer.Deserialize<MaskHistory>(bytes, AtelierProjectStore.Options);
        }
        catch (JsonException e)
        {
            // Ein unlesbarer Verlauf ist kein Grund, nicht weiterzumalen - er beginnt neu.
            SettingsStore.Trace("Maskenverlauf nicht lesbar: " + name + " - " + e.Message);
            return null;
        }
    }
}
