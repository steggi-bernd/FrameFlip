using System.Runtime.InteropServices;
using FrameFlip.Interop;

namespace FrameFlip.Diagnostics;

/// <summary>
/// GPU-Auslastung ueber die PDH-Zaehler "GPU Engine(*)\Utilization Percentage" -
/// dieselbe Quelle, aus der auch der Task-Manager liest. Direkt ueber pdh.dll,
/// damit kein NuGet-Paket noetig ist.
///
/// Die Instanzen sind prozess- und engine-spezifisch
/// (pid_1234_luid_..._eng_0_engtype_3D). Innerhalb einer Engine-Art werden die
/// Werte aufsummiert, ueber die Engine-Arten hinweg wird das Maximum genommen -
/// eine mit Cycles ausgelastete Karte schlaegt so voll durch.
/// </summary>
public sealed class GpuLoadCounter : IDisposable
{
    private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";

    private IntPtr _query;
    private IntPtr _counter;
    private bool _available;
    private bool _primed;
    private bool _disposed;

    public bool IsAvailable => _available;

    public GpuLoadCounter()
    {
        try
        {
            if (NativeMethods.PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) return;
            if (NativeMethods.PdhAddEnglishCounterW(_query, CounterPath, IntPtr.Zero, out _counter) != 0)
            {
                NativeMethods.PdhCloseQuery(_query);
                _query = IntPtr.Zero;
                return;
            }

            _available = true;
        }
        catch (Exception)
        {
            // Ohne GPU-Zaehler laeuft die Lasterkennung eben nur auf CPU und RAM.
            _available = false;
        }
    }

    /// <summary>Aktuelle Auslastung in Prozent, oder null wenn nicht ermittelbar.</summary>
    public double? Read()
    {
        if (!_available || _disposed) return null;

        try
        {
            if (NativeMethods.PdhCollectQueryData(_query) != 0) return null;

            if (!_primed)
            {
                // Der erste Aufruf liefert nur die Basis fuer die Differenzbildung.
                _primed = true;
                return null;
            }

            uint size = 0;
            uint count = 0;
            uint status = NativeMethods.PdhGetFormattedCounterArrayW(
                _counter, NativeMethods.PDH_FMT_DOUBLE | NativeMethods.PDH_FMT_NOCAP100,
                ref size, out count, IntPtr.Zero);

            if (status != NativeMethods.PDH_MORE_DATA || size == 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = NativeMethods.PdhGetFormattedCounterArrayW(
                    _counter, NativeMethods.PDH_FMT_DOUBLE | NativeMethods.PDH_FMT_NOCAP100,
                    ref size, out count, buffer);

                if (status != 0 || count == 0) return null;

                var perEngineType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                int itemSize = Marshal.SizeOf<NativeMethods.PdhCounterValueItem>();

                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<NativeMethods.PdhCounterValueItem>(buffer + i * itemSize);
                    if (item.Status != 0 || item.Name == IntPtr.Zero) continue;

                    string? instance = Marshal.PtrToStringUni(item.Name);
                    if (instance is null) continue;

                    int marker = instance.LastIndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                    string engine = marker >= 0 ? instance[(marker + 8)..] : "unknown";

                    perEngineType.TryGetValue(engine, out double sum);
                    perEngineType[engine] = sum + item.Value;
                }

                if (perEngineType.Count == 0) return null;

                double peak = 0;
                foreach (double value in perEngineType.Values)
                    if (value > peak) peak = value;

                return Math.Clamp(peak, 0, 100);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _available = false;

        if (_query != IntPtr.Zero)
        {
            try { NativeMethods.PdhCloseQuery(_query); } catch (Exception) { }
            _query = IntPtr.Zero;
        }
    }
}
