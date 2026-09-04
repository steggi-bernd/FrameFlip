using System.Diagnostics;
using System.Runtime;

namespace FrameFlip.Interop;

/// <summary>
/// Rueckgabe des Speichers nach dem Schliessen des Viewers.
/// Die Pixelpuffer liegen auf dem LOH - ohne Kompaktierung bliebe der Working Set stehen,
/// auch wenn managed nichts mehr referenziert ist.
/// </summary>
public static class MemoryTrimmer
{
    public static void TrimNow()
    {
        try
        {
            // Aggressive gibt Speicher tatsaechlich ans Betriebssystem zurueck, statt
            // die Segmente fuer die naechste Allokation vorzuhalten.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            // Working Set an das OS zurueckgeben. Seiten kommen bei Bedarf zurueck.
            using var process = Process.GetCurrentProcess();
            NativeMethods.SetProcessWorkingSetSize(process.Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception)
        {
            // Aufraeumen ist Kuer, kein Grund fuer einen Absturz.
        }
    }
}
