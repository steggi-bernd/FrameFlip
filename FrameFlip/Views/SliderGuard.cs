using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FrameFlip.Views;

/// <summary>
/// Der Abbruch am Regler: Rechtsklick waehrend des Ziehens legt den alten Wert zurueck.
///
/// Ein Regler ist das einzige Bedienelement ohne Rueckweg. Ein Knopf wird gedrueckt
/// oder nicht; ein Feld laesst sich leer machen. Wer aber an einem Regler zieht und
/// merkt, dass es falsch war, hat den Ausgangswert schon verloren - er stand nur so
/// lange da, bis die Maus sich bewegte. Rueckgaengig gibt es, aber das ist ein
/// Schritt zu spaet und einer zu weit: Es nimmt auch das zurueck, was vorher richtig
/// war.
///
/// Rechts ist dafuer der richtige Knopf. Er tut an einem Regler sonst nichts, und
/// "abbrechen, was ich gerade tue" ist genau das, was er in jedem Zeichenprogramm
/// beim Ziehen bedeutet.
///
/// Angehaengt statt eingebaut, weil es sonst nur an den Reglern gaebe, an die jemand
/// gedacht hat. So haengt es am STIL, und damit an jedem Regler, der diesen Stil
/// traegt - auch an denen, die es noch nicht gibt.
/// </summary>
public static class SliderGuard
{
    /// <summary>Haengt den Abbruch an einen Regler. Gesetzt wird das im Stil.</summary>
    public static readonly DependencyProperty CancelOnRightClickProperty =
        DependencyProperty.RegisterAttached(
            "CancelOnRightClick", typeof(bool), typeof(SliderGuard),
            new PropertyMetadata(false, OnCancelChanged));

    public static void SetCancelOnRightClick(DependencyObject at, bool value)
        => at.SetValue(CancelOnRightClickProperty, value);

    public static bool GetCancelOnRightClick(DependencyObject at)
        => (bool)at.GetValue(CancelOnRightClickProperty);

    /// <summary>
    /// Der Wert, mit dem das Ziehen anfing - und ob ueberhaupt gezogen wird.
    ///
    /// Am Regler selbst und nicht in einer Liste nebenher: Eine Liste muesste wissen,
    /// wann ein Regler verschwindet, und ein vergessener Eintrag hielte ihn am Leben.
    /// </summary>
    private static readonly DependencyProperty StartValueProperty =
        DependencyProperty.RegisterAttached(
            "StartValue", typeof(double?), typeof(SliderGuard), new PropertyMetadata(null));

    private static void OnCancelChanged(DependencyObject at, DependencyPropertyChangedEventArgs e)
    {
        if (at is not Slider slider) return;

        // Erst abhaengen, dann anhaengen: Ein Stil kann mehrfach greifen, und zwei
        // Abbrueche an demselben Regler waeren einer zuviel.
        slider.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted));
        slider.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragDone));

        slider.PreviewMouseRightButtonDown -= OnRightDown;
        slider.PreviewMouseLeftButtonDown -= OnLeftDown;

        if (!true.Equals(e.NewValue)) return;

        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragDone));

        slider.PreviewMouseRightButtonDown += OnRightDown;
        slider.PreviewMouseLeftButtonDown += OnLeftDown;
    }

    /// <summary>
    /// Gemerkt wird schon beim Druecken, nicht erst beim Ziehen.
    ///
    /// Diese Regler springen zum Zeiger (IsMoveToPointEnabled): Der Wert aendert sich
    /// also bereits mit dem Klick, bevor der Griff ueberhaupt zu ziehen anfaengt. Wer
    /// erst dort merkt, hat den Ausgangswert schon verloren - genau den, um den es
    /// geht.
    /// </summary>
    private static void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && slider.GetValue(StartValueProperty) is null)
            slider.SetValue(StartValueProperty, slider.Value);
    }

    private static void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is Slider slider && slider.GetValue(StartValueProperty) is null)
            slider.SetValue(StartValueProperty, slider.Value);
    }

    private static void OnDragDone(object sender, DragCompletedEventArgs e)
    {
        if (sender is not Slider slider) return;

        slider.SetValue(StartValueProperty, null);

        // Den Hof wieder loswerden, den der Griff beim Ziehen aufsetzt.
        //
        // WPF haelt den Griff nach dem Ziehen fuer den Beruehrten - er bleibt es, bis
        // woanders hingefasst wird. Der Ring bliebe dann an einem Regler stehen, den
        // niemand mehr bedient, und beim naechsten stuende ein zweiter daneben.
        Release(slider);
    }

    /// <summary>
    /// Rechts bricht ab - und zwar nur, solange gezogen wird.
    ///
    /// Ein Rechtsklick auf einen ruhenden Regler tut nichts. Er darf nichts tun:
    /// Sonst legte ein Klick daneben einen Wert zurueck, den niemand angefasst hat.
    /// </summary>
    private static void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        if (slider.GetValue(StartValueProperty) is not double back) return;

        slider.SetValue(StartValueProperty, null);

        // Erst das Ziehen beenden, dann den Wert legen. Andersherum schriebe der
        // laufende Zug den zurueckgelegten Wert sofort wieder ueber.
        if (Grip(slider) is { IsDragging: true } grip) grip.CancelDrag();

        Release(slider);

        slider.Value = back;

        e.Handled = true;
    }

    /// <summary>Gibt Maus und Griff wieder frei - nach Abbruch wie nach Ziehen.</summary>
    private static void Release(Slider slider)
    {
        if (Grip(slider) is { } grip && grip.IsMouseCaptured) grip.ReleaseMouseCapture();

        if (Mouse.Captured is DependencyObject held && Belongs(held, slider))
            Mouse.Capture(null);
    }

    private static bool Belongs(DependencyObject at, Slider slider)
    {
        for (var up = at; up is not null; up = VisualParent(up))
            if (ReferenceEquals(up, slider)) return true;

        return false;
    }

    private static DependencyObject? VisualParent(DependencyObject at)
        => at is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(at) : null;

    /// <summary>
    /// Der Griff aus der Vorlage - er traegt das Ziehen und den Hof.
    ///
    /// Voll ausgeschrieben, weil FrameFlip eine eigene Klasse namens Track hat und
    /// der Compiler sonst die nimmt: Die Sichtbarkeit einer Ebene ist etwas anderes
    /// als die Schiene eines Reglers.
    /// </summary>
    private static Thumb? Grip(Slider slider)
        => slider.Template?.FindName("PART_Track", slider)
               is System.Windows.Controls.Primitives.Track track
           ? track.Thumb
           : null;
}
