using Microsoft.Win32;

namespace DynamicIsland.Settings;

/// <summary>
/// Dark (black pill, the default) or light island. Shared with the settings app, which draws a preview with it.
/// </summary>
public static class IslandTheme
{
    public const string Dark = "";
    public const string Light = "Light";
    public const string System = "System";

    /// <summary>True when the setting means a light island right now ("System" follows Windows' taskbar color).</summary>
    public static bool IsLight(string? setting) => setting switch
    {
        Light => true,
        System => WindowsUsesLightTheme(),
        _ => false,
    };

    /// <summary>Windows' own light/dark choice for the taskbar and Start ("Choose your default Windows mode").</summary>
    public static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }
}
