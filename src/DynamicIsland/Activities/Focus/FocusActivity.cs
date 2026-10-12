using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Activity = DynamicIsland.Core.Activity;

namespace DynamicIsland.Activities.Focus;

/// <summary>
/// (12) Pomodoro focus timer: work and break periods run back to back with a ring showing progress.
/// Every Nth break is a long one. Lengths come from the settings app.
/// </summary>
public sealed class FocusActivity : Activity
{
    private enum Phase { Focus, Break, LongBreak }

    private readonly ActivityManager _manager;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _tick;
    private readonly Stopwatch _stopwatch = new();
    private Phase _phase;
    private TimeSpan _length;
    private int _completedFocus;

    private readonly Border _ring = new() { Width = 18, Height = 18 };
    private readonly Border _bubbleRing = new() { Width = 22, Height = 22 };
    private readonly TextBlock _compactText = Ui.Text("", 15, FontWeights.SemiBold, Ui.Red, tabular: true);
    private readonly TextBlock _expandedText = Ui.Text("", 44, FontWeights.Light, Ui.Red, tabular: true);
    private readonly TextBlock _label = Ui.Text("", 14, FontWeights.SemiBold, Ui.Red);
    private readonly ContentControl _pauseIcon = new();

    public FocusActivity(ActivityManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) => Update();

        Leading = _ring;
        Trailing = _compactText;
        Bubble = _bubbleRing;
        Expanded = BuildExpanded();
    }

    public override string Name => L.T("Focus");
    public override int Priority => 32;
    public override double CompactWidth => 230;
    public override Size ExpandedSize => new(400, 120);

    public override FrameworkElement Leading { get; }
    public override FrameworkElement Trailing { get; }
    public override FrameworkElement Bubble { get; }
    public override FrameworkElement Expanded { get; }

    private AddonSettings Options => _settings.Addons;
    private System.Windows.Media.Brush Color => _phase == Phase.Focus ? Ui.Red : Ui.Green;

    public void Start()
    {
        _completedFocus = 0;
        Begin(Phase.Focus);
        IsActive = true;
    }

    public void Stop()
    {
        _tick.Stop();
        _stopwatch.Reset();
        IsActive = false;
    }

    private void TogglePause()
    {
        if (_stopwatch.IsRunning) _stopwatch.Stop();
        else _stopwatch.Start();
        Update();
    }

    private void Skip() => Next(announce: false);

    private void Begin(Phase phase)
    {
        _phase = phase;
        _length = TimeSpan.FromMinutes(Math.Max(1, phase switch
        {
            Phase.Focus => Options.FocusMinutes,
            Phase.Break => Options.BreakMinutes,
            _ => Options.LongBreakMinutes,
        }));
        _stopwatch.Restart();
        _tick.Start();
        Update();
    }

    private void Next(bool announce)
    {
        Phase next;
        if (_phase == Phase.Focus)
        {
            _completedFocus++;
            next = _completedFocus % Math.Max(1, Options.SessionsBeforeLongBreak) == 0 ? Phase.LongBreak : Phase.Break;
        }
        else
        {
            next = Phase.Focus;
        }
        Begin(next);

        if (!announce) return;
        Chime();
        var (title, subtitle) = next == Phase.Focus
            ? (L.T("Back to focus"), L.F("{0} min · session {1}", (int)_length.TotalMinutes, _completedFocus + 1))
            : (next == Phase.LongBreak ? L.T("Long break") : L.T("Break time"), L.F("{0} min · nice work", (int)_length.TotalMinutes));
        _manager.ShowAlert(new IslandAlert
        {
            Content = Ui.AlertView(Ui.GlyphCircle(next == Phase.Focus ? "" : "", Color, 46, 20), title, subtitle, Color),
            Size = new Size(360, 76),
            Duration = TimeSpan.FromSeconds(5),
            BreaksThroughQuiet = true,
        });
    }

    private void Update()
    {
        var remaining = _length - _stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            Next(announce: true);
            return;
        }

        var text = Ui.FormatTime(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));
        var paused = !_stopwatch.IsRunning;
        foreach (var t in new[] { _compactText, _expandedText })
        {
            t.Text = text;
            t.Foreground = Color;
            t.Opacity = paused ? 0.55 : 1;
        }
        _label.Text = _phase switch
        {
            Phase.Focus => L.F("Focus · {0} of {1}", _completedFocus % Math.Max(1, Options.SessionsBeforeLongBreak) + 1, Options.SessionsBeforeLongBreak),
            Phase.Break => L.T("Break"),
            _ => L.T("Long break"),
        };
        _label.Foreground = Color;
        _pauseIcon.Content = Ui.Shape(paused ? Icons.Play : Icons.Pause, 15, Color);

        var progress = 1 - remaining.TotalSeconds / _length.TotalSeconds;
        _ring.Child = Ring(18, 2.6, progress);
        _bubbleRing.Child = Ring(22, 2.4, progress);
    }

    private Grid Ring(double size, double stroke, double progress) => new()
    {
        Children =
        {
            new System.Windows.Shapes.Ellipse { Width = size, Height = size, Stroke = Ui.Track, StrokeThickness = stroke },
            Icons.Ring(progress, size, stroke, Color),
        },
    };

    private void Chime()
    {
        if (!_settings.Sounds) return;
        try
        {
            var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Calendar.wav");
            if (File.Exists(wav)) new SoundPlayer(wav).Play();
            else SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            Log.Error("Focus chime failed", ex);
        }
    }

    private FrameworkElement BuildExpanded()
    {
        var grid = new Grid { Margin = new Thickness(22, 16, 26, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var pause = Ui.CircleButton(_pauseIcon, Ui.ButtonBg, 44, TogglePause, L.T("Pause / resume"));
        var skip = Ui.CircleButton(Ui.Shape(Icons.Next, 14, Ui.White), Ui.ButtonBg, 44, Skip, L.T("Skip to next"));
        var stop = Ui.CircleButton(Icons.Stroke(Icons.Close, 12, Ui.White, 2.4), Ui.Track, 44, Stop, L.T("Stop"));
        skip.Margin = stop.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(pause);
        buttons.Children.Add(skip);
        buttons.Children.Add(stop);
        grid.Children.Add(buttons);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _label.HorizontalAlignment = HorizontalAlignment.Right;
        _expandedText.HorizontalAlignment = HorizontalAlignment.Right;
        _expandedText.Margin = new Thickness(0, -6, 0, -4);
        right.Children.Add(_label);
        right.Children.Add(_expandedText);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
}
