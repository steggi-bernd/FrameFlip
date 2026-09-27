using System.Windows;
using System.Windows.Controls;
using FrameFlip.Atelier;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Views;

/// <summary>
/// Die Zielzeile ueber dem Bild (docs/Atelier-Arbeitsablauf.md, C1): woran gerade gearbeitet
/// wird, aus dem Bearbeitungsziel der Sitzung. Jedes Glied, das nicht selbst das Ziel ist,
/// waehlt per Klick, wofuer es steht - ueber dieselben Wege wie Ebenenliste und Editor, damit
/// die Auswahl dort mitgeht.
/// </summary>
public partial class AtelierPage
{
    private void SetUpTargetPath() => _recipe.TargetChanged += ShowTargetPath;

    /// <summary>Die Glieder, die gerade dastehen - fuer die Probe.</summary>
    internal IReadOnlyList<TargetStep> TargetSteps { get; private set; } = Array.Empty<TargetStep>();

    /// <summary>Zeichnet die Zielzeile neu - nach einem Zielwechsel und nach Aenderungen an Namen und Aufbau.</summary>
    private void ShowTargetPath()
    {
        TargetStrip.Visibility = _frame is null ? Visibility.Collapsed : Visibility.Visible;
        if (_frame is null) return;

        var (steps, note) = TargetPath.For(_recipe.Target, _graph);
        TargetSteps = steps;

        TargetPathPanel.Children.Clear();

        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0) TargetPathPanel.Children.Add(Chevron());
            TargetPathPanel.Children.Add(Step(steps[i]));
        }

        TargetNote.Text = note ?? string.Empty;
        TargetNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement Step(TargetStep step)
    {
        if (step.Current)
        {
            // Das Ziel selbst: hervorgehoben, ohne Klick - es ist schon gewaehlt.
            var text = new TextBlock { Text = step.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

            var frame = new Border
            {
                Child = text,
                Padding = new Thickness(7, 2, 7, 2),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
            };
            frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

            return frame;
        }

        if (step.GoTo is not { } target)
        {
            var plain = new TextBlock { Text = step.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            plain.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            return plain;
        }

        var button = new Button { Content = step.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "LinkButton");
        button.Click += (_, _) => GoTo(target);

        return button;
    }

    private static TextBlock Chevron()
    {
        var chevron = new TextBlock { Text = "›", FontSize = 13, Margin = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return chevron;
    }

    /// <summary>
    /// "+ Korrektur": eine Korrektur fuer das, was die Zeile nennt - ueber denselben Weg wie die
    /// Werkzeugleiste. Im Stapel die Grundkarte des Ziels, im Graphen ein Knoten an seiner Stelle.
    /// </summary>
    private void OnTargetCorrect(object sender, RoutedEventArgs e)
    {
        // Auf eine Auswahl: Die neue Maskenebene bringt ihre Korrektur schon mit - sie wird das Ziel,
        // statt eine zweite dahinter zu legen.
        if (MaterialiseSelection())
        {
            if (InNodes && SelectedNode is MaskNode mask && _graph?.Into(mask.Id, "Ebene") is { } picture &&
                _graph.Find(picture.From) is LayerGradeNode grade)
            {
                NodeView.Select(grade);
            }
            else if (!InNodes)
            {
                Tools.Show("Basic");
            }

            return;
        }

        UseTool(ToolCatalog.All.Single(entry => entry.TitleKey == "S_Correction"));
    }

    /// <summary>"+ Effekt ...": die Suche der Werkzeugleiste - was darin gewaehlt wird, geht dorthin, wo die Zeile zeigt.</summary>
    private void OnTargetEffect(object sender, RoutedEventArgs e) => ToolBand.OpenSearch();

    /// <summary>
    /// Ein Glied der Zielzeile wurde angeklickt. Gewaehlt wird ueber die Wege, die auch Liste und
    /// Editor nehmen - das Ziel folgt dann von selbst, und beide zeigen dieselbe Wahl.
    /// </summary>
    internal void GoTo(EditingTarget target)
    {
        switch (target)
        {
            case EditingTarget.GraphNode { Node: MixNode mix, FromLayerList: true } when InNodes:
                OnLayerChosen(mix);
                break;

            case EditingTarget.GraphNode { Node: var node } when InNodes:
                NodeView.Select(node);
                NodeView.Reveal(node);
                break;

            case EditingTarget.PictureTarget when InNodes:
                NodeView.Select(null);
                break;
        }
    }
}
