namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Der kantengebundene Pinsel (docs/Atelier-Werkzeugplan.md, W1): Ein Strich bleibt auf der
/// Flaeche, auf der er ansetzt. Welche Flaeche das ist, sagen die Renderpaesse - die
/// Entfernung und die Richtung, in die eine Flaeche zeigt. Wo beide zum Ansatz passen, malt
/// der Strich; wo eine Kante kommt - ein Sprung in der Tiefe, ein Knick in der Normale -,
/// hoert er auf.
///
/// Das Ergebnis ist ein Feld in der Groesse der Maske wie beim objektgebundenen Pinsel. Es
/// wird mit dem Strich gespeichert, damit er ohne die Datei genau so nachspielt.
///
/// Verglichen wird mit dem Ansatz, nicht mit dem Nachbarn: Eine Kugel hat keine Kante, aber
/// ihre Normale dreht sich stetig. Mit der Toleranz reicht der Strich ueber eine Rundung so
/// weit, wie sie sich vom Ansatz entfernen darf.
/// </summary>
public static class SurfaceLimit
{
    /// <summary>
    /// Baut die Begrenzung fuer einen Ansatz bei (<paramref name="x"/>, <paramref name="y"/>)
    /// in Bildpunkten. <paramref name="tolerance"/> 0 bis 1: wie weit Tiefe und Richtung vom
    /// Ansatz abweichen duerfen. Null, wenn kein Pass da ist oder der Punkt ausserhalb liegt.
    /// </summary>
    public static byte[]? Build(FloatFrame? depth, FloatFrame? normal, int x, int y, float tolerance,
                                int cols, int rows, int coarse)
    {
        var any = depth ?? normal;
        if (any is null) return null;

        int width = any.Width, height = any.Height;
        if (x < 0 || y < 0 || x >= width || y >= height) return null;

        if (depth is not null && (depth.Width != width || depth.Height != height)) return null;
        if (normal is not null && (normal.Width != width || normal.Height != height)) return null;

        float t = Math.Clamp(tolerance, 0f, 1f);

        // Tiefe als Anteil der Entfernung: Ein Zentimeter ist nah am Auge viel, fern nichts.
        float depthReach = 0.005f + 0.3f * t;

        // Richtung als Winkel, in Grad.
        float turnReach = 4f + 60f * t;
        float turnCos = MathF.Cos(turnReach * MathF.PI / 180f);
        float turnHalfCos = MathF.Cos(turnReach * 0.5f * MathF.PI / 180f);

        int start = y * width + x;

        float? startDepth = depth is null ? null : Depth(depth, start);
        var startNormal = normal is null ? default : Direction(normal, start);
        bool useNormal = normal is not null && startNormal.Length > 0.5f;

        var cover = new byte[cols * rows];

        Parallel.For(0, rows, my =>
        {
            int py = Math.Min(height - 1, my * coarse + coarse / 2);

            for (int mx = 0; mx < cols; mx++)
            {
                int px = Math.Min(width - 1, mx * coarse + coarse / 2);
                int i = py * width + px;

                float weight = 1f;

                if (depth is not null)
                {
                    float? here = Depth(depth, i);

                    // Der Hintergrund hat keine Entfernung. Wer auf ihm ansetzt, malt nur
                    // auf ihm - und wer auf einer Flaeche ansetzt, nie auf ihm.
                    if (startDepth is null || here is null)
                    {
                        weight = startDepth is null == here is null ? 1f : 0f;
                    }
                    else
                    {
                        float apart = MathF.Abs(here.Value - startDepth.Value) / MathF.Max(1e-6f, startDepth.Value);
                        weight *= 1f - Smooth(depthReach * 0.5f, depthReach, apart);
                    }
                }

                if (useNormal && weight > 0f)
                {
                    var here = Direction(normal!, i);

                    if (here.Length > 0.5f)
                    {
                        float cos = (here.X * startNormal.X + here.Y * startNormal.Y + here.Z * startNormal.Z)
                                    / (here.Length * startNormal.Length);

                        // Im Kosinus gerechnet und nicht im Winkel - kein Arkuskosinus je Punkt.
                        weight *= 1f - Smooth(-turnHalfCos, -turnCos, -cos);
                    }
                    else
                    {
                        weight = 0f;
                    }
                }

                cover[my * cols + mx] = (byte)MathF.Round(Math.Clamp(weight, 0f, 1f) * 255f);
            }
        });

        return cover;
    }

    /// <summary>Die Entfernung an einer Stelle - oder null fuer "hier steht nichts".</summary>
    private static float? Depth(FloatFrame depth, int i)
    {
        float value = depth.R[i];
        return float.IsFinite(value) && value > 0f && value < FloatFrame.NotHit ? value : null;
    }

    private static (float X, float Y, float Z, float Length) Direction(FloatFrame normal, int i)
    {
        float nx = normal.R[i], ny = normal.G[i], nz = normal.B[i];
        if (!float.IsFinite(nx) || !float.IsFinite(ny) || !float.IsFinite(nz)) return default;

        return (nx, ny, nz, MathF.Sqrt(nx * nx + ny * ny + nz * nz));
    }

    /// <summary>0 bis <paramref name="from"/>, weich bis 1 bei <paramref name="to"/>.</summary>
    private static float Smooth(float from, float to, float value)
    {
        if (value <= from) return 0f;
        if (value >= to) return 1f;

        float s = (value - from) / (to - from);
        return s * s * (3f - 2f * s);
    }
}
