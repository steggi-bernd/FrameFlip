namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Setzt die Ebenen zu einem Bild zusammen.
///
/// Das Ergebnis ist wieder ein <see cref="FloatFrame"/> in linearem Licht, und damit
/// geht es unveraendert denselben Weg weiter wie ein einzeln gelesenes Bild:
/// Grundkorrektur, Werkzeuge, Sichtumwandlung. Die Zusammensetzung ist ein Schritt
/// davor und kein zweiter Weg daneben - sonst haette der Stapellauf eine Kette und
/// die Vorschau eine andere, und irgendwann saehe der Export anders aus als das,
/// worauf sich jemand verlassen hat.
///
/// Zusammengesetzt wird VOR der Sichtumwandlung. Das ist die Stelle, an der die
/// Zerlegung entstanden ist: Blender addiert die Passe im linearen Licht zum
/// fertigen Bild. Wer sie hinterher zusammensetzte, addierte bereits durch AgX
/// gegangene Bilder, und die Summe waere nicht das Original, sondern heller.
/// </summary>
public static class LayerComposer
{
    /// <summary>
    /// Baut das Bild aus den Ebenen. Null, wenn keine Ebene etwas beitraegt.
    /// </summary>
    /// <param name="sources">
    /// Die gelesenen Passe, nach dem Namen der Quelle. Eine Ebene, deren Pass fehlt
    /// oder eine andere Groesse hat, wird uebersprungen - eine halb gelesene Datei
    /// soll ein Bild ergeben, das man ansehen kann, und keinen Abbruch.
    /// </param>
    /// <param name="into">
    /// Ein Frame aus einem frueheren Durchgang, in den geschrieben werden darf.
    ///
    /// Bei 1080p sind das 33 Megabyte je Durchgang, bei 4K rund 130 - und
    /// zusammengesetzt wird bei JEDEM Reglerzug. Die Felder jedes Mal neu anzulegen
    /// heisst, sie jedes Mal neu zu nullen und dem Sammler zu ueberlassen; gemessen
    /// war das der groessere Teil der Zeit, nicht die Rechnung.
    ///
    /// Der Aufrufer muss einen Frame uebergeben, der IHM gehoert - niemals einen aus
    /// <paramref name="sources"/>. Hineinzuschreiben, waehrend daraus gelesen wird,
    /// ergaebe ein Bild, das sich mit jedem Durchgang weiter verzieht.
    /// </param>
    /// <param name="step">
    /// Nur jeder n-te Bildpunkt wird zusammengesetzt - dieselben Gitterpunkte, die
    /// der grobe Durchgang der Anzeige anschliessend liest.
    ///
    /// Das ist der Grund, warum es diese Zahl hier ueberhaupt gibt: Beim Ziehen an
    /// einem Regler rechnet die Anzeige ohnehin nur auf einem Gitter und
    /// interpoliert dazwischen. Wer trotzdem jeden Bildpunkt zusammensetzt, rechnet
    /// fuenfzehn Sechzehntel davon fuer nichts - gemessen sind das bei 1080p mit
    /// einer Einstellungsebene neunzig Millisekunden je Reglerzug statt sechs.
    ///
    /// Die Werte ZWISCHEN den Gitterpunkten bleiben dabei stehen, wie sie waren. Das
    /// ist in Ordnung, weil sie niemand liest - aber nur solange die Schrittweite
    /// dieselbe ist. Beim Loslassen laeuft ein voller Durchgang und fuellt alles.
    /// </param>
    /// <param name="number">
    /// Die Bildnummer. Gebraucht fuer gemalte Masken, die nicht gesperrt sind: Dort
    /// gilt je Bild ein eigener Anstrich, und ohne die Nummer waere nicht zu sagen,
    /// welcher.
    /// </param>
    public static FloatFrame? Compose(LayerStack stack, IReadOnlyDictionary<string, FloatFrame> sources,
                                      FloatFrame? into = null, int step = 1, int number = 0)
    {
        step = Math.Clamp(step, 1, 16);

        var used = new List<(ImageLayer Layer, FloatFrame? Frame, StepKind Kind)>();
        int width = 0, height = 0;

        Collect(stack.Layers, sources, used, ref width, ref height, depth: 0);

        // Keine SICHTBARE Passebene? Dann gibt das Bild selbst die Groesse vor.
        //
        // Die Regel dort drueben - nur ein Pass legt die Leinwand fest - hat einen
        // guten Grund: Sonst bestimmte ein Wasserzeichen von zweihundert Punkten die
        // Groesse, wenn es zufaellig zuunterst liegt. Sie hatte aber eine Luecke, und
        // die war teuer: Ist die Passebene AUSGEBLENDET, legt niemand eine Groesse
        // fest, und dann kam ueberhaupt kein Bild zustande.
        //
        // Im Atelier sah das aus, als taete das Einblenden nichts. Der Rueckfall
        // zeigte weiter die Datei, jede eingeblendete Ebene blieb wirkungslos - und
        // in dem Augenblick, in dem jemand die unterste Ebene einblendete, erschienen
        // alle anderen auf einmal. Nach einer wiederhergestellten Sitzung ist genau
        // das der Normalfall, denn dort kommt der Stapel zurueck, wie er stand.
        //
        // Das Bild liegt immer unter dem leeren Schluessel. Es ist die richtige
        // Antwort auf "wie gross ist die Leinwand", ganz gleich, welche Ebene gerade
        // zu sehen ist: Die Leinwand haengt nicht daran, was jemand eingeblendet hat.
        if (width == 0 && sources.TryGetValue("", out var canvas))
        {
            width = canvas.Width;
            height = canvas.Height;
        }

        // Und wenn es auch das nicht gibt: die groesste Ebene, die etwas mitbringt.
        // Kein gutes Mass, aber ein Bild - und ein Bild ist besser als keines.
        if (width == 0)
        {
            foreach (var (_, frame, _) in used)
            {
                if (frame is null) continue;
                if ((long)frame.Width * frame.Height <= (long)width * height) continue;

                width = frame.Width;
                height = frame.Height;
            }
        }

        // Ohne einen einzigen Pass gibt es nichts, worauf eine Korrektur wirken
        // koennte - und auch keine Bildgroesse. Ein Stapel aus lauter
        // Einstellungsebenen ist kein Bild.
        if (used.Count == 0 || width == 0) return null;

        // Eine Einstellungsebene ohne Wirkung bleibt trotzdem stehen. Sie
        // herauszunehmen waere die naheliegende Ersparnis und ein Fehler: Traegt sie
        // eine angeschnittene Ebene, haengt diese danach an einer anderen - und der
        // Stapel rechnet etwas anderes, sobald man die Korrektur auf null stellt.
        // Der Durchlauf kostet ohnehin fast nichts; die Kette kehrt sofort zurueck.

        // Eine einzelne unveraenderte Ebene ist das Bild selbst. Sie durchzureichen
        // spart bei 4K rund hundert Megabyte und eine Kopie.
        //
        // Nicht aber, wenn sie freigestellt ist und nicht aus einer EXR kommt. Dann
        // muesste ihre Deckung angewandt werden, und auf diesem Weg tut es niemand:
        // Was hier zurueckgegeben wird, geht unveraendert an die Anzeige. Genau daran
        // hing der lange gesuchte Fehler - eine freigestellte PNG als BILD geoeffnet
        // zeigte den Muell unter ihrer Deckung, dieselbe Datei als EBENE nicht.
        bool bare = used[0].Frame is null ||
                    used[0].Frame!.IsSceneReferred ||
                    !used[0].Frame!.HasMatte;

        if (used.Count == 1 && used[0].Kind == StepKind.Layer && bare &&
            used[0].Layer.IsNeutral && used[0].Layer.LiesOnBlack)
        {
            return used[0].Frame!;
        }

        int count = width * height;

        // Der alte Frame taugt, wenn er die richtige Groesse hat und keiner der
        // Quellen ist. Das zweite prueft der Aufrufer; hier wird nur die Groesse
        // abgeglichen.
        bool reuse = into is not null && into.Width == width && into.Height == height &&
                     into.A is not null;

        var r = reuse ? into!.R : new float[count];
        var g = reuse ? into!.G : new float[count];
        var b = reuse ? into!.B : new float[count];
        var a = reuse ? into!.A! : new float[count];

        bool sceneReferred = used.First(u => u.Frame is not null).Frame!.IsSceneReferred;

        // Je Ebene einmal vorbereitet, damit die innere Schleife nur noch multipliziert.
        var plans = new Plan[used.Count];
        for (int i = 0; i < used.Count; i++)
        {
            var layer = used[i].Layer;
            float gain = MathF.Pow(2f, layer.Exposure);

            // Die unterste Ebene kann sich an nichts anschneiden. Eine Schnittmaske
            // ohne Traeger als solche zu behandeln hiesse, sie verschwinden zu
            // lassen - sie wird stattdessen zur gewoehnlichen Ebene.
            bool clipped = layer.Clipped && i > 0;

            // Der Pass, aus dem die Maske liest. Fehlt er, faellt die Maske weg -
            // eine Ebene ganz verschwinden zu lassen, weil ihre Maske nicht gelesen
            // werden konnte, waere die falsche Antwort auf eine fehlende Datei.
            FloatFrame? maskFrame = null;
            FloatFrame[]? maskLevels = null;
            float[]? maskIds = null;
            var maskKind = layer.Mask.Kind;

            float maskFloor = 0f, maskSpan = 1f;

            if (maskKind == MaskKind.Pass)
            {
                if (sources.TryGetValue(layer.Mask.Source, out var found) &&
                    found.Width == width && found.Height == height)
                {
                    maskFrame = found;

                    // Ein Pass, der ohnehin zwischen 0 und 1 liegt, geht unveraendert
                    // ein - er IST die Maske. Einer, der darueber hinausgeht, ist
                    // eine Groesse in eigenen Einheiten: eine Tiefe in Metern. Der
                    // wird auf seine eigene Spanne bezogen, sonst waere alles ueber
                    // eins voll gedeckt und der Regler ohne Wirkung.
                    var (low, high) = found.MaskRange;

                    if (high > 1.0001f || low < -0.0001f)
                    {
                        maskFloor = low;
                        maskSpan = MathF.Max(1e-6f, high - low);
                    }
                }
                else
                {
                    maskKind = MaskKind.None;
                }
            }
            else if (maskKind == MaskKind.Cryptomatte)
            {
                var levels = new List<FloatFrame>();

                foreach (string level in layer.Mask.Levels)
                {
                    if (sources.TryGetValue(level, out var found) &&
                        found.Width == width && found.Height == height)
                    {
                        levels.Add(found);
                    }
                }

                maskLevels = levels.ToArray();
                maskIds = layer.Mask.Picks.Select(p => p.Id).ToArray();

                // Ohne Stufen oder ohne Auswahl gibt es nichts zu maskieren. Die
                // Maske fallen zu lassen ist hier die richtige Antwort: Eine leere
                // Auswahl liesse die Ebene ganz verschwinden, und das saehe aus wie
                // ein Fehler statt wie "es ist noch nichts ausgewaehlt".
                if (maskLevels.Length == 0 || maskIds.Length == 0) maskKind = MaskKind.None;
            }

            // Die Kette einer Einstellungsebene wird EINMAL vorbereitet, nicht je
            // Bildpunkt. Bei 4K waeren es sonst 25 Millionen Tabellenaufbauten.
            var grade = layer.Content == LayerContent.Adjustment
                ? layer.Grade()
                : default;

            // Platziert wird nur, wenn es etwas zu platzieren gibt: eine Ebene in
            // der Groesse der Leinwand und ohne Einstellung geht den geraden Weg
            // ueber den Index, ohne Abtasten und ohne Grenzpruefung.
            var source = used[i].Frame;

            bool placed = source is not null &&
                          (!layer.Place.IsNeutral ||
                           source.Width != width || source.Height != height);

            var placement = placed
                ? LayerPlacement.Prepare(layer.Place, source!.Width, source.Height, width, height)
                : default;

            plans[i] = new Plan(used[i].Frame, layer.Mode, Math.Clamp(layer.Opacity, 0f, 1f),
                                gain * layer.Tint.R, gain * layer.Tint.G, gain * layer.Tint.B, clipped,
                                layer.Mask, maskKind, maskFrame, maskLevels, maskIds,
                                layer.Content, grade, used[i].Kind, placed, placement,
                                maskFloor, maskSpan, layer.MatteFloor, layer.BlendInDisplay,
                                layer.Reveal, layer.Mask.PaintFor(number));
        }

        // Die Gitterpunkte einmal aufschreiben, statt sie je Bildpunkt auszurechnen.
        // Bei Schrittweite eins ist es die vollstaendige Liste; die letzte Spalte
        // liegt in beiden Faellen auf dem Rand, weil der grobe Durchgang der Anzeige
        // es genauso haelt - eine Abweichung um einen Bildpunkt waere ein Streifen
        // am rechten Rand, der nie mitgerechnet wird.
        int[] columns = Grid(width, step);
        int[] rows = Grid(height, step);

        Parallel.For(0, rows.Length, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        ry =>
        {
            int y = rows[ry];
            int start = y * width;

            // Der Stand vor jeder offenen Gruppe. Je Zeile einmal geholt, nicht je
            // Bildpunkt - verschachtelt wird selten und flach.
            Span<float> saved = stackalloc float[MaxDepth * 3];

            for (int cx = 0; cx < columns.Length; cx++)
            {
                int x = columns[cx];
                int i = start + x;
                int depth = 0;

                // Der Untergrund ist Schwarz und nicht die unterste Ebene: Damit
                // gilt fuer JEDE Ebene dieselbe Regel, auch fuer die unterste. Auf
                // Add ergibt das die Summe der Passe und damit wieder das Bild, das
                // gerendert wurde; auf Normal legt die unterste Ebene sich einfach
                // auf das Schwarz.
                float vr = 0f, vg = 0f, vb = 0f, va = 0f;

                // Die laufende Gruppe: eine Traegerebene und alles, was sich an sie
                // anschneidet. Sie wird erst als Ganzes auf das Ergebnis gemischt,
                // und zwar mit der Mischung des Traegers - genau so, wie eine
                // Schnittmaske in Photoshop wirkt.
                float gr = 0f, gg = 0f, gb = 0f;
                var groupMode = BlendMode.Normal;
                bool groupDisplay = false;
                float groupOpacity = 1f;
                bool open = false;

                for (int p = 0; p < plans.Length; p++)
                {
                    ref readonly var plan = ref plans[p];
                    var frame = plan.Frame;

                    // --- die Klammern einer Gruppe ---
                    if (plan.Kind != StepKind.Layer)
                    {
                        // Eine Schnittgruppe darf keine Gruppengrenze ueberschreiten.
                        if (open)
                        {
                            Blend(groupMode, groupDisplay, groupOpacity, vr, vg, vb, gr, gg, gb,
                                  out vr, out vg, out vb);
                            open = false;
                        }

                        if (plan.Kind == StepKind.Begin)
                        {
                            // Der Stand von jetzt wird gesichert, und die Gruppe
                            // rechnet darauf weiter. Sie sieht also, was unter ihr
                            // liegt - eine Gruppe aus Korrekturen faende sonst
                            // Schwarz vor.
                            if (depth < MaxDepth)
                            {
                                saved[depth * 3] = vr;
                                saved[depth * 3 + 1] = vg;
                                saved[depth * 3 + 2] = vb;
                                depth++;
                            }

                            continue;
                        }

                        if (depth == 0) continue;

                        depth--;

                        float br = saved[depth * 3];
                        float bg = saved[depth * 3 + 1];
                        float bb = saved[depth * 3 + 2];

                        // Das Ergebnis der Gruppe auf den gesicherten Stand - mit
                        // ihrer Mischung, Deckkraft und Maske. Ohne all das ist eine
                        // Gruppe damit genau so, als waere sie nicht da.
                        float groupFactor = plan.Mask == MaskKind.None
                            ? plan.Opacity
                            : plan.Opacity * Factor(in plan, x, y, width, height, i,
                                                    vr, vg, vb, br, bg, bb);

                        Blend(plan.Mode, plan.Display, groupFactor, br, bg, bb, vr, vg, vb,
                              out vr, out vg, out vb);

                        continue;
                    }

                    bool inGroup = plan.Clipped && open;

                    // Die offene Gruppe wird ZUERST eingerechnet, nicht erst nach
                    // dem Lesen dieser Ebene.
                    //
                    // Das war zuerst andersherum, und es kostete die halbe Wirkung:
                    // Eine Einstellungsebene und eine Helligkeitsmaske lesen beide,
                    // was unter ihnen liegt - und "darunter" war dann der Stand VOR
                    // der letzten Ebene. Eine Korrektur ueber zwei Passen rechnete
                    // mit dem ersten und uebersah den zweiten.
                    if (!inGroup && open)
                    {
                        Blend(groupMode, groupDisplay, groupOpacity, vr, vg, vb, gr, gg, gb,
                              out vr, out vg, out vb);
                        open = false;
                    }

                    float lr, lg, lb;
                    // Die Deckung, die die Ebene SELBST mitbringt - aus ihrem
                    // Alphakanal, aus der weichen Kante ihrer Flaeche oder aus
                    // beidem. Sie wirkt wie eine Maske und wird weiter unten zur
                    // oertlichen Deckkraft verrechnet.
                    float ownAlpha = 1f;
                    bool hasOwnAlpha = false;

                    if (plan.Content == LayerContent.Adjustment)
                    {
                        // Eine Einstellungsebene nimmt als Eingang das, worauf sie
                        // wirkt: angeschnitten die Gruppe, sonst das Ergebnis
                        // darunter. Damit heisst "Schnittmaske" hier genau dasselbe
                        // wie in Photoshop - die Korrektur gilt nur fuer die eine
                        // Ebene darunter.
                        lr = inGroup ? gr : vr;
                        lg = inGroup ? gg : vg;
                        lb = inGroup ? gb : vb;

                        plan.Grade.Apply(ref lr, ref lg, ref lb);

                        lr *= plan.ScaleR;
                        lg *= plan.ScaleG;
                        lb *= plan.ScaleB;
                    }
                    else if (plan.Placed)
                    {
                        // Ausserhalb ihrer Flaeche traegt die Ebene nichts bei - und
                        // zwar wirklich nichts, nicht Schwarz. Ein Wasserzeichen
                        // wuerde sonst das halbe Bild ausloeschen.
                        float covered = plan.Placement.Coverage(x, y, out float u, out float v);
                        if (covered <= 0f) continue;

                        plan.Placement.Sample(frame!, u, v, out lr, out lg, out lb, out ownAlpha);

                        lr *= plan.ScaleR;
                        lg *= plan.ScaleG;
                        lb *= plan.ScaleB;

                        // Die eigene Deckung der Ebene zaehlt mit, und die weiche
                        // Kante der Flaeche ebenso: Ein Logo mit durchsichtigem Rand
                        // bleibt durchsichtig, und eine gedrehte Kante bleibt glatt.
                        ownAlpha *= covered;
                        hasOwnAlpha = true;
                    }
                    else
                    {
                        lr = frame!.R[i] * plan.ScaleR;
                        lg = frame.G[i] * plan.ScaleG;
                        lb = frame.B[i] * plan.ScaleB;

                        // Eine Bildebene in Bildgroesse bringt ihre Deckung genauso
                        // mit wie eine platzierte - nur dass sie nicht abgetastet
                        // werden muss. Sie hier zu uebergehen war der Fehler, der
                        // freigestellte PNGs mit pixeligem Nebel fuellte: Unter der
                        // Deckung steht in den meisten Dateien, was zufaellig im
                        // Puffer stand, und ohne Deckung wird genau das gezeigt.
                        //
                        // Nicht bei einem Pass aus einer EXR. Dort heisst Deckung
                        // null "hier wurde nichts getroffen", und die Farbe daneben
                        // ist trotzdem echt - das Umgebungslicht steht dort. Einen
                        // solchen Pass mit seinem Alpha zu multiplizieren loeschte
                        // den Himmel.
                        //
                        // Bei allem anderen SCHON - und das schliesst das Grundbild
                        // ein. Das war der Fehler, der lange gesucht wurde: Die
                        // unterste Ebene im Atelier ist ein Pass, und eine PNG, die
                        // als Bild geoeffnet wird, ist damit auch einer. Ihre
                        // Freistellung galt dann nicht, und unter der Freistellung
                        // steht in den meisten Dateien, was zufaellig im Puffer
                        // stand. Dieselbe Datei war als EBENE still und als BILD ein
                        // Rauschteppich - gemessen 0,00 gegen 83,78 Stufen.
                        //
                        // Unterschieden wird nicht nach der Art der Ebene, sondern
                        // nach der Herkunft des Feldes: Was aus einer EXR kommt,
                        // traegt Licht und meint es so; was ueber Windows' Decoder
                        // kam, ist ein Bild und hat eine Maske.
                        bool matte = plan.Content == LayerContent.Image ||
                                     !frame.IsSceneReferred;

                        if (matte && frame.A is not null)
                        {
                            ownAlpha = frame.A[i];
                            hasOwnAlpha = true;
                        }
                    }

                    // Die Maske greift an genau einer Stelle an: Sie macht die
                    // Deckkraft oertlich. Damit gilt fuer jede Mischung und jede
                    // Schnittmaske dieselbe Regel, und es gibt keinen Fall, in dem
                    // eine Maske etwas anderes bedeutet als sonst.
                    float opacity = plan.Mask == MaskKind.None
                        ? plan.Opacity
                        : plan.Opacity * Factor(in plan, x, y, width, height, i,
                                                lr, lg, lb,
                                                inGroup ? gr : vr,
                                                inGroup ? gg : vg,
                                                inGroup ? gb : vb);

                    // Die eigene Deckung wirkt wie eine Maske: Sie macht die
                    // Deckkraft oertlich. Dieselbe Stelle, dieselbe Regel - und
                    // dieselbe Stelle, an der ein Schleier im Alphakanal gesaeubert
                    // wird, falls jemand das eingestellt hat.
                    if (hasOwnAlpha)
                    {
                        float matte = ImageLayer.CleanMatte(ownAlpha, plan.MatteFloor);

                        // Aufdecken kommt NACH dem Saeubern: Erst wird entschieden,
                        // was als durchsichtig gilt, dann wird es aufgezogen.
                        // Andersherum saeuberte man weg, was man gerade zeigen wollte.
                        opacity *= Math.Clamp(ImageLayer.Lift(matte, plan.Reveal), 0f, 1f);
                    }

                    if (inGroup)
                    {
                        Blend(plan.Mode, plan.Display, opacity, gr, gg, gb, lr, lg, lb,
                              out gr, out gg, out gb);
                    }
                    else
                    {
                        gr = lr;
                        gg = lg;
                        gb = lb;
                        groupMode = plan.Mode;
                        groupDisplay = plan.Display;

                        // Die Gruppe fuehrt die Deckkraft ihres Traegers mit, und
                        // damit auch dessen Maske: Erst wenn die Gruppe geschlossen
                        // wird, mischt sie sich auf das Ergebnis, und bis dahin muss
                        // der oertliche Wert erhalten bleiben.
                        groupOpacity = opacity;
                        open = true;
                    }

                    // Die Deckung ist keine Mischung, sondern eine Abdeckung: Wo
                    // irgendeine Ebene deckt, deckt das Ergebnis. Sie durch dieselbe
                    // Formel zu schicken wie die Farbe hiesse, Alpha auf Add zu
                    // summieren - drei Passe ergaeben Deckung 3.
                    // Eine Einstellungsebene deckt nichts ab - sie faerbt nur, was
                    // schon da ist. Ihr eine Deckung zuzurechnen hiesse, ein Bild
                    // undurchsichtig zu machen, das es nicht war.
                    if (frame is null) continue;

                    float la = (hasOwnAlpha ? 1f : frame.A is null ? 1f : frame.A[i]) * opacity;
                    if (la > va) va = la;
                }

                if (open)
                    Blend(groupMode, groupDisplay, groupOpacity, vr, vg, vb, gr, gg, gb,
                          out vr, out vg, out vb);

                r[i] = vr;
                g[i] = vg;
                b[i] = vb;
                a[i] = va;
            }
        });

        // Derselbe Frame, wenn seine Felder wiederverwendet wurden - sonst haette
        // der Aufrufer zwei Huellen um dieselben Daten und wuesste nicht, welche gilt.
        return reuse
            ? into!
            : new FloatFrame
            {
                Width = width,
                Height = height,
                R = r,
                G = g,
                B = b,
                A = a,
                Layer = used.Count == 1 ? used[0].Frame?.Layer : null,
                IsSceneReferred = sceneReferred,
            };
    }

