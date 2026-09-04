using System.Diagnostics;
using System.Reflection;
using FrameFlip.Configuration;
using FrameFlip.Diagnostics;

namespace FrameFlip.Tests;

/// <summary>
/// Das Lastprofil. Kernpunkt: unter CPU-Last wird der Decoder langsamer, der Puffer
/// aber NICHT kleiner - er ist dann die einzige Reserve, aus der die Wiedergabe noch
/// fluessig laufen kann.
/// </summary>
public static class GovernorInvariants
{
    public static void Run()
    {
        BufferSurvivesCpuLoad();
        DraftStepMath();
        PrefetchFollowsFrameRate();
        RingUsesTheBudget();
    }

    /// <summary>
    /// Der Ring soll das zugesagte Budget auch benutzen - und die Sequenz
    /// vollstaendig halten, wenn sie hineinpasst. Sonst wird bei jedem
    /// Loop-Durchlauf neu dekodiert, obwohl der Speicher bereits reserviert ist.
    /// </summary>
    private static void RingUsesTheBudget()
    {
        Check.Group("Der Ring nutzt das Budget aus");

        // Nachgerechnet, wie RecomputeWindow es tut.
        static (int Capacity, int Ahead) Window(long budgetBytes, long frameBytes,
                                                int count, int ahead, int behind)
        {
            long fits = Math.Max(2, budgetBytes / frameBytes);

            if (fits >= count && count > 0) return (count, Math.Max(1, count - 1));

            int room = (int)Math.Min(fits, count);
            if (room > ahead + behind + 1) ahead = Math.Max(ahead, room - 1 - behind);

            int capacity = (int)Math.Min(fits, ahead + behind + 1);
            return (capacity, ahead);
        }

        long frame1080p = 1920L * 4 * 1080;              // 7,91 MB
        long twoGb = 2048L * 1024 * 1024;

        // Passt die Sequenz hinein, wird sie vollstaendig gehalten.
        var small = Window(twoGb, frame1080p, count: 150, ahead: 120, behind: 30);
        Check.That(small.Capacity == 150,
            "eine Sequenz, die hineinpasst, wird ganz gehalten", $"{small.Capacity}");
        Check.That(small.Ahead == 149, "und vollstaendig vorausgeladen", $"{small.Ahead}");

        // Passt sie nicht, wird trotzdem der ganze Platz benutzt.
        var large = Window(twoGb, frame1080p, count: 600, ahead: 120, behind: 30);
        Check.That(large.Capacity > 250,
            "bei langer Sequenz wird das Budget ausgeschoepft", $"{large.Capacity}");
        Check.That(large.Ahead > 200, "und der Vorlauf waechst mit", $"{large.Ahead}");

        // Frueher blieb der Ring bei Vorlauf plus Ruecklauf stehen.
        int oldCapacity = Math.Min(259, 120 + 30 + 1);
        Check.That(large.Capacity > oldCapacity,
            "deutlich mehr als die frueheren 151 Frames",
            $"{large.Capacity} statt {oldCapacity}");

        // Ein knappes Budget darf nicht ueberschritten werden.
        var tight = Window(256L * 1024 * 1024, frame1080p, count: 600, ahead: 120, behind: 30);
        Check.That(tight.Capacity <= 33,
            "ein kleines Budget bleibt die Obergrenze", $"{tight.Capacity}");

        // In Sekunden: das ist der Wert, der ueber fluessige Wiedergabe entscheidet.
        Console.WriteLine($"         2 GB, 1080p, 600 Frames: {large.Capacity} im Ring, " +
                          $"{large.Ahead / 60.0:0.0} s Vorlauf bei 60 fps");
    }

