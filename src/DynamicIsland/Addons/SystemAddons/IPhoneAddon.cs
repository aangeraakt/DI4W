using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Activities.Notifications;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>
/// iPhone, through Windows' Phone Link. Its notifications and calls arrive as Phone Link notifications and are
/// shown by the notification presenter; this add-on adds the iPhone's battery (read over Bluetooth, which the
/// linked iPhone shares) with low-battery warnings, and tells the settings app whether everything is set up.
/// </summary>
public sealed class IPhoneAddon : Addon
{
    private const string ContainerId = "System.Devices.Aep.ContainerId";
    private const int CriticalPercent = 10;

    private DispatcherTimer? _poll;
    private int? _lastPercent;
    private bool _checking;

    public IPhoneAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.IPhone);
    public override string Name => "iPhone";
    protected override bool IsEnabled(AddonSettings o) => o.IPhone;

    protected override void Start()
    {
        _lastPercent = null;
        _poll = new DispatcherTimer { Interval = Power.Interval(TimeSpan.FromMinutes(3)) };
        _poll.Tick += async (_, _) => await Check();
        _poll.Start();
        _ = Check();
    }

    protected override void Stop()
    {
        _poll?.Stop();
        _poll = null;
    }

    public override void Preview() => Show(new IslandAlert
    {
        Content = Ui.AlertView(Ui.GlyphCircle("", Ui.Green, 46, 20), "Sam", L.T("On my way, see you in 10!"),
                               accessory: Ui.Text("iPhone · Messages", 11.5, FontWeights.Medium, Ui.Grey)),
        Size = new Size(420, 76),
        Duration = TimeSpan.FromSeconds(5),
    });

    private async Task Check()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            if (!PhoneLinkInstalled())
            {
                SetStatus(L.T("Install Phone Link from the Microsoft Store, then link your iPhone in it."));
                return;
            }

            var phone = await FindIPhone();
            if (phone is null)
            {
                SetStatus(L.T("Open Phone Link and link your iPhone. Its notifications and calls then show up here."));
                return;
            }

            var percent = await BluetoothBattery.ReadAsync(phone.Id, phone.Properties.TryGetValue(ContainerId, out var c) && c is Guid g ? g : null,
                                                           lowEnergy: true, phone.Name);
            SetStatus(percent is { } p ? L.F("{0} is linked · {1}% battery", phone.Name, p) : L.F("{0} is linked", phone.Name), problem: false);
            if (percent is { } now) Warn(phone.Name, now);
        }
        catch (Exception ex)
        {
            Log.Info($"iPhone check failed: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>A warning when the battery drops past the low mark, and again past 10%.</summary>
    private void Warn(string phone, int percent)
    {
        var previous = _lastPercent;
        _lastPercent = percent;
        if (previous is not { } before || percent >= before) return;
        var low = Math.Clamp(Options.IPhoneBatteryLowPercent, CriticalPercent + 1, 50);
        if (before > CriticalPercent && percent <= CriticalPercent) Announce(phone, percent, Ui.Red);
        else if (before > low && percent <= low) Announce(phone, percent, Ui.Amber);
    }

    private void Announce(string phone, int percent, System.Windows.Media.Brush color) => Show(new IslandAlert
    {
        Content = Ui.AlertView(new System.Windows.Controls.Border { Width = 46, Height = 46, Child = Icons.Battery(percent / 100.0, false, color, 34) },
                               L.F("{0} battery is low", phone), L.F("{0}% left", percent), color),
        Size = new Size(380, 76),
        Duration = TimeSpan.FromSeconds(6),
        OnClick = PhoneLink.Open,
    });

    /// <summary>The paired iPhone (Phone Link pairs it over Bluetooth LE), or null.</summary>
    private static async Task<DeviceInformation?> FindIPhone()
    {
        var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), new[] { ContainerId });
        return devices.FirstOrDefault(d => d.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase));
    }

    private static bool PhoneLinkInstalled()
    {
        try
        {
            return new Windows.Management.Deployment.PackageManager().FindPackagesForUser(string.Empty, PhoneLink.PackageFamily).Any();
        }
        catch
        {
            return true; // can't tell; don't nag
        }
    }
}
