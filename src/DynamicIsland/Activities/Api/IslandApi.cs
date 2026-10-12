using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Activities.Claude;
using DynamicIsland.Addons;
using DynamicIsland.Core;
using DynamicIsland.UI;

namespace DynamicIsland.Activities.Api;

/// <summary>
/// (Island API) Lets scripts and apps on this PC show their own pop-ups and progress in the island:
///   POST /alert     {"title":"Backup done","message":"312 files","icon":"success","color":"green","seconds":5,"url":"https://…"}
///   POST /progress  {"id":"backup","title":"Backing up","value":0.4,"detail":"120 of 312 files"}
///   POST /progress  {"id":"backup","done":true,"message":"Backup done"}
/// Requests from web pages are refused by the server; see ClaudeHookServer.
/// </summary>
public sealed class IslandApi
{
    private const int MaxProgress = 5;
    private const int MaxAlertsPerMinute = 30;
    private static readonly TimeSpan ProgressExpiry = TimeSpan.FromMinutes(15);

    private readonly ActivityManager _manager;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, ProgressItem> _progress = new();
    private readonly Queue<DateTime> _recentAlerts = new();
    private readonly DispatcherTimer _expire;

    public IslandApi(ActivityManager manager, Dispatcher dispatcher)
    {
        _manager = manager;
        _dispatcher = dispatcher;
        _expire = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMinutes(1) };
        _expire.Tick += (_, _) => ExpireStale();
        _expire.Start();
    }

    /// <summary>Called by the server on a background thread; null means "not an API path".</summary>
    public LocalResponse? Handle(LocalRequest request) => (request.Method, request.Path.TrimEnd('/').ToLowerInvariant()) switch
    {
        ("GET", "/api" or "") => new LocalResponse(200, LocalResponse.ToJson(new
        {
            app = "Dynamic Island for Windows",
            version = Updates.UpdateChecker.CurrentVersion.ToString(),
            endpoints = new[] { "POST /alert", "POST /progress" },
        })),
        ("POST", "/alert") => _dispatcher.Invoke(() => Alert(request.Body)),
        ("POST", "/progress") => _dispatcher.Invoke(() => Progress(request.Body)),
        _ => null,
    };

    // ───────────────────────────── alerts ─────────────────────────────

    private LocalResponse Alert(JsonElement body)
    {
        if (Text(body, "title", 120) is not { Length: > 0 } title) return LocalResponse.Error(400, "\"title\" is required.");
        if (!AllowAlert()) return LocalResponse.Error(429, $"More than {MaxAlertsPerMinute} alerts a minute; slow down.");
        if (Link(body, out var url) is { } linkError) return linkError;

        var color = Color(body) ?? Ui.Blue;
        var seconds = Number(body, "seconds") is { } s ? Math.Clamp(s, 1, 60) : 5;
        var sticky = body.TryGetProperty("sticky", out var st) && st.ValueKind == JsonValueKind.True;

        _manager.ShowAlert(new IslandAlert
        {
            Content = Ui.AlertView(Ui.GlyphCircle(Glyph(body, "info"), color, 46, 20), title, Text(body, "message", 300) ?? ""),
            Size = new Size(400, 76),
            Duration = TimeSpan.FromSeconds(seconds),
            StickyUntilHover = sticky,
            OnClick = url is null ? null : () => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }),
        });
        return LocalResponse.Ok;
    }

    private bool AllowAlert()
    {
        var now = DateTime.UtcNow;
        while (_recentAlerts.Count > 0 && now - _recentAlerts.Peek() > TimeSpan.FromMinutes(1)) _recentAlerts.Dequeue();
        if (_recentAlerts.Count >= MaxAlertsPerMinute) return false;
        _recentAlerts.Enqueue(now);
        return true;
    }

    // ───────────────────────────── progress ─────────────────────────────

    private sealed class ProgressItem
    {
        public required AddonActivity Activity { get; init; }
        public required Border Ring { get; init; }
        public required Border BubbleRing { get; init; }
        public required TextBlock Percent { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Detail { get; init; }
        public required Border Fill { get; init; }
        public Brush Color { get; set; } = Ui.Blue;
        public DateTime Updated { get; set; }
    }

    private LocalResponse Progress(JsonElement body)
    {
        if (Text(body, "id", 64) is not { Length: > 0 } id) return LocalResponse.Error(400, "\"id\" is required.");

        if (body.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
        {
            if (_progress.Remove(id, out var finished))
            {
                finished.Activity.Hide();
                if (Text(body, "message", 120) is { Length: > 0 } message)
                {
                    _manager.ShowAlert(new IslandAlert
                    {
                        Content = Ui.AlertView(Ui.GlyphCircle("", Ui.Green, 46, 20), message, Text(body, "detail", 300) ?? ""),
                        Size = new Size(400, 76),
                        Duration = TimeSpan.FromSeconds(4),
                    });
                }
            }
            return LocalResponse.Ok;
        }

        if (!_progress.TryGetValue(id, out var item))
        {
            if (_progress.Count >= MaxProgress) return LocalResponse.Error(429, $"At most {MaxProgress} progress items at once.");
            item = Create(id);
            _progress[id] = item;
        }

        double? value = Number(body, "value") is { } v ? Math.Clamp(v, 0, 1) : null;
        item.Color = Color(body) ?? item.Color;
        item.Title.Text = Text(body, "title", 120) ?? item.Title.Text;
        item.Detail.Text = Text(body, "detail", 200) ?? item.Detail.Text;
        item.Percent.Text = value is { } p ? $"{p * 100:0}%" : "…";
        item.Percent.Foreground = item.Title.Foreground = item.Color;
        item.Ring.Child = Ring(18, 2.6, value ?? 0.25, item.Color);
        item.BubbleRing.Child = Ring(22, 2.4, value ?? 0.25, item.Color);
        item.Fill.Width = 356 * (value ?? 0);
        item.Fill.Background = item.Color;
        item.Updated = DateTime.UtcNow;
        item.Activity.Show();
        return LocalResponse.Ok;
    }

    private ProgressItem Create(string id)
    {
        var ring = new Border { Width = 18, Height = 18 };
        var bubble = new Border { Width = 22, Height = 22 };
        var percent = Ui.Text("", 14, FontWeights.SemiBold, Ui.Blue, tabular: true);
        var title = Ui.Text(id, 15, FontWeights.SemiBold, Ui.Blue);
        var detail = Ui.Text("", 13, FontWeights.Normal, Ui.Grey);
        var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { Width = 356, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Track, Child = fill, Margin = new Thickness(0, 10, 0, 0) };
        var expanded = new StackPanel { Margin = new Thickness(22, 16, 22, 14), Children = { title, detail, track } };

        var activity = new AddonActivity($"API: {id}", 14, ring, percent, bubble, expanded, compactWidth: 220, expandedSize: new Size(400, 100));
        _manager.Register(activity);
        return new ProgressItem { Activity = activity, Ring = ring, BubbleRing = bubble, Percent = percent, Title = title, Detail = detail, Fill = fill };
    }

    /// <summary>A script that crashed never sends "done"; don't leave its progress in the island forever.</summary>
    private void ExpireStale()
    {
        foreach (var (id, item) in _progress.Where(p => DateTime.UtcNow - p.Value.Updated > ProgressExpiry).ToList())
        {
            item.Activity.Hide();
            _progress.Remove(id);
        }
    }

    private static Grid Ring(double size, double stroke, double progress, Brush color) => new()
    {
        Children =
        {
            new System.Windows.Shapes.Ellipse { Width = size, Height = size, Stroke = Ui.Track, StrokeThickness = stroke },
            Icons.Ring(progress, size, stroke, color),
        },
    };

    // ───────────────────────────── input ─────────────────────────────

    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = "", ["success"] = "", ["warning"] = "", ["error"] = "",
        ["download"] = "", ["upload"] = "", ["music"] = "", ["mail"] = "",
        ["timer"] = "", ["bell"] = "", ["code"] = "", ["home"] = "",
        ["star"] = "", ["heart"] = "", ["game"] = "", ["sync"] = "",
    };

    private static readonly Dictionary<string, Brush> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blue"] = Ui.Blue, ["green"] = Ui.Green, ["orange"] = Ui.Orange, ["red"] = Ui.Red, ["purple"] = Ui.Purple,
        ["teal"] = Ui.Teal, ["yellow"] = Ui.Amber, ["indigo"] = Ui.Indigo, ["gray"] = Ui.ButtonBg, ["grey"] = Ui.ButtonBg,
    };

    public static IReadOnlyCollection<string> IconNames => Glyphs.Keys;

    private static string Glyph(JsonElement body, string fallback) =>
        Text(body, "icon", 20) is { } name && Glyphs.TryGetValue(name, out var glyph) ? glyph : Glyphs[fallback];

    private static Brush? Color(JsonElement body)
    {
        if (Text(body, "color", 20) is not { } value) return null;
        if (Colors.TryGetValue(value, out var named)) return named;
        try
        {
            return value.StartsWith('#') && value.Length == 7 ? Ui.Brush(value) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Only web links: a click on an alert should never be able to start a program.</summary>
    private static LocalResponse? Link(JsonElement body, out string? url)
    {
        url = Text(body, "url", 2000);
        if (url is null) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? null
            : LocalResponse.Error(400, "\"url\" must be an http or https link.");
    }

    private static string? Text(JsonElement body, string name, int max)
    {
        if (!body.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return null;
        var text = v.GetString()!.Trim();
        return text.Length > max ? text[..max] + "…" : text;
    }

    private static double? Number(JsonElement body, string name) =>
        body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
