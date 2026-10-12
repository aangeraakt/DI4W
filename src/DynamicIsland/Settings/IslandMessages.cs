using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DynamicIsland.Settings;

/// <summary>
/// Small commands from the settings app to the running island (shared by both), sent as WM_COPYDATA to the
/// island's window. Only "preview:&lt;add-on&gt;" for now, which shows a sample pop-up.
/// </summary>
public static class IslandMessages
{
    /// <summary>Marks our messages, so a stray WM_COPYDATA from another program is ignored ("DI4W").</summary>
    public const int Tag = 0x44493457;

    public const string PreviewPrefix = "preview:";

    private const string IslandTitle = "Dynamic Island";
    private const int WM_COPYDATA = 0x004A;

    /// <summary>Sends the command; false if the island isn't running (or didn't answer within a second).</summary>
    public static bool Send(string text)
    {
        var window = FindIsland();
        if (window == IntPtr.Zero) return false;

        var bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var data = new COPYDATASTRUCT { dwData = (IntPtr)Tag, cbData = bytes.Length, lpData = buffer };
            return SendMessageTimeout(window, WM_COPYDATA, IntPtr.Zero, ref data, SMTO_ABORTIFHUNG, 1000, out _) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The text of one of our WM_COPYDATA messages, or null for anything else.</summary>
    public static string? Read(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return null;
        var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        if (data.dwData != (IntPtr)Tag || data.lpData == IntPtr.Zero || data.cbData is <= 0 or > 4096) return null;
        return Marshal.PtrToStringUni(data.lpData, data.cbData / 2).TrimEnd('\0');
    }

    /// <summary>The island's own window: titled "Dynamic Island" and owned by a DynamicIsland process.</summary>
    private static IntPtr FindIsland()
    {
        var islands = Process.GetProcessesByName("DynamicIsland").Select(p => (uint)p.Id).ToHashSet();
        if (islands.Count == 0) return IntPtr.Zero;
        var window = IntPtr.Zero;
        while ((window = FindWindowEx(IntPtr.Zero, window, null, IslandTitle)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(window, out var pid);
            if (islands.Contains(pid)) return window;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    private const uint SMTO_ABORTIFHUNG = 0x2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, int msg, IntPtr wParam, ref COPYDATASTRUCT data,
                                                    uint flags, uint timeout, out IntPtr result);
}
