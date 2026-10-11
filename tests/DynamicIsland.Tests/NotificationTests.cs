using DynamicIsland.Activities.Notifications;
using DynamicIsland.Addons.Productivity;
using DynamicIsland.Settings;
using Xunit;

namespace DynamicIsland.Tests;

public class NotificationTests
{
    [Theory]
    [InlineData("Discord", "Discord")]
    [InlineData("Telegram Desktop", "Telegram")]
    [InlineData("Microsoft Teams (work or school)", "Microsoft Teams")]
    [InlineData("whatsapp", "WhatsApp")]
    public void Finds_chat_app_presets(string displayName, string preset) =>
        Assert.Equal(preset, ChatApps.Find(displayName)?.Name);

    [Theory]
    [InlineData("Spotify")]
    [InlineData("Steam")]
    public void Other_apps_have_no_preset(string displayName) => Assert.Null(ChatApps.Find(displayName));

    [Theory]
    [InlineData("Anna", "Incoming call")]
    [InlineData("Incoming video call", "Sam")]
    [InlineData("Jan", "is calling you")]
    [InlineData("Inkomende video-oproep", "Jan")]
    [InlineData("Mark", "belt je")]
    [InlineData("Lisa", "Inkomend spraakgesprek")]
    public void Recognises_calls(string title, string body) =>
        Assert.True(NotificationPresenter.IsCall(new IncomingNotification("Discord", title, body, null)));

    [Theory]
    [InlineData("#general", "Anna: are we still calling it a day?")]
    [InlineData("Jan", "I called you earlier")]
    [InlineData("Mark", "Missed call")]
    public void Ignores_ordinary_messages(string title, string body) =>
        Assert.False(NotificationPresenter.IsCall(new IncomingNotification("Discord", title, body, null)));

    [Theory]
    [InlineData(@"C:\Downloads\photo.jpg.crdownload", "photo.jpg")]
    [InlineData(@"C:\Downloads\setup.exe.part", "setup.exe")]
    [InlineData(@"C:\Downloads\Unconfirmed 123456.crdownload", "New download")]
    public void Shows_download_names_without_the_temporary_extension(string path, string expected) =>
        Assert.Equal(expected, DownloadsAddon.DisplayName(path));
}
