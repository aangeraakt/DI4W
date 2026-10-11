using System.Reflection;
using CommunityToolkit.WinUI.Controls;
using DynamicIsland.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DynamicIsland.SettingsApp.Pages;

/// <summary>Switches and options for every add-on, grouped like the list they came from.</summary>
public sealed partial class AddonsPage : Page
{
    private static AddonSettings Options => SettingsStore.Current.Addons;

    public AddonsPage()
    {
        InitializeComponent();

        Section("System");
        Toggle(nameof(AddonSettings.Volume), "Volume", "A bar in the island when you change the volume.", "");
        Toggle(nameof(AddonSettings.Brightness), "Brightness", "A bar when the screen brightness changes. Works on built-in laptop and tablet screens.", "");
        Expander(nameof(AddonSettings.Battery), "Battery", "Charger plugged in or unplugged, and warnings when the battery runs low. Laptops only.", "",
                 Number("Low battery warning at", nameof(AddonSettings.BatteryLowPercent), 5, 50, "%"),
                 Number("Very low battery warning at", nameof(AddonSettings.BatteryCriticalPercent), 1, 30, "%"));
        Toggle(nameof(AddonSettings.Bluetooth), "Bluetooth devices", "Headphones, mice and controllers connecting or disconnecting, with their battery level when available.", "");
        Toggle(nameof(AddonSettings.UsbDrives), "USB drives", "USB sticks and external drives plugged in or removed. Click the pop-up to open the drive.", "");
        Toggle(nameof(AddonSettings.Network), "Wi-Fi and internet", "When the internet drops or comes back, and which network you joined.", "");
        Toggle(nameof(AddonSettings.PrivacyIndicators), "Microphone and camera in use", "An orange (microphone) or green (camera) dot next to the island while an app uses them.", "");
        Toggle(nameof(AddonSettings.LockKeys), "Caps Lock and Num Lock", "A short message when you turn them on or off.", "");
        Toggle(nameof(AddonSettings.PerformanceWarnings), "Heavy load warnings", "When the CPU or memory stays nearly full, or an NVIDIA graphics card runs hot.", "");

        Section("Productivity");
        Expander(nameof(AddonSettings.Calendar), "Calendar", "Meeting reminders with a Join button for Teams, Zoom, Google Meet and Webex.", "",
                 CalendarFeeds(),
                 Number("Remind me before a meeting", nameof(AddonSettings.CalendarReminderMinutes), 1, 60, "min"));
        Expander(null, "Focus timer", "Lengths for the Pomodoro timer. Start it by clicking the empty island and choosing Focus.", "",
                 Number("Focus", nameof(AddonSettings.FocusMinutes), 1, 180, "min"),
                 Number("Break", nameof(AddonSettings.BreakMinutes), 1, 60, "min"),
                 Number("Long break", nameof(AddonSettings.LongBreakMinutes), 1, 120, "min"),
                 Number("Long break after", nameof(AddonSettings.SessionsBeforeLongBreak), 1, 12, "sessions"));
        Toggle(nameof(AddonSettings.Clipboard), "Clipboard", "A short preview of what you copy. Passwords from password managers are never shown.", "");
        Toggle(nameof(AddonSettings.Screenshots), "Screenshots", "A thumbnail when a screenshot is saved. Click it to open the image.", "");
        Toggle(nameof(AddonSettings.Downloads), "Downloads", "Download progress in the island and \"Download complete\" for files saved to your Downloads folder.", "");
        Toggle(nameof(AddonSettings.FileCopy), "File copies", "Progress of copies and moves in File Explorer.", "");

        Section("Alerts");
        Toggle(nameof(AddonSettings.DoNotDisturb), "Follow Do Not Disturb", "While Windows' Do Not Disturb is on, only alarms and timers pop up, and a moon shows in the island.", "");
        Toggle(nameof(AddonSettings.WindowsUpdate), "Windows Update", "Tells you when a restart is needed to finish installing updates.", "");

        Section("Info");
        Expander(nameof(AddonSettings.Weather), "Weather", "The temperature in the empty island, and a warning before it starts raining. From Open-Meteo, no account needed.", "",
                 WeatherLocation(),
                 Switch("Fahrenheit", "Show °F instead of °C.", nameof(AddonSettings.WeatherFahrenheit)),
                 Switch("Rain alerts", "\"Rain in about 15 min\" when it's dry now.", nameof(AddonSettings.RainAlerts)));
        Toggle(nameof(AddonSettings.GameMode), "Game mode",
               "Keeps the island visible over fullscreen games, with GPU load and temperature. Frame rate isn't available: Windows only shares it with programs running as administrator.",
               "");
    }

