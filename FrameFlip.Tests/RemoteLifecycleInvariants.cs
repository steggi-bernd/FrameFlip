using System.Reflection;
using FrameFlip.Configuration;
using FrameFlip.Lifecycle;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>Verbindungsregeln mit synthetischer Kopplung, ohne Bridge, Netzwerk oder persoenliche Einstellungen.</summary>
public static class RemoteLifecycleInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var key = PairingKey.Create();
        var settings = new AppSettings
        {
            RemoteEnabled = true, PairingSecret = PairingStore.Protect(key), RelayHost = "relay.example",
            AdaptiveResources = false,
        };
        Check.Group("Remote-Lebenszyklus - Voraussetzungen und Zustandsweitergabe");
        Check.That(settings.PairingSecret.Length > 0, "synthetischer Testschluessel laesst sich ohne Konfigurationsdatei schuetzen");
        MissingPrerequisites(settings);
        ReplaceWithoutWaiting(settings, key);
        SettingsChanges(settings);
        InvalidRelay(settings);
        FailedConstruction(settings);
        LoadMonitorAfterFailedRestart(settings);
        HostDisposal(settings);
    }

    private static void MissingPrerequisites(AppSettings settings)
    {
        foreach (string reason in new[] { "ausgeschaltet", "ohne Bridge", "ohne Schluessel", "defekter Schluessel" })
        {
            using var host = new Harness(settings);
            switch (reason)
            {
                case "ausgeschaltet": host.Settings.RemoteEnabled = false; break;
                case "ohne Bridge": host.BridgeAvailable = false; break;
                case "ohne Schluessel": host.Settings.PairingSecret = string.Empty; break;
                case "defekter Schluessel": host.Settings.PairingSecret = "kein DPAPI-Paket"; break;
            }
            host.Restart();
            Check.That(host.Links.Count == 0 && host.Current is null, $"{reason}: kein Verbindungsaufbau");
            Check.That(host.Changes.SequenceEqual(new bool[] { false }), $"{reason}: Lastbedarf wird ohne Remote neu bewertet");
        }

        using var running = new Harness(settings);
        running.Restart();
        running.BridgeAvailable = false;
        running.Restart();
        Check.That(running.Current is null && running.Links.Single().Disposals == 1
                   && running.Changes.SequenceEqual(new bool[] { true, false }),
                   "entfallene Bridge beendet eine vorhandene Verbindung und meldet den Lastbedarf neu");
    }

    private static void ReplaceWithoutWaiting(AppSettings settings, PairingKey key)
    {
        using var host = new Harness(settings);
        host.Restart();
        var first = host.Links.Single();
        Check.That(first.Starts == 1 && ReferenceEquals(host.Current, first), "gueltige Kopplung erzeugt und startet genau eine Verbindung");
        Check.That(first.SawCurrentAtStart && host.Changes.SequenceEqual(new bool[] { true }),
                   "Start kennt die aktuelle Verbindung, danach wird ihr Lastbedarf gemeldet");
        Check.That(host.Invites.Single().Relay == settings.RelayHost && host.Invites.Single().Key.RoomId == key.RoomId,
                   "Relay-Adresse und entschluesselter Testschluessel erreichen die Verbindungsfabrik");
        first.State = RelayState.Waiting;
        Check.That(host.State == RelayState.Waiting, "UI liest den aktuellen Wartezustand");
        first.State = RelayState.Paired;
        Check.That(host.State == RelayState.Paired, "UI sieht spaetere Kopplung ohne Verbindungswechsel");
        first.State = RelayState.Off;
        Check.That(host.Controller.HasConnection && host.State == RelayState.Off,
                   "Verbindungsbesitz bleibt vom momentanen Relay-Zustand unabhaengig");

        host.Restart();
        var second = host.Links.Last();
        Check.That(first.Disposals == 1 && !first.Disposal.Task.IsCompleted && second.Starts == 1,
                   "Neustart beginnt die neue Verbindung, waehrend die alte noch asynchron schliesst");
        Check.That(first.SawNoCurrentAtDispose && host.EmptyAtCreate.All(value => value),
                   "alte Verbindung ist vor Freigabe und vor neuer Konstruktion nicht mehr aktiv");
        Check.That(host.Steps.SequenceEqual(new[] { "create:1", "start:1", "changed:True", "dispose:1", "create:2", "start:2", "changed:True" }),
                   "Austausch meldet erst die fertig gestartete neue Verbindung an den Lastbedarf");
        first.Disposal.SetResult();
        Check.That(ReferenceEquals(host.Current, second), "spaetes Ende der alten Verbindung loescht die neue nicht");

        var disabled = host.Settings.Clone();
        disabled.RemoteEnabled = false;
        host.Update(disabled);
        Check.That(second.Disposals == 1 && host.Current is null && host.State is null,
                   "Abschalten gibt die zweite Verbindung frei und zeigt keinen Remote-Zustand mehr");
        Check.That(host.Changes.SequenceEqual(new bool[] { true, true, false }), "Abschalten meldet den entfallenen Remote-Lastbedarf");
    }

    private static void SettingsChanges(AppSettings settings)
    {
        Check.Group("Remote-Lebenszyklus - Einstellungen ohne unnoetige Neuverbindung");
        foreach (var (label, change) in new (string, Action<AppSettings>)[]
        {
            ("Sprache", value => value.Language = "en"),
            ("Wiedergabe", value => value.Fps = 60),
            ("Dateifreigabe", value => value.FileAccessEnabled = true),
            ("Bibliotheksfreigabe", value => value.LibraryAccessEnabled = true),
            ("Lastregelung", value => value.AdaptiveResources = true),
            ("Decoderlimit", value => value.MaxDecoderThreads = 3),
            ("gleiche Werte", _ => { }),
        })
        {
            using var host = new Harness(settings);
            host.Restart();
            var current = host.Current;
            var next = host.Settings.Clone();
            change(next);
            host.Update(next);
            Check.That(ReferenceEquals(host.Current, current) && host.Links.Count == 1 && host.Links[0].Disposals == 0
                       && host.Changes.Count == 1, $"{label}: bestehende Verbindung bleibt erhalten");
        }

        foreach (string field in new[] { "Relay", "Schluessel" })
        {
            using var host = new Harness(settings);
            host.Restart();
            var next = host.Settings.Clone();
            if (field == "Relay") next.RelayHost = "other.example:443";
            else next.PairingSecret = PairingStore.Protect(PairingKey.Create());
            host.Update(next);
            Check.That(host.Links.Count == 2 && host.Links[0].Disposals == 1 && host.Links[1].Starts == 1,
                       $"{field}: Aenderung ersetzt die bestehende Verbindung genau einmal");
        }

        using var enabled = new Harness(settings);
        enabled.Settings.RemoteEnabled = false;
        enabled.Restart();
        var on = enabled.Settings.Clone();
        on.RemoteEnabled = true;
        enabled.Update(on);
        Check.That(enabled.Links.Count == 1 && enabled.Changes.SequenceEqual(new bool[] { false, true }),
                   "Einschalten startet die Verbindung mit den neuen Einstellungen");
    }

    private static void InvalidRelay(AppSettings settings)
    {
        Check.Group("Remote-Lebenszyklus - unbrauchbare Relay-Adresse");
        foreach (bool existing in new[] { false, true })
        {
            using var host = new Harness(settings);
            if (existing) host.Restart();
            host.Settings.RelayHost = "wss://invalid.example/path";
            host.Restart();
            Check.That(host.Current is null && host.Links.Count == (existing ? 1 : 0),
                       $"ungueltige Adresse (vorher aktiv: {existing}) erzeugt keine Verbindung");
            Check.That(!existing || host.Links[0].Disposals == 1, "vorhandene Verbindung wird auch vor einem fehlgeschlagenen Aufbau freigegeben");
            Check.That(host.Changes.SequenceEqual(existing ? new bool[] { true, false } : new bool[] { false }),
                       $"ungueltige Adresse (vorher aktiv: {existing}) bewertet den Lastbedarf ohne Remote neu");
            host.Settings.RelayHost = settings.RelayHost;
            host.Restart();
            Check.That(host.Current is not null && host.Links.Last().Starts == 1, "nach korrigierter Adresse ist erneutes Verbinden moeglich");
        }
    }

    private static void HostDisposal(AppSettings settings)
    {
        using var host = new Harness(settings);
        host.Restart();
        var current = host.Links.Single();
        host.Close();
        Check.That(current.Disposals == 1 && !current.Disposal.Task.IsCompleted && host.Current is null,
                   "Host-Beenden gibt Remote frei, ohne auf die Netzwerkschleife zu warten");
        host.Close();
        Check.That(current.Disposals == 1 && host.Changes.Count == 1, "wiederholtes Host-Beenden schliesst nicht doppelt und startet keinen Lastmonitor");
        host.Restart();
        var next = host.Settings.Clone();
        next.RelayHost = "after-dispose.example";
        host.Update(next);
        Check.That(host.Current is null && host.Links.Count == 1 && host.Changes.Count == 1,
                   "nach dem Beenden erzeugen spaete Aufrufe weder Verbindungen noch Lastbedarf");
    }

    private static void FailedConstruction(AppSettings settings)
    {
        using var factory = new Harness(settings) { FailCreate = true };
        factory.Restart();
        Check.That(factory.Current is null && factory.Links.Count == 0 && factory.Changes.SequenceEqual(new bool[] { false }),
                   "abgewiesene Konstruktion hinterlaesst keinen Remote-Lastbedarf");

        using var start = new Harness(settings) { FailStart = true };
        start.Restart();
        var failed = start.Links.Single();
        Check.That(failed.Starts == 1 && failed.Disposals == 1 && start.Current is null,
                   "abgewiesener Start gibt die bereits erzeugte Verbindung frei");
        Check.That(start.Changes.SequenceEqual(new bool[] { false }), "nach abgewiesenem Start wird Lastbedarf ohne Remote gemeldet");
        start.FailStart = false;
        start.Restart();
        Check.That(start.Current is not null && start.Links.Last().Starts == 1, "nach abgewiesenem Start bleibt erneuter Aufbau moeglich");
    }

    private static void LoadMonitorAfterFailedRestart(AppSettings settings)
    {
        using var host = new Harness(settings, useLoadMonitor: true);
        var loadField = typeof(AppHost).GetField("_loadMonitor", Hidden)!;
        host.Restart();
        Check.That(loadField.GetValue(host.Host) is not null,
                   "Host startet bei Remote-Verbindung auch ohne adaptive Regelung die Lastmessung");
        host.Settings.RelayHost = "https://invalid.example/";
        host.Restart();
        Check.That(loadField.GetValue(host.Host) is null,
                   "fehlgeschlagener Wechsel stoppt die echte Lastmessung, wenn kein anderer Verbraucher offen ist");
    }

    private sealed class Harness : IDisposable
    {
        internal readonly AppHost Host;
        internal bool BridgeAvailable = true;
        internal bool FailCreate, FailStart;
        internal readonly List<FakeLink> Links = new();
        internal readonly List<PairingInvite> Invites = new();
        internal readonly List<bool> EmptyAtCreate = new();
        internal readonly List<bool> Changes = new();
        internal readonly List<string> Steps = new();
        internal AppRemoteController Controller => (AppRemoteController)typeof(AppHost).GetField("_remote", Hidden)!.GetValue(Host)!;
        internal IAppRemoteLink? Current => (IAppRemoteLink?)typeof(AppRemoteController).GetField("_current", Hidden)!.GetValue(Controller);
        internal RelayState? State => Controller.State;
        internal AppSettings Settings
        {
            get => (AppSettings)typeof(AppHost).GetField("_settings", Hidden)!.GetValue(Host)!;
            set => typeof(AppHost).GetField("_settings", Hidden)!.SetValue(Host, value);
        }

        internal Harness(AppSettings settings, bool useLoadMonitor = false)
        {
            Host = new AppHost(new AppRemoteSources(() => BridgeAvailable, invite =>
            {
                EmptyAtCreate.Add(Current is null);
                Invites.Add(invite);
                if (FailCreate) throw new ArgumentException("Synthetisch abgewiesene Konstruktion");
                var link = new FakeLink(this, Links.Count + 1);
                Links.Add(link);
                Steps.Add($"create:{Links.Count}");
                return link;
            }), () =>
            {
                Changes.Add(Current is not null);
                Steps.Add($"changed:{Current is not null}");
                if (useLoadMonitor) typeof(AppHost).GetMethod("EnsureLoadMonitor", Hidden)!.Invoke(Host, null);
            });
            Settings = settings.Clone();
        }

        internal void Restart() => typeof(AppHost).GetMethod("StartRemote", Hidden)!.Invoke(Host, null);
        internal void Update(AppSettings next)
        {
            var previous = Settings;
            Settings = next;
            Controller.SettingsChanged(previous);
        }
        internal void Close() => Host.Dispose();
        public void Dispose()
        {
            Close();
            foreach (var link in Links) link.Disposal.TrySetResult();
        }
    }

    private sealed class FakeLink(Harness host, int id) : IAppRemoteLink
    {
        public RelayState State { get; set; } = RelayState.Connecting;
        internal int Starts, Disposals;
        internal bool SawCurrentAtStart, SawNoCurrentAtDispose;
        internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Start()
        {
            Starts++;
            SawCurrentAtStart = ReferenceEquals(host.Current, this);
            host.Steps.Add($"start:{id}");
            if (host.FailStart) throw new ArgumentException("Synthetisch abgewiesener Start");
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            SawNoCurrentAtDispose = host.Current is null;
            host.Steps.Add($"dispose:{id}");
            return new ValueTask(Disposal.Task);
        }
    }
}
