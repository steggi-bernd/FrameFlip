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
            Shutdown();
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
    }

    private sealed class Harness : IDisposable
    {
        internal readonly AppHost Host;
        internal readonly List<FakeService> Services = new();
        internal readonly List<bool> EmptyAtCreate = new();
        internal readonly List<FakeMonitor> Monitors = new();
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
        internal bool CurrentAtStart, EmptyAtDispose, HoldDisposal;
        internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Start()
        {
            Starts++;
            CurrentAtStart = ReferenceEquals(host.Host.Watch, Service);
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            EmptyAtDispose = host.Host.Watch is null;
            if (!HoldDisposal) Disposal.TrySetResult();
            return new ValueTask(Disposal.Task);
        }
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
