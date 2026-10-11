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

    public override string Name => "Battery";
    protected override bool IsEnabled(AddonSettings o) => o.Battery;

    protected override void Start()
    {
        if (PowerManager.BatteryStatus == BatteryStatus.NotPresent) return; // desktop PC: nothing to show
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
            Announce("Charging", $"{percent}%", percent, Ui.Green, charging: true);
        }
        else
        {
            var left = PowerManager.RemainingDischargeTime;
            var estimate = left > TimeSpan.FromMinutes(1) && left < TimeSpan.FromDays(1)
                ? left.TotalHours >= 1 ? $" · {(int)left.TotalHours} h {left.Minutes} min left" : $" · {left.Minutes} min left"
                : "";
            Announce("On battery", $"{percent}%{estimate}", percent, percent <= Options.BatteryLowPercent ? Ui.Red : Ui.White, charging: false);
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
            Announce("Battery very low", $"{percent}% left. Plug in your charger.", percent, Ui.Red, false, urgent: true);
        }
        else if (previous > Options.BatteryLowPercent && percent <= Options.BatteryLowPercent)
        {
            Announce("Low battery", $"{percent}% left", percent, Ui.Amber, false);
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
