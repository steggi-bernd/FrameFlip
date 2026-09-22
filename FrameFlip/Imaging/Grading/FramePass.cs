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
///   ist dieselbe Zweiteilung wie ueberall sonst, nur faellt sie hier auf.
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
    void Apply(IntPtr pixels, int width, int height, int stride);
}