    /// <summary>
    /// Die Stellen, an denen gerechnet wird. Bei Schrittweite eins alle.
    ///
    /// Die letzte liegt immer auf dem Rand - genau wie im groben Durchgang der
    /// Anzeige. Rechnete das Gitter hier bis ueber den Rand hinaus oder hoerte einen
    /// Schritt frueher auf, bliebe der letzte Streifen ungerechnet und zoege beim
    /// Reglerzug eine sichtbare Kante nach sich.
    /// </summary>
    private static int[] Grid(int size, int step)
    {
        if (step <= 1)
        {
            var all = new int[size];
            for (int i = 0; i < size; i++) all[i] = i;

            return all;
        }

        int count = (size + step - 1) / step + 1;
        var grid = new int[count];

        for (int i = 0; i < count; i++) grid[i] = Math.Min(i * step, size - 1);

        return grid;
    }

    /// <summary>
    /// Wie tief Gruppen ineinander stehen duerfen.
    ///
    /// Acht ist keine gegriffene Zahl, sondern die Grenze, ab der der gesicherte
    /// Stand je Bildpunkt teurer wuerde als die Gruppen wert sind. Wer tiefer
    /// schachtelt, hat ein anderes Problem als diese Grenze.
    /// </summary>
    private const int MaxDepth = 8;

