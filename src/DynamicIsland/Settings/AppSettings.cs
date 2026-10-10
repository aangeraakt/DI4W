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

    /// <summary>Global shortcut that hides/shows the island, e.g. "Ctrl+Alt+I". Empty = no shortcut.</summary>
    public string HideHotkey { get; set; } = "Ctrl+Alt+I";

    /// <summary>Look for new releases on GitHub now and then and announce them in the island.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

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
