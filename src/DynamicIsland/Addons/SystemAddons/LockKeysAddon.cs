using System.Windows.Threading;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>
/// (8) "Caps Lock on/off" and "Num Lock on/off". Polls the toggle state, which Windows keeps current for
/// every process; cheaper and safer than a global keyboard hook (which can slow typing system-wide).
/// </summary>
public sealed class LockKeysAddon : Addon
{
    private const int VK_CAPITAL = 0x14, VK_NUMLOCK = 0x90;

    private readonly HudView _hud = new("", Ui.White);
    private DispatcherTimer? _poll;
    private bool _caps, _num;

    public LockKeysAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.LockKeys);
    public override string Name => "Lock keys";
    protected override bool IsEnabled(AddonSettings o) => o.LockKeys;

    protected override void Start()
    {
        _caps = IsOn(VK_CAPITAL);
        _num = IsOn(VK_NUMLOCK);
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _poll.Tick += (_, _) => Check();
        _poll.Start();
    }

    protected override void Stop()
    {
        _poll?.Stop();
        _poll = null;
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
        _hud.Set(null, $"{key} {(on ? "on" : "off")}");
        Show(_hud.Alert);
    }

    private static bool IsOn(int vk) => (Interop.Win32.GetKeyState(vk) & 1) != 0;
}