    /// <summary>Ob ein Schritt eine Ebene ist oder die Klammer einer Gruppe.</summary>
    private enum StepKind
    {
        Layer,
        Begin,
        End,
    }

    /// <summary>
    /// Macht aus dem Baum eine flache Folge mit Klammern.
    ///
    /// Flach und nicht rekursiv, weil die innere Schleife je Bildpunkt laeuft: Eine
    /// Rekursion dort waere bei 4K fuenfundzwanzig Millionen Aufrufe tief. Die
    /// Schachtelung steckt stattdessen in zwei Marken und einer kleinen Halde.
    /// </summary>
    private static void Collect(IEnumerable<ImageLayer> layers,
                                IReadOnlyDictionary<string, FloatFrame> sources,
                                List<(ImageLayer Layer, FloatFrame? Frame, StepKind Kind)> into,
                                ref int width, ref int height, int depth)
    {
        foreach (var layer in layers)
        {
            if (!layer.Visible || layer.Opacity <= 0.0005f) continue;

            // Was obenauf liegt, gehoert nicht in den Stapel: Es wird erst nach der
            // Bildwerdung aufgetragen, siehe Overlays.
            if (layer.OnTop && layer.Content == LayerContent.Image) continue;

            if (layer.Content == LayerContent.Group)
            {
                // Zu tief geschachtelt: die Gruppe faellt weg, ihre Kinder bleiben.
                // Sie stillschweigend mitsamt Inhalt zu verschlucken waere die
                // schlechtere Antwort - man saehe ein Bild, in dem etwas fehlt.
                if (depth >= MaxDepth)
                {
                    Collect(layer.Children, sources, into, ref width, ref height, depth);
                    continue;
                }

                into.Add((layer, null, StepKind.Begin));
                Collect(layer.Children, sources, into, ref width, ref height, depth + 1);
                into.Add((layer, null, StepKind.End));

                continue;
            }

            // Eine Einstellungsebene bringt kein Bild mit - sie rechnet mit dem, was
            // schon da ist. Sie gibt deshalb auch keine Groesse vor; die kommt von
            // den Passen darunter.
            if (layer.Content == LayerContent.Adjustment)
            {
                into.Add((layer, null, StepKind.Layer));
                continue;
            }

            if (!sources.TryGetValue(layer.Source, out var frame)) continue;

            // Die erste brauchbare Ebene gibt die Groesse vor.
            //
            // Frueher fiel alles Abweichende heraus - Skalieren war eine andere
            // Aufgabe als Mischen. Jetzt gibt es die Platzierung, und damit ist eine
            // Ebene anderer Groesse kein Sonderfall mehr, sondern ein Logo: Sie wird
            // mittig eingepasst, und Massstab und Versatz rechnen von dort weiter.
            //
            // Die GROESSE gibt sie trotzdem nicht vor. Sonst bestimmte ein
            // Wasserzeichen von 200 Punkten die Leinwand, wenn es zufaellig zuunterst
            // liegt.
            if (width == 0 && layer.Content == LayerContent.Pass)
            {
                width = frame.Width;
                height = frame.Height;
            }

            into.Add((layer, frame, StepKind.Layer));
        }
    }

