using System.Windows;
using System.Windows.Controls.Primitives;

namespace FrameFlip.Views;

/// <summary>
/// Die rechte Spalte: zwei Reiter, und dass sie sich merkt, wie breit sie stand.
///
/// "Alles sehr starr" war die Klage, und sie traf zu: Die Spalte war dreihundert
/// Punkte breit, der Ebenenstreifen so hoch, wie er eben wurde, und daran liess sich
/// nichts aendern. Beides ist jetzt zu ziehen.
///
/// Gemerkt wird es, weil eine Aufteilung, die man bei jedem Start neu herstellt,
/// beim dritten Mal nicht mehr hergestellt wird. Das ist der ganze Unterschied
/// zwischen "verstellbar" und "eingerichtet".
///
/// Was hier NICHT passiert, steht ebenso ausdruecklich im Entwurf: Es gibt kein
/// Andocken, kein Abreissen, keine schwebenden Fenster. Das waere ein Andockrahmen,
/// und den in WPF ohne Fremdpakete zu bauen sind Wochen und eine dauerhafte Quelle
/// merkwuerdiger Fehler. Zwei Griffe und ein Gedaechtnis decken das ab, was "weniger
/// starr" tatsaechlich heisst.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Ein Reiter der rechten Spalte wurde gewaehlt.</summary>
    private void OnPanelTabChanged(object sender, RoutedEventArgs e) => ApplyPanelTab();

    /// <summary>
    /// Zeigt den Inhalt des gewaehlten Reiters - und nur ihn, in voller Hoehe.
    ///
    /// Der Ebenenreiter ist nur waehlbar, wenn ein Bild offen ist: Ohne Bild gibt
    /// es keine Ebene, und ein Reiter, der auf eine leere Flaeche fuehrt, waere ein
    /// Klick, der nichts beantwortet.
    /// </summary>
    private void ApplyPanelTab()
    {
        // Waehrend die Oberflaeche aufgebaut wird, meldet der erste Reiter sein
        // Haekchen bereits - bevor die Flaechen darunter existieren. Ein Wert, der
        // beim Aufbau gesetzt wird, loest eben auch beim Aufbau aus.
        if (Layers is null || Tools is null || LayersTab is null || ColourTab is null) return;

        bool layers = LayersTab.IsChecked == true && _layersShown;

        Layers.Visibility = layers ? Visibility.Visible : Visibility.Collapsed;
        Tools.Visibility = layers ? Visibility.Collapsed : Visibility.Visible;

        if (!layers && ColourTab.IsChecked != true) ColourTab.IsChecked = true;
    }

    /// <summary>
    /// Ob ein Bild mit Ebenen offen ist - unabhaengig davon, welcher Reiter vorn
    /// liegt.
    ///
    /// Frueher hiess das "der Ebenenstreifen ist sichtbar", und der Greifrahmen im
    /// Bild haengte daran. Mit Reitern ist der Streifen unsichtbar, sobald man auf
    /// die Farbe schaut - und der Rahmen waere mit ihm verschwunden.
    /// </summary>
    private bool _layersShown;

    /// <summary>Stellt die Aufteilung der letzten Sitzung wieder her.</summary>
    private void RestoreColumns()
        => RightColumn.Width = new GridLength(Math.Clamp(_settings.AtelierColumnWidth, 220, 900));

    /// <summary>
    /// Gemerkt wird beim LOSLASSEN und nicht waehrend des Ziehens.
    ///
    /// Waehrend des Ziehens kommen dreissig Meldungen je Sekunde, und jede davon
    /// schriebe die Einstellungsdatei. Das ist nicht nur Verschwendung, sondern die
    /// Art Verschwendung, die auffaellt: Eine Datei, die waehrend eines Zuges
    /// dauernd geschrieben wird, laesst den Zug haken.
    /// </summary>
    private void OnColumnResized(object sender, DragCompletedEventArgs e)
    {
        _settings.AtelierColumnWidth = RightColumn.ActualWidth;
        _persist(_settings);
    }

}
