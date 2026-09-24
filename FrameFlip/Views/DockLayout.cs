namespace FrameFlip.Views;

/// <summary>Wo ein Feld angedockt sein kann.</summary>
public enum DockZone
{
    Left,
    Right,
    Bottom,
}

/// <summary>
/// Eine Gruppe in einer Zone: ein oder mehrere Felder als Reiter, von denen eines vorn
/// liegt.
/// </summary>
public sealed class DockGroup
{
    /// <summary>Die Felder der Gruppe, in der Reihenfolge ihrer Reiter.</summary>
    public List<string> Panels { get; set; } = new();

    /// <summary>Welches Feld vorn liegt.</summary>
    public string? Active { get; set; }

    /// <summary>
    /// Der Anteil dieser Gruppe an ihrer Zone - ein Gewicht, keine Punktzahl. Zwei
    /// Gruppen mit 1 und 3 teilen sich die Zone im Verhaeltnis eins zu drei, gleich
    /// wie hoch das Fenster gerade ist.
    /// </summary>
    public double Weight { get; set; } = 1;

    /// <summary>
    /// Ob die Gruppe eingeklappt ist: Nur ihre Reiterleiste steht noch da, und ihr
    /// Platz gehoert den anderen. Stehen alle Gruppen einer Seitenzone so, schrumpft
    /// die Zone auf einen schmalen Streifen mit senkrechten Reitern.
    /// </summary>
    public bool Collapsed { get; set; }
}

/// <summary>
/// Die Anordnung der Felder im Atelier: welche Zone welche Gruppen fuehrt, und wie
/// breit oder hoch die Zonen sind.
///
/// Ein reines Modell ohne Oberflaeche, und das mit Absicht. Andocken ist die Art
/// Funktion, deren Fehler nicht beim Ziehen auffallen, sondern drei Sitzungen spaeter:
/// ein Feld, das zweimal vorkommt, eines, das verschwunden ist, eine leere Gruppe, die
/// als Loch stehen bleibt. Alles das laesst sich hier pruefen, ohne ein Fenster
/// aufzumachen - und die Oberflaeche zeichnet nur noch, was hier steht.
///
/// Die eine Zusage, die alles andere traegt: JEDES bekannte Feld steht GENAU EINMAL
/// irgendwo. <see cref="Normalise"/> stellt das her, nach jedem Zug und nach jedem
/// Laden - auch aus einer Einstellungsdatei, die von Hand oder von einer anderen
/// Fassung geschrieben wurde.
/// </summary>
public sealed class DockLayout
{
    public List<DockGroup> Left { get; set; } = new();
    public List<DockGroup> Right { get; set; } = new();
    public List<DockGroup> Bottom { get; set; } = new();

    /// <summary>Breite der linken Zone in Punkten.</summary>
    public double LeftWidth { get; set; } = 300;

    /// <summary>Breite der rechten Zone in Punkten.</summary>
    public double RightWidth { get; set; } = 320;

    /// <summary>Hoehe der unteren Zone in Punkten.</summary>
    public double BottomHeight { get; set; } = 240;

    /// <summary>
    /// Die Anordnung, mit der man anfaengt: rechts oben die Verteilung, darunter Farbe
    /// und Ebenen als Reiter.
    ///
    /// Die Verteilung oben, wie in jedem Entwicklungsprogramm - man sieht beim
    /// Ziehen eines Reglers hin, was mit den Lichtern passiert. Und als eigene Gruppe,
    /// damit sie stehen bleibt, waehrend man zwischen Farbe und Ebenen wechselt.
    /// </summary>
    public static DockLayout Default() => new()
    {
        Right =
        {
            new DockGroup { Panels = { "histogram" }, Active = "histogram", Weight = 1 },
            new DockGroup { Panels = { "colour", "layers" }, Active = "colour", Weight = 4 },
        },
    };

    /// <summary>Die Gruppen einer Zone.</summary>
    public List<DockGroup> Zone(DockZone zone) => zone switch
    {
        DockZone.Left => Left,
        DockZone.Bottom => Bottom,
        _ => Right,
    };

    /// <summary>Alle Zonen mit ihren Gruppen.</summary>
    public IEnumerable<(DockZone Zone, List<DockGroup> Groups)> Zones()
    {
        yield return (DockZone.Left, Left);
        yield return (DockZone.Right, Right);
        yield return (DockZone.Bottom, Bottom);
    }

    /// <summary>
    /// Ein Klick auf einen Reiter. Liegt das Feld schon vorn und offen, klappt die
    /// Gruppe ein; sonst kommt es nach vorn, und die Gruppe geht auf.
    ///
    /// Derselbe Griff holt ein Feld und nimmt es wieder weg - wer ein Feld nicht
    /// braucht, soll nicht erst ein Menue suchen muessen.
    /// </summary>
    public void Toggle(string panel)
    {
        if (Find(panel) is not { } at) return;

        var group = Zone(at.Zone)[at.Group];

        if (group.Active == panel && !group.Collapsed)
        {
            group.Collapsed = true;
            return;
        }

        group.Active = panel;
        group.Collapsed = false;
    }

    /// <summary>Ob in einer Zone alles eingeklappt ist - dann ist sie nur noch ein Streifen.</summary>
    public bool IsFolded(DockZone zone) => Zone(zone) is { Count: > 0 } groups && groups.All(g => g.Collapsed);

