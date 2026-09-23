using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Der Farbstreifen im Knotenmodus: Er zeigt die Einstellungen des gewaehlten Knotens.
///
/// Ein Werkzeugknoten zeigt seine Karte - dieselbe wie im Stapel, und sie bearbeitet
/// das Werkzeug des Knotens selbst. Eine Ebenenkorrektur zeigt die Karten, die eine
/// Ebene haben kann. Was keine Karte hat - Mischen, Maske, Platzieren, die
/// Grundkorrektur des Bildes -, bekommt einfache Felder mit denselben Reglern.
///
/// Palette und Zielschalter verschwinden: Hinzugefuegt wird im Graphen, und das Ziel
/// ist der Knoten.
/// </summary>
public partial class GradingPanel
{
    /// <summary>Welche Karten im Knotenmodus zu sehen sind - null heisst: der gewoehnliche Stapel.</summary>
    private HashSet<string>? _focus;

    /// <summary>Ob der Streifen gerade einen Knoten zeigt.</summary>
    public bool InNodeFocus => _focus is not null;

    /// <summary>
    /// Zeigt die Einstellungen eines Knotens.
    /// </summary>
    /// <param name="hint">Ein Satz ueber allem - etwa "einen Knoten waehlen". Null: keiner.</param>
    /// <param name="sections">Die Karten, die zu sehen sind.</param>
    /// <param name="fields">Einfache Felder unter dem Hinweis, fuer Knoten ohne Karte.</param>
    public void ShowNode(string? hint, IReadOnlyCollection<string> sections, IReadOnlyList<NodeField> fields)
    {
        _focus = new HashSet<string>(sections, StringComparer.Ordinal);

        TargetBar.Visibility = Visibility.Collapsed;
        PaletteBar.Visibility = Visibility.Collapsed;

        NodeHint.Text = hint ?? "";
        NodeHint.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;

        BuildFields(fields);
        ShowActive();
    }

    /// <summary>Zurueck zum gewoehnlichen Stapel.</summary>
    public void LeaveNodes()
    {
        if (_focus is null) return;

        _focus = null;

        TargetBar.Visibility = Visibility.Visible;
        PaletteBar.Visibility = Visibility.Visible;
        NodeHint.Visibility = Visibility.Collapsed;
        NodeFields.Children.Clear();
        NodeFields.Visibility = Visibility.Collapsed;

        ShowActive();
    }

    private void BuildFields(IReadOnlyList<NodeField> fields)
    {
        NodeFields.Children.Clear();
        NodeFields.Visibility = fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool first = true;

        foreach (var field in fields)
        {
            var row = Row(field);
            if (row is null) continue;

            if (!first && row is FrameworkElement element)
                element.Margin = new Thickness(0, 10, 0, 0);

            NodeFields.Children.Add(row);
            first = false;
        }
    }

    private UIElement? Row(NodeField field) => field switch
    {
        SliderField slider => SliderRow(slider),
        ChoiceField choice => ChoiceRow(choice),
        SwitchField toggle => SwitchRow(toggle),
        InfoField info => new TextBlock
        {
            Text = info.Text,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)FindResource("DashLabel"),
        },
        _ => null,
    };

    private UIElement SliderRow(SliderField field)
    {
        var panel = new StackPanel();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 2) };

        head.Children.Add(new TextBlock { Text = Strings.T(field.LabelKey), Style = (Style)FindResource("PanelLabel") });

        var value = new TextBlock { Style = (Style)FindResource("PanelValue") };
        head.Children.Add(value);

        var slider = new Slider
        {
            Style = (Style)FindResource("PanelSlider"),
            Minimum = field.Min,
            Maximum = field.Max,
            SmallChange = (field.Max - field.Min) / 200,
            LargeChange = (field.Max - field.Min) / 20,
        };

        void Show(double v) => value.Text = v.ToString(field.Format, CultureInfo.CurrentCulture);

        _filling = true;

        try
        {
            slider.Value = field.Get();
        }
        finally
        {
            _filling = false;
        }

        Show(field.Get());

        slider.ValueChanged += (_, e) =>
        {
            if (_filling) return;

            field.Set(e.NewValue);
            Show(e.NewValue);

            // Wie an jedem Regler: waehrend des Zuges grob, das Loslassen holt der
            // Zeitgeber der Seite nach.
            Raise(interim: true);
        };

        // Doppelklick stellt zurueck, wie an den Karten.
        slider.MouseDoubleClick += (_, _) => slider.Value = field.Default;

        panel.Children.Add(head);
        panel.Children.Add(slider);

        return panel;
    }

    private UIElement ChoiceRow(ChoiceField field)
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = Strings.T(field.LabelKey),
            Style = (Style)FindResource("PanelLabel"),
            Margin = new Thickness(0, 0, 0, 4),
        });

        var box = new ComboBox { Style = (Style)FindResource("OverlayCombo"), HorizontalAlignment = HorizontalAlignment.Stretch };
        int current = field.Get();

        foreach (var (key, v) in field.Options)
        {
            var item = new ComboBoxItem
            {
                Style = (Style)FindResource("OverlayComboItem"),
                Content = Strings.T(key),
                Tag = v,
            };

            box.Items.Add(item);
            if (v == current) box.SelectedItem = item;
        }

        box.SelectionChanged += (_, _) =>
        {
            if (_filling || box.SelectedItem is not ComboBoxItem { Tag: int v }) return;

            field.Set(v);
            Raise(interim: false);
        };

        panel.Children.Add(box);

        return panel;
    }

    private UIElement SwitchRow(SwitchField field)
    {
        var toggle = new ToggleButton
        {
            Style = (Style)FindResource("OverlayToggle"),
            Content = Strings.T(field.LabelKey),
            IsChecked = field.Get(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 11,
        };

        toggle.Click += (_, _) =>
        {
            field.Set(toggle.IsChecked == true);
            Raise(interim: false);
        };

        return toggle;
    }
}
