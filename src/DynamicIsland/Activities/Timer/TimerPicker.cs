using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Activities.Timer;

/// <summary>
/// The panel that opens when you click the idle island, with three modes:
/// Timer (preset chips plus a minutes:seconds picker you change with the scroll wheel or +/-),
/// Focus (Pomodoro) and Stopwatch. No text boxes, because the island never takes keyboard focus.
/// </summary>
public static class TimerPicker
{
    public static readonly Size PanelSize = new(420, 170);
    private static readonly int[] PresetMinutes = { 1, 3, 5, 10, 15, 25 };

    /// <param name="recent">Builds the Recent tab (the latest alerts); <paramref name="startTab"/> 3 opens on it.</param>
    public static FrameworkElement Build(Action<TimeSpan> startTimer, Action startFocus, Action startStopwatch, AddonSettings options,
                                         Func<FrameworkElement> recent, int startTab = 0)
    {
        var root = new Grid { Margin = new Thickness(20, 14, 20, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var modes = new (string Name, string Glyph, Brush Color, Func<FrameworkElement> View)[]
        {
            (L.T("Timer"), Icons.TimerGlyph, Ui.Orange, () => BuildTimer(startTimer)),
            (L.T("Focus"), "", Ui.Red, () => BuildFocus(startFocus, options)),
            (L.T("Stopwatch"), "", Ui.Amber, () => BuildStopwatch(startStopwatch)),
            (L.T("Recent"), "", Ui.Blue, recent),
        };
        var start = modes[Math.Clamp(startTab, 0, modes.Length - 1)];

        // Header: icon + title on the left, mode switch on the right.
        var header = new Grid();
        var icon = Ui.Glyph(start.Glyph, 15, start.Color);
        var title = Ui.Text(start.Name, 15, FontWeights.SemiBold, Ui.Primary);
        title.Margin = new Thickness(8, 0, 0, 0);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, title } });

        var content = new ContentControl { Content = start.View() };
        Grid.SetRow(content, 1);

        var switcher = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var tabs = new List<Button>();
        foreach (var mode in modes)
        {
            var label = Ui.Text(mode.Name, 12, FontWeights.SemiBold, Ui.Grey);
            Button? tab = null;
            tab = Ui.Button(label, () =>
            {
                icon.Text = mode.Glyph;
                icon.Foreground = mode.Color;
                title.Text = mode.Name;
                content.Content = mode.View();
                foreach (var t in tabs) Select(t, t == tab);
            }, Brushes.Transparent, double.NaN, 24);
            tab.Padding = new Thickness(10, 0, 10, 0);
            tab.Margin = new Thickness(4, 0, 0, 0);
            tabs.Add(tab);
            switcher.Children.Add(tab);
        }
        Select(tabs[Array.IndexOf(modes, start)], true);
        header.Children.Add(switcher);

        root.Children.Add(header);
        root.Children.Add(content);
        return root;

