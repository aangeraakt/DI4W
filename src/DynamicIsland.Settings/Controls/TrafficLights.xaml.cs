using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace DynamicIsland.SettingsApp.Controls;

/// <summary>The red / yellow / green window buttons from macOS. The window decides what each one does.</summary>
public sealed partial class TrafficLights : UserControl
{
    private static readonly (Color Fill, Color Edge) Red = (Hex(0xFF5F57), Hex(0xE0443E));
    private static readonly (Color Fill, Color Edge) Yellow = (Hex(0xFEBC2E), Hex(0xDEA123));
    private static readonly (Color Fill, Color Edge) Green = (Hex(0x28C840), Hex(0x1AAB29));

    private bool _active = true;
    private bool _hovering;

    public event Action? CloseRequested;
    public event Action? MinimizeRequested;
    public event Action? ZoomRequested;

    public TrafficLights()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Render();
        Render();
    }

    /// <summary>Like macOS, the buttons turn grey while the window is in the background (unless hovered).</summary>
    public bool IsWindowActive
    {
        get => _active;
        set
        {
            _active = value;
            Render();
        }
    }

    private void Render()
    {
        var colored = _active || _hovering;
        var grey = ActualTheme == ElementTheme.Dark ? (Hex(0x4A4A4A), Hex(0x3A3A3A)) : (Hex(0xDDDDDD), Hex(0xC8C8C8));
        Paint(CloseDot, colored ? Red : grey);
        Paint(MinimizeDot, colored ? Yellow : grey);
        Paint(ZoomDot, colored ? Green : grey);

        var glyphs = _hovering ? 1 : 0;
        CloseGlyph.Opacity = MinimizeGlyph.Opacity = ZoomGlyph.Opacity = glyphs;
    }

    private static void Paint(Ellipse dot, (Color Fill, Color Edge) colors)
    {
        dot.Fill = new SolidColorBrush(colors.Fill);
        dot.Stroke = new SolidColorBrush(colors.Edge);
    }

    private void OnGroupEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovering = true;
        Render();
    }

    private void OnGroupExited(object sender, PointerRoutedEventArgs e)
    {
        // Exits from the individual dots bubble up here too; only stop hovering once the pointer left the group.
        var p = e.GetCurrentPoint(Group).Position;
        if (p.X >= 0 && p.Y >= 0 && p.X < Group.ActualWidth && p.Y < Group.ActualHeight) return;
        _hovering = false;
        Render();
    }

    // Pressed buttons darken slightly, as on macOS.
    private void OnPressed(object sender, PointerRoutedEventArgs e) => ((UIElement)sender).Opacity = 0.75;

    private void OnReleased(object sender, PointerRoutedEventArgs e) => ((UIElement)sender).Opacity = 1;

    private void OnClose(object sender, TappedRoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnMinimize(object sender, TappedRoutedEventArgs e) => MinimizeRequested?.Invoke();

    private void OnZoom(object sender, TappedRoutedEventArgs e) => ZoomRequested?.Invoke();

    private static Color Hex(uint rgb) => ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
