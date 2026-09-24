using System.IO;
using System.Reflection;
using FrameFlip.Configuration;
using FrameFlip.Diagnostics;
using FrameFlip.Interop;
using FrameFlip.Lifecycle;
using FrameFlip.Remote;
using FrameFlip.Web;

namespace FrameFlip.Tests;

/// <summary>Der Hostweg des Zuschauerdienstes, ohne Netzwerk oder persoenliche Einstellungen.</summary>
public static class WatchLifecycleInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "frameflip-watch-lifecycle-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        var previousLanguage = FrameFlip.Localization.Strings.Current;
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));
        try
        {
            Prerequisites();
            ReplaceWithoutWaiting();
            SettingsChanges();
            KeysAndCodes();
            LoadDemand();
            FailedStarts();
            Shutdown();
            BoundedShutdown();
        }
        finally
        {
            FrameFlip.Localization.Strings.Apply(previousLanguage);
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Prerequisites()
    {
        Check.Group("Zuschauer-Lebenszyklus - Voraussetzungen");
        foreach (string reason in new[] { "aus", "keine Zustimmung", "alte Zustimmung", "Relay ungueltig" })
        {
            using var host = new Harness();
            host.Settings.WatchSecret = "";
            switch (reason)
            {
                case "aus": host.Settings.WatchEnabled = false; break;
                case "keine Zustimmung": host.Settings.TermsAccepted = 0; host.Settings.Normalize(); break;
                case "alte Zustimmung": host.Settings.TermsAccepted = AppSettings.TermsVersion - 1; host.Settings.Normalize(); break;
                default: host.Settings.RelayHost = "https://invalid.example/path"; break;
            }
            host.Host.ApplyWatch();
            Check.That(host.Services.Count == 0 && host.Host.Watch is null, reason + ": kein Dienst");
            Check.That(host.Settings.WatchSecret == "", reason + ": kein neues Geheimnis");
        }

        using var denied = new Harness();
        var request = denied.Settings.Clone();
        request.TermsAccepted = 0;
        Check.That(denied.Apply(request) is not null && denied.Services.Count == 0,
                   "ApplySettings weist den Start ohne Zustimmung vor dem Dienstaufbau ab");
    }

    private static void ReplaceWithoutWaiting()
    {
        Check.Group("Zuschauer-Lebenszyklus - Austausch");
        using var host = new Harness();
        host.Host.ApplyWatch();
        var first = host.Services.Single();
        Check.That(first.Starts == 1 && ReferenceEquals(host.Host.Watch, first.Service),
                   "Host gibt dieselbe oeffentliche Dienstfassade weiter");
        Check.That(first.CurrentAtStart && first.Relay == host.Settings.RelayHost,
                   "Start besitzt schon den Dienst und verwendet den eingestellten Relay");

        first.HoldDisposal = true;
        host.Host.ApplyWatch();
        var second = host.Services.Last();
        Check.That(first.Disposals == 1 && !first.Disposal.Task.IsCompleted && second.Starts == 1,
                   "Neustart wartet nicht auf den alten Dienst");
        Check.That(first.EmptyAtDispose && host.EmptyAtCreate.All(value => value),
                   "alte Referenz ist vor Freigabe und neuer Konstruktion entfernt");
        first.Disposal.SetResult();
        Check.That(ReferenceEquals(host.Host.Watch, second.Service),
                   "spaetes Ende des alten Dienstes entfernt den neuen nicht");
        Check.That(first.Key.Text == second.Key.Text, "ein Neustart behaelt den Zuschauer-Link");

        host.Settings.WatchEnabled = false;
        host.Host.ApplyWatch();
        Check.That(host.Host.Watch is null && second.Disposals == 1,
                   "Abschalten gibt den Dienst frei");
        host.Settings.WatchEnabled = true;
        host.Host.ApplyWatch();
        Check.That(host.Services.Last().Key.Text == first.Key.Text,
                   "auch Aus- und Einschalten erneuert das Geheimnis nicht");
        host.Settings.RelayHost = "wss://invalid.example/path";
        host.Host.ApplyWatch();
        Check.That(host.Host.Watch is null && host.Services.Last().Disposals == 1,
                   "ungueltiger Relay nach einem Wechsel laesst keinen alten Dienst zurueck");
    }

    private static void SettingsChanges()
    {
        Check.Group("Zuschauer-Lebenszyklus - Einstellungen");
        using var host = new Harness();
        host.Host.ApplyWatch();
        var first = host.Host.Watch;
        foreach (var change in new Action<AppSettings>[]
        {
            settings => settings.Fps = 60,
            settings => settings.MemoryBudgetMb = 256,
            settings => settings.Language = "en",
            settings => settings.Language = "de",
            _ => { },
        })
        {
            var next = host.Settings.Clone();
            change(next);
            Check.That(host.Apply(next) is null && ReferenceEquals(first, host.Host.Watch)
                       && host.Services.Count == 1, "fremde oder gleiche Einstellungen erhalten den Dienst");
        }
        var relay = host.Settings.Clone();
        relay.RelayHost = "other.example";
        Check.That(host.Apply(relay) is null && host.Services.Count == 2
                   && host.Services.Last().Relay == relay.RelayHost,
                   "Relay-Wechsel ersetzt genau einmal");
        var secret = host.Settings.Clone();
        secret.WatchSecret = WatchStore.Protect(WatchKey.Create(null));
        Check.That(host.Apply(secret) is null && host.Services.Count == 3,
                   "Geheimnis-Wechsel ersetzt genau einmal");
        var disabled = host.Settings.Clone();
        disabled.WatchEnabled = false;
        Check.That(host.Apply(disabled) is null && host.Host.Watch is null,
                   "Ausschalten ueber ApplySettings erreicht den Lebenszyklus");
    }

    private static void KeysAndCodes()
    {
        Check.Group("Zuschauer-Lebenszyklus - Link und Kennwort");
        using var host = new Harness();
        host.Settings.WatchSecret = "defekt";
        host.Host.ApplyWatch();
        var first = host.Services.Single().Key;
        Check.That(WatchStore.TryUnprotect(SettingsStore.Load().WatchSecret, out var saved)
                   && saved!.Text == first.Text, "erster Start legt ein lesbares geschuetztes Geheimnis ab");

        host.Host.SetWatchCode("  abcd  ");
        var coded = host.Services.Last().Key;
        Check.That(coded.Text == first.Text && coded.Code == "abcd" && host.Services.Count == 2,
                   "Kennwortwechsel behaelt den Link, trimmt das Kennwort und startet neu");
        string before = host.Settings.WatchSecret;
        host.Host.SetWatchCode("abc");
        Check.That(host.Settings.WatchSecret == before && host.Services.Count == 2,
                   "ungueltiges Kennwort aendert weder Speicher noch Dienst");

        host.Host.RenewWatchLink();
        var renewed = host.Services.Last().Key;
        Check.That(renewed.Text != first.Text && renewed.Code == coded.Code && host.Services.Count == 3,
                   "Linkerneuerung wechselt das Geheimnis und behaelt das Kennwort");
        Check.That(WatchStore.TryUnprotect(SettingsStore.Load().WatchSecret, out saved)
                   && saved!.Text == renewed.Text && saved.Code == renewed.Code,
                   "Linkerneuerung ist fuer den naechsten Start gespeichert");
        host.Host.SetWatchCode(null);
        Check.That(host.Services.Last().Key.Code is null && host.Services.Last().Key.Text == renewed.Text,
                   "leeres Kennwort stellt die freien Plaetze mit demselben Link her");

        host.Settings.WatchEnabled = false;
        host.Host.ApplyWatch();
        int count = host.Services.Count;
        host.Host.RenewWatchLink();
        Check.That(host.Services.Count == count && host.Host.Watch is null,
                   "Linkerneuerung schaltet einen ausgeschalteten Dienst nicht ein");
    }

    private static void LoadDemand()
    {
        Check.Group("Zuschauer-Lebenszyklus - Lastbedarf ohne Fenster oder Handy");
        using var host = new Harness();
        host.Host.ApplyWatch();
        Check.That(host.Monitors.Count == 1 && host.Monitors[0].Starts == 1,
                   "Zuschauerdienst startet Lastmessung auch ohne adaptive Regelung");
        host.Host.ApplyWatch();
        Check.That(host.Monitors.Count == 1 && host.Monitors[0].Disposals == 0,
                   "Dienstwechsel verwendet die bestehende Messreihe weiter");
        host.Settings.RelayHost = "https://invalid.example/";
        host.Host.ApplyWatch();
        Check.That(host.Monitors.Count == 1 && host.Monitors[0].Disposals == 1,
                   "fehlgeschlagener Relay-Wechsel beendet den entfallenen Lastbedarf");
        host.Settings.RelayHost = "relay.example";
        host.Host.ApplyWatch();
        Check.That(host.Monitors.Count == 2 && host.Monitors[1].Starts == 1,
                   "korrigierter Relay beginnt eine neue Messreihe");
        host.Settings.WatchEnabled = false;
        host.Host.ApplyWatch();
        Check.That(host.Monitors.Count == 2 && host.Monitors[1].Disposals == 1,
                   "Abschalten des letzten Verbrauchers beendet die Lastmessung");
    }

    private static void Shutdown()
    {
        Check.Group("Zuschauer-Lebenszyklus - Beenden");
        using var host = new Harness();
        host.Host.ApplyWatch();
        var current = host.Services.Single();
        host.Host.Dispose();
        host.Host.Dispose();
        Check.That(current.Disposals == 1 && current.EmptyAtDispose && host.Host.Watch is null,
                   "Host gibt den Dienst einmal frei und entfernt die UI-Referenz zuerst");
        int monitors = host.Monitors.Count;
        host.Host.ApplyWatch();
        var previous = host.Settings.Clone();
        host.Settings.RelayHost = "after-dispose.example";
        host.Controller.SettingsChanged(previous);
        Check.That(host.Host.Watch is null && host.Services.Count == 1 && host.Monitors.Count == monitors,
                   "spaete Neustart- und Einstellungsaufrufe nach Hostende bleiben wirkungslos");
    }

    private static void FailedStarts()
    {
        Check.Group("Zuschauer-Lebenszyklus - fehlgeschlagener Aufbau");
        using var host = new Harness();
        host.Host.ApplyWatch();
        host.FailCreate = true;
        Check.Throws<InvalidOperationException>(host.Host.ApplyWatch, "Fabrikfehler bleibt beim Aufrufer");
        Check.That(host.Host.Watch is null && host.Services[0].Disposals == 1
                   && host.Monitors[0].Disposals == 1, "Fabrikfehler gibt alten Dienst und Lastbedarf frei");
        host.FailCreate = false;
        host.FailStart = true;
        Check.Throws<InvalidOperationException>(host.Host.ApplyWatch, "Startfehler bleibt beim Aufrufer");
        Check.That(host.Host.Watch is null && host.Services.Last().Disposals == 1
                   && host.Monitors.Count == 1, "Startfehler gibt den halben Dienst frei und erzeugt keinen Lastbedarf");
        host.FailStart = false;
        host.Host.ApplyWatch();
        Check.That(host.Host.Watch is not null && host.Monitors.Count == 2,
                   "erneuter Aufbau nach Fehler ist moeglich");
        host.Services.Last().ThrowOnDispose = true;
        host.Host.ApplyWatch();
        Check.That(ReferenceEquals(host.Host.Watch, host.Services.Last().Service),
                   "synchrone Freigabeausnahme der alten Leitung verhindert den neuen Dienst nicht");
        host.Services.Last().FaultOnDispose = true;
        host.Host.ApplyWatch();
        Check.That(ReferenceEquals(host.Host.Watch, host.Services.Last().Service),
                   "asynchrone Freigabeausnahme wird beobachtet und beendet den neuen Dienst nicht");
    }

    private static void BoundedShutdown()
    {
        Check.Group("Zuschauer-Lebenszyklus - begrenztes Ende alter und aktueller Dienste");
        var key = WatchKey.Create(null);
        var settings = new AppSettings { WatchEnabled = true, RelayHost = "relay.example" };
        var services = new List<PendingService>();
        int changes = 0, invalid = 0;
        using var controller = new AppWatchController(() => settings,
            new AppWatchSources((_, _) => { var service = new PendingService(key); services.Add(service); return service; }),
            () => key, () => invalid++, () => changes++, TimeSpan.Zero);
        controller.Restart();
        controller.Restart();
        var pending = (List<Task>)typeof(AppWatchController).GetField("_closing", Hidden)!.GetValue(controller)!;
        Check.That(pending.Count == 1 && !pending[0].IsCompleted,
                   "ein noch schliessender Vorgaenger bleibt bis zum Hostende beruecksichtigt");
        controller.Dispose();
        controller.Dispose();
        controller.Restart();
        Check.That(services.Count == 2 && services.All(service => service.Disposals == 1)
                   && !controller.HasService && controller.Service is null,
                   "Ende schliesst jeden Dienst einmal und kehrt auch ohne Netzwerkantwort zurueck");
        Check.That(changes == 2 && invalid == 0, "Hostende und spaeter Neustart melden keinen neuen Lastbedarf");
        foreach (var service in services) service.Disposal.SetResult();
        Check.That(!controller.HasService, "spaete Freigaben koennen den beendeten Controller nicht wiederbeleben");
    }

    private sealed class Harness : IDisposable
    {
        internal readonly AppHost Host;
        internal readonly List<FakeService> Services = new();
        internal readonly List<bool> EmptyAtCreate = new();
        internal readonly List<FakeMonitor> Monitors = new();
        internal bool FailCreate, FailStart;
        internal AppWatchController Controller => (AppWatchController)typeof(AppHost).GetField("_watch", Hidden)!.GetValue(Host)!;
        internal AppSettings Settings
        {
            get => (AppSettings)typeof(AppHost).GetField("_settings", Hidden)!.GetValue(Host)!;
            set => typeof(AppHost).GetField("_settings", Hidden)!.SetValue(Host, value);
        }

        internal Harness()
        {
            Host = new AppHost(new AppWatchSources((key, relay) =>
            {
                EmptyAtCreate.Add(Host!.Watch is null);
                if (FailCreate) throw new InvalidOperationException("Synthetischer Fabrikfehler");
                var service = new FakeService(this, key, relay);
                Services.Add(service);
                return service;
            }), new AppLoadSources((_, _) =>
            {
                var monitor = new FakeMonitor();
                Monitors.Add(monitor);
                return monitor;
            }, () => null, _ => { }));
            Settings = new AppSettings
            {
                WatchEnabled = true, TermsAccepted = AppSettings.TermsVersion,
                WatchSecret = WatchStore.Protect(WatchKey.Create(null)),
                RelayHost = "relay.example", AdaptiveResources = false,
            };
            // Der Einstellungsweg soll keinen globalen Hotkey des Nutzers belegen.
            var hotkeys = (HotKeyService)typeof(AppHost).GetField("_hotkeys", Hidden)!.GetValue(Host)!;
            HotKeyDefinition.TryParse(Settings.Hotkey, out var definition);
            typeof(HotKeyService).GetField("<Current>k__BackingField", Hidden)!.SetValue(hotkeys, definition);
        }

        internal string? Apply(AppSettings settings)
            => (string?)typeof(AppHost).GetMethod("ApplySettings", Hidden)!.Invoke(Host, new object[] { settings });

        public void Dispose()
        {
            foreach (var service in Services) service.Disposal.TrySetResult();
            Host.Dispose();
        }
    }

    private sealed class FakeService(Harness host, WatchKey key, string relay) : IAppWatchService
    {
        public WatchService Service { get; } = new(key, relay, null, () => null, () => null);
        internal WatchKey Key => key;
        internal string Relay => relay;
        internal int Starts, Disposals;
        internal bool CurrentAtStart, EmptyAtDispose, HoldDisposal, ThrowOnDispose, FaultOnDispose;
        internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Start()
        {
            Starts++;
            CurrentAtStart = ReferenceEquals(host.Host.Watch, Service);
            if (host.FailStart) throw new InvalidOperationException("Synthetischer Startfehler");
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            EmptyAtDispose = host.Host.Watch is null;
            if (ThrowOnDispose) throw new IOException("Synthetischer Freigabefehler");
            if (FaultOnDispose) return new ValueTask(Task.FromException(new IOException("Synthetischer asynchroner Freigabefehler")));
            if (!HoldDisposal) Disposal.TrySetResult();
            return new ValueTask(Disposal.Task);
        }
    }

    private sealed class PendingService(WatchKey key) : IAppWatchService
    {
        public WatchService Service { get; } = new(key, "relay.example", null, () => null, () => null);
        internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals;
        public void Start() { }
        public ValueTask DisposeAsync() { Disposals++; return new ValueTask(Disposal.Task); }
    }

    private sealed class FakeMonitor : IAppLoadMonitor
    {
        public event Action<LoadSnapshot, ResourceProfile>? Updated { add { } remove { } }
        public LoadSnapshot? LastSnapshot => null;
        public int MaxDecoderThreads => 1;
        internal int Starts, Disposals;
        public void Start() => Starts++;
        public void SetRenderMode(bool rendering) { }
        public void Dispose() => Disposals++;
    }
}
