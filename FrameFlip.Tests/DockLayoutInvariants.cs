using System.Text.Json;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Anordnung der angedockten Felder - ohne Fenster geprueft.
///
/// Andocken ist die Art Funktion, deren Fehler nicht beim Ziehen auffallen, sondern
/// drei Sitzungen spaeter: ein Feld, das zweimal vorkommt, eines, das verschwunden
/// ist, eine leere Gruppe, die als Loch stehen bleibt. Die Zusage, die alles traegt:
/// JEDES bekannte Feld steht GENAU EINMAL irgendwo.
/// </summary>
public static class DockLayoutInvariants
{
    private static readonly string[] Known = { "histogram", "colour", "layers" };

    public static void Run()
    {
        TheDefaultIsWhole();
        MovesKeepEveryPanelOnce();
        EmptyGroupsVanish();
        ABrokenFileIsRepaired();
        TheLayoutSurvivesSaving();
        ASecondClickFolds();
    }

    /// <summary>Jedes bekannte Feld genau einmal, keine leere Gruppe, jede Gruppe mit einem Feld vorn.</summary>
    private static bool Whole(DockLayout layout, out string why)
    {
        var all = layout.Zones().SelectMany(z => z.Groups).SelectMany(g => g.Panels).ToList();

        foreach (string panel in Known)
        {
            int times = all.Count(p => p == panel);

            if (times != 1)
            {
                why = $"{panel} steht {times} Mal da";
                return false;
            }
        }

        if (all.Count != Known.Length)
        {
            why = $"{all.Count} Felder statt {Known.Length}";
            return false;
        }

        foreach (var (zone, groups) in layout.Zones())
        {
            foreach (var group in groups)
            {
                if (group.Panels.Count == 0)
                {
                    why = $"leere Gruppe in {zone}";
                    return false;
                }

                if (group.Active is null || !group.Panels.Contains(group.Active))
                {
                    why = $"eine Gruppe in {zone} hat kein Feld vorn";
                    return false;
                }
            }
        }

        why = "";
        return true;
    }

    private static void TheDefaultIsWhole()
    {
        Check.Group("Andocken: die Grundanordnung");

        var layout = DockLayout.Default();

        Check.That(Whole(layout, out string why), "jedes Feld genau einmal", why);

        Check.That(layout.Find("histogram") is { Zone: DockZone.Right, Group: 0 },
                   "die Verteilung steht rechts oben");

        Check.That(layout.Find("colour") is { Zone: DockZone.Right, Group: 1 } &&
                   layout.Find("layers") is { Zone: DockZone.Right, Group: 1 },
                   "Farbe und Ebenen teilen sich darunter eine Gruppe als Reiter");

        Check.That(layout.Right[1].Active == "colour", "und vorn liegt die Farbe");
    }

