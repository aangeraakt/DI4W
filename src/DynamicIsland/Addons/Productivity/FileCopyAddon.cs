using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.Productivity;

/// <summary>
/// (18) Progress of File Explorer copies and moves. Explorer's progress dialog ("OperationStatusWindow")
/// puts the percentage in its title, in any language ("23% complete", "23% voltooid"), even when minimized.
/// </summary>
public sealed partial class FileCopyAddon : Addon
{
    private const double BarWidth = 336; // expanded width 380 minus margins

    private readonly TextBlock _percent = Ui.Text("", 14, FontWeights.SemiBold, Ui.Teal, tabular: true);
    private readonly TextBlock _title = Ui.Text("", 15, FontWeights.SemiBold, Ui.White);
    private readonly Border _fill = new() { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Teal, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _track;
    private readonly AddonActivity _activity;
    private DispatcherTimer? _poll;
    private int _lastPercent = -1;

    private static TimeSpan IdleInterval => Power.Interval(TimeSpan.FromSeconds(3), 2);

    public FileCopyAddon(AddonContext context) : base(context)
    {
        _track = new Border { Width = BarWidth, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Track, Child = _fill, Margin = new Thickness(0, 12, 0, 0) };
        var expanded = new StackPanel { Margin = new Thickness(22, 18, 22, 16), Children = { _title, _track } };
        _activity = new AddonActivity("File copy", 12,
            Ui.Glyph("", 15, Ui.Teal), _percent, Ui.Glyph("", 13, Ui.Teal), expanded,
            compactWidth: 220, expandedSize: new Size(380, 84));
        context.Manager.Register(_activity);
    }

    public override string Key => nameof(AddonSettings.FileCopy);
    public override string Name => "File copy";
    protected override bool IsEnabled(AddonSettings o) => o.FileCopy;

    protected override void Start()
    {
        _poll = new DispatcherTimer { Interval = IdleInterval };
        _poll.Tick += (_, _) => Check();
        _poll.Start();
    }

    protected override void Stop()
    {
        _poll?.Stop();
        _poll = null;
        _activity.Hide();
        _lastPercent = -1;
    }

    private void Check()
    {
        var (percent, title) = ReadProgress();
        if (percent is null)
        {
            // The dialog closed. Explorer doesn't say whether it finished or was cancelled; near the end means done.
            if (_lastPercent >= 90) AnnounceDone();
            _lastPercent = -1;
            _activity.Hide();
            if (_poll is not null) _poll.Interval = IdleInterval; // nothing copying: look less often
            return;
        }

        _lastPercent = percent.Value;
        _percent.Text = $"{percent}%";
        _title.Text = title;
        _fill.Width = Math.Max(6, BarWidth * percent.Value / 100.0);
        _activity.Show();
        if (_poll is not null) _poll.Interval = TimeSpan.FromSeconds(1); // copying: keep the percentage live
    }

    private void AnnounceDone() => Show(new IslandAlert
    {
        Content = Ui.AlertView(Ui.GlyphCircle("", Ui.Teal, 46, 20), L.T("Files copied"), L.T("File Explorer finished")),
        Size = new Size(340, 76),
        Duration = TimeSpan.FromSeconds(3),
    });

    /// <summary>The lowest percentage across open copy dialogs (the one that will finish last), with its title.</summary>
    private static (int? Percent, string Title) ReadProgress()
    {
        int? lowest = null;
        var title = "";
        var hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "OperationStatusWindow", null)) != IntPtr.Zero)
        {
            var text = new StringBuilder(256);
            GetWindowText(hwnd, text, text.Capacity);
            var match = PercentPattern().Match(text.ToString());
            if (!match.Success) continue;
            var value = int.Parse(match.Groups[1].Value);
            if (lowest is null || value < lowest)
            {
                lowest = value;
                title = text.ToString();
            }
        }
        return (lowest, title);
    }

    [GeneratedRegex(@"(\d{1,3})\s?%")]
    private static partial Regex PercentPattern();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
}
