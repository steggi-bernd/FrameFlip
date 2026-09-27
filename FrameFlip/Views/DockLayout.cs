namespace FrameFlip.Views;

/// <summary>Wo ein Feld angedockt sein kann.</summary>
public enum DockZone
{
    Left,
    Right,
    Bottom,

    /// <summary>
    /// Ueber dem Bild, ueber die ganze Breite - der Platz der Werkzeugeinstellungen
    /// (Entscheidung 10). Wie unten liegen die Gruppen nebeneinander.
    /// </summary>
    Top,
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
    public List<DockGroup> Top { get; set; } = new();

    /// <summary>Breite der linken Zone in Punkten.</summary>
    public double LeftWidth { get; set; } = 300;

    /// <summary>Breite der rechten Zone in Punkten.</summary>
    public double RightWidth { get; set; } = 320;

    /// <summary>Hoehe der unteren Zone in Punkten.</summary>
    public double BottomHeight { get; set; } = 240;

    /// <summary>
    /// Hoehe der oberen Zone in Punkten - nur, wenn dort ein Feld liegt, das nicht so hoch
    /// ist wie sein Inhalt. Die Werkzeugeinstellungen bestimmen ihre Hoehe selbst.
    /// </summary>
    public double TopHeight { get; set; } = 200;

    /// <summary>
    /// Die Anordnung, mit der man anfaengt: oben die Werkzeugeinstellungen, rechts oben die
    /// Verteilung, darunter Farbe und Ebenen als Reiter, unten rechts die Ausgabe.
    ///
    /// Die Verteilung oben, wie in jedem Entwicklungsprogramm - man sieht beim
    /// Ziehen eines Reglers hin, was mit den Lichtern passiert. Und als eigene Gruppe,
    /// damit sie stehen bleibt, waehrend man zwischen Farbe und Ebenen wechselt.
    /// </summary>
    public static DockLayout Default() => new()
    {
        // Die Einstellungen des gewaehlten Werkzeugs als Leiste ueber dem Bild - und wer
        // sie anders will, zieht sie an eine Seite (Entscheidung 10).
        Top =
        {
            new DockGroup { Panels = { "tool" }, Active = "tool", Weight = 1 },
        },

        Right =
        {
            new DockGroup { Panels = { "histogram" }, Active = "histogram", Weight = 1 },
            new DockGroup { Panels = { "colour", "layers" }, Active = "colour", Weight = 4 },

            // Die Ausgabe unten rechts, und bei einer Anordnung von frueher kommt sie von selbst
            // hierher (siehe Normalise). Ihre Hoehe richtet sich nach dem Inhalt (DockHost.
            // FitsContent) - das Gewicht zaehlt erst, wenn sie mit anderen Feldern eine Gruppe teilt.
            new DockGroup { Panels = { "export" }, Active = "export", Weight = 0.8 },
        },
    };

    /// <summary>Die Gruppen einer Zone.</summary>
    public List<DockGroup> Zone(DockZone zone) => zone switch
    {
        DockZone.Left => Left,
        DockZone.Bottom => Bottom,
        DockZone.Top => Top,
        _ => Right,
    };

    /// <summary>Ob die Gruppen einer Zone nebeneinander liegen - oben und unten - statt untereinander.</summary>
    public static bool IsHorizontal(DockZone zone) => zone is DockZone.Bottom or DockZone.Top;

    /// <summary>Alle Zonen mit ihren Gruppen.</summary>
    public IEnumerable<(DockZone Zone, List<DockGroup> Groups)> Zones()
    {
        yield return (DockZone.Left, Left);
        yield return (DockZone.Right, Right);
        yield return (DockZone.Bottom, Bottom);
        yield return (DockZone.Top, Top);
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
    /// Grundanordnung stehen - zu einem Feld, mit dem sie dort eine Gruppe teilen, sonst
    /// als eigene Gruppe in dieselbe Zone. So kommen die Werkzeugeinstellungen bei einer
    /// Anordnung von frueher nach oben.
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
            var fresh = Default();
            var home = fresh.Find(missing);

            if (home is { } spot)
            {
                // Zu einem Feld der Grundanordnung, das mit ihm in derselben Gruppe
                // stand - wenn es noch zusammen irgendwo liegt.
                var partner = fresh.Zone(spot.Zone)[spot.Group].Panels
                                   .FirstOrDefault(p => p != missing && seen.Contains(p));

                if (partner is not null && Find(partner) is { } at)
                {
                    Zone(at.Zone)[at.Group].Panels.Add(missing);
                    seen.Add(missing);
                    continue;
                }
            }

            Zone(home?.Zone ?? DockZone.Right).Add(new DockGroup { Panels = { missing }, Active = missing, Weight = 1 });
            seen.Add(missing);
        }

        LeftWidth = Clean(LeftWidth, 300);
        RightWidth = Clean(RightWidth, 320);
        BottomHeight = Clean(BottomHeight, 240);
        TopHeight = Clean(TopHeight, 200);

        static double Clean(double value, double fallback)
            => value is > 120 and < 3000 ? value : fallback;
    }

    /// <summary>Eine eigene Kopie - fuer die Einstellungen, die sie festhalten.</summary>
    public DockLayout Clone() => new()
    {
        Left = Left.Select(CloneGroup).ToList(),
        Right = Right.Select(CloneGroup).ToList(),
        Bottom = Bottom.Select(CloneGroup).ToList(),
        Top = Top.Select(CloneGroup).ToList(),
        LeftWidth = LeftWidth,
        RightWidth = RightWidth,
        BottomHeight = BottomHeight,
        TopHeight = TopHeight,
    };

    private static DockGroup CloneGroup(DockGroup group) => new()
    {
        Panels = new List<string>(group.Panels),
        Active = group.Active,
        Weight = group.Weight,
        Collapsed = group.Collapsed,
    };
}
