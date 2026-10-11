using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.UI;

namespace DynamicIsland.Addons;

/// <summary>
/// The slim iOS-style bar for volume, brightness and lock keys: icon, level bar, value. One view and one
/// alert are reused, so holding a key updates the bar in place instead of queuing pop-ups.
/// </summary>
public sealed class HudView
{
    private const double BarWidth = 150;

    private readonly TextBlock _icon;
    private readonly Border _track;
    private readonly Border _fill;
    private readonly TextBlock _value;

    public HudView(string glyph, Brush accent)
    {
        _icon = Ui.Glyph(glyph, 16, Ui.White);
        _icon.Width = 22;
        _fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = accent, HorizontalAlignment = HorizontalAlignment.Left };
        _track = new Border
        {
            Width = BarWidth, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Track,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Child = _fill,
        };
        _value = Ui.Text("", 14, FontWeights.SemiBold, Ui.White, tabular: true);
        _value.MinWidth = 40;
        _value.TextAlignment = TextAlignment.Right;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _icon, _track, _value },
        };
        Alert = new IslandAlert
        {
            Content = new Grid { Height = 40, Children = { row } },
            Size = new Size(300, 40),
            Duration = TimeSpan.FromSeconds(1.6),
            Urgent = true,
        };
    }

    public IslandAlert Alert { get; }

    /// <summary>Level 0..1 with the value text, or no level (just icon and text, e.g. "Caps Lock on").</summary>
    public void Set(double? level, string value, string? glyph = null)
    {
        if (glyph is not null) _icon.Text = glyph;
        _track.Visibility = level is null ? Visibility.Collapsed : Visibility.Visible;
        _fill.Width = BarWidth * Math.Clamp(level ?? 0, 0, 1);
        _value.Text = value;
        _value.MinWidth = level is null ? 0 : 40;
        _value.Margin = level is null ? new Thickness(10, 0, 0, 0) : new Thickness(0);
    }
}
