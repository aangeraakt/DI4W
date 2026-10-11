using System.Diagnostics;
using DynamicIsland.Settings;
using Microsoft.UI.Dispatching;

namespace DynamicIsland.SettingsApp;

/// <summary>
/// The settings the pages edit. Every change is saved straight away (like Windows Settings, no Save button);
/// the island watches the file and applies it.
/// Only the changes made here are written, on top of the latest file, so values the island wrote while this
/// window was open (such as its install path after an update) are never overwritten with old ones.
/// </summary>
public static class SettingsStore
{
    public static AppSettings Current { get; } = AppSettings.Load();

    private static readonly List<Action<AppSettings>> Pending = new();
    private static DispatcherQueueTimer? _saveTimer;

    /// <summary>Applies a change and saves shortly after, so dragging a slider doesn't write the file 60 times a second.
    /// The change may run twice (here and on the latest file), so it must be safe to repeat.</summary>
    public static void Change(Action<AppSettings> change)
    {
        change(Current);
        Pending.Add(change);
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(250);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => Save();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes now (on close).</summary>
    public static void Flush()
    {
        _saveTimer?.Stop();
        Save();
    }

    private static void Save()
    {
        if (Pending.Count == 0) return;
        var changes = Pending.ToList();
        Pending.Clear();
        var saved = AppSettings.Update(latest => changes.ForEach(c => c(latest)));
        Current.CopyFrom(saved); // pick up what the island changed meanwhile
    }

    /// <summary>Path of DynamicIsland.exe: the one the island recorded, else the one next to (or above) this app.</summary>
    public static string? IslandExe
    {
        get
        {
            if (File.Exists(Current.IslandPath)) return Current.IslandPath;
            var dir = AppContext.BaseDirectory;
            foreach (var candidate in new[] { Path.Combine(dir, "..", "DynamicIsland.exe"), Path.Combine(dir, "DynamicIsland.exe") })
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }
    }

    public static bool IslandRunning => Process.GetProcessesByName("DynamicIsland").Length > 0;

    public static void StartIsland()
    {
        if (IslandExe is { } exe) Open(exe);
    }

    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DynamicIsland.Core.Log.Error($"Could not open {target}", ex);
        }
    }
}
