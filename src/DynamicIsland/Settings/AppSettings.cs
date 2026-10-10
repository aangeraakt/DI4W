using System.IO;
using System.Text.Json;
using DynamicIsland.Core;
using Microsoft.Win32;

namespace DynamicIsland.Settings;

/// <summary>
/// Settings shared by the island and the separate settings app (this file is compiled into both).
/// The settings app writes settings.json; the island watches it and applies changes live.
/// </summary>
public sealed class AppSettings
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    /// <summary>Distance from the top of the screen to the island, in DIPs.</summary>
    public double TopOffset { get; set; } = 8;

    /// <summary>Overall size multiplier (1.0 = default).</summary>
    public double Scale { get; set; } = 1.0;

    public bool HideInFullscreen { get; set; } = true;

    public bool MediaEnabled { get; set; } = true;

    /// <summary>When false only Spotify is shown; when true any app using Windows media controls is.</summary>
    public bool MediaAnyPlayer { get; set; } = false;

    public int MediaHideAfterPauseSeconds { get; set; } = 10;

    public bool ClaudeEnabled { get; set; } = true;
    public int ClaudePort { get; set; } = 47823;

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>App display names whose Windows notifications pop out of the island.</summary>
    public List<string> NotificationApps { get; set; } = new() { "Discord" };

    public bool Sounds { get; set; } = true;

    /// <summary>Written by the island on startup so the settings app can find it (Start with Windows).</summary>
    public string? IslandPath { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read settings, using defaults", ex);
        }
        var s = new AppSettings();
        s.Save();
        return s;
    }

    /// <summary>Reads the file without falling back to defaults; null if it is missing, locked or half-written.</summary>
    public static AppSettings? TryLoad()
    {
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<AppSettings>(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // Write to a temp file and swap it in, so the other app never reads a half-written file.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    /// <summary>Copies every value from <paramref name="other"/> into this instance, so existing references see them.</summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var p in typeof(AppSettings).GetProperties())
        {
            if (p.CanWrite) p.SetValue(this, p.GetValue(other));
        }
    }
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIsland";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <param name="islandExe">Path of DynamicIsland.exe (not the settings app).</param>
    public static void Set(bool enabled, string islandExe)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{islandExe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