    // Rec.-709-Luminanz, dieselben Gewichte wie im uebrigen Bildweg.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>
    /// Der Maskenwert eines Bildpunkts, zwischen 0 und 1.
    /// </summary>
    /// <param name="lr">
    /// Die Ebene selbst, bereits mit Belichtung und Farbe - bei einer
    /// Einstellungsebene also das korrigierte Ergebnis. Eine Helligkeitsmaske auf ihr
    /// fragt damit "wo ist es NACH der Korrektur hell", und das ist die Frage, die
    /// man beim Hinsehen stellt.
    /// </param>
    /// <param name="ur">Was an dieser Stelle schon darunter liegt.</param>
    /// <summary>
    /// Mischt - in linearem Licht oder im Anzeigeraum, je nachdem, was die Ebene sagt.
    ///
    /// An einer Stelle, weil der Composer an vier Stellen mischt: je Ebene, beim
    /// Schliessen einer Gruppe, beim Auftragen einer Gruppe und am Ende. Vier
    /// Verzweigungen waeren vier Gelegenheiten, eine davon zu vergessen.
    /// </summary>
    private static void Blend(BlendMode mode, bool display, float opacity,
                              float ur, float ug, float ub,
                              float or_, float og, float ob,
                              out float r, out float g, out float b)
    {
        if (display)
            Blending.MixDisplay(mode, opacity, ur, ug, ub, or_, og, ob, out r, out g, out b);
        else
            Blending.Mix(mode, opacity, ur, ug, ub, or_, og, ob, out r, out g, out b);
    }

