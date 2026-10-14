using System.Windows.Threading;

namespace DynamicIsland.Core;

public sealed class ActivityManager
{
    private readonly List<Activity> _activities = new();
    private readonly Queue<IslandAlert> _alerts = new();
    private readonly DispatcherTimer _alertTimer;
    private TimeSpan _alertRemaining;
    private readonly System.Diagnostics.Stopwatch _alertClock = new();
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
            // Count real time: timer ticks can arrive late while the UI thread is busy, and counting
            // ticks would then stretch a 4 s alert to 10 s or more.
            var elapsed = _alertClock.Elapsed;
            _alertClock.Restart();
            var a = CurrentAlert;
            if (a is null) { _alertTimer.Stop(); return; }
            if (a.Sticky || a.StickyUntilHover || HoldAlert) return;
            _alertRemaining -= elapsed;
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
                   .OrderByDescending(Rank)
                   .ToList();

    private IReadOnlyList<string> _order = Array.Empty<string>();

    /// <summary>The user's order of activity kinds, most important first (Settings → General); empty = built-in.</summary>
    public IReadOnlyList<string> Order
    {
        get => _order;
        set
        {
            if (_order.SequenceEqual(value)) return;
            _order = value.ToList();
            Changed?.Invoke();
        }
    }

    private int Rank(Activity a)
    {
        if (a == _pinned) return int.MaxValue;
        var index = IndexOf(_order, a.Kind);
        // Ordered kinds outrank everything else; kinds the list doesn't know yet keep their own priority.
        return index >= 0 ? 100_000 - index : a.Priority;
    }

    private static int IndexOf(IReadOnlyList<string> list, string kind)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], kind, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    // ───────────────────────────── history ─────────────────────────────

    /// <summary>An alert that was shown (or held back by Do Not Disturb), as text for the Recent list.</summary>
    public sealed record PastAlert(DateTime Time, string Title, string Detail, Action? OnClick, bool Missed, IslandAlert Source);

    private const int HistorySize = 10;
    private readonly LinkedList<PastAlert> _history = new();
    private readonly HashSet<IslandAlert> _samples = new();

    /// <summary>The latest alerts, newest first.</summary>
    public IReadOnlyList<PastAlert> History => _history.ToList();

    /// <summary>Off while the settings app's previews run, so samples don't end up in the history.</summary>
    public bool Recording { get; set; } = true;

    /// <summary>An alert that was held back (an add-on's quiet times): only listed in Recent.</summary>
    public void Miss(IslandAlert alert) => Remember(alert, missed: true);

    private void Remember(IslandAlert alert, bool missed)
    {
        // Volume and brightness bars are momentary feedback, not news.
        if (alert.Urgent || _samples.Contains(alert) || _history.Any(p => p.Source == alert)) return; // (or shown again after an interruption)
        var texts = UI.ViewText.Texts(alert.Content);
        if (texts.Count == 0) return;
        _history.AddFirst(new PastAlert(DateTime.Now, texts[0], string.Join(" · ", texts.Skip(1)), alert.OnClick, missed, alert));
        while (_history.Count > HistorySize) _history.RemoveLast();
    }

    /// <summary>Called when the user clicks the bubble: swap it into the main pill.</summary>
    public void Pin(Activity activity)
    {
        _pinned = activity;
        Changed?.Invoke();
    }

    /// <summary>Do Not Disturb: only alerts with <see cref="IslandAlert.BreaksThroughQuiet"/> are shown.</summary>
    public bool Quiet { get; set; }

    public void ShowAlert(IslandAlert alert)
    {
        if (!Recording) _samples.Add(alert);
        if (Quiet && !alert.BreaksThroughQuiet)
        {
            Remember(alert, missed: true);
            return;
        }

        // Showing the alert that's already up (a HUD whose view was updated) just restarts its timer.
        if (alert == CurrentAlert)
        {
            _alertRemaining = alert.Duration;
            Changed?.Invoke();
            return;
        }
        if (_alerts.Contains(alert)) return;

        if (CurrentAlert is null)
        {
            Present(alert);
        }
        else if (alert.Urgent)
        {
            // Put the interrupted alert back at the front, unless it was a HUD itself (those are momentary).
            var waiting = _alerts.ToList();
            _alerts.Clear();
            if (!CurrentAlert.Urgent) _alerts.Enqueue(CurrentAlert);
            foreach (var a in waiting) _alerts.Enqueue(a);
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
        Remember(alert, missed: false);
        CurrentAlert = alert;
        _alertRemaining = alert.Duration;
        _alertClock.Restart();
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
