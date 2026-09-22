namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Zufall, der sich wiederholen laesst.
///
/// Ein Glitch soll aussehen wie ein Zufall und sich verhalten wie eine Einstellung:
/// Derselbe Startwert muss bei jedem Rechnen dasselbe Bild geben - beim Loslassen
/// eines Reglers, beim naechsten Oeffnen, im Export. System.Random kann das nicht
/// zuverlaessig, sobald mehrere Faeden rechnen, und es kann vor allem nicht "gib mir
/// den Wert fuer Band 17", ohne die sechzehn davor zu ziehen.
///
/// Deshalb ein Mischverfahren: Die Koordinaten werden zu einer Zahl verrieben, und
/// dieselben Koordinaten geben immer dieselbe Zahl - ohne Zustand, ohne Reihenfolge,
/// auf jedem Faden.
/// </summary>
public static class GlitchNoise
{
    /// <summary>Ein Wert von 0 bis unter 1, bestimmt allein durch die vier Zahlen.</summary>
    public static float Unit(int a, int b = 0, int c = 0, int d = 0)
    {
        unchecked
        {
            uint h = (uint)a * 0x8DA6B343u;
            h ^= (uint)b * 0xD8163841u;
            h ^= (uint)c * 0xCB1AB31Fu;
            h ^= (uint)d * 0x165667B1u;

            // Durchruehren: Ohne diesen Schritt laegen benachbarte Baender auf
            // benachbarten Werten, und der Zufall saehe aus wie ein Verlauf.
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;

            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>Dasselbe, auf -1 bis 1 gelegt.</summary>
    public static float Signed(int a, int b = 0, int c = 0, int d = 0)
        => Unit(a, b, c, d) * 2f - 1f;
}
