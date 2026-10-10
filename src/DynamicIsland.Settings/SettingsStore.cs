using System.Diagnostics;
using DynamicIsland.Settings;
using Microsoft.UI.Dispatching;

namespace DynamicIsland.SettingsApp;

/// <summary>
/// The settings the pages edit. Every change is saved straight away (like Windows Settings, no Save button);
/// the island watches the file and applies it.
/// </summary>
public static class SettingsStore
{
    public static AppSettings Current { get; } = AppSettings.Load();

    private static DispatcherQueueTimer? _saveTimer;

    /// <summary>Applies a change and saves shortly after, so dragging a slider doesn't write the file 60 times a second.</summary>
    public static void Change(Action<AppSettings> change)
    {
        change(Current);
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(250);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => Current.Save();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes a pending change now (on close).</summary>
    public static void Flush()
    {
        if (_saveTimer is not { IsRunning: true }) return;
        _saveTimer.Stop();
        Current.Save();
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
