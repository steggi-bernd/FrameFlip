using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameFlip.Localization;

using FontFamily = System.Windows.Media.FontFamily;

namespace FrameFlip.Views;

/// <summary>Ein Favorit in der Effektzeile: sein Zeichen, sein Name, und ob er am Ziel geht.</summary>
public sealed record FavouriteItem(string Key, string Glyph, string Title, bool Enabled, string? Why);

/// <summary>
/// Versuch (docs/Atelier-UX-Versuch.md): der Kopf der Einstellungen sagt, worauf sie wirken, und
/// statt der Palette steht eine Zeile "+ Effekt auf ..." mit den Favoriten daneben.
/// </summary>
public partial class GradingPanel
{
    /// <summary>"+ Effekt" wurde gedrueckt - die Seite oeffnet den Katalog.</summary>
    public event Action? AddEffectWanted;

    /// <summary>Ein Favorit wurde gedrueckt.</summary>
    public event Action<string>? FavouriteWanted;

    private void OnAddEffectClicked(object sender, RoutedEventArgs e) => AddEffectWanted?.Invoke();

    /// <summary>Der Kopf fuer eine Ebene oder das Gesamtbild.</summary>
    private void ShowTargetHead(string? layer, bool onLayer, bool locked)
    {
        bool onIt = onLayer && layer is not null;
        string whole = Strings.T("S_WholeRow");

        TargetCrumb.Text = onIt ? whole + " ›" : "";
        TargetCrumb.Visibility = onIt ? Visibility.Visible : Visibility.Collapsed;
        TargetTitle.Text = onIt ? layer! : whole;
        TargetKind.Text = onIt ? Strings.T(locked ? "S_TargetKindAdjustment" : "S_TargetKindLayer") : Strings.T("S_WholeRowNote");
        AddEffectButton.Content = Strings.T("S_AddEffectOn", onIt ? layer : whole);
    }

    /// <summary>Im Knotenmodus: der Knoten ist das Ziel.</summary>
    public void ShowNodeHead(string? node)
    {
        TargetCrumb.Text = Strings.T("S_NodeCrumb") + " ›";
        TargetCrumb.Visibility = Visibility.Visible;
        TargetTitle.Text = node ?? Strings.T("S_NodeNone");
        TargetKind.Text = "";
        AddEffectButton.Content = Strings.T("S_AddEffectOn", node ?? Strings.T("S_NodeCrumb"));
    }

    /// <summary>Die Einstellungen der gewaehlten Ebene - sichtbar, solange eine gewaehlt ist.</summary>
    public void ShowLayerTools(bool shown)
        => LayerToolsHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Die Favoriten neben "+ Effekt".</summary>
    public void ShowFavourites(IEnumerable<FavouriteItem> items)
    {
        FavouriteBar.Children.Clear();

        foreach (var item in items.Take(6))
        {
            var button = new Button
            {
                Content = new TextBlock { Text = item.Glyph, FontSize = 14, FontFamily = new FontFamily("Segoe UI Symbol") },
                MinWidth = 32,
                Margin = new Thickness(0, 0, 4, 0),
                IsEnabled = item.Enabled,
                ToolTip = item.Why is { } why ? item.Title + " – " + why : item.Title,
                Tag = item.Key,
            };
            button.SetResourceReference(StyleProperty, "OverlayButton");
            ToolTipService.SetShowOnDisabled(button, true);
            System.Windows.Automation.AutomationProperties.SetName(button, item.Title);
            button.Click += (_, _) => FavouriteWanted?.Invoke(item.Key);

            FavouriteBar.Children.Add(button);
        }
    }

    /// <summary>Ob ein Abschnitt nur dem ganzen Bild gilt - an einer Ebene wird er nie gerechnet.</summary>
    internal bool PictureOnlySection(string section)
        => Sections.FirstOrDefault(s => s.Prefix == section).Tab is { } tab && PictureOnly.Contains(tab);

    /// <summary>Ob ein Abschnitt schon im Stapel steht - dann heisst die Aktion "bearbeiten".</summary>
    internal bool SectionInStack(string section) => InStack(section);

    /// <summary>Welcher Pass einem Abschnitt fehlt - oder null, wenn nichts fehlt.</summary>
    internal string? MissingPass(string section) => section switch
    {
        "Depth" when !_hasDepth => Strings.T("S_PassDepth"),
        "Motion" when !_hasMotion => Strings.T("S_PassMotion"),
        _ => null,
    };
}
