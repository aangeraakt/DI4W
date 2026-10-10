using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Activities.Claude;

/// <summary>
/// "Claude is working" live activity plus the "done" / "needs input" alerts,
/// driven by Claude Code hooks (see tools/claude-hook.ps1).
/// </summary>
public sealed class ClaudeActivity : Activity
{
    private sealed record Session(string Id, string Project, DateTime Started);

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private readonly ActivityManager _manager;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Session> _sessions = new();
    private readonly DispatcherTimer _tick;

    private readonly TextBlock _compactText = Ui.Text("", 14, FontWeights.SemiBold, Ui.ClaudeOrange, tabular: true);
    private readonly StackPanel _sessionList = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _expandedTitle = Ui.Text("Claude is working", 16, FontWeights.SemiBold, Ui.White);

    public ClaudeActivity(ActivityManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;

        Leading = Spinning(Icons.Claude(18), 6);
        var trailing = new StackPanel { Orientation = Orientation.Horizontal };
        trailing.Children.Add(_compactText);
        Trailing = trailing;
        Bubble = Spinning(Icons.Claude(18), 6);
        Expanded = BuildExpanded();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Update();
    }

    public override string Name => "Claude";
    public override int Priority => 20;
    public override double CompactWidth => 250;
    public override Size ExpandedSize => new(400, 92 + Math.Min(_sessions.Count, 4) * 24);

    public override FrameworkElement Leading { get; }
    public override FrameworkElement Trailing { get; }
    public override FrameworkElement Bubble { get; }
    public override FrameworkElement Expanded { get; }

    /// <summary>Handles one hook event. Runs on the UI thread.</summary>
    public void Handle(string kind, JsonElement payload)
    {
        var id = Str(payload, "session_id") ?? "default";
        var cwd = Str(payload, "cwd");
        var project = string.IsNullOrWhiteSpace(cwd) ? "Claude Code" : Path.GetFileName(cwd.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(project)) project = cwd ?? "Claude Code";

        switch (kind)
        {
            case "start":
                _sessions[id] = new Session(id, project, DateTime.Now);
                break;

            case "stop":
                var took = _sessions.TryGetValue(id, out var s) ? DateTime.Now - s.Started : (TimeSpan?)null;
                _sessions.Remove(id);
                ShowDone(project, took);
                break;

            case "notify":
                var message = Str(payload, "message") ?? "Claude needs your input";
                ShowNeedsInput(project, message);
                break;

            case "end":
                _sessions.Remove(id);
                break;
        }
        Update();
    }

    private void Update()
    {
        // Esc-interrupted turns never send "stop"; drop sessions that have been silent too long.
        foreach (var stale in _sessions.Values.Where(x => DateTime.Now - x.Started > StaleAfter).ToList())
        {
            _sessions.Remove(stale.Id);
        }

        if (_sessions.Count == 0)
        {
            _tick.Stop();
            IsActive = false;
            return;
        }

        var oldest = _sessions.Values.MinBy(x => x.Started)!;
        _compactText.Text = _sessions.Count > 1
            ? $"{_sessions.Count} working"
            : Ui.FormatTime(DateTime.Now - oldest.Started);

        _expandedTitle.Text = _sessions.Count > 1 ? $"Claude is working · {_sessions.Count} sessions" : "Claude is working";
        _sessionList.Children.Clear();
        foreach (var session in _sessions.Values.OrderBy(x => x.Started).Take(4))
        {
            var row = new Grid { Height = 24 };
            row.Children.Add(Ui.Text(session.Project, 14, FontWeights.Normal, Ui.Grey));
            var t = Ui.Text(Ui.FormatTime(DateTime.Now - session.Started), 14, FontWeights.Medium, Ui.ClaudeOrange, tabular: true);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(t);
            _sessionList.Children.Add(row);
        }

        _tick.Start();
        var wasActive = IsActive;
        IsActive = true;
        if (wasActive) RaiseChanged(); // session count may change the expanded height
    }

    private void ShowDone(string project, TimeSpan? took)
    {
        var icon = Ui.GlyphCircle("", Ui.Brush("#2A1A14"), 46, 1);
        icon.Child = Icons.Claude(26);
        var check = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Ui.Green,
            Child = Icons.Stroke(Icons.Check, 13, Brushes.Black, 2.6),
        };
        var subtitle = took is { } t && t.TotalSeconds >= 1 ? $"{project} · {Ui.FormatTime(t)}" : project;
        _manager.ShowAlert(new IslandAlert
        {
            Content = Ui.AlertView(icon, "Claude is done", subtitle, accessory: check),
            Size = new Size(380, 76),
            Duration = TimeSpan.FromSeconds(5),
        });
        Chime("Windows Notify Messaging.wav");
    }

    private void ShowNeedsInput(string project, string message)
    {
        var icon = Ui.GlyphCircle("", Ui.Brush("#2A1A14"), 46, 1);
        icon.Child = Icons.Claude(26);
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Ui.Amber, Margin = new Thickness(0, 0, 6, 0) };
        _manager.ShowAlert(new IslandAlert
        {
            Content = Ui.AlertView(icon, $"Claude needs you · {project}", message, Ui.Amber, dot),
            Size = new Size(420, 80),
            StickyUntilHover = true,
        });
        Chime("Windows Notify System Generic.wav");
    }

    private void Chime(string file)
    {
        if (!_settings.Sounds) return;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", file);
            if (File.Exists(path)) new SoundPlayer(path).Play();
        }
        catch (Exception ex)
        {
            Log.Error("Chime failed", ex);
        }
    }

    private FrameworkElement BuildExpanded()
    {
        var root = new Grid { Margin = new Thickness(20, 18, 24, 14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logo = Spinning(Icons.Claude(40), 8);
        logo.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(logo);

        var stack = new StackPanel { Margin = new Thickness(16, 2, 0, 0) };
        stack.Children.Add(_expandedTitle);
        stack.Children.Add(_sessionList);
        Grid.SetColumn(stack, 1);
        root.Children.Add(stack);
        return root;
    }

    /// <summary>The spark slowly rotates while Claude is thinking.</summary>
    private static FrameworkElement Spinning(FrameworkElement element, double secondsPerTurn)
    {
        var rotate = new RotateTransform();
        element.RenderTransform = rotate;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(secondsPerTurn)) { RepeatBehavior = RepeatBehavior.Forever });
        return element;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
