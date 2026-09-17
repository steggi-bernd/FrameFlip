using System.Windows;
using System.Windows.Controls.Primitives;

namespace FrameFlip.Views;

/// <summary>
/// Die Aufteilung der rechten Spalte - und dass sie sich merkt, wie sie stand.
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
    /// <summary>
    /// Die gemerkte Hoehe des Ebenenstreifens - in Grenzen, die ein Fenster ueberlebt.
    ///
    /// Eine Zahl aus der Einstellungsdatei ist nicht vertrauenswuerdig: Sie kann von
    /// einem groesseren Bildschirm stammen, von einer aelteren Fassung, oder jemand
    /// hat sie von Hand geaendert. Ungeprueft uebernommen ergaebe sie eine Spalte, in
    /// der die Farbkorrektur nicht mehr vorkommt.
    /// </summary>
    private double LayerHeight() => Math.Clamp(_settings.AtelierLayersHeight, 64, 900);

    /// <summary>Ein Abschnittskopf wurde angeklickt.</summary>
    private void OnSectionToggled(object sender, RoutedEventArgs e) => ApplySections();

    /// <summary>
    /// Verteilt die Hoehe nach dem, was offen ist.
    ///
    /// Die eine Feinheit, die es braucht: Ist die Farbkorrektur zugeklappt, bekommen
    /// die Ebenen den ganzen Platz. Sonst staende unter einem zugeklappten Kopf eine
    /// leere Flaeche, die nichts zeigt und auch nichts zeigen kann - und "zuklappen"
    /// haette dann nichts gespart, was sein einziger Zweck ist.
    /// </summary>
    private void ApplySections()
    {
        // Waehrend die Oberflaeche aufgebaut wird, meldet der Abschnittskopf sein
        // Haekchen bereits - und zwar in dem Augenblick, in dem der Aufbau bei ihm
        // angekommen ist. Was weiter unten in der Datei steht, gibt es dann noch
        // nicht. Ein Wert, der beim Aufbau gesetzt wird, loest eben auch beim Aufbau
        // aus, und das ist die haeufigste Art, eine Oberflaeche zum Absturz zu
        // bringen, die im Uebersetzer einwandfrei aussieht.
        if (Layers is null || Tools is null || LayerSplitter is null || LayersRow is null) return;

        bool colour = ColourHeader.IsChecked == true;
        bool layers = Layers.Visibility == Visibility.Visible;

        Tools.Visibility = colour ? Visibility.Visible : Visibility.Collapsed;
        ColourRow.Height = colour ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        LayersRow.Height = !layers ? new GridLength(0)
            : colour ? new GridLength(LayerHeight())
                     : new GridLength(1, GridUnitType.Star);

        // Ein Griff zwischen zwei Flaechen, von denen eine zu ist, kann nichts
        // aufteilen. Ihn trotzdem zu zeigen hiesse, an etwas ziehen zu lassen, das
        // sich nicht bewegt.
        LayerSplitter.Visibility = layers && colour ? Visibility.Visible : Visibility.Collapsed;
    }

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

    private void OnLayersResized(object sender, DragCompletedEventArgs e)
    {
        _settings.AtelierLayersHeight = LayersRow.ActualHeight;
        _persist(_settings);
    }
}
