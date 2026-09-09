using System.Reflection;
using System.Windows;
using FrameFlip.Interop;
using FrameFlip.Lifecycle;
using FrameFlip.Localization;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace FrameFlip.Tests;

/// <summary>Echte WinForms-Menues und Icons, ohne sichtbaren Tray oder registrierten Hotkey.</summary>
public static class TrayInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo LanguageEvent = typeof(Strings).GetField("Changed", BindingFlags.Static | BindingFlags.NonPublic)!;

    public static void Run()
    {
        Check.Group("Tray - Menueaktionen, Sprache und Benachrichtigungen");
        using var tray = new Fixture();
        var menu = tray.Icon.ContextMenuStrip!;
        var items = menu.Items.Cast<WinForms.ToolStripItem>().ToArray();
        Check.That(!tray.Icon.Visible && tray.Icon.Text == "FrameFlip", "Test-Tray bleibt unsichtbar und besitzt den Programmnamen");
        Check.That(items.Length == 6 && items[2] is WinForms.ToolStripSeparator && items[4] is WinForms.ToolStripSeparator,
                   "vier Aktionen behalten Reihenfolge und beide Trenner");
        Check.That(Labels(items).SequenceEqual(new[] { "FrameFlip öffnen", "Vorschau öffnen", "Einstellungen …", "Beenden" }),
                   "Tray startet mit deutschen Beschriftungen");
        foreach (int index in new[] { 0, 1, 3, 5 }) items[index].PerformClick();
        Check.That(tray.Actions.SequenceEqual(new[] { "main", "toggle", "settings", "exit" }),
                   "jeder Menuepunkt ruft genau seine Aktion auf");
        typeof(WinForms.NotifyIcon).GetMethod("OnDoubleClick", Hidden)!.Invoke(tray.Icon, new object[] { EventArgs.Empty });
        Check.That(tray.Actions.SequenceEqual(new[] { "main", "toggle", "settings", "exit", "main" }),
                   "Doppelklick oeffnet das Hauptfenster");

        WithEnglish(() =>
        {
            Check.That(Labels(items).SequenceEqual(new[] { "Open FrameFlip", "Open preview", "Settings …", "Quit" }),
                       "Sprachwechsel aktualisiert alle vier Beschriftungen");
            Check.That(menu.Items.Cast<WinForms.ToolStripItem>().SequenceEqual(items), "Sprachwechsel verwendet dieselben Menueeintraege");
            tray.Actions.Clear();
            foreach (int index in new[] { 0, 1, 3, 5 }) items[index].PerformClick();
            Check.That(tray.Actions.SequenceEqual(new[] { "main", "toggle", "settings", "exit" }),
                       "Sprachwechsel vervielfacht keine Aktions-Handler");
        });
        Check.That(Labels(items).SequenceEqual(new[] { "FrameFlip öffnen", "Vorschau öffnen", "Einstellungen …", "Beenden" }),
                   "Rueckwechsel stellt alle deutschen Beschriftungen wieder her");

        tray.Tooltip(HotKeyDefinition.Default);
        Check.That(tray.Icon.Text == "FrameFlip – Ctrl+Alt+Space", "Tooltip zeigt den aktuellen Hotkey");
        HotKeyDefinition.TryParse("Alt+Shift+F11", out var next);
        tray.Tooltip(next);
        Check.That(tray.Icon.Text == "FrameFlip – Alt+Shift+F11", "Hotkey-Aenderung ersetzt den Tooltip");
        tray.Controller.UpdateTooltip(new string('x', 50));
        Check.That(tray.Icon.Text == "FrameFlip – " + new string('x', 50), "Tooltip mit genau 62 Zeichen bleibt vollstaendig");
        tray.Controller.UpdateTooltip(new string('x', 51));
        Check.That(tray.Icon.Text == "FrameFlip – " + new string('x', 50), "zu langer Tooltip wird auf 62 Zeichen begrenzt");
        tray.Notify("Tray-Test");
        // WinForms lehnt leere Balloon-Texte ab. Der Fehler darf nicht in den Host gelangen.
        Check.Throws<ArgumentException>(() => tray.Icon.ShowBalloonTip(4000, "FrameFlip", string.Empty, WinForms.ToolTipIcon.Info),
                                        "WinForms weist den fuer den Fehlertest verwendeten leeren Meldungstext tatsaechlich ab");
        tray.Notify(string.Empty);
        tray.Notify("Nach dem Fehler");
        Check.That(!tray.Icon.Visible, "auch nach abgewiesener Meldung bleiben Benachrichtigungen ohne Ausnahme und ohne sichtbaren Tray");

        var drawingIcon = tray.Icon.Icon;
        Check.That(drawingIcon is not null && drawingIcon.Width > 0, "mitgeliefertes ICO wird als eigenes Icon geladen");
        var lateLanguageCallback = (Action)Handlers().Except(tray.PreviousHandlers).Single();
        int disposed = 0;
        tray.Icon.Disposed += (_, _) => disposed++;
        tray.Close();
        Check.That(!tray.Icon.Visible && menu.IsDisposed && items.All(item => item.IsDisposed),
                   "Beenden entfernt den Tray und gibt Menue samt Eintraegen frei");
        Check.That(Handlers().SequenceEqual(tray.PreviousHandlers), "Beenden meldet den Sprachwechsel-Handler wieder ab");
        Check.That(drawingIcon is not null && IsDisposed(drawingIcon), "Beenden gibt auch das geladene Icon frei");
        tray.Close();
        Check.That(menu.IsDisposed, "wiederholtes Beenden bleibt folgenlos");
        Check.That(disposed == 1 && tray.Icon.Icon is null && tray.Icon.ContextMenuStrip is null,
                   "NotifyIcon wird genau einmal freigegeben und haelt weder Icon noch Menue fest");
        var finalText = tray.Icon.Text;
        tray.Controller.UpdateTooltip("Spaeter Hotkey");
        tray.Notify("Spaete Meldung");
        Check.That(!tray.Icon.Visible && tray.Icon.Text == finalText, "spaete Aufrufe beleben den beendeten Tray nicht wieder");
        WithEnglish(() =>
        {
            lateLanguageCallback();
            Check.That(Labels(items).SequenceEqual(new[] { "FrameFlip öffnen", "Vorschau öffnen", "Einstellungen …", "Beenden" }),
                       "auch ein schon erfasster Sprachwechsel-Callback laesst freigegebene Eintraege in Ruhe");
        });

        IndependentLifetimes();
        HostLifetime();
    }

    private static string?[] Labels(WinForms.ToolStripItem[] items) => new[] { items[0].Text, items[1].Text, items[3].Text, items[5].Text };

    private static bool IsDisposed(Drawing.Icon icon)
    {
        try { return icon.Handle == IntPtr.Zero; }
        catch (ObjectDisposedException) { return true; }
    }

    private static Delegate[] Handlers() => ((Action?)LanguageEvent.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>();

    private static void WithEnglish(Action check)
    {
        // Der Testlaeufer besitzt eine andere ResourceAssembly als die Anwendung.
        // Daher laden wir das echte Woerterbuch explizit und senden dasselbe Changed-Ereignis.
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var english = new ResourceDictionary { Source = new Uri("/FrameFlip;component/Localization/Strings.en.xaml", UriKind.Relative) };
        dictionaries.Add(english);
        try
        {
            ((Action?)LanguageEvent.GetValue(null))?.Invoke();
            check();
        }
        finally
        {
            dictionaries.Remove(english);
            ((Action?)LanguageEvent.GetValue(null))?.Invoke();
        }
    }

    private static void IndependentLifetimes()
    {
        var before = Handlers();
        using var first = new Fixture();
        using var second = new Fixture();
        var secondItems = second.Icon.ContextMenuStrip!.Items.Cast<WinForms.ToolStripItem>().ToArray();
        Check.That(Handlers().Length == before.Length + 2, "jede Tray-Instanz besitzt genau ein Sprachwechsel-Abonnement");
        first.Close();
        WithEnglish(() => Check.That(Labels(secondItems).SequenceEqual(new[] { "Open FrameFlip", "Open preview", "Settings …", "Quit" }),
                                     "Beenden einer Instanz laesst Sprachwechsel der anderen weiterlaufen"));
        second.Close();
        Check.That(Handlers().SequenceEqual(before), "mehrere Tray-Lebenszyklen hinterlassen keine Sprachwechsel-Abonnements");
    }

    private static void HostLifetime()
    {
        using var host = new AppHost();
        var trayField = typeof(AppHost).GetField("_tray", Hidden)!;
        Check.That(trayField.GetValue(host) is null, "Host-Konstruktion erzeugt noch keinen Tray");
        typeof(AppHost).GetMethod("UpdateTooltip", Hidden)!.Invoke(host, null);
        typeof(AppHost).GetMethod("Notify", Hidden)!.Invoke(host, new object[] { "Vor dem Start" });

        using var tray = new Fixture();
        trayField.SetValue(host, tray.Controller);
        var hotkeys = (HotKeyService)typeof(AppHost).GetField("_hotkeys", Hidden)!.GetValue(host)!;
        typeof(HotKeyService).GetProperty("Current")!.SetValue(hotkeys, HotKeyDefinition.Default);
        typeof(AppHost).GetMethod("UpdateTooltip", Hidden)!.Invoke(host, null);
        Check.That(tray.Icon.Text == "FrameFlip – Ctrl+Alt+Space", "Host reicht den registrierten Hotkey an den Tray weiter");
        typeof(AppHost).GetMethod("Notify", Hidden)!.Invoke(host, new object[] { "Host-Meldung" });
        var menu = tray.Icon.ContextMenuStrip!;
        host.Dispose();
        Check.That(trayField.GetValue(host) is null && menu.IsDisposed && Handlers().SequenceEqual(tray.PreviousHandlers),
                   "Host-Beenden gibt seinen Tray samt Abonnement frei und loescht die Referenz");
        typeof(AppHost).GetMethod("UpdateTooltip", Hidden)!.Invoke(host, null);
        typeof(AppHost).GetMethod("Notify", Hidden)!.Invoke(host, new object[] { "Nach dem Beenden" });
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly List<string> Actions = new();
        internal readonly Delegate[] PreviousHandlers = Handlers();
        internal AppTrayController Controller { get; }
        internal WinForms.NotifyIcon Icon { get; }

        internal Fixture()
        {
            Controller = new AppTrayController(() => Actions.Add("main"), () => Actions.Add("toggle"),
                () => Actions.Add("settings"), () => Actions.Add("exit"), visible: false);
            Icon = (WinForms.NotifyIcon)typeof(AppTrayController).GetField("_trayIcon", Hidden)!.GetValue(Controller)!;
        }

        internal void Tooltip(HotKeyDefinition hotkey) => Controller.UpdateTooltip(hotkey.ToString());

        internal void Notify(string message) => Controller.Notify(message);
        internal void Close() => Controller.Dispose();

        public void Dispose() => Close();
    }
}
