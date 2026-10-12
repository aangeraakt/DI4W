using System.Runtime.InteropServices;
using System.Windows.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>
/// (8) "Caps Lock on/off" and "Num Lock on/off". Windows reports key presses to the island through raw input
/// (no global hook, which can slow typing system-wide); only the Caps Lock and Num Lock keys are looked at,
/// and only to then read their on/off state. Nothing else about typing is used or stored.
/// </summary>
public sealed class LockKeysAddon : Addon
{
    private const int VK_CAPITAL = 0x14, VK_NUMLOCK = 0x90;
    private const int WM_INPUT = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x00000100, RIDEV_REMOVE = 0x00000001, RID_INPUT = 0x10000003;
    private const ushort RI_KEY_BREAK = 1;

    private readonly HudView _hud = new("", Ui.White);
    private bool _caps, _num;

    public LockKeysAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.LockKeys);
    public override string Name => "Lock keys";
    protected override bool IsEnabled(AddonSettings o) => o.LockKeys;

    protected override void Start()
    {
        _caps = IsOn(VK_CAPITAL);
        _num = IsOn(VK_NUMLOCK);
        var hwnd = new WindowInteropHelper(Context.Window).EnsureHandle();
        // Keyboard (usage page 1, usage 6), delivered even while another app has focus.
        var device = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd };
        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            throw new InvalidOperationException($"Keyboard input registration refused ({Marshal.GetLastWin32Error()})");
        }
        Context.Window.MessageReceived += OnMessage;
    }

    protected override void Stop()
    {
        Context.Window.MessageReceived -= OnMessage;
        var device = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_INPUT || ReadKey(lParam) is not { } key) return;
        if (!key.Released || (key.VirtualKey != VK_CAPITAL && key.VirtualKey != VK_NUMLOCK)) return;
        // Windows flips the toggle state as the key goes down; read it once this message has been handled.
        Context.Ui(Check);
    }

    private void Check()
    {
        var caps = IsOn(VK_CAPITAL);
        var num = IsOn(VK_NUMLOCK);
        if (caps != _caps) Announce("Caps Lock", caps);
        else if (num != _num) Announce("Num Lock", num);
        _caps = caps;
        _num = num;
    }

    private void Announce(string key, bool on)
    {
        _hud.Set(null, on ? L.F("{0} on", key) : L.F("{0} off", key));
        Show(_hud.Alert);
    }

    private static bool IsOn(int vk) => (Interop.Win32.GetKeyState(vk) & 1) != 0;

    /// <summary>The virtual key and whether it was released, from a WM_INPUT keyboard message.</summary>
    private static (int VirtualKey, bool Released)? ReadKey(IntPtr rawInput)
    {
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(rawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 1024) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInput, RID_INPUT, buffer, ref size, headerSize) != size) return null;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != 1) return null; // RIM_TYPEKEYBOARD
            var keyboard = Marshal.PtrToStructure<RAWKEYBOARD>(buffer + (int)headerSize);
            return (keyboard.VKey, (keyboard.Flags & RI_KEY_BREAK) != 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage, usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType, dwSize;
        public IntPtr hDevice, wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode, Flags, Reserved, VKey;
        public uint Message, ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
}
