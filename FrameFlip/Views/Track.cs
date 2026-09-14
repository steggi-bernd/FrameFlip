using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace FrameFlip.Views;

/// <summary>
/// Gesperrte Versalien.
///
/// Der Entwurf setzt jede Beschriftung gesperrt - SEQUENZEN, METRIKEN, RESTZEIT.
/// WPF kennt dafuer keine Eigenschaft: Es gibt FontStretch, das die Buchstaben
/// selbst schmaler macht, aber nichts fuer den Abstand zwischen ihnen.
///
/// Hier wird zwischen die Zeichen ein Haarspatium (U+200A) gesetzt. Die Alternative
/// waere, jedes Zeichen in einen eigenen Textblock zu legen und die Abstaende als
/// Rand zu setzen - das gaebe Abstaende auf den Pixel genau, kostet aber je
/// Beschriftung ein Dutzend Elemente im Layoutbaum, und diese Beschriftungen stehen
/// in Listen, die sich bei jedem Takt neu aufbauen.
///
/// Der ungesperrte Text wandert nach AutomationProperties.Name. Sonst laese eine
/// Sprachausgabe "S E Q U E N Z E N", und auch die Pruefwerkzeuge fanden die
/// Beschriftungen nicht wieder.
/// </summary>
public static class Track
{
    private const char Hair = ' ';

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Track),
        new PropertyMetadata(null, OnTextChanged));

    /// <summary>Wie viele Haarspatien zwischen zwei Zeichen. Zwei entsprechen etwa 0,2 em.</summary>
    public static readonly DependencyProperty AmountProperty = DependencyProperty.RegisterAttached(
        "Amount", typeof(int), typeof(Track),
        new PropertyMetadata(2, OnTextChanged));

    public static void SetText(DependencyObject target, string? value) => target.SetValue(TextProperty, value);

    public static string? GetText(DependencyObject target) => (string?)target.GetValue(TextProperty);

    public static void SetAmount(DependencyObject target, int value) => target.SetValue(AmountProperty, value);

    public static int GetAmount(DependencyObject target) => (int)target.GetValue(AmountProperty);

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBlock block) return;

        string? text = GetText(block);

        if (string.IsNullOrEmpty(text))
        {
            block.Text = string.Empty;
            return;
        }

        block.Text = Spaced(text, GetAmount(block));

        // Der lesbare Text bleibt erhalten - fuer Sprachausgaben und fuer Werkzeuge,
        // die die Oberflaeche ueber ihre Beschriftungen pruefen.
        AutomationProperties.SetName(block, text);
    }

    private static string Spaced(string text, int amount)
    {
        if (amount <= 0 || text.Length < 2) return text;

        var gap = new string(Hair, amount);
        var builder = new System.Text.StringBuilder(text.Length * (1 + amount));

        for (int i = 0; i < text.Length; i++)
        {
            builder.Append(text[i]);

            // Kein Abstand hinter dem letzten Zeichen: Sonst sitzt eine zentrierte
            // Beschriftung um ein halbes Spatium zu weit links.
            if (i < text.Length - 1) builder.Append(gap);
        }

        return builder.ToString();
    }
}
