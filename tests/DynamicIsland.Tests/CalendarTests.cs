using DynamicIsland.Addons.Productivity;
using Xunit;

namespace DynamicIsland.Tests;

public class CalendarTests
{
    // A weekly stand-up with a Teams link, a cancelled meeting, and an all-day event.
    private const string Ics = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:standup
        DTSTART:20261005T080000Z
        DTEND:20261005T081500Z
        RRULE:FREQ=WEEKLY;BYDAY=MO,WE
        SUMMARY:Stand-up
        DESCRIPTION:Join: https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=x
        END:VEVENT
        BEGIN:VEVENT
        UID:cancelled
        DTSTART:20261012T100000Z
        DTEND:20261012T110000Z
        SUMMARY:Old planning
        STATUS:CANCELLED
        END:VEVENT
        BEGIN:VEVENT
        UID:holiday
        DTSTART;VALUE=DATE:20261012
        DTEND;VALUE=DATE:20261013
        SUMMARY:Day off
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Expands_recurring_meetings_in_the_window()
    {
        // Monday 12 October 2026, whole day: one stand-up occurrence.
        var meetings = CalendarAddon.Parse(Ics, new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc));

        var standup = Assert.Single(meetings);
        Assert.Equal("Stand-up", standup.Title);
        Assert.Equal(new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), standup.Start);
        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/", standup.JoinUrl);
    }

    [Fact]
    public void Skips_cancelled_and_all_day_events()
    {
        var meetings = CalendarAddon.Parse(Ics, new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc));
        Assert.DoesNotContain(meetings, m => m.Title is "Old planning" or "Day off");
    }

    [Theory]
    [InlineData("Location: https://zoom.us/j/123456789?pwd=abc.", "https://zoom.us/j/123456789?pwd=abc")]
    [InlineData("<https://meet.google.com/abc-defg-hij>", "https://meet.google.com/abc-defg-hij")]
    [InlineData("Room 4.12", null)]
    [InlineData("https://example.com/agenda", null)]
    public void Finds_online_meeting_links(string text, string? expected) =>
        Assert.Equal(expected, CalendarAddon.JoinLink(text));
}
