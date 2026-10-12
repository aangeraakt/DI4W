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
    /// <summary>%AppData%\DynamicIsland. Settable so tests can use a temporary folder.</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

    public static string FilePath => Path.Combine(Folder, "settings.json");

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

    /// <summary>Global shortcut that hides/shows the island, e.g. "Ctrl+Alt+I". Empty = no shortcut.</summary>
    public string HideHotkey { get; set; } = "Ctrl+Alt+I";

    /// <summary>Global shortcut that opens/closes the island's expanded view (keyboard and screen reader use).</summary>
    public string ExpandHotkey { get; set; } = "Ctrl+Alt+O";

    /// <summary>Hidden with the shortcut or "Pause island"; remembered so it stays hidden after a restart.</summary>
    public bool Paused { get; set; }

    /// <summary>"" = Windows' language, "en" = English, "nl" = Dutch.</summary>
    public string Language { get; set; } = "";

    /// <summary>Which display: "" = main display, "Mouse" = the one with the mouse, or a device name (\\.\DISPLAY2).</summary>
    public string Monitor { get; set; } = "";

    /// <summary>Look for new releases on GitHub now and then and announce them in the island.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>On/off switches and options for the add-ons (volume, battery, calendar, weather…).</summary>
    public AddonSettings Addons { get; set; } = new();

    /// <summary>Written by the island on startup so the settings app can find it (Start with Windows).</summary>
    public string? IslandPath { get; set; }

    // Relaxed escaping keeps the file readable for hand edits ("Ctrl+Alt+I" instead of "Ctrl+Alt+I").
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
            // Usually a typo from a hand edit. Keep the broken file so nothing is lost, then start fresh.
            Log.Error($"Failed to read settings; kept a copy as {Path.GetFileName(FilePath)}.bad and using defaults", ex);
            try
            {
                File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            }
            catch (Exception copyError)
            {
                Log.Error("Could not keep a copy of the broken settings", copyError);
            }
        }
        var s = new AppSettings();
        s.Save();
        return s;
    }

    /// <summary>
    /// Changes settings on disk without overwriting what the other app changed: under a lock shared by the
    /// island and the settings app, reads the latest file, applies <paramref name="change"/> and saves.
    /// Returns the saved settings.
    /// </summary>
    public static AppSettings Update(Action<AppSettings> change)
    {
        using var gate = new Mutex(false, "DynamicIsland4Windows.SettingsFile");
        var owned = false;
        try
        {
            owned = gate.WaitOne(TimeSpan.FromSeconds(3));
        }
        catch (AbandonedMutexException)
        {
            owned = true; // the other app quit while holding it; the file itself is always whole (atomic swap)
        }

        try
        {
            var latest = File.Exists(FilePath) ? TryLoad() ?? Load() : new AppSettings();
            change(latest);
            latest.Save();
            return latest;
        }
        finally
        {
            if (owned) gate.ReleaseMutex();
        }
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

/// <summary>
/// Add-on switches. Everything that shows up uninvited (system pop-ups) defaults to on; things that need
/// setup (calendar feeds, weather location) or cost resources (game overlay) say so in the settings app.
/// </summary>
public sealed class AddonSettings
{
    // System
    public bool Volume { get; set; } = true;
    public bool Brightness { get; set; } = true;
    public bool Battery { get; set; } = true;
    public int BatteryLowPercent { get; set; } = 20;
    public int BatteryCriticalPercent { get; set; } = 10;
    public bool Bluetooth { get; set; } = true;
    public bool UsbDrives { get; set; } = true;
    public bool Network { get; set; } = true;
    public bool PrivacyIndicators { get; set; } = true;
    public bool LockKeys { get; set; } = true;
    public bool PerformanceWarnings { get; set; } = true;

    // Productivity
    public bool Calendar { get; set; } = true;

    /// <summary>iCalendar (.ics) links, e.g. Outlook's or Google's "secret address" for a calendar.</summary>
    public List<string> CalendarFeeds { get; set; } = new();
    public int CalendarReminderMinutes { get; set; } = 5;

    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsBeforeLongBreak { get; set; } = 4;

    public bool Clipboard { get; set; } = true;
    public bool Screenshots { get; set; } = true;
    public bool Downloads { get; set; } = true;
    public bool FileCopy { get; set; } = true;

    // Apps and alerts
    public bool Calls { get; set; } = true;
    public bool DoNotDisturb { get; set; } = true;
    public bool WindowsUpdate { get; set; } = true;

    // Info
    public bool Weather { get; set; } = true;

    /// <summary>City name for the weather. Empty: use Windows location if allowed.</summary>
    public string WeatherLocation { get; set; } = "";
    public bool WeatherFahrenheit { get; set; } = false;
    public bool RainAlerts { get; set; } = true;

    /// <summary>Keep the island visible over fullscreen games and show GPU/CPU load. Off by default.</summary>
    public bool GameMode { get; set; } = false;

    // Developers
    /// <summary>Local API (127.0.0.1, same port as Claude Code) so scripts can show alerts and progress.</summary>
    public bool IslandApi { get; set; } = true;
}

/// <summary>
/// Chat apps with built-in support: matched on the name Windows shows for their notifications, with a brand
/// color for the fallback icon and the process names used to bring the app forward when an alert is clicked.
/// </summary>
public static class ChatApps
{
    public sealed record ChatApp(string Name, string Color, string[] Processes, bool HasCalls);

    public static readonly ChatApp[] All =
    {
        new("Discord", "#5865F2", new[] { "Discord", "DiscordPTB", "DiscordCanary" }, true),
        new("WhatsApp", "#25D366", new[] { "WhatsApp", "WhatsApp.Root" }, true),
        new("Telegram", "#2AABEE", new[] { "Telegram" }, true),
        new("Microsoft Teams", "#5B5FC7", new[] { "ms-teams", "Teams" }, true),
        new("Slack", "#4A154B", new[] { "slack" }, true),
        new("Signal", "#3A76F0", new[] { "Signal" }, true),
        new("Outlook", "#0F6CBD", new[] { "olk", "OUTLOOK" }, false),
        new("Mail", "#0F6CBD", new[] { "HxOutlook" }, false),
    };

    /// <summary>The preset whose name appears in a notification's app name ("Telegram Desktop" -> Telegram).</summary>
    public static ChatApp? Find(string appDisplayName) =>
        All.FirstOrDefault(a => appDisplayName.Contains(a.Name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Converts between "Ctrl+Alt+I" and the modifier flags / virtual-key code RegisterHotKey takes.</summary>
public static class Hotkey
{
    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;

    private static readonly (string Name, uint Flag)[] Modifiers =
    {
        ("Ctrl", MOD_CONTROL), ("Alt", MOD_ALT), ("Shift", MOD_SHIFT), ("Win", MOD_WIN),
    };

    private static readonly Dictionary<uint, string> KeyNames = new()
    {
        [0x20] = "Space", [0x0D] = "Enter", [0x09] = "Tab", [0x08] = "Backspace", [0x2E] = "Delete", [0x2D] = "Insert",
        [0x24] = "Home", [0x23] = "End", [0x21] = "PageUp", [0x22] = "PageDown",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x13] = "Pause",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    public static bool TryParse(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var mod = Modifiers.FirstOrDefault(m => m.Name.Equals(raw, StringComparison.OrdinalIgnoreCase));
            if (mod.Flag != 0)
            {
                modifiers |= mod.Flag;
                continue;
            }
            if (key != 0) return false; // two non-modifier keys
            key = ParseKey(raw);
            if (key == 0) return false;
        }
        return key != 0 && modifiers != 0;
    }

    public static string Format(uint modifiers, uint key)
    {
        var parts = Modifiers.Where(m => (modifiers & m.Flag) != 0).Select(m => m.Name).ToList();
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    /// <summary>Display name of a virtual-key code (unknown keys show as hex, which TryParse reads back).</summary>
    public static string KeyName(uint key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(), // 0-9, A-Z
        >= 0x60 and <= 0x69 => $"Num{key - 0x60}",
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        _ => KeyNames.TryGetValue(key, out var name) ? name : $"0x{key:X2}",
    };

    private static uint ParseKey(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length > 1 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24) return (uint)(0x6F + f);
        if (name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[3..], out var n) && n is >= 0 and <= 9) return (uint)(0x60 + n);
        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex)) return hex;
        foreach (var (vk, keyName) in KeyNames)
        {
            if (keyName.Equals(name, StringComparison.OrdinalIgnoreCase)) return vk;
        }
        return 0;
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
