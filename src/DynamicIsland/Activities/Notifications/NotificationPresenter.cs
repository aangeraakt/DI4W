using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Activities.Notifications;

/// <summary>
/// Turns incoming notifications into island alerts: (19) chat apps get their brand color and clicking switches
/// to the right app; (20) incoming calls become a large call card with answer/decline.
/// </summary>
public sealed partial class NotificationPresenter
{
    private readonly ActivityManager _manager;
    private readonly AppSettings _settings;

    public NotificationPresenter(ActivityManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
    }

    public void Show(IncomingNotification n)
    {
        if (n.FromIPhone)
        {
            ShowFromIPhone(n);
            return;
        }

        var preset = ChatApps.Find(n.App);
        var color = preset is null ? Ui.DiscordBlurple : Ui.Brush(preset.Color);
        FrameworkElement icon = n.Logo is not null ? Ui.Image(n.Logo, 46, 12) : Ui.GlyphCircle(Icons.ChatGlyph, color, 46, 20);
        var processes = preset?.Processes ?? new[] { n.App.Replace(" ", "") };
        void Open() => FocusFirst(processes);

        if (_settings.Addons.Calls && (preset?.HasCalls ?? false) && IsCall(n))
        {
            ShowCall(n, preset!.Name, icon, Open);
            return;
        }

        _manager.ShowAlert(new IslandAlert
        {
            Content = Ui.AlertView(icon, n.Title, n.Body),
            Size = new Size(400, string.IsNullOrEmpty(n.Body) || n.Body.Length < 40 ? 76 : 88),
            Duration = TimeSpan.FromSeconds(5),
            OnClick = Open,
        });
    }

    /// <summary>An iPhone notification through Phone Link: the iPhone app's color and a phone icon, "iPhone · app"
    /// under the message; calls get the call card, whose answer button brings Phone Link forward to take it.</summary>
    private void ShowFromIPhone(IncomingNotification n)
    {
        var color = PhoneLink.ColorFor(n.App) is { } hex ? Ui.Brush(hex) : Ui.ButtonBg;
        var icon = Ui.GlyphCircle("", color, 46, 20); // cell phone
        if (_settings.Addons.Calls && IsCall(n))
        {
            ShowCall(n, "iPhone", icon, PhoneLink.Open);
            return;
        }

        var source = n.App == "iPhone" ? "iPhone" : $"iPhone · {n.App}";
        var subtitle = string.IsNullOrWhiteSpace(n.Body) ? source : $"{n.Body}";
        var view = Ui.AlertView(icon, n.Title, subtitle,
                                accessory: string.IsNullOrWhiteSpace(n.Body) ? null : Ui.Text(source, 11.5, FontWeights.Medium, Ui.Grey));
        _manager.ShowAlert(new IslandAlert
        {
            Content = view,
            Size = new Size(420, string.IsNullOrEmpty(n.Body) || n.Body.Length < 40 ? 76 : 88),
            Duration = TimeSpan.FromSeconds(5),
            OnClick = PhoneLink.Open,
        });
    }

    private void ShowCall(IncomingNotification n, string app, FrameworkElement icon, Action open)
    {
        // The caller is usually the title; some apps put "Incoming call" there and the name in the body.
        var caller = CallPattern().IsMatch(n.Title) && !string.IsNullOrWhiteSpace(n.Body) ? n.Body : n.Title;
        IslandAlert? alert = null;

        var decline = Ui.CircleButton(Ui.Glyph("", 18, Ui.OnColor), Ui.Red, 44, () => _manager.DismissAlert(alert), L.T("Decline"));
        var answer = Ui.CircleButton(Ui.Glyph("", 18, Ui.OnColor), Ui.Green, 44, () =>
        {
            open();
            _manager.DismissAlert(alert);
        }, L.F("Answer in {0}", app));
        answer.Margin = new Thickness(10, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { decline, answer } };

        alert = new IslandAlert
        {
            Content = Ui.AlertView(icon, caller, L.F("Incoming call · {0}", app), accessory: buttons),
            Size = new Size(420, 84),
            Duration = TimeSpan.FromSeconds(30),
            OnClick = open,
        };
        _manager.ShowAlert(alert);
    }

    /// <summary>Call notifications from the supported apps, in English, Dutch, German and French (apps write them in
    /// the Windows language).</summary>
    internal static bool IsCall(IncomingNotification n) => CallPattern().IsMatch($"{n.Title} {n.Body}");

    [GeneratedRegex(@"incoming (voice |video |audio )?call|is calling|calling you|wants to (video )?call|ringing|" +
                    @"inkomende? (spraak|video)?\s?(oproep|gesprek)|belt (je|u)|wil je bellen|video-?oproep|spraakoproep|" +
                    @"eingehender (sprach|video)?\s?anruf|ruft (dich|sie) an|videoanruf|sprachanruf|" +
                    @"appel (vocal |vidéo |video )?entrant|vous appelle|t'appelle|appel vidéo",
                    RegexOptions.IgnoreCase)]
    private static partial Regex CallPattern();

    private static void FocusFirst(IEnumerable<string> processes)
    {
        foreach (var p in processes)
        {
            if (System.Diagnostics.Process.GetProcessesByName(p).Length == 0) continue;
            Win32.FocusProcess(p);
            return;
        }
    }
}
