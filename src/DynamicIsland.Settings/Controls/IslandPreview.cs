using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DynamicIsland.SettingsApp.Controls;

/// <summary>
/// A drawing of the top of a screen with the island on it (playing music), at its real size, distance from
/// the top and colors. Used on the General page and in the welcome tour.
/// </summary>
public sealed class IslandPreview : UserControl
{
    private const double PillWidth = 250, PillHeight = 34, ScreenHeight = 132;

    private readonly Border _pill;
    private readonly Border _art;
    private readonly StackPanel _bars;
    private readonly Grid _content;

    public IslandPreview()
    {
        _art = new Border
        {
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Gradient(Color.FromArgb(255, 0xFF, 0x7A, 0x59), Color.FromArgb(255, 0xC9, 0x3F, 0x8E)),
        };
        _bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _content = new Grid { Children = { _art, _bars } };
        _pill = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = _content };

        // A wallpaper-like backdrop so both the black and the light island stand out.
        var screen = new Border
        {
            Height = ScreenHeight,
            CornerRadius = new CornerRadius(8),
            Background = Gradient(Color.FromArgb(255, 0x5B, 0x7C, 0xE8), Color.FromArgb(255, 0xB5, 0x8C, 0xE0)),
            Child = new Grid { Children = { _pill } },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(screen, DynamicIsland.Settings.L.T("Preview of the island"));
        Content = screen;
        Update(1, 8, false);
    }

    /// <param name="scale">The Size setting (1 = normal).</param>
    /// <param name="topOffset">Distance from the top of the screen, before scaling (as in the settings).</param>
    public void Update(double scale, double topOffset, bool light)
    {
        var w = PillWidth * scale;
        var h = PillHeight * scale;
        _pill.Width = w;
        _pill.Height = h;
        _pill.CornerRadius = new CornerRadius(h / 2);
        _pill.Margin = new Thickness(0, topOffset * scale, 0, 0);
        _pill.Background = new SolidColorBrush(light ? Color.FromArgb(255, 0xF5, 0xF5, 0xF7) : Colors.Black);
        _pill.BorderBrush = new SolidColorBrush(Color.FromArgb(light ? (byte)0x22 : (byte)0, 0, 0, 0));
        _pill.BorderThickness = new Thickness(light ? 1 : 0);

        // Same 16 px insets as the real island's compact view.
        _content.Margin = new Thickness(16 * scale, 0, 16 * scale, 0);
        _art.Width = _art.Height = 22 * scale;

        _bars.Children.Clear();
        _bars.Spacing = 2.5 * scale;
        var green = new SolidColorBrush(Color.FromArgb(255, 0x30, 0xD1, 0x58));
        foreach (var level in new[] { 0.45, 0.8, 0.6, 1.0, 0.55 })
        {
            _bars.Children.Add(new Border
            {
                Width = 3 * scale,
                Height = 16 * scale * level,
                CornerRadius = new CornerRadius(1.5 * scale),
                Background = green,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private static LinearGradientBrush Gradient(Color from, Color to) => new()
    {
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(1, 1),
        GradientStops = { new GradientStop { Color = from, Offset = 0 }, new GradientStop { Color = to, Offset = 1 } },
    };
}
