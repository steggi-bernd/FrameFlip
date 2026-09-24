using System.Windows;
using System.Windows.Controls;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Die Andockflaeche des Ateliers - und dass sie sich merkt, wie sie stand.
///
/// Die Felder lassen sich in Zonen links, rechts und unter dem Bild ziehen, dort als
/// Reiter oder uebereinander. Was wo steht, haelt <see cref="DockLayout"/> fest, und
/// die Einstellungen merken es sich: Eine Anordnung, die man bei jedem Start neu
/// herstellt, wird beim dritten Mal nicht mehr hergestellt.
/// </summary>
public partial class AtelierPage
{
    /// <summary>
    /// Ob ein Bild mit Ebenen offen ist - unabhaengig davon, wo die Ebenen gerade
    /// stehen und ob sie vorn liegen.
    ///
    /// Frueher hiess das "der Ebenenstreifen ist sichtbar", und der Greifrahmen im
    /// Bild haengte daran. Mit Reitern ist der Streifen unsichtbar, sobald etwas
    /// anderes vorn liegt - und der Rahmen waere mit ihm verschwunden.
    /// </summary>
    private bool _layersShown;

    /// <summary>
    /// Richtet die Andockflaeche ein: die Verteilung in ihr eigenes Feld, die
    /// gemerkte Anordnung, und das Merken selbst.
    /// </summary>
    private void SetUpDock()
    {
        // Die Verteilung steht im Farbfeld, rechnet dort und wird von dort gefuellt.
        // Zu sehen ist sie aber als eigenes Feld - herausgehaengt und in ihren Platz
        // gesetzt. Alle Namen und Handler bleiben, wo sie waren.
        if (Tools.HistogramBlock.Parent is Panel parent) parent.Children.Remove(Tools.HistogramBlock);

        HistogramSlot.Content = Tools.HistogramBlock;

        // Der Name steht jetzt auf dem Reiter - ein zweites Mal darunter waere Laerm.
        Tools.HistogramLabel.Visibility = Visibility.Hidden;

        Dock.Load(_settings.AtelierDock ?? FirstLayout());

        Dock.LayoutChanged += layout =>
        {
            _settings.AtelierDock = layout.Clone();
            _persist(_settings);
        };

        Dock.SetAvailable("layers", false, Strings.T("S_LayersUnavailable"));
    }

    /// <summary>
    /// Die erste Anordnung - mit der Breite, die die rechte Spalte schon hatte.
    ///
    /// Wer die Spalte vorher breiter gezogen hat, soll das nach dem Umstieg nicht noch
    /// einmal tun muessen.
    /// </summary>
    private DockLayout FirstLayout()
    {
        var layout = DockLayout.Default();

        layout.RightWidth = Math.Clamp(_settings.AtelierColumnWidth, 220, 900);

        return layout;
    }
}
