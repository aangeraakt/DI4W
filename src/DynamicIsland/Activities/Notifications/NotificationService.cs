using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Activities.Notifications;

public sealed record IncomingNotification(string App, string Title, string Body, BitmapSource? Logo);

/// <summary>
/// Watches Windows toast notifications (Action Center) and raises the ones from allow-listed
/// apps such as Discord. Polls once a second, because the NotificationChanged event
/// is only delivered to packaged apps with a background task.
/// </summary>
public sealed class NotificationService
{
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<uint> _seen = new();
    private readonly Dictionary<string, BitmapSource?> _logoCache = new();
    private UserNotificationListener? _listener;
    private DispatcherTimer? _poll;
    private bool _polling;

    /// <summary>Notifications older than this never pop out of the island.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    public event Action<IncomingNotification>? Received;

    /// <summary>Human readable status for the tray menu ("Allowed", "Denied", error…).</summary>
    public string Status { get; private set; } = "Not started";

    public NotificationService(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
    }

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            Status = access.ToString();
            Log.Info($"Notification access: {access}");
            if (access != UserNotificationListenerAccessStatus.Allowed) return;

            // Don't replay notifications that were already sitting in Action Center.
            foreach (var n in await _listener.GetNotificationsAsync(NotificationKinds.Toast))
            {
                _seen.Add(n.Id);
            }

            _poll = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(800) };
            _poll.Tick += async (_, _) => await Poll();
            _poll.Start();
        }
        catch (Exception ex)
        {
            Status = "Unavailable (" + ex.GetType().Name + ")";
            Log.Error("Notification listener unavailable", ex);
        }
    }

    private async Task Poll()
    {
        if (_polling || _listener is null) return;
        _polling = true;
        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var ids = new HashSet<uint>();
            foreach (var n in current.OrderBy(n => n.CreationTime))
            {
                ids.Add(n.Id);
                if (!_seen.Add(n.Id)) continue;
                // Old news, whatever the list says: Windows sometimes returns an empty list for a moment (after sleep,
                // locking, or Explorer restarting), after which everything in Action Center would look new.
                if (DateTimeOffset.Now - n.CreationTime > MaxAge) continue;
                if (!_settings.NotificationsEnabled) continue;
                var parsed = await Parse(n);
                if (parsed is not null) Received?.Invoke(parsed);
            }
            // Forget dismissed notifications so the set doesn't grow forever.
            _seen.IntersectWith(ids);
        }
        catch (Exception ex)
        {
            Log.Error("Notification poll failed", ex);
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task<IncomingNotification?> Parse(UserNotification n)
    {
        string app;
        try
        {
            app = n.AppInfo.DisplayInfo.DisplayName;
        }
        catch
        {
            return null;
        }

        if (!_settings.NotificationApps.Any(a => app.Contains(a, StringComparison.OrdinalIgnoreCase))) return null;

        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new();
        var title = texts.Count > 0 ? texts[0] : app;
        var body = string.Join(" ", texts.Skip(1));

        return new IncomingNotification(app, title, body, await Logo(n, app));
    }

    private async Task<BitmapSource?> Logo(UserNotification n, string app)
    {
        if (_logoCache.TryGetValue(app, out var cached)) return cached;
        BitmapSource? result = null;
        try
        {
            var reference = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(96, 96));
            using var winStream = await reference.OpenReadAsync();
            using var stream = winStream.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            result = bmp;
        }
        catch (Exception ex)
        {
            Log.Error($"No logo for {app}", ex);
        }
        _logoCache[app] = result;
        return result;
    }
}
