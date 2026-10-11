using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Microsoft.Win32;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>
/// (7) Orange dot while an app uses the microphone, green while one uses the camera, next to the pill.
/// Windows records "in use right now" per app in the capability consent store; this reads that.
/// </summary>
public sealed class PrivacyAddon : Addon
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private readonly Ellipse _micDot = Dot(Ui.Orange);
    private readonly Ellipse _camDot = Dot(Ui.Green);
    private DispatcherTimer? _poll;
    private HashSet<string> _mic = new(), _cam = new();

    public PrivacyAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.PrivacyIndicators);
    public override string Name => "Privacy indicators";
    protected override bool IsEnabled(AddonSettings o) => o.PrivacyIndicators;

    protected override void Start()
    {
        _mic = InUse("microphone");
        _cam = InUse("webcam");
        Context.Window.IndicatorPanel.Children.Add(_camDot);
        Context.Window.IndicatorPanel.Children.Add(_micDot);
        Render();
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _poll.Tick += (_, _) => Check();
        _poll.Start();
    }

    protected override void Stop()
    {
        _poll?.Stop();
        _poll = null;
        Context.Window.IndicatorPanel.Children.Remove(_camDot);
        Context.Window.IndicatorPanel.Children.Remove(_micDot);
    }

    private void Check()
    {
        var mic = InUse("microphone");
        var cam = InUse("webcam");
        var newMic = mic.Except(_mic).FirstOrDefault();
        var newCam = cam.Except(_cam).FirstOrDefault();
        _mic = mic;
        _cam = cam;
        Render();

        if (newCam is not null) Announce($"{newCam} is using the camera", "", Ui.Green);
        else if (newMic is not null) Announce($"{newMic} is using the microphone", "", Ui.Orange);
    }

    private void Render()
    {
        _micDot.Visibility = _mic.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _camDot.Visibility = _cam.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _micDot.ToolTip = _mic.Count > 0 ? "Microphone: " + string.Join(", ", _mic) : null;
        _camDot.ToolTip = _cam.Count > 0 ? "Camera: " + string.Join(", ", _cam) : null;
    }

    private void Announce(string text, string glyph, Brush color)
    {
        var hud = new HudView(glyph, color);
        hud.Set(null, text);
        Show(hud.Alert);
    }

    /// <summary>Apps currently using the capability: a start time without a stop time.</summary>
    private static HashSet<string> InUse(string capability)
    {
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey($@"{Store}\{capability}");
            if (root is null) return apps;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key is null) continue;
                if (name == "NonPackaged")
                {
                    foreach (var exe in key.GetSubKeyNames())
                    {
                        using var sub = key.OpenSubKey(exe);
                        if (sub is not null && Active(sub)) apps.Add(DesktopAppName(exe));
                    }
                }
                else if (Active(key))
                {
                    apps.Add(PackagedAppName(name));
                }
            }
        }
        catch
        {
            // Registry layout differs on older builds; just show nothing.
        }
        return apps;
    }

    private static bool Active(RegistryKey key) =>
        key.GetValue("LastUsedTimeStop") is long stop && stop == 0 && key.GetValue("LastUsedTimeStart") is long start && start != 0;

    // "C:#Program Files#Discord#app-1.0#Discord.exe" -> its file description, else "Discord".
    private static string DesktopAppName(string encoded)
    {
        var path = encoded.Replace('#', '\\');
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description) return description;
        }
        catch
        {
            // fall through to the file name
        }
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    // "Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "WindowsCamera"
    private static string PackagedAppName(string family)
    {
        var name = family.Split('_')[0];
        return name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
    }

    private static Ellipse Dot(Brush color) => new()
    {
        Width = 8, Height = 8, Fill = color, Margin = new Thickness(0, 0, 6, 0),
        Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center,
    };
}
