using System.Management;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>
/// (2) Brightness bar. Uses the WMI brightness event, which built-in laptop/tablet screens raise.
/// External monitors don't report brightness changes to Windows, so on desktops this stays silent.
/// </summary>
public sealed class BrightnessAddon : Addon
{
    private readonly HudView _hud = new("", Ui.White);
    private ManagementEventWatcher? _watcher;

    public BrightnessAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.Brightness);
    public override string Name => "Brightness";
    protected override bool IsEnabled(AddonSettings o) => o.Brightness;

    protected override void Start()
    {
        if (!HasInternalDisplay())
        {
            SetStatus(L.T("No built-in screen found. External monitors don't report brightness changes to Windows."), problem: false);
            return; // nothing to watch; stay "on" but idle
        }

        _watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"),
                                              new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
        _watcher.EventArrived += (_, e) =>
        {
            var level = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
            Context.Ui(() =>
            {
                _hud.Set(level / 100.0, level.ToString());
                Show(_hud.Alert);
            });
        };
        _watcher.Start();
    }

    protected override void Stop()
    {
        _watcher?.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }

    private static bool HasInternalDisplay()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            return searcher.Get().Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