    /// <summary>
    /// Jeder Zug, den die Maus machen kann, laesst jedes Feld genau einmal stehen.
    ///
    /// Nicht ein paar ausgesuchte Faelle, sondern alle: jedes Feld in jede Zone, als
    /// Reiter in jede vorhandene Gruppe und als neue Gruppe an jede Stelle - und das
    /// aus vielen verschiedenen Ausgangslagen, die durch die Zuege selbst entstehen.
    /// </summary>
    private static void MovesKeepEveryPanelOnce()
    {
        Check.Group("Andocken: kein Zug verliert oder verdoppelt ein Feld");

        var layout = DockLayout.Default();
        var random = new Random(3);

        int moves = 0, broken = 0, hidden = 0;
        string first = "", firstHidden = "";

        for (int round = 0; round < 400; round++)
        {
            string panel = Known[random.Next(Known.Length)];
            var zone = (DockZone)random.Next(3);
            int groups = layout.Zone(zone).Count;
            int group = random.Next(groups + 2) - 1;   // auch daneben: -1 und hinter dem Ende
            bool asTab = random.Next(2) == 0;

            // Zwischendurch Klicks auf Reiter - eingeklappte Gruppen sind die
            // Ausgangslage, aus der ein Zug am ehesten etwas verschluckt.
            if (random.Next(3) == 0) layout.Toggle(Known[random.Next(Known.Length)]);

            layout.Move(panel, zone, group, asTab);
            layout.Normalise(Known);
            moves++;

            if (!Whole(layout, out string why))
            {
                broken++;
                if (first.Length == 0) first = $"Zug {moves}: {panel} nach {zone}/{group}/{asTab} - {why}";
            }

            // Wer ein Feld gerade gezogen hat, will es sehen: vorn, und offen.
            if (layout.Find(panel) is { } at &&
                layout.Zone(at.Zone)[at.Group] is var landed &&
                (landed.Collapsed || landed.Active != panel))
            {
                hidden++;
                if (firstHidden.Length == 0) firstHidden = $"Zug {moves}: {panel} nach {zone}/{group}/{asTab}";
            }
        }

        Check.That(broken == 0, $"in {moves} zufaelligen Zuegen bleibt jedes Feld genau einmal", first);
        Check.That(hidden == 0, "und ein gezogenes Feld liegt danach vorn und offen", firstHidden);

        // Und die Zuege tun, was sie sagen.
        var fresh = DockLayout.Default();

        fresh.Move("layers", DockZone.Left, 0, asTab: false);
        fresh.Normalise(Known);

        Check.That(fresh.Find("layers") is { Zone: DockZone.Left },
                   "die Ebenen lassen sich nach links ziehen");
        Check.That(fresh.Right[1].Panels.SequenceEqual(new[] { "colour" }),
                   "und fehlen danach in ihrer alten Gruppe");

        fresh.Move("histogram", DockZone.Left, 0, asTab: true);
        fresh.Normalise(Known);

        Check.That(fresh.Left.Count == 1 && fresh.Left[0].Panels.SequenceEqual(new[] { "layers", "histogram" }),
                   "als Reiter landet ein Feld in der Gruppe, auf die es gezogen wurde",
                   string.Join(", ", fresh.Left.SelectMany(g => g.Panels)));

        Check.That(fresh.Left[0].Active == "histogram", "und liegt dort vorn - man will es sehen");

        fresh.Move("colour", DockZone.Bottom, 0, asTab: false);
        fresh.Normalise(Known);

        Check.That(fresh.Find("colour") is { Zone: DockZone.Bottom } && fresh.Right.Count == 0,
                   "auch unter das Bild - und die rechte Zone ist dann leer");
    }

    /// <summary>
    /// Eine Gruppe, aus der das letzte Feld gezogen wird, verschwindet - und die
    /// Stelle, an der das Feld landen soll, verrutscht dabei nicht.
    /// </summary>
    private static void EmptyGroupsVanish()
    {
        Check.Group("Andocken: leere Gruppen verschwinden, ohne dass etwas verrutscht");

        var layout = DockLayout.Default();

        // Die Verteilung (Gruppe 0) ans Ende derselben Zone - "hinter Gruppe 1". Ihre
        // alte Gruppe wird dabei leer und faellt weg, und damit rueckt alles dahinter
        // um eins auf. Ohne Ausgleich landete sie wieder vorn.
        layout.Move("histogram", DockZone.Right, 2, asTab: false);
        layout.Normalise(Known);

        Check.That(layout.Right.Count == 2, "es bleiben zwei Gruppen", $"{layout.Right.Count}");
        Check.That(layout.Right[1].Panels.SequenceEqual(new[] { "histogram" }),
                   "und die Verteilung steht jetzt unten, wie gewollt",
                   string.Join(" | ", layout.Right.Select(g => string.Join(",", g.Panels))));

        // Als Reiter in die eigene Gruppe: nichts veraendert sich.
        var before = JsonSerializer.Serialize(layout);

        layout.Move("colour", DockZone.Right, 0, asTab: true);

        Check.That(JsonSerializer.Serialize(layout) == before,
                   "in die eigene Gruppe gezogen aendert sich nichts");
    }