    /// <summary>Wo ein Feld steht - oder null, wenn nirgends.</summary>
    public (DockZone Zone, int Group)? Find(string panel)
    {
        foreach (var (zone, groups) in Zones())
            for (int i = 0; i < groups.Count; i++)
                if (groups[i].Panels.Contains(panel)) return (zone, i);

        return null;
    }

    /// <summary>
    /// Verschiebt ein Feld.
    ///
    /// <paramref name="asTab"/> legt es als Reiter in die Gruppe <paramref
    /// name="group"/> der Zone. Ohne legt es eine NEUE Gruppe an dieser Stelle an -
    /// neben oder unter den anderen. Das Feld liegt danach vorn, denn wer es gerade
    /// hingezogen hat, will es sehen.
    ///
    /// Gezaehlt wird die Stelle in der Zone VOR dem Zug. Verlaesst das Feld dabei
    /// eine Gruppe, die dadurch leer wird und in derselben Zone davor lag, rueckt
    /// alles dahinter auf - das wird hier ausgeglichen, statt es dem Aufrufer zu
    /// ueberlassen, der von der leeren Gruppe nichts wissen kann.
    /// </summary>
    public void Move(string panel, DockZone zone, int group, bool asTab)
    {
        var targets = Zone(zone);
        DockGroup? into = asTab && group >= 0 && group < targets.Count ? targets[group] : null;

        // Als Reiter in die eigene Gruppe: nichts zu tun, ausser es nach vorn zu holen.
        // Wer ein Feld gerade gezogen hat, will es sehen - auch aus einer eingeklappten
        // Gruppe heraus.
        if (into is not null && into.Panels.Contains(panel))
        {
            into.Active = panel;
            into.Collapsed = false;
            return;
        }

        int insertAt = Math.Clamp(group, 0, targets.Count);

        // Herausnehmen.
        foreach (var (_, groups) in Zones())
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (!groups[i].Panels.Remove(panel)) continue;

                if (groups[i].Active == panel) groups[i].Active = groups[i].Panels.FirstOrDefault();

                if (groups[i].Panels.Count == 0)
                {
                    groups.RemoveAt(i);

                    if (ReferenceEquals(groups, targets) && i < insertAt) insertAt--;
                }

                break;
            }
        }

        if (into is not null && targets.Contains(into))
        {
            into.Panels.Add(panel);
            into.Active = panel;
            into.Collapsed = false;
        }
        else
        {
            targets.Insert(Math.Clamp(insertAt, 0, targets.Count),
                           new DockGroup { Panels = { panel }, Active = panel, Weight = 1 });
        }
    }

    /// <summary>
    /// Stellt die Zusage her: jedes bekannte Feld genau einmal, keine leeren Gruppen,
    /// in jeder Gruppe liegt ein Feld vorn, das auch zu ihr gehoert.
    ///
    /// Unbekannte Felder fallen weg - eine aeltere oder neuere Fassung kann Felder
    /// kennen, die es hier nicht gibt. Fehlende kommen dorthin, wo sie in der
    /// Grundanordnung stehen; gibt es die Gruppe dort nicht mehr, in eine neue rechts.
    /// </summary>
    public void Normalise(IReadOnlyCollection<string> known)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, groups) in Zones())
        {
            foreach (var group in groups)
            {
                group.Panels = group.Panels
                    .Where(p => known.Contains(p) && seen.Add(p))
                    .ToList();

                if (group.Active is null || !group.Panels.Contains(group.Active))
                    group.Active = group.Panels.FirstOrDefault();

                if (!(group.Weight > 0) || double.IsInfinity(group.Weight)) group.Weight = 1;
            }

            groups.RemoveAll(g => g.Panels.Count == 0);
        }

        foreach (string missing in known.Where(p => !seen.Contains(p)))
        {
            var home = Default().Find(missing);

            if (home is { } spot && spot.Zone == DockZone.Right)
            {
                // Zu einem Feld der Grundanordnung, das mit ihm in derselben Gruppe
                // stand - wenn es noch zusammen irgendwo liegt.
                var partner = Default().Right[spot.Group].Panels
                                       .FirstOrDefault(p => p != missing && seen.Contains(p));

                if (partner is not null && Find(partner) is { } at)
                {
                    Zone(at.Zone)[at.Group].Panels.Add(missing);
                    seen.Add(missing);
                    continue;
                }
            }

            Right.Add(new DockGroup { Panels = { missing }, Active = missing, Weight = 1 });
            seen.Add(missing);
        }

        LeftWidth = Clean(LeftWidth, 300);
        RightWidth = Clean(RightWidth, 320);
        BottomHeight = Clean(BottomHeight, 240);

        static double Clean(double value, double fallback)
            => value is > 120 and < 3000 ? value : fallback;
    }

    /// <summary>Eine eigene Kopie - fuer die Einstellungen, die sie festhalten.</summary>
    public DockLayout Clone() => new()
    {
        Left = Left.Select(CloneGroup).ToList(),
        Right = Right.Select(CloneGroup).ToList(),
        Bottom = Bottom.Select(CloneGroup).ToList(),
        LeftWidth = LeftWidth,
        RightWidth = RightWidth,
        BottomHeight = BottomHeight,
    };

    private static DockGroup CloneGroup(DockGroup group) => new()
    {
        Panels = new List<string>(group.Panels),
        Active = group.Active,
        Weight = group.Weight,
        Collapsed = group.Collapsed,
    };
}
