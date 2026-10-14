using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Activities.Timer;

/// <summary>
/// The Recent tab of the panel: the last alerts, newest first, including ones Do Not Disturb held back
/// (marked with a moon). Click one to do what the alert did (open the chat, the drive, the download…).
/// </summary>
public static class RecentView
{
    public static FrameworkElement Build(IReadOnlyList<ActivityManager.PastAlert> history, Action close)
    {
        if (history.Count == 0)
        {
            var empty = Ui.Text(L.T("Nothing yet. Alerts show up here, also the ones that were held back."), 13, FontWeights.Normal, Ui.Grey);
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(0, 18, 0, 0);
            empty.VerticalAlignment = VerticalAlignment.Top;
            return empty;
        }

        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var past in history) list.Children.Add(Row(past, close));
        // The island never takes focus, but the mouse wheel still scrolls the list.
        return new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Focusable = false,
            CanContentScroll = false,
        };
    }

    private static FrameworkElement Row(ActivityManager.PastAlert past, Action close)
    {
        var grid = new Grid { Height = 34, Background = Brushes.Transparent, Cursor = past.OnClick is null ? null : Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Ui.Text(past.Time.ToString("t"), 12, FontWeights.Medium, Ui.Grey, tabular: true));

        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = Ui.Font };
        text.Inlines.Add(new System.Windows.Documents.Run(past.Title) { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Primary });
        if (past.Detail.Length > 0)
        {
            text.Inlines.Add(new System.Windows.Documents.Run("  " + past.Detail) { FontSize = 13, Foreground = Ui.Grey });
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (past.Missed)
        {
            var moon = Ui.Glyph("", 12, Ui.Indigo);
            moon.Margin = new Thickness(8, 0, 0, 0);
            moon.ToolTip = L.T("Held back (Do Not Disturb or quiet times)");
            Grid.SetColumn(moon, 2);
            grid.Children.Add(moon);
        }

        if (past.OnClick is { } click)
        {
            grid.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                close();
                click();
            };
        }
        return grid;
    }
}
