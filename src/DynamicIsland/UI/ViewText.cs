using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.UI;

/// <summary>The readable text inside a view, for screen readers and the alert history.</summary>
public static class ViewText
{
    /// <summary>Text blocks and tooltips in reading order, without icon-font glyphs or repeats.</summary>
    public static List<string> Texts(object? root)
    {
        var words = new List<string>();
        void Walk(object? node)
        {
            switch (node)
            {
                case TextBlock t when !string.IsNullOrWhiteSpace(t.Text) && !IsGlyph(t.Text):
                    words.Add(t.Text.Trim());
                    break;
                case FrameworkElement { ToolTip: string tip } when !string.IsNullOrWhiteSpace(tip):
                    words.Add(tip);
                    break;
            }
            if (node is DependencyObject d)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
            }
        }
        Walk(root);
        return words.Distinct().ToList();
    }

    // Icons are drawn with private-use characters of the icon font; there's nothing to read there.
    private static bool IsGlyph(string text) => text.Trim().All(c => c is >= '' and <= '');
}
