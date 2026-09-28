using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ein Durchgang ueber das FERTIGE Bild, der Reihe nach.
///
/// Die sechste und unbequemste Passart. Die anderen fuenf haben alle dieselbe
/// angenehme Eigenschaft: Jeder Bildpunkt laesst sich fuer sich rechnen, also auf
/// beliebig vielen Faeden und notfalls auf einem groben Raster, dessen Luecken danach
/// aufgefuellt werden. Manches geht so nicht.
///
/// Fehlerdiffusion schreibt in Nachbarn, die noch nicht an der Reihe waren. Pixel
/// Sorting ordnet ganze Laeufe um. Beides braucht das Bild als GANZES, in einer
/// festen Reihenfolge, und beides wird dadurch teuer:
///
///   Ein Faden. Nicht aus Bequemlichkeit - die Reihenfolge IST das Verfahren.
///
///   Immer volle Aufloesung. Ein grobes Raster waere nicht ein groeberes Ergebnis,
///   sondern ein anderes: Es entschiede, welche Punkte den Fehler abbekommen.
///
///   Beim Ziehen an einem Regler faellt es deshalb aus. Was man waehrend des Zuges
///   sieht, ist das Bild ohne diesen Durchgang; beim Loslassen kommt er dazu. Das
///   ist dieselbe Zweiteilung wie ueberall sonst, nur faellt sie hier auf. Die
///   Ausnahme ist, wer eine grobe Fassung kennt - siehe <see cref="ICoarseFramePass"/>.
///
/// Gerechnet wird auf den FERTIGEN Anzeigewerten - Bgra32, so wie sie gleich auf dem
/// Schirm stehen. Das ist bei Fehlerdiffusion nicht nur bequem, sondern richtig:
/// Rastern ist eine Aussage darueber, wieviele Stufen man SIEHT.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DiffusionTool), DiffusionTool.KindName)]
[JsonDerivedType(typeof(SortTool), SortTool.KindName)]
public interface IFramePass
{
    /// <summary>Kennung fuer die Speicherung.</summary>
    string Kind { get; }

    /// <summary>True, wenn nichts zu rechnen ist.</summary>
    bool IsNeutral { get; }

    void Prepare();

    /// <summary>
    /// Der ganze Rahmen, in Bgra32 und der Reihe nach.
    /// </summary>
    /// <param name="pixels">Der Zielpuffer - dieselben Bytes, die gleich gezeigt werden.</param>
    /// <param name="number">
    /// Die Nummer des Bildes - fuer alles, was je Bild anders ausfallen darf. Ein
    /// Glitch, der ueber die ganze Sequenz an derselben Stelle steht, ist keiner.
    /// </param>
    void Apply(IntPtr pixels, int width, int height, int stride, int number = 0);
}

/// <summary>
/// Ein Durchgang, der auch eine grobe Fassung kennt - fuer die Vorschau beim Ziehen eines Reglers
/// (docs/Atelier-Arbeitsablauf.md, C7c).
///
/// Nicht jeder kann das. Fehlerdiffusion entscheidet nach der Nachbarschaft, und ein Gitter
/// aendert die Nachbarschaft - dort waere das grobe Ergebnis ein anderes, nicht ein groeberes, und
/// der Durchgang bleibt beim Ziehen aus. Pixel Sort dagegen sortiert auf dem Gitter dieselben
/// Laeufe, nur kuerzer; aufgeblasen sieht das aus wie das volle Bild, etwas weicher.
/// </summary>
public interface ICoarseFramePass : IFramePass
{
    /// <summary>
    /// Derselbe Durchgang auf dem Gitter der groben Vorschau - jeder <paramref name="step"/>-te
    /// Bildpunkt. Laengen in Bildpunkten gelten im selben Verhaeltnis.
    /// </summary>
    void ApplyCoarse(IntPtr pixels, int width, int height, int stride, int number, int step);
}
