using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Activity = DynamicIsland.Core.Activity;

namespace DynamicIsland.Activities.Media;

/// <summary>Now playing: album art + waveform in the pill, full player when expanded.</summary>
public sealed class MediaActivity : Activity
{
    private readonly MediaService _service;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _progressTimer;

    private readonly Border _leadingArt = Ui.Image(null, 22, 6);
    private readonly Border _bubbleArt = Ui.Image(null, 24, 12);
    private readonly Border _expandedArt = Ui.Image(null, 62, 14);
    private readonly WaveformView _compactWave = new(20, 14);
    private readonly WaveformView _expandedWave = new(24, 18);
    private readonly TextBlock _title = Ui.Text("", 17, FontWeights.SemiBold, Ui.White);
    private readonly TextBlock _artist = Ui.Text("", 15, FontWeights.Normal, Ui.Grey);
    private readonly TextBlock _elapsed = Ui.Text("0:00", 12, FontWeights.Medium, Ui.Grey, tabular: true);
    private readonly TextBlock _remaining = Ui.Text("-0:00", 12, FontWeights.Medium, Ui.Grey, tabular: true);
    private readonly Border _progressTrack = new() { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Track, Cursor = Cursors.Hand };
    private readonly Border _progressFill = new() { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.White, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ContentControl _playPauseIcon = new();

    public MediaActivity(MediaService service, AppSettings settings)
    {
        _service = service;
        _settings = settings;

        Leading = _leadingArt;
        Trailing = _compactWave;
        Bubble = _bubbleArt;
        Expanded = BuildExpanded();

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_service.Current is not { IsPlaying: true }) IsActive = false;
        };

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        _service.Updated += OnUpdated;
    }

    public override string Name => "Now Playing";
    public override int Priority => 10;
    public override double CompactWidth => 280;
    public override Size ExpandedSize => new(440, 196);

    public override FrameworkElement Leading { get; }
    public override FrameworkElement Trailing { get; }
    public override FrameworkElement Bubble { get; }
    public override FrameworkElement Expanded { get; }

    private void OnUpdated(MediaSnapshot? s)
    {
        if (s is null)
        {
            WaveformView.SetPlaying(false);
            _progressTimer.Stop();
            IsActive = false;
            return;
        }

        var brush = s.Art is null ? (Brush)Ui.ButtonBg : new ImageBrush(s.Art) { Stretch = Stretch.UniformToFill };
        _leadingArt.Background = brush;
        _bubbleArt.Background = brush;
        _expandedArt.Background = brush;
        _compactWave.SetColor(s.Accent);
        _expandedWave.SetColor(s.Accent);
        _title.Text = s.Title;
        _artist.Text = s.Artist;
        _playPauseIcon.Content = Ui.Shape(s.IsPlaying ? Icons.Pause : Icons.Play, 26, Ui.White);

        WaveformView.SetPlaying(s.IsPlaying);
        UpdateProgress();

        if (s.IsPlaying)
        {
            _hideTimer.Stop();
            _progressTimer.Start();
            IsActive = true;
        }
        else
        {
            _progressTimer.Stop();
            if (IsActive)
            {
                _hideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.MediaHideAfterPauseSeconds));
                _hideTimer.Start();
            }
        }
        RaiseChanged();
    }

    private void UpdateProgress()
    {
        var s = _service.Current;
        if (s is null) return;
        var pos = s.LivePosition;
        var dur = s.Duration;
        _elapsed.Text = Ui.FormatTime(pos);
        _remaining.Text = "-" + Ui.FormatTime(dur - pos);
        var frac = dur.TotalSeconds > 0 ? pos.TotalSeconds / dur.TotalSeconds : 0;
        _progressFill.Width = Math.Max(_progressFill.Height, _progressTrack.ActualWidth * Math.Clamp(frac, 0, 1));
    }

    private FrameworkElement BuildExpanded()
    {
        // Mirrors the iOS expanded music island: art + titles, waveform, scrubber, transport controls.
        var root = new Grid { Margin = new Thickness(22, 20, 22, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Row 0: art | title/artist | waveform
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _expandedArt.Cursor = Cursors.Hand;
        _expandedArt.ToolTip = "Open Spotify";
        _expandedArt.MouseLeftButtonUp += (_, e) =>
        {
            OpenPlayer();
            e.Handled = true;
        };
        top.Children.Add(_expandedArt);

        var titles = new StackPanel { Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_title);
        titles.Children.Add(_artist);
        Grid.SetColumn(titles, 1);
        top.Children.Add(titles);

        _expandedWave.VerticalAlignment = VerticalAlignment.Top;
        _expandedWave.Margin = new Thickness(0, 6, 2, 0);
        Grid.SetColumn(_expandedWave, 2);
        top.Children.Add(_expandedWave);
        root.Children.Add(top);

        // Row 1: elapsed | scrubber | remaining
        var progress = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        progress.Children.Add(_elapsed);

        var bar = new Grid { VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand, Height = 16 };
        bar.Children.Add(_progressTrack);
        bar.Children.Add(_progressFill);
        bar.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            var s = _service.Current;
            if (s is null || s.Duration <= TimeSpan.Zero) return;
            var frac = Math.Clamp(e.GetPosition(_progressTrack).X / _progressTrack.ActualWidth, 0, 1);
            await _service.Seek(TimeSpan.FromSeconds(s.Duration.TotalSeconds * frac));
        };
        bar.SizeChanged += (_, _) => UpdateProgress();
        Grid.SetColumn(bar, 1);
        progress.Children.Add(bar);

        _remaining.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_remaining, 2);
        progress.Children.Add(_remaining);
        Grid.SetRow(progress, 1);
        root.Children.Add(progress);

        // Row 2: ⏮ ⏯ ⏭
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        controls.Children.Add(Ui.Button(Ui.Shape(Icons.Previous, 30, Ui.White), () => _ = _service.Previous(), null, 54, 46, "Previous"));
        var pp = Ui.Button(_playPauseIcon, () => _ = _service.TogglePlayPause(), null, 54, 46, "Play / pause");
        pp.Margin = new Thickness(28, 0, 28, 0);
        controls.Children.Add(pp);
        controls.Children.Add(Ui.Button(Ui.Shape(Icons.Next, 30, Ui.White), () => _ = _service.Next(), null, 54, 46, "Next"));
        Grid.SetRow(controls, 2);
        root.Children.Add(controls);

        return root;
    }

    private void OpenPlayer()
    {
        var id = _service.Current?.AppId ?? "";
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            if (Process.GetProcessesByName("Spotify").Length > 0)
            {
                Win32.FocusProcess("Spotify");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Could not open Spotify", ex);
            }
        }
    }
}
