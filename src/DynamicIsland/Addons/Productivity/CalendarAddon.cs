using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using DynamicIsland.Updates;
using Ical.Net.CalendarComponents;
using IcalCalendar = Ical.Net.Calendar;

namespace DynamicIsland.Addons.Productivity;

/// <summary>
/// (11) "Meeting in 5 min" with a Join button for Teams/Zoom/Meet/Webex links. Reads calendar links (.ics)
/// that Outlook, Google Calendar and iCloud can publish; recurring meetings and time zones are handled.
/// </summary>
public sealed partial class CalendarAddon : Addon
{
    private sealed record Meeting(string Key, string Title, DateTime Start, DateTime End, string? Location, string? JoinUrl);

    private readonly HashSet<string> _announced = new();
    private List<Meeting> _meetings = new();
    private string _loadedFeeds = "";
    private DispatcherTimer? _check;
    private DispatcherTimer? _refresh;

    public CalendarAddon(AddonContext context) : base(context) { }

    public override string Name => "Calendar";
    protected override bool IsEnabled(AddonSettings o) => o.Calendar && o.CalendarFeeds.Count > 0;

    protected override void Start()
    {
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _refresh.Tick += async (_, _) => await Refresh();
        _refresh.Start();
        _check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _check.Tick += (_, _) => Check();
        _check.Start();
        _ = Refresh();
    }

    protected override void Stop()
    {
        _refresh?.Stop();
        _check?.Stop();
        _refresh = _check = null;
        _meetings.Clear();
    }

    private async Task Refresh()
    {
        var from = DateTime.UtcNow.AddMinutes(-5);
        var to = DateTime.UtcNow.AddDays(1);
        var meetings = new List<Meeting>();
        _loadedFeeds = string.Join("\n", Options.CalendarFeeds);
        foreach (var feed in Options.CalendarFeeds.ToList())
        {
            try
            {
                var url = Regex.Replace(feed.Trim(), "^webcals?://", "https://", RegexOptions.IgnoreCase);
                var ics = await UpdateChecker.Http.GetStringAsync(url);
                // Parsing and expanding recurrences can take a moment for big calendars; keep it off the UI thread.
                meetings.AddRange(await Task.Run(() => Parse(ics, from, to)));
            }
            catch (Exception ex)
            {
                Log.Info($"Calendar feed failed: {ex.Message}");
            }
        }
        _meetings = meetings;
        Check();
    }

    private static List<Meeting> Parse(string ics, DateTime fromUtc, DateTime toUtc)
    {
        var result = new List<Meeting>();
        var calendar = IcalCalendar.Load(ics);
        foreach (var occurrence in calendar.GetOccurrences(fromUtc, toUtc))
        {
            if (occurrence.Source is not CalendarEvent ev || ev.IsAllDay) continue;
            if (ev.Status?.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true) continue;
            var start = occurrence.Period.StartTime.AsUtc;
            var end = occurrence.Period.EndTime?.AsUtc ?? start.AddMinutes(30);
            var join = JoinLink($"{ev.Location} {ev.Url} {ev.Description}");
            result.Add(new Meeting($"{ev.Uid}|{start:O}", ev.Summary ?? "Meeting", start, end, ev.Location, join));
        }
        return result;
    }

    private void Check()
    {
        // Calendar links were added or removed in the settings: load them now, not at the next refresh.
        var feeds = string.Join("\n", Options.CalendarFeeds);
        if (feeds != _loadedFeeds)
        {
            _loadedFeeds = feeds;
            _ = Refresh();
            return;
        }

        var now = DateTime.UtcNow;
        var lead = TimeSpan.FromMinutes(Math.Max(1, Options.CalendarReminderMinutes));
        foreach (var m in _meetings.OrderBy(m => m.Start))
        {
            if (m.Start - now > lead || now > m.Start.AddMinutes(2) || !_announced.Add(m.Key)) continue;
            Announce(m, now);
        }
    }

    private void Announce(Meeting m, DateTime now)
    {
        var minutes = (int)Math.Ceiling((m.Start - now).TotalMinutes);
        var when = minutes <= 0 ? "Starting now" : $"In {minutes} min";
        var times = $"{m.Start.ToLocalTime():t}–{m.End.ToLocalTime():t}";
        var place = !string.IsNullOrWhiteSpace(m.Location) && m.JoinUrl is null ? $" · {m.Location}" : "";

        FrameworkElement? join = null;
        if (m.JoinUrl is { } url)
        {
            var button = Ui.Button(Ui.Text("Join", 14, FontWeights.SemiBold, System.Windows.Media.Brushes.Black),
                                   () => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }), Ui.Green, 64, 32);
            button.Padding = new Thickness(0);
            join = button;
        }

        Show(new IslandAlert
        {
            Content = Ui.AlertView(Ui.GlyphCircle("", Ui.Red, 46, 20), m.Title, $"{when} · {times}{place}", accessory: join),
            Size = new Size(420, 76),
            Duration = TimeSpan.FromSeconds(10),
            StickyUntilHover = true,
        });
    }

    /// <summary>The first online-meeting link in the text.</summary>
    private static string? JoinLink(string text)
    {
        var match = JoinPattern().Match(text);
        return match.Success ? match.Value.TrimEnd('.', ',', ')', '>') : null;
    }

    [GeneratedRegex(@"https://[^\s""<>]*(teams\.microsoft\.com/l/meetup-join|teams\.live\.com/meet|zoom\.us/(j|my|w)/|meet\.google\.com/|webex\.com/(meet|join)|whereby\.com/)[^\s""<>]*",
                    RegexOptions.IgnoreCase)]
    private static partial Regex JoinPattern();
}