    // ───────────────────────────── building blocks ─────────────────────────────

    private void Section(string title) =>
        Root.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SectionHeader"] });

    private void Toggle(string property, string header, string description, string glyph) => Root.Children.Add(new SettingsCard
    {
        Header = header,
        Description = description,
        HeaderIcon = new FontIcon { Glyph = glyph },
        Content = BoolSwitch(property),
    });

    /// <summary>A card that opens to show options; with a property it also has an on/off switch.</summary>
    private void Expander(string? property, string header, string description, string glyph, params SettingsCard[] items)
    {
        var expander = new SettingsExpander
        {
            Header = header,
            Description = description,
            HeaderIcon = new FontIcon { Glyph = glyph },
        };
        if (property is not null) expander.Content = BoolSwitch(property);
        foreach (var item in items) expander.Items.Add(item);
        Root.Children.Add(expander);
    }

    private static SettingsCard Switch(string header, string description, string property) =>
        new() { Header = header, Description = description, Content = BoolSwitch(property) };

    private static ToggleSwitch BoolSwitch(string property)
    {
        var prop = Property(property);
        var toggle = new ToggleSwitch { IsOn = (bool)prop.GetValue(Options)! };
        toggle.Toggled += (_, _) => SettingsStore.Change(s => prop.SetValue(s.Addons, toggle.IsOn));
        return toggle;
    }

    private static SettingsCard Number(string header, string property, int min, int max, string unit)
    {
        var prop = Property(property);
        var box = new NumberBox
        {
            Value = (int)prop.GetValue(Options)!,
            Minimum = min,
            Maximum = max,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 110,
        };
        box.ValueChanged += (_, e) =>
        {
            if (!double.IsNaN(e.NewValue)) SettingsStore.Change(s => prop.SetValue(s.Addons, (int)Math.Round(e.NewValue)));
        };
        return new SettingsCard
        {
            Header = header,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { box, new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
    }

    private static PropertyInfo Property(string name) =>
        typeof(AddonSettings).GetProperty(name) ?? throw new ArgumentException($"No add-on setting {name}");

    // ───────────────────────────── calendar links ─────────────────────────────

    private SettingsCard CalendarFeeds()
    {
        var list = new StackPanel { Spacing = 4 };
        var input = new TextBox { PlaceholderText = "https://…/calendar.ics", MinWidth = 320 };
        var add = new Button { Content = "Add", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

        void Render()
        {
            list.Children.Clear();
            foreach (var feed in Options.CalendarFeeds.ToList())
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = Shorten(feed), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(8) };
                ToolTipService.SetToolTip(remove, "Remove");
                remove.Click += (_, _) =>
                {
                    SettingsStore.Change(s => s.Addons.CalendarFeeds.Remove(feed));
                    Render();
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                list.Children.Add(row);
            }
        }

        void Add()
        {
            var url = input.Text.Trim();
            input.Text = "";
            if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("webcal", StringComparison.OrdinalIgnoreCase))
                || Options.CalendarFeeds.Contains(url)) return;
            SettingsStore.Change(s => s.Addons.CalendarFeeds.Add(url));
            Render();
        }

        add.Click += (_, _) => Add();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) Add();
        };
        Render();

        var help = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "Paste a calendar link (.ics). Outlook: Settings → Calendar → Shared calendars → Publish a calendar → copy the ICS link. " +
                   "Google Calendar: Settings → your calendar → Secret address in iCal format. Anyone with the link can see the calendar, so keep it private.",
        };
        var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { input, add } };

        return new SettingsCard
        {
            Header = "Calendar links",
            ContentAlignment = ContentAlignment.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 10, Children = { help, list, entry } },
        };
    }

    // "https://outlook.office365.com/owa/calendar/abc…/calendar.ics" -> "outlook.office365.com/…/calendar.ics"
    private static string Shorten(string url)
    {
        if (!Uri.TryCreate(url.Replace("webcal://", "https://"), UriKind.Absolute, out var uri)) return url;
        var file = uri.Segments.LastOrDefault() ?? "";
        return $"{uri.Host}/…/{file}";
    }

    // ───────────────────────────── weather ─────────────────────────────

    private static SettingsCard WeatherLocation()
    {
        var box = new TextBox { Text = Options.WeatherLocation, PlaceholderText = "e.g. Amsterdam", MinWidth = 220 };
        void Save() => SettingsStore.Change(s => s.Addons.WeatherLocation = box.Text.Trim());
        box.LostFocus += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) Save();
        };
        return new SettingsCard
        {
            Header = "City",
            Description = "Leave empty to use your Windows location (Settings → Privacy & security → Location → let desktop apps access your location).",
            Content = box,
        };
    }
}
