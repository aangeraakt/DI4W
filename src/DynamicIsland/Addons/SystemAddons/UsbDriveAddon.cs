using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>(5) USB stick / external drive plugged in or removed, with its name and size. Click opens it.</summary>
public sealed class UsbDriveAddon : Addon
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 2;
    private const ushort DBTF_NET = 2;

    private readonly Dictionary<char, string> _names = new();

    public UsbDriveAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.UsbDrives);
    public override string Name => "USB drives";
    protected override bool IsEnabled(AddonSettings o) => o.UsbDrives;

    public override void Preview() =>
        Announce(L.T("USB drive"), L.F("Connected ({0}:)", 'E') + $" · {FormatSize(64L * 1024 * 1024 * 1024)}", connected: true, null);

    protected override void Start()
    {
        foreach (var d in DriveInfo.GetDrives()) Remember(d);
        Context.Window.MessageReceived += OnMessage;
    }

    protected override void Stop() => Context.Window.MessageReceived -= OnMessage;

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_DEVICECHANGE || lParam == IntPtr.Zero) return;
        var evt = wParam.ToInt32();
        if (evt != DBT_DEVICEARRIVAL && evt != DBT_DEVICEREMOVECOMPLETE) return;
        if (Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_VOLUME) return;

        var unitMask = (uint)Marshal.ReadInt32(lParam, 12);
        var flags = (ushort)Marshal.ReadInt16(lParam, 16);
        if ((flags & DBTF_NET) != 0) return; // mapped network drives

        for (var i = 0; i < 26; i++)
        {
            if ((unitMask & (1u << i)) == 0) continue;
            var letter = (char)('A' + i);
            // Let Windows finish mounting before reading the label and size.
            if (evt == DBT_DEVICEARRIVAL) Context.Dispatcher.BeginInvoke(() => Arrived(letter), System.Windows.Threading.DispatcherPriority.Background);
            else Removed(letter);
        }
    }

    private async void Arrived(char letter)
    {
        await Task.Delay(700);
        try
        {
            var drive = new DriveInfo(letter + ":\\");
            if (!drive.IsReady) return;
            var name = Remember(drive);
            var size = drive.TotalSize > 0 ? $" · {FormatSize(drive.TotalSize)}" : "";
            Announce(name, L.F("Connected ({0}:)", letter) + size, connected: true, () => Process.Start("explorer.exe", drive.RootDirectory.FullName));
        }
        catch (Exception ex)
        {
            Log.Info($"Drive {letter}: not readable yet: {ex.Message}");
        }
    }

    private void Removed(char letter)
    {
        var name = _names.TryGetValue(letter, out var n) ? n : L.F("Drive {0}:", letter);
        _names.Remove(letter);
        Announce(name, L.T("Removed"), connected: false, null);
    }

    private string Remember(DriveInfo d)
    {
        var letter = d.Name[0];
        try
        {
            var name = d.IsReady && !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : L.F("Drive {0}:", letter);
            _names[letter] = name;
            return name;
        }
        catch
        {
            return L.F("Drive {0}:", letter);
        }
    }

    private void Announce(string name, string subtitle, bool connected, Action? onClick) => Show(new IslandAlert
    {
        Content = Ui.AlertView(Ui.GlyphCircle("", connected ? Ui.Teal : Ui.ButtonBg, 46, 20), name, subtitle),
        Size = new Size(360, 76),
        Duration = TimeSpan.FromSeconds(3.5),
        OnClick = onClick,
    });

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
