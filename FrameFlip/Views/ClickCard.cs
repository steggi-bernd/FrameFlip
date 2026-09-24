using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FrameFlip.Views;

/// <summary>
/// Eine anklickbare Kachel.
///
/// Die Projektseite war aus Rahmen mit Mausereignissen gebaut. Das sieht richtig aus
/// und ist es fuer die Maus auch - aber ein Rahmen nimmt keinen Fokus, hoert auf
/// keine Taste und meldet sich bei keiner Sprachausgabe. Wer die Seite ohne Maus
/// bedienen wollte, kam schlicht nicht hinein, und Pruefwerkzeuge sahen eine Flaeche
/// ohne Namen und ohne Wirkung.
///
/// Statt jede Kachel in einen Knopf zu verpacken - was das Aussehen ueber eine
/// Vorlage haette nachbauen muessen - bekommt der Rahmen hier, was ihm fehlte: Fokus,
/// Tastatur und einen Automatisierungsknoten, der "Aufrufen" kennt. Das Aussehen
/// bleibt dabei Zeile fuer Zeile dasselbe.
/// </summary>
public sealed class ClickCard : Border
{
    /// <summary>Fuer XAML. Was beim Ausloesen geschieht, traegt der Code nach.</summary>
    public ClickCard()
    {
        Focusable = true;
        Cursor = Cursors.Hand;

        KeyDown += OnKeyDown;

        /* Die Maus gehoert hierher, nicht an jede Aufrufstelle.
         *
         * Sie fehlte: Die Klasse konnte Fokus, Tastatur und Bedienungshilfen, aber
         * ein Mausklick tat nichts. Dass die Kacheln trotzdem reagierten, lag daran,
         * dass jeder Aufrufer sich selbst ein MouseLeftButtonUp anhaengte - dreimal
         * dieselbe Zeile.
         *
         * Der Rueckwegknopf im Projektpfad war der vierte Aufrufer, und er vergass
         * es. Er liess sich mit der Tastatur ausloesen und meldete sich brav bei den
         * Bedienungshilfen - weshalb er im Automatentest tadellos lief und unter dem
         * Mauszeiger nichts tat. */
        MouseLeftButtonUp += (_, _) => Activate();
    }

    public ClickCard(string name, Action click) : this()
    {
        OnActivate = click;
        AutomationProperties.SetName(this, name);
    }

    /// <summary>Was der Klick tut. Ohne das bleibt die Kachel ein Rahmen.</summary>
    public Action? OnActivate { get; set; }

    /// <summary>
    /// Wird vor dem Ausloesen gefragt. Auf der Projektseite laeuft ein Ziehvorgang
    /// ueber dieselben Kacheln - waehrenddessen ist ein Loslassen kein Klick.
    /// </summary>
    public Func<bool>? Suppress { get; init; }

    public void Activate()
    {
        if (Suppress?.Invoke() == true) return;

        OnActivate?.Invoke();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;

        Activate();
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CardPeer(this);

    private sealed class CardPeer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        private readonly ClickCard _card;

        public CardPeer(ClickCard card) : base(card) => _card = card;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(ClickCard);

        protected override bool IsContentElementCore() => true;

        public override object? GetPattern(PatternInterface pattern)
            => pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern);

        public void Invoke() => _card.Dispatcher.BeginInvoke(new Action(_card.Activate));
    }
}
