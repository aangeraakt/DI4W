using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Activity = DynamicIsland.Core.Activity;

namespace DynamicIsland.Activities.Stopwatches;

/// <summary>(13) Stopwatch that counts up, with pause/resume and stop. Shows tenths when expanded.</summary>
public sealed class StopwatchActivity : Activity
{
    private const string Glyph = "";

    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _tick;
    private readonly TextBlock _compactText = Ui.Text("0:00", 15, FontWeights.SemiBold, Ui.Amber, tabular: true);
    private readonly TextBlock _expandedText = Ui.Text("0:00.0", 44, FontWeights.Light, Ui.Amber, tabular: true);
    private readonly ContentControl _pauseIcon = new();

    public StopwatchActivity()
    {
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => Update();
        Leading = Ui.Glyph(Glyph, 15, Ui.Amber);
        Trailing = _compactText;
        Bubble = Ui.Glyph(Glyph, 13, Ui.Amber);
        Expanded = BuildExpanded();
    }

    public override string Name => L.T("Stopwatch");
    public override int Priority => 28;
    public override double CompactWidth => 230;
    public override Size ExpandedSize => new(400, 120);

    public override FrameworkElement Leading { get; }
    public override FrameworkElement Trailing { get; }
    public override FrameworkElement Bubble { get; }
    public override FrameworkElement Expanded { get; }

    public bool IsRunning => _stopwatch.IsRunning;

    public void Start()
    {
        _stopwatch.Restart();
        _tick.Start();
        IsActive = true;
        Update();
    }

    public void Stop()
    {
        _stopwatch.Reset();
        _tick.Stop();
        IsActive = false;
    }

    private void TogglePause()
    {
        if (_stopwatch.IsRunning) _stopwatch.Stop();
        else _stopwatch.Start();
        Update();
    }

    private void Update()
    {
        var t = _stopwatch.Elapsed;
        _compactText.Text = Ui.FormatTime(t);
        _expandedText.Text = $"{Ui.FormatTime(t)}.{t.Milliseconds / 100}";
        var paused = !_stopwatch.IsRunning;
        _compactText.Opacity = _expandedText.Opacity = paused ? 0.55 : 1;
        _pauseIcon.Content = Ui.Shape(paused ? Icons.Play : Icons.Pause, 15, Ui.Amber);
    }

    private FrameworkElement BuildExpanded()
    {
        var grid = new Grid { Margin = new Thickness(22, 16, 26, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var pause = Ui.CircleButton(_pauseIcon, Ui.Brush("#4A4010"), 50, TogglePause, L.T("Pause / resume"));
        var stop = Ui.CircleButton(Icons.Stroke(Icons.Close, 13, Ui.White, 2.4), Ui.Track, 50, Stop, L.T("Stop"));
        stop.Margin = new Thickness(12, 0, 0, 0);
        buttons.Children.Add(pause);
        buttons.Children.Add(stop);
        grid.Children.Add(buttons);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var label = Ui.Text(L.T("Stopwatch"), 14, FontWeights.SemiBold, Ui.Amber);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        _expandedText.HorizontalAlignment = HorizontalAlignment.Right;
        _expandedText.Margin = new Thickness(0, -6, 0, -4);
        right.Children.Add(label);
        right.Children.Add(_expandedText);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
}
