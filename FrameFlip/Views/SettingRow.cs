using System.Windows;
using System.Windows.Controls;

namespace FrameFlip.Views;

/// <summary>
/// Eine Einstellung: Bezeichnung, ein (i) mit Erklaerung, das Bedienelement.
///
/// Der Anlass war nicht der Entwurf, sondern eine Zaehlung. Die Einstellungsseite
/// trug VIERZIG von Hand gesetzte Abstaende in ZWEIUNDZWANZIG verschiedenen Werten -
/// 0,14,0,0 neben 0,6,0,0 neben 0,0,0,20, jeder einzeln entschieden und keiner mit
/// einem anderen abgestimmt. Deshalb fluchtete nichts, und deshalb sah die Seite
/// unfertig aus, obwohl jedes einzelne Feld richtig war.
///
/// Hier kommen die Masse aus einer Vorlage. Eine Zeile sieht aus wie die naechste,
/// weil sie dieselbe Zeile IST, und ein geaenderter Abstand aendert alle.
///
/// Zum (i): Es zeigt nicht, was die Einstellung heisst - das steht daneben -, sondern
/// was sie tut und WANN man sie anfasst. Der zweite Teil ist der eigentliche Gewinn.
/// "RAM-Budget (MB)" versteht jeder; dass man es anfasst, wenn die Wiedergabe bei
/// grossen Sequenzen stockt, steht nirgends.
/// </summary>
public class SettingRow : HeaderedContentControl
{
    /* Kein DefaultStyleKeyProperty.OverrideMetadata hier.
     *
     * Das schickt WPF in die Themenwoerterbuecher (Themes/Generic.xaml), und dort
     * liegt nichts - FrameFlips Stile haengen an den Anwendungsressourcen. Ein
     * IMPLIZITER Stil mit TargetType greift von dort aus zuverlaessig, ohne dass
     * eine zweite Ablage noetig waere. */

    /// <summary>
    /// Was die Einstellung tut und wann sie sinnvoll ist. Leer heisst: kein (i).
    /// </summary>
    public static readonly DependencyProperty ExplainProperty = DependencyProperty.Register(
        nameof(Explain), typeof(string), typeof(SettingRow), new PropertyMetadata(null));

    public string? Explain
    {
        get => (string?)GetValue(ExplainProperty);
        set => SetValue(ExplainProperty, value);
    }

    /// <summary>
    /// Eine Einstellung, die man oefter braucht - oder die etwas nach draussen
    /// oeffnet. Sie bekommt Gewicht, damit der Blick nicht ueber alles gleich
    /// hinweggleitet.
    ///
    /// Sparsam zu vergeben: Wenn alles wichtig ist, ist nichts hervorgehoben.
    /// </summary>
    public static readonly DependencyProperty ImportantProperty = DependencyProperty.Register(
        nameof(Important), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    public bool Important
    {
        get => (bool)GetValue(ImportantProperty);
        set => SetValue(ImportantProperty, value);
    }

    /// <summary>
    /// Eine zweite Zeile unter dem Bedienelement - fuer das, was immer sichtbar
    /// bleiben muss statt hinter einem (i) zu verschwinden.
    /// </summary>
    public static readonly DependencyProperty FootnoteProperty = DependencyProperty.Register(
        nameof(Footnote), typeof(string), typeof(SettingRow), new PropertyMetadata(null));

    public string? Footnote
    {
        get => (string?)GetValue(FootnoteProperty);
        set => SetValue(FootnoteProperty, value);
    }
}

/// <summary>
/// Eine Gruppe von Einstellungen, die zusammengehoeren - mit Ueberschrift.
///
/// Sie ersetzt die Trennlinien. Eine Linie sagt "hier ist ein Schnitt" und sonst
/// nichts; eine Gruppe mit Namen sagt, WORUM es in dem Abschnitt geht. Nebenbei
/// verschwindet damit die Sorte Fehler, die eine Linie quer durch eine Beschriftung
/// zeichnet - es gibt keine Linien mehr.
/// </summary>
public class SettingGroup : HeaderedItemsControl
{
    static SettingGroup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SettingGroup), new FrameworkPropertyMetadata(typeof(SettingGroup)));
    }
}
