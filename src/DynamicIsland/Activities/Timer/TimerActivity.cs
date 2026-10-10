using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Activity = DynamicIsland.Core.Activity;

namespace DynamicIsland.Activities.Timer;

/// <summary>Countdown timer: orange icon + orange countdown in the pill, iOS-style controls when expanded.</summary>
public sealed class TimerActivity : Activity
{
    private readonly ActivityManager _manager;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _tick;
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _duration;
    private SoundPlayer? _alarm;

    private readonly TextBlock _compactText;
    private readonly TextBlock _expandedText;
    private readonly ContentControl _pauseIcon;
    private readonly Border _progressRing;

    public TimerActivity(ActivityManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => Update();

        Leading = Ui.Glyph(Icons.TimerGlyph, 15, Ui.Orange);

        _compactText = Ui.Text("0:00", 15, FontWeights.SemiBold, Ui.Orange, tabular: true);
        Trailing = _compactText;

        _progressRing = new Border { Width = 22, Height = 22 };
        Bubble = new Grid { Children = { _progressRing, Ui.Glyph(Icons.TimerGlyph, 12, Ui.Orange) } };

        _expandedText = Ui.Text("0:00", 46, FontWeights.Light, Ui.Orange, tabular: true);
        _pauseIcon = new ContentControl();
        Expanded = BuildExpanded();
    }

    public override string Name => "Timer";
    public override int Priority => 30;
    public override double CompactWidth => 230;
    public override Size ExpandedSize => new(400, 120);

    public override FrameworkElement Leading { get; }
    public override FrameworkElement Trailing { get; }
    public override FrameworkElement Bubble { get; }
    public override FrameworkElement Expanded { get; }

    public bool IsRunning => _stopwatch.IsRunning;
    public TimeSpan Remaining => _duration - _stopwatch.Elapsed;

    public void Start(TimeSpan duration)
    {
        StopAlarm();
        _duration = duration;
        _stopwatch.Restart();
        _tick.Start();
        IsActive = true;
        Update();
    }

    public void TogglePause()
    {
        if (_stopwatch.IsRunning) _stopwatch.Stop();
        else _stopwatch.Start();
        Update();
    }

    public void Cancel()
    {
        _stopwatch.Reset();
        _tick.Stop();
        IsActive = false;
    }

    private void Update()
    {
        var remaining = Remaining;
        if (remaining <= TimeSpan.Zero)
        {
            Finish();
            return;
        }

        // Round up so "5:00" shows for the whole first second, like iOS.
        var shown = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
        var text = Ui.FormatTime(shown);
        _compactText.Text = text;
        _expandedText.Text = text;

        var paused = !_stopwatch.IsRunning;
        _compactText.Opacity = paused ? 0.55 : 1;
        _expandedText.Opacity = paused ? 0.55 : 1;
        _pauseIcon.Content = Ui.Shape(paused ? Icons.Play : Icons.Pause, 15, Ui.Orange);

        DrawRing(1 - remaining.TotalSeconds / _duration.TotalSeconds);
    }

    private void DrawRing(double progress)
    {
        // Small arc around the bubble icon showing elapsed fraction.
        const double size = 22, stroke = 2.2, r = (size - stroke) / 2;
        progress = Math.Clamp(1 - progress, 0.0001, 0.9999);
        var angle = progress * 2 * Math.PI;
        var start = new Point(size / 2, stroke / 2);
        var end = new Point(size / 2 + r * Math.Sin(angle), size / 2 - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        _progressRing.Child = new System.Windows.Shapes.Path
        {
            Data = new PathGeometry(new[] { fig }),
            Stroke = Ui.Orange,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
    }

    private void Finish()
    {
        _tick.Stop();
        _stopwatch.Reset();
        IsActive = false;

        var label = Ui.FormatTime(_duration);
        var icon = Ui.GlyphCircle(Icons.TimerGlyph, Ui.OrangeDim, 46, 22);
        ((TextBlock)icon.Child).Foreground = Ui.Orange;

        // Pulse the icon while ringing.
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        var pulse = new ScaleTransform();
        icon.RenderTransform = pulse;
        var anim = new DoubleAnimation(1, 1.12, TimeSpan.FromMilliseconds(450))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase(),
        };
        pulse.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        pulse.BeginAnimation(ScaleTransform.ScaleYProperty, anim);

        IslandAlert? alert = null;
        var stop = Ui.CircleButton(Ui.Text("Stop", 13, FontWeights.SemiBold, Brushes.Black), Ui.Orange, 46, () =>
        {
            if (alert is not null) _manager.DismissAlert(alert);
        });
        stop.Padding = new Thickness(0);

        alert = new IslandAlert
        {
            Content = Ui.AlertView(icon, "Timer", $"{label} is done", Ui.Orange, stop),
            Size = new Size(380, 76),
            Sticky = true,
            OnDismissed = StopAlarm,
        };
        _manager.ShowAlert(alert);
        PlayAlarm();
    }

    private void PlayAlarm()
    {
        if (!_settings.Sounds) return;
        try
        {
            var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (!File.Exists(wav)) wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");
            _alarm = new SoundPlayer(wav);
            _alarm.PlayLooping();
        }
        catch (Exception ex)
        {
            Log.Error("Could not play timer alarm", ex);
        }
    }

    private void StopAlarm()
    {
        _alarm?.Stop();
        _alarm?.Dispose();
        _alarm = null;
    }

    private FrameworkElement BuildExpanded()
    {
        // Layout follows the iOS timer live activity: round controls left, big orange countdown right.
        var grid = new Grid { Margin = new Thickness(22, 16, 26, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var pause = Ui.CircleButton(_pauseIcon, Ui.OrangeDim, 50, TogglePause, "Pause / resume");
        var cancel = Ui.CircleButton(Icons.Stroke(Icons.Close, 13, Ui.White, 2.4), Ui.Track, 50, Cancel, "Cancel");
        cancel.Margin = new Thickness(12, 0, 0, 0);
        buttons.Children.Add(pause);
        buttons.Children.Add(cancel);
        grid.Children.Add(buttons);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var label = Ui.Text("Timer", 14, FontWeights.SemiBold, Ui.Orange);
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
