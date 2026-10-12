using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using static DynamicIsland.Interop.Win32;

namespace DynamicIsland.Addons.Alerts;

/// <summary>
/// (21) Follows Windows' Do Not Disturb / Focus: the island goes quiet (only alarms, timers and urgent alerts)
/// and shows a moon in the empty pill.
/// </summary>
public sealed class DoNotDisturbAddon : Addon
{
    // Windows notification state "quiet hours profile" (what Do Not Disturb / Focus assist sets): 0 = off.
    private const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;

    private readonly System.Windows.Controls.TextBlock _moon = Ui.Glyph("", 14, Ui.Indigo);
    private DispatcherTimer? _poll;
    private bool _quiet;

    public DoNotDisturbAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.DoNotDisturb);
    public override string Name => "Do Not Disturb";
    protected override bool IsEnabled(AddonSettings o) => o.DoNotDisturb;

    /// <summary>The real thing is just the moon in the empty island; the sample says what it means.</summary>
    public override void Preview()
    {
        var hud = new HudView(_moon.Text, Ui.Indigo);
        hud.Set(null, L.T("Do Not Disturb is on"));
        Show(hud.Alert);
    }

    protected override void Start()
    {
        _moon.ToolTip = L.T("Do Not Disturb is on");
        _poll = new DispatcherTimer { Interval = Power.Interval(TimeSpan.FromSeconds(3)) };
        _poll.Tick += (_, _) => Apply(IsQuiet());
        _poll.Start();
        Apply(IsQuiet());
    }

    protected override void Stop()
    {
        _poll?.Stop();
        _poll = null;
        Apply(false);
    }

    private void Apply(bool quiet)
    {
        if (quiet == _quiet) return;
        _quiet = quiet;
        Context.Manager.Quiet = quiet;
        Context.SetIdleLeading(quiet ? _moon : null);
    }

    private static bool IsQuiet()
    {
        if (SHQueryUserNotificationState(out var state) == 0 && state == QUERY_USER_NOTIFICATION_STATE.QUNS_QUIET_TIME) return true;
        try
        {
            var name = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
            uint size = 4;
            var status = NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, out var profile, ref size);
            return status == 0 && size >= 4 && profile != 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope,
                                                  out uint changeStamp, out int buffer, ref uint bufferSize);
}
