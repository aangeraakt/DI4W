using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Windows.Media.Control;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace DynamicIsland.Activities.Media;

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    BitmapSource? Art,
    Color Accent,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionTakenAt,
    string AppId)
{
    /// <summary>Position extrapolated to "now" while playing (apps only report it occasionally).</summary>
    public TimeSpan LivePosition
    {
        get
        {
            var p = IsPlaying ? Position + (DateTimeOffset.Now - PositionTakenAt) : Position;
            if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
            return p < TimeSpan.Zero ? TimeSpan.Zero : p;
        }
    }
}

/// <summary>
/// Reads Spotify (or any player) through the Windows media session API, the same
/// source as the volume flyout's media controls. No Spotify account or API key needed.
/// </summary>
public sealed class MediaService
{
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private GsmtcManager? _manager;
    private GsmtcSession? _session;
    private string? _artKey;
    private BitmapSource? _art;
    private Color _accent = Ui.Primary.Color;
    private bool _refreshQueued;

    public event Action<MediaSnapshot?>? Updated;

    public MediaSnapshot? Current { get; private set; }

    public MediaService(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GsmtcManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => Post(SelectSession);
            _manager.CurrentSessionChanged += (_, _) => Post(SelectSession);
            SelectSession();
        }
        catch (Exception ex)
        {
            Log.Error("Media session manager unavailable", ex);
        }
    }

    public async Task TogglePlayPause()
    {
        if (_session is not null) await _session.TryTogglePlayPauseAsync();
    }

    public async Task Next()
    {
        if (_session is not null) await _session.TrySkipNextAsync();
    }

    public async Task Previous()
    {
        if (_session is not null) await _session.TrySkipPreviousAsync();
    }

    public async Task Seek(TimeSpan position)
    {
        if (_session is not null) await _session.TryChangePlaybackPositionAsync(position.Ticks);
    }

    private void Post(Action action) => _dispatcher.BeginInvoke(action);

    private void SelectSession()
    {
        if (_manager is null) return;

        GsmtcSession? pick = null;
        try
        {
            var sessions = _manager.GetSessions();
            pick = sessions.FirstOrDefault(s => s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
            if (pick is null && _settings.MediaAnyPlayer) pick = _manager.GetCurrentSession();
        }
        catch (Exception ex)
        {
            Log.Error("Could not enumerate media sessions", ex);
        }

        if (ReferenceEquals(pick, _session) && pick is not null && _session is not null &&
            pick.SourceAppUserModelId == _session.SourceAppUserModelId)
        {
            QueueRefresh();
            return;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }

        _session = pick;
        if (_session is not null)
        {
            Log.Info($"Media session: {_session.SourceAppUserModelId}");
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
        }
        QueueRefresh();
    }

    private void OnSessionChanged(GsmtcSession sender, object args) => Post(QueueRefresh);

    private void QueueRefresh()
    {
        // Several events usually fire together (track change); coalesce them.
        if (_refreshQueued) return;
        _refreshQueued = true;
        _dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(60);
            _refreshQueued = false;
            await RefreshAsync();
        });
    }

    private async Task RefreshAsync()
    {
        var session = _session;
        if (session is null)
        {
            Publish(null);
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            if (props is null || string.IsNullOrWhiteSpace(props.Title))
            {
                Publish(null);
                return;
            }

            var key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
            if (key != _artKey || _art is null)
            {
                _artKey = key;
                _art = await LoadArt(props.Thumbnail);
                _accent = _art is null ? Ui.Primary.Color : DominantColor(_art);
            }

            var playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position;
            var taken = timeline.LastUpdatedTime;
            if (taken.Year < 2000) taken = DateTimeOffset.Now;

            Publish(new MediaSnapshot(props.Title, props.Artist, _art, _accent, playing, position, duration, taken,
                                      session.SourceAppUserModelId));
        }
        catch (Exception ex)
        {
            Log.Error("Media refresh failed", ex);
        }
    }

    private void Publish(MediaSnapshot? snapshot)
    {
        Current = snapshot;
        Updated?.Invoke(snapshot);
    }

    private static async Task<BitmapSource?> LoadArt(Windows.Storage.Streams.IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var winStream = await reference.OpenReadAsync();
            using var stream = winStream.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 240;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            // Spotify's thumbnails can include a transparent/letterboxed border; crop to the centered square.
            var side = Math.Min(bmp.PixelWidth, bmp.PixelHeight);
            if (bmp.PixelWidth != bmp.PixelHeight)
            {
                var crop = new CroppedBitmap(bmp, new System.Windows.Int32Rect((bmp.PixelWidth - side) / 2, (bmp.PixelHeight - side) / 2, side, side));
                crop.Freeze();
                return crop;
            }
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Error("Could not load album art", ex);
            return null;
        }
    }

    /// <summary>Average colour weighted towards saturated pixels, lifted so it stays visible on black.</summary>
    private static Color DominantColor(BitmapSource art)
    {
        try
        {
            var small = new TransformedBitmap(art, new ScaleTransform(16.0 / art.PixelWidth, 16.0 / art.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            var w = bgra.PixelWidth;
            var h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            double r = 0, g = 0, b = 0, total = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                double pb = pixels[i] / 255.0, pg = pixels[i + 1] / 255.0, pr = pixels[i + 2] / 255.0;
                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var sat = max == 0 ? 0 : (max - min) / max;
                var weight = sat * sat * max + 0.02;
                r += pr * weight;
                g += pg * weight;
                b += pb * weight;
                total += weight;
            }
            r /= total;
            g /= total;
            b /= total;

            var lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            if (Palette.Light)
            {
                // On the light island: darken light colours until they read on near-white (keeping the hue).
                if (lum > 0.4)
                {
                    var scale = 0.4 / lum;
                    r *= scale;
                    g *= scale;
                    b *= scale;
                }
                return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
            }

            // Lift dark colours towards a readable brightness.
            if (lum < 0.55)
            {
                var max = Math.Max(r, Math.Max(g, b));
                var scale = max > 0 ? Math.Min(1 / max, 0.6 / Math.Max(lum, 0.05)) : 1;
                r = Math.Min(1, r * scale);
                g = Math.Min(1, g * scale);
                b = Math.Min(1, b * scale);
                lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                if (lum < 0.45)
                {
                    var t = (0.45 - lum) / (1 - lum);
                    r += (1 - r) * t;
                    g += (1 - g) * t;
                    b += (1 - b) * t;
                }
            }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
        catch
        {
            return Ui.Primary.Color;
        }
    }
}
