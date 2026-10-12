using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>
/// (10) Warns when the PC stays under heavy load: CPU above 90% for a minute, memory above 90% for 30 s,
/// or (NVIDIA) the GPU above 85 °C. Names the process using the most. At most once per 15 min per kind.
/// </summary>
public sealed class PerformanceAddon : Addon
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    private readonly SystemLoad.CpuMeter _cpu = new();
    private readonly Dictionary<string, DateTime> _since = new();
    private readonly Dictionary<string, DateTime> _lastWarned = new();
    private DispatcherTimer? _poll;

    public PerformanceAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.PerformanceWarnings);
    public override string Name => "Performance warnings";
    protected override bool IsEnabled(AddonSettings o) => o.PerformanceWarnings;

    protected override void Start()
    {
        _cpu.Read();
        _poll = new DispatcherTimer { Interval = Power.Interval(TimeSpan.FromSeconds(5)) };
        _poll.Tick += async (_, _) => await Check();
        _poll.Start();
    }

    protected override void Stop()
    {
        _poll?.Stop();
        _poll = null;
        _since.Clear();
    }

    private async Task Check()
    {
        var cpu = _cpu.Read();
        if (Sustained("cpu", cpu > 90, TimeSpan.FromMinutes(1)))
        {
            var top = await SystemLoad.TopCpuProcessAsync();
            Warn("cpu", L.T("CPU is working hard"), L.F("{0:0}% for a minute", cpu) + (top is null ? "" : L.F(" · mostly {0}", top)));
        }

        var memory = SystemLoad.MemoryPercent();
        if (Sustained("memory", memory > 90, TimeSpan.FromSeconds(30)))
        {
            var top = SystemLoad.TopMemoryProcess();
            Warn("memory", L.T("Memory almost full"), L.F("{0}% in use", memory) + (top is null ? "" : L.F(" · {0} uses the most", top)));
        }

        if (SystemLoad.GpuTemperature() is { } temp && Sustained("gpu", temp >= 85, TimeSpan.FromSeconds(20)))
        {
            Warn("gpu", L.T("GPU is running hot"), $"{temp} °C");
        }
    }

    /// <summary>True once the condition has held for the given time (and not warned about recently).</summary>
    private bool Sustained(string kind, bool condition, TimeSpan duration)
    {
        if (!condition)
        {
            _since.Remove(kind);
            return false;
        }
        var now = DateTime.UtcNow;
        if (!_since.TryGetValue(kind, out var start)) _since[kind] = start = now;
        if (now - start < duration) return false;
        return !_lastWarned.TryGetValue(kind, out var last) || now - last > Cooldown;
    }

    private void Warn(string kind, string title, string subtitle)
    {
        _lastWarned[kind] = DateTime.UtcNow;
        _since.Remove(kind);
        Show(new IslandAlert
        {
            Content = Ui.AlertView(Ui.GlyphCircle("", Ui.Amber, 46, 20), title, subtitle, Ui.Amber),
            Size = new Size(400, 76),
            Duration = TimeSpan.FromSeconds(6),
            StickyUntilHover = true,
            OnClick = () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }),
        });
    }
}
