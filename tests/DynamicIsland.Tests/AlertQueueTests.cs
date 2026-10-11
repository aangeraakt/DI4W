using System.Windows.Controls;
using DynamicIsland.Core;
using Xunit;

namespace DynamicIsland.Tests;

public class AlertQueueTests
{
    private static IslandAlert Alert(bool urgent = false, bool breaksThrough = false) =>
        new() { Content = new Border(), Urgent = urgent, BreaksThroughQuiet = breaksThrough };

    // WPF elements and timers need an STA thread.
    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    [Fact]
    public void Shows_alerts_one_at_a_time_in_order() => OnSta(() =>
    {
        var manager = new ActivityManager();
        var first = Alert();
        var second = Alert();
        manager.ShowAlert(first);
        manager.ShowAlert(second);

        Assert.Same(first, manager.CurrentAlert);
        manager.DismissAlert();
        Assert.Same(second, manager.CurrentAlert);
        manager.DismissAlert();
        Assert.Null(manager.CurrentAlert);
    });

    [Fact]
    public void Urgent_alert_jumps_ahead_and_the_interrupted_one_comes_back() => OnSta(() =>
    {
        var manager = new ActivityManager();
        var message = Alert();
        var queued = Alert();
        var volume = Alert(urgent: true);
        manager.ShowAlert(message);
        manager.ShowAlert(queued);
        manager.ShowAlert(volume);

        Assert.Same(volume, manager.CurrentAlert);
        manager.DismissAlert();
        Assert.Same(message, manager.CurrentAlert); // resumed before the rest of the line
        manager.DismissAlert();
        Assert.Same(queued, manager.CurrentAlert);
    });

    [Fact]
    public void Showing_the_current_alert_again_does_not_queue_it_twice() => OnSta(() =>
    {
        var manager = new ActivityManager();
        var hud = Alert(urgent: true);
        manager.ShowAlert(hud);
        manager.ShowAlert(hud);
        manager.ShowAlert(hud);

        manager.DismissAlert();
        Assert.Null(manager.CurrentAlert);
    });

    [Fact]
    public void Do_not_disturb_only_lets_through_alarms() => OnSta(() =>
    {
        var manager = new ActivityManager { Quiet = true };
        manager.ShowAlert(Alert());
        Assert.Null(manager.CurrentAlert);

        var alarm = Alert(breaksThrough: true);
        manager.ShowAlert(alarm);
        Assert.Same(alarm, manager.CurrentAlert);
    });

    [Fact]
    public void Dismissing_a_queued_alert_removes_it_from_the_line() => OnSta(() =>
    {
        var manager = new ActivityManager();
        var first = Alert();
        var second = Alert();
        var third = Alert();
        manager.ShowAlert(first);
        manager.ShowAlert(second);
        manager.ShowAlert(third);

        manager.DismissAlert(second);
        manager.DismissAlert();
        Assert.Same(third, manager.CurrentAlert);
    });
}