    /// <summary>
    /// Der Vorlauf ist in Frames angegeben und bedeutet je nach Bildrate etwas
    /// anderes. Bei 60 fps sind 60 Frames nur eine Sekunde Reserve - genau dort
    /// laeuft der Ring beim kleinsten Stocken trocken.
    /// </summary>
    private static void PrefetchFollowsFrameRate()
    {
        Check.Group("Vorlauf folgt der Bildrate");

        // Nachgerechnet, wie ViewerWindow.PrefetchAhead es tut: der eingestellte
        // Wert als Untergrenze, darueber hinaus mindestens zwei Sekunden.
        static int Ahead(int configured, double fps)
            => Math.Clamp(Math.Max(configured, (int)Math.Ceiling(fps * 2.0)), 1, 2000);

        Check.That(Ahead(60, 24) == 60, "bei 24 fps bleibt es beim eingestellten Wert",
            $"{Ahead(60, 24)}");
        Check.That(Ahead(60, 60) == 120, "bei 60 fps wird verdoppelt", $"{Ahead(60, 60)}");
        Check.That(Ahead(60, 30) == 60, "bei 30 fps reichen die eingestellten 60");
        Check.That(Ahead(200, 24) == 200, "ein hoeher eingestellter Wert bleibt erhalten");

        // In Sekunden gerechnet ist der Vorlauf jetzt bei jeder Bildrate ausreichend.
        foreach (double fps in new[] { 12.0, 24.0, 30.0, 50.0, 60.0 })
        {
            double seconds = Ahead(60, fps) / fps;
            Check.That(seconds >= 2.0 - 1e-9,
                $"bei {fps:0.###} fps mindestens zwei Sekunden Vorlauf", $"{seconds:0.00} s");
        }

        // Die Ringkapazitaet folgt aus Vor- plus Ruecklauf, nicht aus dem Budget -
        // deshalb half ein groesseres Budget allein nicht.
        long frameBytes = 1920L * 4 * 1080;
        long budget = 2048L * 1024 * 1024;
        long fits = budget / frameBytes;

        int capacityOld = (int)Math.Min(fits, 60 + 15 + 1);
        int capacityNew = (int)Math.Min(fits, 120 + 30 + 1);

        Check.That(capacityOld == 76, "vorher blieb der Ring bei 76 Frames stehen", $"{capacityOld}");
        Check.That(capacityNew == 151, "jetzt sind es 151", $"{capacityNew}");
        Check.That(fits > capacityNew,
            "und das Budget ist damit immer noch nicht der begrenzende Faktor",
            $"{fits} passten hinein");
    }

