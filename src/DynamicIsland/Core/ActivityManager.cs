using System.Windows.Threading;

namespace DynamicIsland.Core;

public sealed class ActivityManager
{
    private readonly List<Activity> _activities = new();
    private readonly Queue<IslandAlert> _alerts = new();
    private readonly DispatcherTimer _alertTimer;
    private TimeSpan _alertRemaining;
    private Activity? _pinned;

    public event Action? Changed;

    public IslandAlert? CurrentAlert { get; private set; }

    /// <summary>Set by the window while the mouse is over the island, so alerts don't vanish under the cursor.</summary>
    public bool HoldAlert { get; set; }

    public ActivityManager()
    {
        _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _alertTimer.Tick += (_, _) =>
        {
            var a = CurrentAlert;
            if (a is null) { _alertTimer.Stop(); return; }
            if (a.Sticky || a.StickyUntilHover || HoldAlert) return;
            _alertRemaining -= _alertTimer.Interval;
            if (_alertRemaining <= TimeSpan.Zero) DismissAlert();
        };
    }

    public void Register(Activity activity)
    {
        _activities.Add(activity);
        activity.Changed += () =>
        {
            if (!activity.IsActive && _pinned == activity) _pinned = null;
            Changed?.Invoke();
        };
    }

    /// <summary>Active activities, most important first. A user-pinned activity jumps to the front.</summary>
    public IReadOnlyList<Activity> Active =>
        _activities.Where(a => a.IsActive)
                   .OrderByDescending(a => a == _pinned ? int.MaxValue : a.Priority)
                   .ToList();

    /// <summary>Called when the user clicks the bubble: swap it into the main pill.</summary>
    public void Pin(Activity activity)
    {
        _pinned = activity;
        Changed?.Invoke();
    }

    public void ShowAlert(IslandAlert alert)
    {
        if (CurrentAlert is null)
        {
            Present(alert);
        }
        else
        {
            _alerts.Enqueue(alert);
        }
    }

    public void DismissAlert(IslandAlert? specific = null)
    {
        if (specific is not null && specific != CurrentAlert)
        {
            // Remove it from the queue if it hasn't been shown yet.
            var rest = _alerts.Where(a => a != specific).ToList();
            _alerts.Clear();
            foreach (var a in rest) _alerts.Enqueue(a);
            specific.OnDismissed?.Invoke();
            return;
        }

        var old = CurrentAlert;
        CurrentAlert = null;
        _alertTimer.Stop();
        old?.OnDismissed?.Invoke();

        if (_alerts.Count > 0)
        {
            Present(_alerts.Dequeue());
        }
        else
        {
            Changed?.Invoke();
        }
    }

    private void Present(IslandAlert alert)
    {
        CurrentAlert = alert;
        _alertRemaining = alert.Duration;
        _alertTimer.Start();
        Changed?.Invoke();
    }

    /// <summary>Called by the window when the mouse touches the current alert.</summary>
    public void AlertHovered()
    {
        if (CurrentAlert is { StickyUntilHover: true } a)
        {
            a.StickyUntilHover = false;
            _alertRemaining = TimeSpan.FromSeconds(2.5);
        }
    }
}
