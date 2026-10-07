using System.IO;
using FrameFlip.Configuration;

namespace FrameFlip.Atelier;

/// <summary>
/// "Nur dieses Bild" (docs/Atelier-Werkzeugplan.md, Entscheidung 7): Ein Bild einer Folge
/// wird zu einem eigenen Einzelbild. Im Quellordner entsteht ein Ordner mit dem Namen des
/// Bildes, darin eine Kopie des Originals und ein eigenes Projekt, das mit dem Stand der
/// Folge beginnt - samt Maskenverlauf. Was danach dort gemalt und ausgegeben wird, bleibt
/// dort: FrameFlip legt Projekt und Schnell-Exporte immer im Ordner "FrameFlip" neben dem
/// Bild ab.
///
/// Das Einzelbild merkt sich, woher es kommt (<see cref="AtelierProject.Origin"/>), damit
/// der Weg zurueck in die Folge auch nach einem Neustart da ist.
/// </summary>
internal static class FrameDetach
{
    /// <summary>Der Ordner fuer das Einzelbild: im Quellordner, benannt wie das Bild ohne Endung.</summary>
    public static string FolderFor(string framePath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(framePath))!, Path.GetFileNameWithoutExtension(framePath));

    /// <summary>Wo die Kopie des Bildes liegt.</summary>
    public static string CopyFor(string framePath) => Path.Combine(FolderFor(framePath), Path.GetFileName(framePath));

    /// <summary>
    /// Legt das Einzelbild an, wenn es noch keines gibt, und liefert den Pfad der Kopie. Gibt
    /// es den Ordner mit der Kopie schon, wird er genommen, wie er ist - ein Einzelbild, an
    /// dem schon gearbeitet wurde, wird nicht ueberschrieben.
    /// </summary>
    /// <param name="project">Der Stand der Folge - mit ihm beginnt das Einzelbild.</param>
    public static string Detach(string framePath, AtelierProject project, AtelierProjectStore store)
    {
        string copy = CopyFor(framePath);
        if (File.Exists(copy)) return copy;

        // Was noch unterwegs ist - der Maskenverlauf der Folge -, soll mit hinueber.
        AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));

        Directory.CreateDirectory(FolderFor(framePath));

        // Erst unter anderem Namen und dann umbenannt: Eine halb kopierte Datei - bei einer
        // EXR mit allen Paessen sind das Gigabytes - soll nie als fertiges Einzelbild dastehen.
        string partial = copy + ".kopie";
        File.Copy(framePath, partial, overwrite: true);
        File.Move(partial, copy);

        var key = SequenceKey.Of(copy)!;
        project.Origin = Path.GetFullPath(framePath);
        project.Frame = Path.GetFileName(copy);
        store.Save(key, project);

        // Der Maskenverlauf der Folge kommt mit - die Masken sind dieselben.
        if (SequenceKey.Of(framePath) is { } from)
        {
            string? source = store.SidePaths(from, "x").Select(Path.GetDirectoryName).FirstOrDefault(Directory.Exists);
            string? target = store.SidePaths(key, "x").Select(Path.GetDirectoryName).FirstOrDefault();

            if (source is not null && target is not null) CopyFolder(source, target);
        }

        return copy;
    }

    private static void CopyFolder(string from, string to)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Ohne Verlauf ist das Einzelbild trotzdem da - es beginnt ihn dann neu.
            SettingsStore.Trace("Maskenverlauf nicht mitgenommen: " + e.Message);
        }
    }
}
