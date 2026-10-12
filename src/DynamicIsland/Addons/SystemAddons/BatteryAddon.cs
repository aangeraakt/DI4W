using System.Windows;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Windows.System.Power;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>(3) Charger plugged in / unplugged with the charge level, and warnings when the battery runs low.</summary>
public sealed class BatteryAddon : Addon
{
    private bool _pluggedIn;
    private int _lastPercent;

    public BatteryAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.Battery);
    public override string Name => "Battery";
    protected override bool IsEnabled(AddonSettings o) => o.Battery;

    protected override void Start()
    {
        if (PowerManager.BatteryStatus == BatteryStatus.NotPresent)
        {
            SetStatus(L.T("This PC has no battery."), problem: false);
            return; // desktop PC: nothing to show
        }
        _pluggedIn = IsPluggedIn;
        _lastPercent = PowerManager.RemainingChargePercent;
        PowerManager.PowerSupplyStatusChanged += OnPowerSupply;
        PowerManager.RemainingChargePercentChanged += OnPercent;
    }

    protected override void Stop()
    {
        PowerManager.PowerSupplyStatusChanged -= OnPowerSupply;
        PowerManager.RemainingChargePercentChanged -= OnPercent;
    }

    private static bool IsPluggedIn => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;

    private void OnPowerSupply(object? sender, object e) => Context.Ui(() =>
    {
        var plugged = IsPluggedIn;
        if (plugged == _pluggedIn) return;
        _pluggedIn = plugged;
        var percent = PowerManager.RemainingChargePercent;

        if (plugged)
        {
            Announce(L.T("Charging"), $"{percent}%", percent, Ui.Green, charging: true);
        }
        else
        {
            var left = PowerManager.RemainingDischargeTime;
            var estimate = left > TimeSpan.FromMinutes(1) && left < TimeSpan.FromDays(1)
                ? left.TotalHours >= 1 ? L.F(" · {0} h {1} min left", (int)left.TotalHours, left.Minutes) : L.F(" · {0} min left", left.Minutes)
                : "";
            Announce(L.T("On battery"), $"{percent}%{estimate}", percent, percent <= Options.BatteryLowPercent ? Ui.Red : Ui.White, charging: false);
        }
    });

    private void OnPercent(object? sender, object e) => Context.Ui(() =>
    {
        var percent = PowerManager.RemainingChargePercent;
        var previous = _lastPercent;
        _lastPercent = percent;
        if (_pluggedIn || percent >= previous) return;

        // Warn once when crossing each threshold on the way down.
        if (previous > Options.BatteryCriticalPercent && percent <= Options.BatteryCriticalPercent)
        {
            Announce(L.T("Battery very low"), L.F("{0}% left. Plug in your charger.", percent), percent, Ui.Red, false, urgent: true);
        }
        else if (previous > Options.BatteryLowPercent && percent <= Options.BatteryLowPercent)
        {
            Announce(L.T("Low battery"), L.F("{0}% left", percent), percent, Ui.Amber, false);
        }
    });

    private void Announce(string title, string subtitle, int percent, System.Windows.Media.Brush color, bool charging, bool urgent = false)
    {
        var icon = new System.Windows.Controls.Border
        {
            Width = 46, Height = 46,
            Child = Icons.Battery(percent / 100.0, charging, color, 34),
        };
        Show(new IslandAlert
        {
            Content = Ui.AlertView(icon, title, subtitle, color == Ui.White ? null : color),
            Size = new Size(360, 76),
            Duration = TimeSpan.FromSeconds(urgent ? 8 : 3.5),
            StickyUntilHover = urgent,
            BreaksThroughQuiet = urgent,
        });
    }
}
