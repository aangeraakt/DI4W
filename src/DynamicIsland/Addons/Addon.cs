using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;

namespace DynamicIsland.Addons;

/// <summary>What an add-on can reach: settings, the alert/activity manager and the island window.</summary>
public sealed class AddonContext
{
    private FrameworkElement? _idleLeading;
    private FrameworkElement? _idleTrailing;

    public AddonContext(AppSettings settings, ActivityManager manager, IslandWindow window, Dispatcher dispatcher)
    {
        Settings = settings;
        Manager = manager;
        Window = window;
        Dispatcher = dispatcher;
    }

    public AppSettings Settings { get; }
    public AddonSettings Options => Settings.Addons;
    public ActivityManager Manager { get; }
    public IslandWindow Window { get; }
    public Dispatcher Dispatcher { get; }

    /// <summary>Left slot of the empty pill (Do Not Disturb moon).</summary>
    public void SetIdleLeading(FrameworkElement? view)
    {
        _idleLeading = view;
        Window.SetIdleContent(_idleLeading, _idleTrailing);
    }

    /// <summary>Right slot of the empty pill (weather).</summary>
    public void SetIdleTrailing(FrameworkElement? view)
    {
        _idleTrailing = view;
        Window.SetIdleContent(_idleLeading, _idleTrailing);
    }

    /// <summary>Runs on the UI thread; for callbacks from WinRT/WMI/file watchers.</summary>
    public void Ui(Action action) => Dispatcher.BeginInvoke(action);
}

/// <summary>
/// An optional feature with its own switch in the settings app. The host starts it when its switch is on
/// and stops it when turned off. A failing add-on is logged and left off; it never takes the island down.
/// </summary>
public abstract class Addon
{
    protected Addon(AddonContext context) => Context = context;

    protected AddonContext Context { get; }
    protected AddonSettings Options => Context.Options;

    public abstract string Name { get; }

    /// <summary>The add-on's switch in <see cref="AddonSettings"/>; the settings app shows the status under it.</summary>
    public abstract string Key { get; }

    public bool Running { get; private set; }

    /// <summary>What the settings app shows under this add-on ("Set a city to show the weather").</summary>
    public string? StatusMessage { get; private set; }
    public bool StatusIsProblem { get; private set; }
    public event Action? StatusChanged;

    /// <summary>Message for the settings app; <paramref name="problem"/> when the user needs to do something.</summary>
    protected void SetStatus(string? message, bool problem = true)
    {
        if (message == StatusMessage && problem == StatusIsProblem) return;
        StatusMessage = message;
        StatusIsProblem = problem && message is not null;
        StatusChanged?.Invoke();
    }

    protected abstract bool IsEnabled(AddonSettings options);
    protected abstract void Start();
    protected abstract void Stop();

    /// <summary>The settings changed while this add-on was running (e.g. a new weather city).</summary>
    protected virtual void OnOptionsChanged() { }

    /// <summary>
    /// Shows a sample of what this add-on pops up, with made-up data (the settings app's Preview button).
    /// Works whether or not the add-on is on; buttons in a preview never do anything real.
    /// </summary>
    public abstract void Preview();

    /// <summary>Starts or stops the add-on to match its switch.</summary>
    public void Sync()
    {
        var wanted = IsEnabled(Options);
        if (wanted == Running)
        {
            if (Running) TryCall(OnOptionsChanged);
            return;
        }
        try
        {
            if (!wanted) SetStatus(null);
            if (wanted) Start();
            else Stop();
            Running = wanted;
            Log.Info($"Add-on {Name} {(wanted ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            Log.Error($"Add-on {Name} failed to {(wanted ? "start" : "stop")}", ex);
            if (wanted) TryStop();
            Running = false;
            if (wanted) SetStatus(L.F("Couldn't start: {0}", ex.Message));
        }
        StatusChanged?.Invoke(); // Running changed
    }

    public void Shutdown()
    {
        if (Running) TryStop();
        Running = false;
    }

    private void TryStop() => TryCall(Stop);

    private void TryCall(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Add-on {Name} failed", ex);
        }
    }

    /// <summary>Shows a pop-up, unless this add-on's quiet times apply right now (then it only goes to Recent).
    /// Previews from the settings app always show.</summary>
    protected void Show(IslandAlert alert)
    {
        var manager = Context.Manager;
        if (manager.Recording && Options.Quiet.TryGetValue(Key, out var quiet) && !quiet.IsEmpty
            && quiet.IsQuiet(DateTime.Now.TimeOfDay, quiet.Apps.Count > 0 ? Interop.Win32.ForegroundProcessName() : null))
        {
            manager.Miss(alert);
            return;
        }
        manager.ShowAlert(alert);
    }
}
