using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.UI;

namespace DynamicIsland.Activities.Timer;

/// <summary>
/// The panel that opens when you click the idle island: preset chips plus a custom
/// minutes:seconds picker you change with the scroll wheel or the +/- buttons.
/// No text box, because the island never takes keyboard focus.
/// </summary>
public static class TimerPicker
{
    public static readonly Size PanelSize = new(420, 170);
    private static readonly int[] PresetMinutes = { 1, 3, 5, 10, 15, 25 };

    public static FrameworkElement Build(Action<TimeSpan> start)
    {
        var minutes = 5;
        var seconds = 0;

        var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Ui.Glyph(Icons.TimerGlyph, 15, Ui.Orange));
        var title = Ui.Text("Timer", 15, FontWeights.SemiBold, Ui.White);
        title.Margin = new Thickness(8, 0, 0, 0);
        header.Children.Add(title);
        root.Children.Add(header);

        // Preset chips.
        var presets = new UniformGrid { Columns = PresetMinutes.Length, Margin = new Thickness(-3, 12, -3, 0) };
        foreach (var m in PresetMinutes)
        {
            var chip = Ui.Button(Ui.Text($"{m} min", 13, FontWeights.SemiBold, Ui.White),
                                 () => start(TimeSpan.FromMinutes(m)), Ui.ButtonBg, double.NaN, 30);
            chip.Margin = new Thickness(3, 0, 3, 0);
            presets.Children.Add(chip);
        }
        Grid.SetRow(presets, 1);
        root.Children.Add(presets);

        // Custom row: [-] mm : ss [+]            [Start]
        var custom = new Grid { Margin = new Thickness(0, 14, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var minText = Ui.Text("", 34, FontWeights.Light, Ui.White, tabular: true);
        var secText = Ui.Text("", 34, FontWeights.Light, Ui.White, tabular: true);
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
            target.ToolTip = "Scroll to change";
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

        var minus = Ui.CircleButton(Ui.Text("−", 18, FontWeights.SemiBold, Ui.White), Ui.ButtonBg, 32, () =>
        {
            minutes = Math.Max(0, minutes - 1);
            Render();
        }, "−1 min");
        var plus = Ui.CircleButton(Ui.Text("+", 18, FontWeights.SemiBold, Ui.White), Ui.ButtonBg, 32, () =>
        {
            minutes = Math.Min(180, minutes + 1);
            Render();
        }, "+1 min");

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

        Grid.SetRow(custom, 2);
        root.Children.Add(custom);

        Render();
        return root;
    }
}