    private static float Factor(in Plan plan, int x, int y, int width, int height, int i,
                                float lr, float lg, float lb,
                                float ur, float ug, float ub)
    {
        float value;

        switch (plan.Mask)
        {
            case MaskKind.Luminance:
                value = Masking.Perceptual(LumaR * lr + LumaG * lg + LumaB * lb);
                break;

            case MaskKind.Underlying:
                value = Masking.Perceptual(LumaR * ur + LumaG * ug + LumaB * ub);
                break;

            case MaskKind.Pass:
                // Ein anderer Weg als bei der Helligkeit, und das mit Absicht.
                //
                // Nebel, Verschattung und Indexmasken sind bereits Masken: Ihr Wert
                // IST der Anteil. Er wird durchgereicht und bekommt nur einen
                // Schwarz- und einen Weisspunkt, wie jede Maske, die man anzieht.
                // Durch das Bereichsfenster der Helligkeitsmaske geschickt taete er
                // in Grundstellung nichts - jeder Wert zwischen 0 und 1 liegt im
                // Fenster 0 bis 1.
                //
                // Ueber die Luminanz und nicht ueber Rot allein: Bei einem
                // Graustufenpass sind beide identisch, bei einem farbigen waere Rot
                // eine willkuerliche Wahl.
                var m = plan.MaskFrame!;
                float raw = LumaR * m.R[i] + LumaG * m.G[i] + LumaB * m.B[i];

                // Auf die eigene Spanne bezogen, wenn der Pass keine ist. Bei einem
                // Nebelpass ist der Boden null und die Spanne eins - dann steht hier
                // derselbe Wert wie vorher.
                raw = (raw - plan.MaskFloor) / plan.MaskSpan;

                return Fit(Masking.Levels(raw, plan.MaskLow, plan.MaskHigh), plan.MaskInvert);

            case MaskKind.Cryptomatte:
                // Dieselbe Behandlung wie beim Pass: Die Deckung IST der Anteil, und
                // Schwarz- und Weisspunkt ziehen ihn an - damit laesst sich eine
                // weiche Kante wegnehmen oder stehenlassen.
                float coverage = Masking.Coverage(plan.MaskLevels!, plan.MaskIds!, i);

                return Fit(Masking.Levels(coverage, plan.MaskLow, plan.MaskHigh), plan.MaskInvert);

            case MaskKind.Painted:
                // Der Anstrich IST der Anteil - wie beim Pass und bei der Kryptomatte.
                // Schwarz- und Weisspunkt ziehen ihn an, damit sich eine weiche
                // Pinselkante nachtraeglich haerten oder weiter aufweichen laesst.
                if (plan.Paint is not { } paint) return plan.MaskInvert ? 1f : 0f;

                return Fit(Masking.Levels(paint.At(x, y), plan.MaskLow, plan.MaskHigh),
                           plan.MaskInvert);

            case MaskKind.Colour:
            {
                // Gelesen wird der UNTERGRUND, nicht die Ebene selbst - und das ist
                // der Unterschied zur Helligkeitsmaske.
                //
                // Zwei Gruende. Eine weisse Flaeche hat keinen Farbton; eine
                // Farbbereichsmaske auf ihr waere immer leer, und niemand kaeme
                // darauf, warum. Und eine Einstellungsebene, die den Farbton
                // verschiebt, jagte ihrem eigenen Ergebnis hinterher: Die Maske
                // waehlt Blau, die Korrektur macht daraus Gruen, die Maske waehlt es
                // nicht mehr. "Wo das Bild blau IST" ist die Frage, die man stellt.
                //
                // Der Farbton des Punktes auf dem Farbkreis - und wie weit er vom
                // gesuchten entfernt liegt. Die Entfernung geht ueber den KUERZEREN
                // Weg: Rot liegt bei 0 und bei 360, und ein Bereich um Rot, der bei
                // 350 aufhoert, waere keiner.
                float high = MathF.Max(ur, MathF.Max(ug, ub));
                float low = MathF.Min(ur, MathF.Min(ug, ub));
                float chroma = high - low;

                // Grau hat keinen Farbton, den man treffen koennte. Nicht "weit weg",
                // sondern "gar nicht dabei" - sonst faenge jede Maske das halbe Bild.
                if (chroma <= 1e-6f || high <= 1e-6f) return Fit(0f, plan.MaskInvert);

                float hue = high == ur
                    ? 60f * (((ug - ub) / chroma) % 6f)
                    : high == ug
                        ? 60f * ((ub - ur) / chroma + 2f)
                        : 60f * ((ur - ug) / chroma + 4f);

                if (hue < 0f) hue += 360f;

                float away = MathF.Abs(hue - plan.MaskHue);
                if (away > 180f) away = 360f - away;

                // Weich ueber die Kante hinaus. Der Weichzeichner ist derselbe Regler
                // wie bei der Helligkeitsmaske und zaehlt hier in halben Kreisen.
                float edge = plan.MaskSoftness * 180f;

                float inside = edge <= 1e-4f
                    ? away <= plan.MaskSpread ? 1f : 0f
                    : 1f - Math.Clamp((away - plan.MaskSpread) / edge, 0f, 1f);

                // Je blasser die Farbe, desto weniger gehoert sie dazu. Ein fast
                // graues Blau IST kaum blau - und wer es doch will, zieht den
                // Weisspunkt herunter.
                float pure = Math.Clamp(chroma / high, 0f, 1f);

                return Fit(Masking.Levels(inside * pure, plan.MaskLow, plan.MaskHigh),
                           plan.MaskInvert);
            }

            case MaskKind.Gradient:
                return Fit(Masking.Gradient(x, y, width, height,
                                            plan.GradientCos, plan.GradientSin,
                                            plan.GradientFrom, plan.GradientTo), plan.MaskInvert);

            default:
                return 1f;
        }

        return Fit(Masking.Band(value, plan.MaskLow, plan.MaskHigh, plan.MaskSoftness),
                   plan.MaskInvert);
    }

