using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.Updates;
using Xunit;

namespace DynamicIsland.Tests;

/// <summary>Quiet times, the user's activity order, the Recent list, ARM64 updates and the translation tables.</summary>
public class NewFeatureTests
{
    // ───────────── quiet times ─────────────

    [Theory]
    [InlineData("22:00", "08:00", "23:30", true)]  // across midnight
    [InlineData("22:00", "08:00", "07:59", true)]
    [InlineData("22:00", "08:00", "08:00", false)]
    [InlineData("22:00", "08:00", "12:00", false)]
    [InlineData("13:00", "14:00", "13:30", true)]  // same day
    [InlineData("13:00", "14:00", "14:30", false)]
    public void Quiet_hours(string from, string to, string now, bool quiet)
    {
        var rule = new AddonQuiet { HoursOn = true, From = from, To = to };
        Assert.Equal(quiet, rule.IsQuiet(TimeSpan.Parse(now), null));
    }

    [Fact]
    public void Quiet_while_an_app_is_in_front()
    {
        var rule = new AddonQuiet { Apps = { "OBS64.exe" } };
        Assert.True(rule.IsQuiet(TimeSpan.FromHours(12), "obs64"));
        Assert.False(rule.IsQuiet(TimeSpan.FromHours(12), "chrome"));
        Assert.False(rule.IsQuiet(TimeSpan.FromHours(12), null));
        Assert.False(new AddonQuiet { HoursOn = false }.IsQuiet(TimeSpan.FromHours(23), null)); // hours switched off
    }

    // ───────────── activity order ─────────────

    private sealed class FakeActivity(string kind, int priority) : Activity
    {
        public override string Name => kind;
        public override string Kind => kind;
        public override int Priority => priority;
        public override FrameworkElement Leading => new Border();
        public override FrameworkElement Trailing => new Border();
        public override FrameworkElement Bubble => new Border();
        public override FrameworkElement Expanded => new Border();
        public void Start() => IsActive = true;
    }

    [Fact]
    public void The_users_order_beats_the_built_in_priority() => AlertQueueTestsSta.Run(() =>
    {
        var manager = new ActivityManager();
        var timer = new FakeActivity("Timer", 30);
        var media = new FakeActivity("Media", 10);
        manager.Register(timer);
        manager.Register(media);
        timer.Start();
        media.Start();
        Assert.Same(timer, manager.Active[0]); // built-in

        manager.Order = new[] { "Media", "Timer" };
        Assert.Same(media, manager.Active[0]);

        manager.Order = Array.Empty<string>();
        Assert.Same(timer, manager.Active[0]);
    });

    [Fact]
    public void Saved_order_is_completed_with_new_kinds()
    {
        var order = ActivityKinds.Ordered(new[] { "Media", "Unknown", "Timer", "Media" });
        Assert.Equal("Media", order[0]);
        Assert.Equal("Timer", order[1]);
        Assert.DoesNotContain("Unknown", order);
        Assert.Equal(ActivityKinds.All.Length, order.Count);
    }

    // ───────────── Recent ─────────────

    [Fact]
    public void Recent_keeps_shown_and_held_back_alerts_but_not_bars_or_previews() => AlertQueueTestsSta.Run(() =>
    {
        var manager = new ActivityManager();
        IslandAlert Alert(string title, bool urgent = false) =>
            new() { Content = new TextBlock { Text = title }, Urgent = urgent };

        manager.ShowAlert(Alert("Shown"));
        manager.ShowAlert(Alert("Volume bar", urgent: true));
        manager.Quiet = true;
        manager.ShowAlert(Alert("Held back"));
        manager.Quiet = false;
        manager.Recording = false;
        manager.ShowAlert(Alert("Preview"));
        manager.Recording = true;

        var titles = manager.History.Select(h => h.Title).ToList();
        Assert.Equal(new[] { "Held back", "Shown" }, titles);
        Assert.True(manager.History[0].Missed);

        for (var i = 0; i < 15; i++) manager.Miss(Alert($"Alert {i}"));
        Assert.Equal(10, manager.History.Count);
    });

    // ───────────── updates ─────────────

    [Fact]
    public void Each_build_updates_to_its_own_architecture()
    {
        const string json = """
            { "tag_name": "v9.0.0", "html_url": "https://example.test", "body": "",
              "assets": [
                { "name": "DI4W-v9.0.0-win-x64.msi", "size": 1, "browser_download_url": "https://example.test/x64.msi" },
                { "name": "DI4W-v9.0.0-win-arm64.msi", "size": 2, "browser_download_url": "https://example.test/arm64.msi" } ] }
            """;
        Assert.Equal("DI4W-v9.0.0-win-x64.msi", UpdateChecker.Parse(json, new Version(1, 0, 0), "x64")?.Msi?.Name);
        Assert.Equal("DI4W-v9.0.0-win-arm64.msi", UpdateChecker.Parse(json, new Version(1, 0, 0), "arm64")?.Msi?.Name);
    }

    // ───────────── translation tables ─────────────

    [Fact]
    public void Translation_tables_have_no_duplicate_keys()
    {
        // A repeated key silently overwrites the first translation; catch it in the source.
        var dir = Path.Combine(FindSrc(), "DynamicIsland", "Settings");
        foreach (var file in Directory.GetFiles(dir, "Strings.*.cs"))
        {
            var keys = Regex.Matches(File.ReadAllText(file), @"^\s*\[""((?:[^""\\]|\\.)*)""\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
            var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, $"{Path.GetFileName(file)} repeats: {string.Join(", ", duplicates)}");
        }
    }

    private static string FindSrc()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "DynamicIsland.Settings"))) return src;
        }
        throw new DirectoryNotFoundException("Couldn't find the src folder from the test output.");
    }
}

/// <summary>WPF elements and timers need an STA thread.</summary>
internal static class AlertQueueTestsSta
{
    public static void Run(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
