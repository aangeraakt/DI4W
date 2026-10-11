using DynamicIsland.Addons.Alerts;
using DynamicIsland.Addons.Info;
using DynamicIsland.Addons.Productivity;
using DynamicIsland.Addons.SystemAddons;

namespace DynamicIsland.Addons;

/// <summary>Creates every add-on and starts/stops each one to match its switch in the settings.</summary>
public sealed class AddonHost
{
    private readonly List<Addon> _addons;

    public AddonHost(AddonContext context)
    {
        _addons = new List<Addon>
        {
            new VolumeAddon(context),
            new BrightnessAddon(context),
            new BatteryAddon(context),
            new BluetoothAddon(context),
            new UsbDriveAddon(context),
            new NetworkAddon(context),
            new PrivacyAddon(context),
            new LockKeysAddon(context),
            new PerformanceAddon(context),
            new CalendarAddon(context),
            new ClipboardAddon(context),
            new ScreenshotAddon(context),
            new DownloadsAddon(context),
            new FileCopyAddon(context),
            new DoNotDisturbAddon(context),
            new WindowsUpdateAddon(context),
            new WeatherAddon(context),
            new GameModeAddon(context),
        };
    }

    /// <summary>Call at startup and whenever the settings change.</summary>
    public void Sync() => _addons.ForEach(a => a.Sync());

    public void Shutdown() => _addons.ForEach(a => a.Shutdown());
}