    private static float Fit(float factor, bool invert) => invert ? 1f - factor : factor;

    /// <summary>Was je Ebene einmal feststeht.</summary>
    private readonly struct Plan
    {
        public Plan(FloatFrame? frame, BlendMode mode, float opacity,
                    float sr, float sg, float sb, bool clipped,
                    LayerMask mask, MaskKind kind, FloatFrame? maskFrame,
                    FloatFrame[]? maskLevels, float[]? maskIds,
                    LayerContent content, LayerGrade grade, StepKind step,
                    bool placed, LayerPlacement placement,
                    float maskFloor, float maskSpan, float matteFloor, bool display,
                    float reveal, PaintedMask? paint)
        {
            Paint = paint;
            Display = display;
            Reveal = reveal;
            MatteFloor = matteFloor;
            MaskFloor = maskFloor;
            MaskSpan = maskSpan;
            Content = content;
            Grade = grade;
            Kind = step;
            Placed = placed;
            Placement = placement;
            Frame = frame;
            Mode = mode;
            Opacity = opacity;
            ScaleR = sr;
            ScaleG = sg;
            ScaleB = sb;
            Clipped = clipped;

            Mask = kind;
            MaskFrame = maskFrame;
            MaskLevels = maskLevels;
            MaskIds = maskIds;
            MaskInvert = mask.Invert;
            MaskHue = mask.Hue;
            MaskSpread = mask.Spread;
            MaskLow = mask.Low;
            MaskHigh = mask.High;
            MaskSoftness = mask.Softness;

            // Winkel und Breite einmal je Bild in das umrechnen, was die innere
            // Schleife braucht - bei 4K waeren es sonst 25 Millionen Sinusse.
            float radians = mask.Angle * MathF.PI / 180f;
            GradientCos = MathF.Cos(radians);
            GradientSin = MathF.Sin(radians);

            float half = MathF.Max(0f, mask.Width) / 2f;
            GradientFrom = mask.Centre - half;
            GradientTo = mask.Centre + half;
        }

        public readonly FloatFrame? Frame;
        public readonly StepKind Kind;
        public readonly bool Placed;
        public readonly LayerPlacement Placement;
        public readonly LayerContent Content;
        public readonly LayerGrade Grade;
        public readonly BlendMode Mode;
        public readonly bool Clipped;
        public readonly float Opacity, ScaleR, ScaleG, ScaleB;

        public readonly MaskKind Mask;
        public readonly FloatFrame? MaskFrame;
        public readonly FloatFrame[]? MaskLevels;
        public readonly float[]? MaskIds;
        public readonly bool MaskInvert;

        /// <summary>Der Anstrich, der fuer dieses Bild gilt - siehe LayerMask.</summary>
        public readonly PaintedMask? Paint;
        public readonly float MaskLow, MaskHigh, MaskSoftness;

        /// <summary>Der gesuchte Farbton und seine Weite - nur fuer die Farbbereichsmaske.</summary>
        public readonly float MaskHue, MaskSpread;
        public readonly float MaskFloor, MaskSpan;
        public readonly float MatteFloor;

        /// <summary>Ob diese Ebene im Anzeigeraum mischt - siehe ImageLayer.</summary>
        public readonly bool Display;

        /// <summary>Wieviel von dem gezeigt wird, was unter der Deckung steht.</summary>
        public readonly float Reveal;
        public readonly float GradientCos, GradientSin, GradientFrom, GradientTo;
    }
}
