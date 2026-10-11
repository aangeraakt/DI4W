using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;

namespace DynamicIsland.Addons;

/// <summary>A live activity driven by an add-on (download in progress, file copy, game overlay).</summary>
public sealed class AddonActivity : Activity
{
    private readonly string _name;
    private readonly int _priority;
    private readonly double _compactWidth;
    private readonly Size _expandedSize;

    public AddonActivity(string name, int priority, FrameworkElement leading, FrameworkElement trailing,
                         FrameworkElement bubble, FrameworkElement expanded, double compactWidth = 250, Size? expandedSize = null)
    {
        _name = name;
        _priority = priority;
        _compactWidth = compactWidth;
        _expandedSize = expandedSize ?? new Size(400, 100);
        Leading = leading;
        Trailing = trailing;
        Bubble = bubble;
        Expanded = expanded;
    }

    public override string Name => _name;
    public override int Priority => _priority;
    public override double CompactWidth => _compactWidth;
    public override Size ExpandedSize => _expandedSize;

    public override FrameworkElement Leading { get; }
    public override FrameworkElement Trailing { get; }
    public override FrameworkElement Bubble { get; }
    public override FrameworkElement Expanded { get; }

    public void Show() => IsActive = true;

    public void Hide() => IsActive = false;

    /// <summary>A plain text view for the pill, created per use (WPF elements can have one parent).</summary>
    public static TextBlock Label(string text, double size, System.Windows.Media.Brush brush) =>
        UI.Ui.Text(text, size, FontWeights.SemiBold, brush, tabular: true);
}