        static void Select(Button tab, bool selected)
        {
            tab.Background = selected ? Ui.Track : Brushes.Transparent;
            ((TextBlock)tab.Content).Foreground = selected ? Ui.Primary : Ui.Grey;
        }
    }

    private static FrameworkElement BuildFocus(Action start, AddonSettings o)
    {
        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(L.F("{0} min focus", o.FocusMinutes), 30, FontWeights.Light, Ui.Primary));
        var detail = Ui.Text(L.F("then a {0} min break · every {1} break is {2} min", o.BreakMinutes, Ordinal(o.SessionsBeforeLongBreak), o.LongBreakMinutes),
                             13, FontWeights.Normal, Ui.Grey);
        detail.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(detail);
        grid.Children.Add(text);

        var button = Ui.CircleButton(Ui.Text("Start", 14, FontWeights.SemiBold, Brushes.Black), Ui.Red, 56, start);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private static string Ordinal(int n) => L.Ordinal(n);

    private static FrameworkElement BuildStopwatch(Action start)
    {
        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text("0:00.0", 34, FontWeights.Light, Ui.Amber, tabular: true));
        text.Children.Add(Ui.Text(L.T("Counts up until you stop it"), 13, FontWeights.Normal, Ui.Grey));
        grid.Children.Add(text);

        var button = Ui.CircleButton(Ui.Text("Start", 14, FontWeights.SemiBold, Brushes.Black), Ui.Amber, 56, start);
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private static FrameworkElement BuildTimer(Action<TimeSpan> start)
    {
        var minutes = 5;
        var seconds = 0;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Preset chips.
        var presets = new UniformGrid { Columns = PresetMinutes.Length, Margin = new Thickness(-3, 12, -3, 0) };
        foreach (var m in PresetMinutes)
        {
            var chip = Ui.Button(Ui.Text(L.F("{0} min", m), 13, FontWeights.SemiBold, Ui.Primary),
                                 () => start(TimeSpan.FromMinutes(m)), Ui.ButtonBg, double.NaN, 30);
            chip.Margin = new Thickness(3, 0, 3, 0);
            presets.Children.Add(chip);
        }
        Grid.SetRow(presets, 0);
        root.Children.Add(presets);

        // Custom row: [-] mm : ss [+]            [Start]
        var custom = new Grid { Margin = new Thickness(0, 14, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var minText = Ui.Text("", 34, FontWeights.Light, Ui.Primary, tabular: true);
        var secText = Ui.Text("", 34, FontWeights.Light, Ui.Primary, tabular: true);
        var colon = Ui.Text(":", 34, FontWeights.Light, Ui.Grey);
        colon.Margin = new Thickness(2, -4, 2, 0);

        void Render()
        {
            minText.Text = minutes.ToString("00");
            secText.Text = seconds.ToString("00");
        }

        void Wheel(TextBlock target, Action<int> change)
        {
            target.Cursor = Cursors.SizeNS;
            target.Background = Brushes.Transparent; // hit-testable
            target.ToolTip = L.T("Scroll to change");
            target.MouseWheel += (_, e) =>
            {
                change(e.Delta > 0 ? 1 : -1);
                Render();
                e.Handled = true;
            };
        }

        Wheel(minText, d => minutes = Math.Clamp(minutes + d, 0, 180));
        Wheel(secText, d =>
        {
            seconds += d * 5;
            if (seconds >= 60) { seconds = 0; minutes = Math.Min(180, minutes + 1); }
            if (seconds < 0) { seconds = minutes > 0 ? 55 : 0; if (minutes > 0) minutes--; }
        });

        var minus = Ui.CircleButton(Ui.Text("−", 18, FontWeights.SemiBold, Ui.Primary), Ui.ButtonBg, 32, () =>
        {
            minutes = Math.Max(0, minutes - 1);
            Render();
        }, L.T("−1 min"));
        var plus = Ui.CircleButton(Ui.Text("+", 18, FontWeights.SemiBold, Ui.Primary), Ui.ButtonBg, 32, () =>
        {
            minutes = Math.Min(180, minutes + 1);
            Render();
        }, L.T("+1 min"));

        var picker = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        picker.Children.Add(minus);
        var digits = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0) };
        digits.Children.Add(minText);
        digits.Children.Add(colon);
        digits.Children.Add(secText);
        picker.Children.Add(digits);
        picker.Children.Add(plus);
        custom.Children.Add(picker);

        var startButton = Ui.CircleButton(Ui.Text("Start", 14, FontWeights.SemiBold, Brushes.Black), Ui.Orange, 56, () =>
        {
            var t = new TimeSpan(0, minutes, seconds);
            if (t > TimeSpan.Zero) start(t);
        });
        startButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(startButton, 2);
        custom.Children.Add(startButton);

        Grid.SetRow(custom, 1);
        root.Children.Add(custom);

        Render();
        return root;
    }
}
