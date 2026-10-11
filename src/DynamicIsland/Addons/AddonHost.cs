using System.Windows.Threading;
using DynamicIsland.Addons.Alerts;
using DynamicIsland.Addons.Info;
using DynamicIsland.Addons.Productivity;
using DynamicIsland.Addons.SystemAddons;
using DynamicIsland.Settings;

namespace DynamicIsland.Addons;

/// <summary>
/// Creates every add-on, starts/stops each one to match its switch in the settings, and writes their
/// status to status.json for the settings app ("Set a city to show the weather").
/// </summary>
public sealed class AddonHost
{
    private readonly List<Addon> _addons;
    private readonly DispatcherTimer _writeStatus;
    private string? _notificationAccess;

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

        // Coalesce bursts (startup, several add-ons changing at once) into one write.
        _writeStatus = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _writeStatus.Tick += (_, _) =>
        {
            _writeStatus.Stop();
            WriteStatus();
        };
        foreach (var addon in _addons) addon.StatusChanged += StatusChanged;
    }

    /// <summary>Call at startup and whenever the settings change.</summary>
    public void Sync()
    {
        _addons.ForEach(a => a.Sync());
        StatusChanged();
    }

    public void Shutdown() => _addons.ForEach(a => a.Shutdown());

    /// <summary>Windows notification access, shown on the Notifications page when it isn't allowed.</summary>
    public void SetNotificationAccess(string access)
    {
        _notificationAccess = access;
        StatusChanged();
    }

    private void StatusChanged()
    {
        _writeStatus.Stop();
        _writeStatus.Start();
    }

    private void WriteStatus() => new IslandStatus
    {
        Addons = _addons.ToDictionary(a => a.Key, a => new AddonState(a.Running, a.StatusMessage, a.StatusIsProblem)),
        NotificationAccess = _notificationAccess,
    }.Save();
}
