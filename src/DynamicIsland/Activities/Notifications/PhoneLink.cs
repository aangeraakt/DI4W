using System.Diagnostics;
using DynamicIsland.Core;

namespace DynamicIsland.Activities.Notifications;

/// <summary>
/// Windows' Phone Link app, which pairs with an iPhone over Bluetooth and shows the iPhone's notifications and
/// calls as Windows notifications. The island reads those like any other notification; this recognizes them and
/// works out which iPhone app each one is from.
/// </summary>
public static class PhoneLink
{
    public const string PackageFamily = "Microsoft.YourPhone_8wekyb3d8bbwe";

    /// <summary>Phone Link's notifications, by app id (its display name differs per Windows language).</summary>
    public static bool IsPhoneLink(string? appUserModelId) =>
        appUserModelId is not null && appUserModelId.StartsWith("Microsoft.YourPhone_", StringComparison.OrdinalIgnoreCase);

    /// <summary>iPhone apps by the names Phone Link shows (several languages), with a brand color for the icon.</summary>
    private static readonly (string[] Names, string App, string Color)[] Apps =
    {
        (new[] { "Messages", "iMessage", "Berichten", "Nachrichten", "Messages (iMessage)" }, "Messages", "#30D158"),
        (new[] { "WhatsApp" }, "WhatsApp", "#25D366"),
        (new[] { "Telegram" }, "Telegram", "#2AABEE"),
        (new[] { "Instagram" }, "Instagram", "#E1306C"),
        (new[] { "Snapchat" }, "Snapchat", "#FFC800"),
        (new[] { "Messenger" }, "Messenger", "#0084FF"),
        (new[] { "Signal" }, "Signal", "#3A76F0"),
        (new[] { "Discord" }, "Discord", "#5865F2"),
        (new[] { "Slack" }, "Slack", "#E01E5A"),
        (new[] { "Teams", "Microsoft Teams" }, "Teams", "#5B5FC7"),
        (new[] { "Mail", "E-mail", "Gmail", "Outlook" }, "Mail", "#0A84FF"),
        (new[] { "FaceTime" }, "FaceTime", "#30D158"),
        (new[] { "Phone", "Telefoon", "Telefon", "Téléphone" }, "Phone", "#30D158"),
        (new[] { "X", "Twitter" }, "X", "#3A3A3C"),
    };

    public sealed record Parsed(string? App, string Title, string Body);

    /// <summary>
    /// The notification's texts as Phone Link wrote them, split into the iPhone app (when one of the texts is an
    /// app name), the title (usually the sender) and the message.
    /// </summary>
    public static Parsed Describe(IReadOnlyList<string> texts)
    {
        string? app = null;
        var rest = new List<string>();
        foreach (var text in texts)
        {
            var known = app is null ? Apps.FirstOrDefault(a => a.Names.Any(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase))) : default;
            if (known.App is not null) app = known.App;
            else rest.Add(text.Trim());
        }
        var title = rest.Count > 0 ? rest[0] : app ?? "iPhone";
        return new Parsed(app, title, string.Join(" ", rest.Skip(1)));
    }

    /// <summary>The brand color of a recognized iPhone app, or null.</summary>
    public static string? ColorFor(string app) => Apps.FirstOrDefault(a => a.App == app).Color;

    /// <summary>Brings Phone Link to the front (to answer a call or read the whole conversation).</summary>
    public static void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{PackageFamily}!App") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Info($"Couldn't open Phone Link: {ex.Message}");
        }
    }
}
