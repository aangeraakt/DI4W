using System.Runtime.InteropServices;

namespace DynamicIsland.Settings;

/// <summary>A connected display, in physical pixels. <see cref="Work"/> excludes taskbars and other docked bars.</summary>
public sealed record Display(string Device, string Label, Displays.Rect Bounds, Displays.Rect Work, bool Primary);

/// <summary>
/// The displays the island can sit on (compiled into both apps: the island positions itself, the settings app
/// lists them). The setting <see cref="AppSettings.Monitor"/> is "" (main display), "Mouse" (follow the mouse)
/// or a device name such as \\.\DISPLAY2.
/// </summary>
public static class Displays
{
    public const string FollowMouse = "Mouse";

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    public static List<Display> All()
    {
        var result = new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info)) result.Add(Describe(info));
            return true;
        }, IntPtr.Zero);
        return result.OrderBy(d => Number(d.Device)).ToList();
    }

    /// <summary>The display for a <see cref="AppSettings.Monitor"/> value; falls back to the main display.</summary>
    public static Display Choose(string? setting)
    {
        var all = All();
        var primary = all.FirstOrDefault(d => d.Primary) ?? all.First();
        if (string.IsNullOrEmpty(setting)) return primary;
        if (setting == FollowMouse)
        {
            return GetCursorPos(out var p) ? all.FirstOrDefault(d => d.Bounds.Contains(p.X, p.Y)) ?? primary : primary;
        }
        return all.FirstOrDefault(d => d.Device.Equals(setting, StringComparison.OrdinalIgnoreCase)) ?? primary;
    }

    private static Display Describe(MONITORINFOEX info)
    {
        var primary = (info.dwFlags & 1) != 0; // MONITORINFOF_PRIMARY
        var model = ModelName(info.szDevice);
        var size = $"{info.rcMonitor.Width} × {info.rcMonitor.Height}";
        var label = L.F("Display {0}", Number(info.szDevice)) + (model is null ? "" : $" · {model}") + $" · {size}" + (primary ? " " + L.T("(main)") : "");
        return new Display(info.szDevice, label, info.rcMonitor, info.rcWork, primary);
    }

    // "\\.\DISPLAY2" -> 2
    private static int Number(string device) =>
        int.TryParse(new string(device.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()), out var n) ? n : 0;

    /// <summary>The monitor's model ("DELL U2720Q"), unless Windows only knows it as a generic monitor.</summary>
    private static string? ModelName(string device)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (!EnumDisplayDevices(device, 0, ref dd, 0)) return null;
        var name = dd.DeviceString.Trim();
        return name.Length == 0 || name.Contains("Generic", StringComparison.OrdinalIgnoreCase) || name.Contains("PnP", StringComparison.OrdinalIgnoreCase)
            ? null
            : name;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
