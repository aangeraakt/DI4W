using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>CPU, memory and GPU readings shared by the performance warnings and the game overlay.</summary>
public static class SystemLoad
{
    // ───────────────────────────── CPU ─────────────────────────────

    /// <summary>Whole-system CPU use since this meter's previous reading. One meter per user, so readers don't interfere.</summary>
    public sealed class CpuMeter
    {
        private ulong _lastIdle, _lastTotal;

        /// <summary>0..100; the first reading is 0.</summary>
        public double Read()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            var total = kernel + user; // kernel time includes idle time
            var dIdle = idle - _lastIdle;
            var dTotal = total - _lastTotal;
            var first = _lastTotal == 0;
            _lastIdle = idle;
            _lastTotal = total;
            return first || dTotal == 0 ? 0 : Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
        }
    }

    /// <summary>The process using the most CPU over a one second window (null if none could be read).</summary>
    public static async Task<string?> TopCpuProcessAsync()
    {
        var before = Snapshot();
        await Task.Delay(1000);
        var after = Snapshot();
        return after.Where(a => before.ContainsKey(a.Key))
                    .OrderByDescending(a => a.Value.Cpu - before[a.Key].Cpu)
                    .Select(a => a.Value.Name)
                    .FirstOrDefault();

        static Dictionary<int, (string Name, TimeSpan Cpu)> Snapshot()
        {
            var result = new Dictionary<int, (string, TimeSpan)>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id != 0) result[p.Id] = (p.ProcessName, p.TotalProcessorTime);
                    }
                    catch
                    {
                        // protected/system processes
                    }
                }
            }
            return result;
        }
    }

    // ───────────────────────────── memory ─────────────────────────────

    public static int MemoryPercent()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (int)status.dwMemoryLoad : 0;
    }

    public static string? TopMemoryProcess()
    {
        string? name = null;
        long most = 0;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.WorkingSet64 > most)
                    {
                        most = p.WorkingSet64;
                        name = p.ProcessName;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }
        return name;
    }

    // ───────────────────────────── GPU ─────────────────────────────

    private static readonly Lazy<IntPtr> Nvidia = new(InitNvml);
    private static List<PerformanceCounter>? _gpuCounters;
    private static DateTime _gpuCountersAt;
    private static double? _lastGpu;

    /// <summary>GPU temperature in °C (NVIDIA cards only; null otherwise).</summary>
    public static int? GpuTemperature()
    {
        if (Nvidia.Value == IntPtr.Zero) return null;
        try
        {
            return nvmlDeviceGetTemperature(Nvidia.Value, 0, out var t) == 0 ? (int)t : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>GPU (3D engine) use 0..100: from the NVIDIA driver when available, else Windows' GPU counters.</summary>
    public static double? GpuPercent()
    {
        if (Nvidia.Value != IntPtr.Zero)
        {
            try
            {
                if (nvmlDeviceGetUtilizationRates(Nvidia.Value, out var u) == 0) return u.Gpu;
            }
            catch
            {
                // fall back to counters
            }
        }

        try
        {
            // Counter instances are per process and engine; refresh the set now and then as processes come and go.
            if (_gpuCounters is null || DateTime.UtcNow - _gpuCountersAt > TimeSpan.FromSeconds(15))
            {
                _gpuCounters?.ForEach(c => c.Dispose());
                var category = new PerformanceCounterCategory("GPU Engine");
                _gpuCounters = category.GetInstanceNames()
                                       .Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                                       .Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, readOnly: true))
                                       .ToList();
                _gpuCounters.ForEach(c => c.NextValue()); // first read is always 0
                _gpuCountersAt = DateTime.UtcNow;
                return _lastGpu; // fresh counters read 0 the first time; repeat the previous value
            }
            double sum = 0;
            foreach (var c in _gpuCounters)
            {
                try
                {
                    sum += c.NextValue();
                }
                catch
                {
                    // the process exited
                }
            }
            return _lastGpu = Math.Clamp(sum, 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr InitNvml()
    {
        try
        {
            if (nvmlInit_v2() != 0) return IntPtr.Zero;
            return nvmlDeviceGetHandleByIndex_v2(0, out var device) == 0 ? device : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero; // no NVIDIA driver
        }
    }

    // ───────────────────────────── interop ─────────────────────────────

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu, Memory;
    }

    [DllImport("nvml.dll")]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperature);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
}
