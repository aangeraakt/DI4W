using DynamicIsland.Core;
using NAudio.Dsp;
using NAudio.Wave;

namespace DynamicIsland.Activities.Media;

/// <summary>
/// Captures what the speakers are playing (WASAPI loopback) and turns it into a few
/// smoothed frequency bands for the waveform bars. Only runs while music is playing.
/// </summary>
public sealed class AudioLevelMeter : IDisposable
{
    public const int BandCount = 6;
    private const int FftLog2 = 10;
    private const int FftSize = 1 << FftLog2;

    private readonly float[] _ring = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly double[] _bands = new double[BandCount];
    private readonly double[] _peak = Enumerable.Repeat(0.05, BandCount).ToArray();
    private readonly object _gate = new();
    private int _ringPos;
    private WasapiLoopbackCapture? _capture;
    private int _sampleRate = 48000;
    private DateTime _lastData = DateTime.MinValue;

    public static AudioLevelMeter Shared { get; } = new();

    public bool Failed { get; private set; }

    /// <summary>True when audio frames arrived recently (loopback delivers nothing during silence).</summary>
    public bool HasSignal => (DateTime.UtcNow - _lastData).TotalMilliseconds < 150;

    public void Start()
    {
        if (_capture is not null || Failed) return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) Log.Error("Loopback capture stopped", e.Exception);
            };
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            Log.Error("Loopback capture unavailable; falling back to simulated bars", ex);
            Failed = true;
            _capture = null;
        }
    }

    public void Stop()
    {
        try
        {
            _capture?.StopRecording();
            _capture?.Dispose();
        }
        catch
        {
            // ignored
        }
        _capture = null;
    }

    /// <summary>Copies the current band levels (0..1) into <paramref name="target"/>.</summary>
    public void Read(double[] target)
    {
        lock (_gate)
        {
            Array.Copy(_bands, target, Math.Min(target.Length, BandCount));
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format is null || e.BytesRecorded == 0) return;
        _lastData = DateTime.UtcNow;

        var channels = format.Channels;
        // Shared-mode loopback is 32-bit float in practice (often wrapped as WAVE_FORMAT_EXTENSIBLE).
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                      (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
        var bytesPerSample = format.BitsPerSample / 8;
        var frameBytes = bytesPerSample * channels;

        for (var i = 0; i + frameBytes <= e.BytesRecorded; i += frameBytes)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var o = i + c * bytesPerSample;
                sum += isFloat
                    ? BitConverter.ToSingle(e.Buffer, o)
                    : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, o) / 32768f : 0;
            }
            _ring[_ringPos++] = sum / channels;
            if (_ringPos == FftSize)
            {
                _ringPos = 0;
                Analyze();
            }
        }
    }

    private void Analyze()
    {
        for (var i = 0; i < FftSize; i++)
        {
            _fft[i].X = (float)(_ring[i] * FastFourierTransform.HannWindow(i, FftSize));
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftLog2, _fft);

        // Log-spaced bands between 60 Hz and 10 kHz.
        const double lo = 60, hi = 10000;
        var binHz = (double)_sampleRate / FftSize;
        var levels = new double[BandCount];
        for (var b = 0; b < BandCount; b++)
        {
            var f0 = lo * Math.Pow(hi / lo, (double)b / BandCount);
            var f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / BandCount);
            var i0 = Math.Max(1, (int)(f0 / binHz));
            var i1 = Math.Max(i0 + 1, (int)(f1 / binHz));
            double sum = 0;
            for (var i = i0; i < i1 && i < FftSize / 2; i++)
            {
                sum += Math.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
            }
            levels[b] = sum / (i1 - i0);
        }

        lock (_gate)
        {
            for (var b = 0; b < BandCount; b++)
            {
                // Auto-gain per band so quiet and loud tracks both move the bars nicely.
                _peak[b] = Math.Max(levels[b], _peak[b] * 0.995);
                var v = Math.Clamp(levels[b] / Math.Max(_peak[b], 1e-4), 0, 1);
                v = Math.Pow(v, 0.8);
                _bands[b] = v > _bands[b] ? v : _bands[b] * 0.7 + v * 0.3;
            }
        }
    }

    public void Dispose() => Stop();
}
