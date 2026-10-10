using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland.Activities.Media;

/// <summary>The little animated bars on the right of the pill, tinted with the album art color.</summary>
public sealed class WaveformView : FrameworkElement
{
    private static readonly List<WaveformView> Instances = new();
    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private static readonly double[] Shared = new double[AudioLevelMeter.BandCount];
    private static readonly Random Rng = new();
    private static bool _playing;

    // Bar order: put the mid bands in the middle so it looks like the iOS waveform.
    private static readonly int[] Order = { 3, 1, 0, 2, 4, 5 };

    private readonly double[] _shown = new double[AudioLevelMeter.BandCount];
    private Brush _brush = Brushes.White;

    static WaveformView()
    {
        Timer.Tick += (_, _) => TickAll();
    }

    public WaveformView(double width, double height)
    {
        Width = width;
        Height = height;
        Instances.Add(this);
        IsHitTestVisible = false;
    }

    public void SetColor(Color c)
    {
        _brush = new LinearGradientBrush(c, Color.FromArgb(255, (byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60)), 90);
        _brush.Freeze();
        InvalidateVisual();
    }

    public static void SetPlaying(bool playing)
    {
        _playing = playing;
        if (playing)
        {
            AudioLevelMeter.Shared.Start();
        }
        else
        {
            AudioLevelMeter.Shared.Stop();
        }
        Timer.Start();
    }

    private static void TickAll()
    {
        var meter = AudioLevelMeter.Shared;
        if (_playing && meter.HasSignal)
        {
            meter.Read(Shared);
        }
        else if (_playing && meter.Failed)
        {
            // No loopback available: gentle fake motion.
            for (var i = 0; i < Shared.Length; i++)
            {
                Shared[i] = Shared[i] * 0.6 + Rng.NextDouble() * 0.4;
            }
        }
        else
        {
            for (var i = 0; i < Shared.Length; i++) Shared[i] *= 0.8;
        }

        var settled = true;
        foreach (var v in Instances)
        {
            settled &= v.Advance();
        }
        if (!_playing && settled) Timer.Stop();
    }

    private bool Advance()
    {
        var settled = true;
        for (var i = 0; i < _shown.Length; i++)
        {
            var target = Shared[Order[i]];
            _shown[i] += (target - _shown[i]) * 0.45;
            if (Math.Abs(target - _shown[i]) > 0.01) settled = false;
        }
        if (IsVisible) InvalidateVisual();
        return settled;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var n = _shown.Length;
        var gap = Width * 0.12;
        var barW = (Width - gap * (n - 1)) / n;
        var minH = barW;
        for (var i = 0; i < n; i++)
        {
            var h = minH + (Height - minH) * Math.Clamp(_shown[i], 0, 1);
            var x = i * (barW + gap);
            var y = (Height - h) / 2;
            dc.DrawRoundedRectangle(_brush, null, new Rect(x, y, barW, h), barW / 2, barW / 2);
        }
    }
}
