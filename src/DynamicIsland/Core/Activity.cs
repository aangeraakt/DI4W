using System.Windows;

namespace DynamicIsland.Core;

/// <summary>
/// A live activity shown in the island, the Windows equivalent of an iOS Live Activity.
/// It supplies the compact leading/trailing views, the small "minimal" bubble view used
/// when two activities run at once, and the expanded view.
/// Each view is created once and reused (a WPF element can only have one parent).
/// </summary>
public abstract class Activity
{
    private bool _isActive;

    public event Action? Changed;

    public abstract string Name { get; }

    /// <summary>Higher wins the main pill; the runner-up gets the bubble.</summary>
    public abstract int Priority { get; }

    public bool IsActive
    {
        get => _isActive;
        protected set
        {
            if (_isActive == value) return;
            _isActive = value;
            RaiseChanged();
        }
    }

    public abstract FrameworkElement Leading { get; }
    public abstract FrameworkElement Trailing { get; }
    public abstract FrameworkElement Bubble { get; }
    public abstract FrameworkElement Expanded { get; }

    public virtual double CompactWidth => 300;
    public virtual Size ExpandedSize => new(420, 160);

    protected void RaiseChanged() => Changed?.Invoke();
}

/// <summary>A transient pop-out (notification, "Claude is done", timer finished).</summary>
public sealed class IslandAlert
{
    public required FrameworkElement Content { get; init; }
    public Size Size { get; init; } = new(400, 76);
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(4.5);

    /// <summary>Never auto-dismisses; must be clicked / dismissed in code.</summary>
    public bool Sticky { get; set; }

    /// <summary>Stays until the mouse has hovered it once, then times out normally.</summary>
    public bool StickyUntilHover { get; set; }

    /// <summary>Shown right away, putting any current alert back in line (volume/brightness bars).</summary>
    public bool Urgent { get; init; }

    /// <summary>Still shown while Do Not Disturb is on (alarms, timers, calls).</summary>
    public bool BreaksThroughQuiet { get; init; }

    public Action? OnClick { get; init; }
    public Action? OnDismissed { get; init; }
}