    /// <summary>
    /// Eine Einstellungsdatei, die nicht zur Fassung passt, wird repariert statt
    /// geglaubt - Felder doppelt, unbekannt, fehlend, leere Gruppen, unsinnige
    /// Groessen.
    /// </summary>
    private static void ABrokenFileIsRepaired()
    {
        Check.Group("Andocken: eine kaputte Anordnung wird repariert");

        var broken = new DockLayout
        {
            Left = { new DockGroup { Panels = { "colour", "colour", "gibtsnicht" }, Active = "weg" } },
            Right = { new DockGroup(), new DockGroup { Panels = { "histogram" }, Weight = -3 } },
            RightWidth = 0,
            BottomHeight = double.NaN,
        };

        broken.Normalise(Known);

        Check.That(Whole(broken, out string why), "danach steht jedes Feld genau einmal", why);
        Check.That(broken.Find("layers") is not null, "das fehlende kommt dazu");
        Check.That(broken.Right.All(g => g.Weight > 0), "unsinnige Gewichte werden eins");
        Check.That(broken.RightWidth > 120 && broken.BottomHeight > 120,
                   "und unsinnige Groessen bekommen ihren Grundwert");
    }

    /// <summary>
    /// Ein Klick holt ein Feld, ein zweiter nimmt es weg - derselbe Griff in beide
    /// Richtungen. Und was eingeklappt war, bleibt es auch nach dem Speichern.
    /// </summary>
    private static void ASecondClickFolds()
    {
        Check.Group("Andocken: ein zweiter Klick klappt ein");

        var layout = DockLayout.Default();

        layout.Toggle("colour");

        Check.That(layout.Right[1] is { Collapsed: true, Active: "colour" },
                   "ein Klick auf den vorderen Reiter klappt seine Gruppe ein");
        Check.That(!layout.IsFolded(DockZone.Right), "die Zone bleibt offen, solange oben noch etwas offen ist");

        layout.Toggle("layers");

        Check.That(layout.Right[1] is { Collapsed: false, Active: "layers" },
                   "ein Klick auf einen anderen Reiter der eingeklappten Gruppe holt ihn und klappt auf");

        layout.Toggle("layers");
        layout.Toggle("histogram");

        Check.That(layout.IsFolded(DockZone.Right), "ist alles eingeklappt, ist die Zone nur noch ein Streifen");
        Check.That(!layout.IsFolded(DockZone.Left), "eine leere Zone ist nicht eingeklappt, sondern gar nicht da");

        var read = JsonSerializer.Deserialize<DockLayout>(JsonSerializer.Serialize(layout))!;
        read.Normalise(Known);

        Check.That(read.IsFolded(DockZone.Right) && read.Right[1].Active == "layers",
                   "eingeklappt ueberlebt das Speichern - samt dem, was vorn lag");
        Check.That(layout.Clone().IsFolded(DockZone.Right), "und die Kopie fuer die Einstellungen");

        // In eine eingeklappte Gruppe gezogen: Sie geht auf, und das Feld liegt vorn.
        layout.Move("histogram", DockZone.Right, 1, asTab: true);
        layout.Normalise(Known);

        Check.That(layout.Right.Count == 1 && layout.Right[0] is { Collapsed: false, Active: "histogram" },
                   "wer in eine eingeklappte Gruppe zieht, klappt sie auf",
                   string.Join(" | ", layout.Right.Select(g => $"{string.Join(",", g.Panels)}{(g.Collapsed ? " (zu)" : "")}")));
    }

    private static void TheLayoutSurvivesSaving()
    {
        Check.Group("Andocken: die Anordnung ueberlebt das Speichern");

        var layout = DockLayout.Default();

        layout.Move("layers", DockZone.Left, 0, asTab: false);
        layout.Move("histogram", DockZone.Bottom, 0, asTab: false);
        layout.LeftWidth = 280;
        layout.BottomHeight = 190;
        layout.Normalise(Known);

        var read = JsonSerializer.Deserialize<DockLayout>(JsonSerializer.Serialize(layout))!;

        read.Normalise(Known);

        Check.That(read.Find("layers") is { Zone: DockZone.Left } &&
                   read.Find("histogram") is { Zone: DockZone.Bottom } &&
                   read.Find("colour") is { Zone: DockZone.Right },
                   "jedes Feld steht wieder, wo es stand");

        Check.Near(read.LeftWidth, 280, 1e-9, "die Breite links");
        Check.Near(read.BottomHeight, 190, 1e-9, "und die Hoehe unten");

        var copy = layout.Clone();

        copy.Move("colour", DockZone.Left, 0, asTab: true);

        Check.That(layout.Find("colour") is { Zone: DockZone.Right },
                   "eine Kopie bewegt sich nicht mit");
    }
}