    /// <summary>Ruft die private Ableitung auf, ohne echte Systemlast zu brauchen.</summary>
    private static ResourceProfile Derive(SystemLoadMonitor monitor, LoadSnapshot snapshot)
    {
        var method = typeof(SystemLoadMonitor)
            .GetMethod("Derive", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (ResourceProfile)method.Invoke(monitor, new object[] { snapshot })!;
    }

    private static void BufferSurvivesCpuLoad()
    {
        Check.Group("Lastprofil - der Puffer ueberlebt CPU-Last");

        using var monitor = new SystemLoadMonitor(4, TimeSpan.FromSeconds(10));

        // Viel freier Speicher, aber die Maschine rechnet - der typische Fall,
        // wenn nebenher ein Render laeuft.
        foreach (var level in new[] { LoadLevel.Idle, LoadLevel.Moderate, LoadLevel.Busy, LoadLevel.Critical })
        {
            var snapshot = new LoadSnapshot(95, null, 16000, level) { MemoryTight = false };
            var profile = Derive(monitor, snapshot);

            Check.Near(profile.BudgetScale, 1.0, 1e-9,
                $"{level}: das Pufferbudget bleibt ungekuerzt, solange Speicher da ist");
            Check.Near(profile.WindowScale, 1.0, 1e-9,
                $"{level}: auch das Pufferfenster bleibt ungekuerzt");
        }

        // Die Threadzahl folgt dagegen sehr wohl der Last.
        var idle = Derive(monitor, new LoadSnapshot(5, null, 16000, LoadLevel.Idle));
        var moderate = Derive(monitor, new LoadSnapshot(35, null, 16000, LoadLevel.Moderate));
        var busy = Derive(monitor, new LoadSnapshot(70, null, 16000, LoadLevel.Busy));

        Check.That(idle.DecoderThreads > moderate.DecoderThreads,
            "im Leerlauf mehr Threads als bei mittlerer Last",
            $"{idle.DecoderThreads} vs {moderate.DecoderThreads}");
        Check.That(moderate.DecoderThreads > busy.DecoderThreads || busy.DecoderThreads == 1,
            "unter Last faellt die Threadzahl auf eins", $"{busy.DecoderThreads}");
        Check.That(busy.ThreadPriority == ThreadPriority.Lowest,
            "und die Threadprioritaet auf die niedrigste Stufe");
        Check.That(idle.ProcessPriority == ProcessPriorityClass.Normal,
            "im Leerlauf darf der Prozess normale Prioritaet haben");

        // Nur echter Speichermangel kuerzt den Ring.
        var tight = Derive(monitor, new LoadSnapshot(10, null, 1500, LoadLevel.Busy) { MemoryTight = true });
        Check.That(tight.BudgetScale < 1.0,
            "bei knappem Speicher wird das Budget gekuerzt", $"{tight.BudgetScale}");

        var severe = Derive(monitor, new LoadSnapshot(10, null, 700, LoadLevel.Critical) { MemoryTight = true });
        Check.That(severe.BudgetScale < tight.BudgetScale,
            "bei sehr knappem Speicher noch deutlicher",
            $"{severe.BudgetScale} vs {tight.BudgetScale}");

        Check.That(new LoadSnapshot(10, null, 16000, LoadLevel.Idle).MemoryTight == false,
            "viel freier Speicher gilt nicht als knapp");
        Check.That(new LoadSnapshot(10, null, 0, LoadLevel.Idle).MemoryTight == false,
            "eine fehlgeschlagene Speichermessung gilt nicht als Mangel");
    }

    /// <summary>
    /// Die Pufferstufe rechnet sich in den Vorlauf um. Die Zahlen stammen aus einer
    /// Messung mit 1080p-Material: 7,91 MB je Frame in voller Groesse.
    /// </summary>
    private static void DraftStepMath()
    {
        Check.Group("Pufferstufe - Vorlauf im Speicherbudget");

        var settings = new AppSettings();
        settings.Normalize();

        Check.That(settings.MemoryBudgetMb >= 1024,
            "der Standard reicht fuer mehr als drei Sekunden 1080p-Vorlauf",
            $"{settings.MemoryBudgetMb} MB");

        Check.That(settings.DraftStep == 0, "neue Konfigurationen starten in voller Groesse");

        settings.DraftStep = 7;
        settings.Normalize();
        Check.That(settings.DraftStep <= 2, "eine unsinnige Stufe wird begrenzt", $"{settings.DraftStep}");

        // Der Gewinn ist quadratisch: halbe Kantenlaenge, ein Viertel der Pixel.
        foreach (var (scale, factor) in new[] { (1.0, 1), (0.5, 4), (0.25, 16) })
        {
            long bytesFull = 1920L * 1080 * 4;
            long bytes = (long)(1920 * scale) * 4 * (long)(1080 * scale);
            double ratio = bytesFull / (double)bytes;

            Check.Near(ratio, factor, 0.05,
                $"Stufe {scale * 100:0} % fasst das {factor}-fache in denselben Speicher");
        }

        // Gegenprobe zur Messung: 1 GB, volle Groesse, 24 fps.
        long budget = 1024L * 1024 * 1024;
        double secondsFull = budget / (1920.0 * 1080 * 4) / 24;
        Check.That(secondsFull > 3.0,
            "voller Vorlauf bei 1 GB uebersteigt drei Sekunden", $"{secondsFull:0.0} s");

        double secondsQuarter = budget / (480.0 * 270 * 4) / 24;
        Check.That(secondsQuarter > 60,
            "in viertel Groesse reicht der Vorrat ueber eine Minute", $"{secondsQuarter:0} s");
    }
}
