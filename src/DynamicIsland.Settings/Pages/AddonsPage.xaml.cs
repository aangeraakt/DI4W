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

        Section(L.T("System"));
        Toggle(nameof(AddonSettings.Volume), L.T("Volume"), L.T("A bar in the island when you change the volume."), "");
        Toggle(nameof(AddonSettings.Brightness), L.T("Brightness"), L.T("A bar when the screen brightness changes. Works on built-in laptop and tablet screens."), "");
        Expander(nameof(AddonSettings.Battery), L.T("Battery"), L.T("Charger plugged in or unplugged, and warnings when the battery runs low. Laptops only."), "",
                 Number(L.T("Low battery warning at"), nameof(AddonSettings.BatteryLowPercent), 5, 50, L.T("%")),
                 Number(L.T("Very low battery warning at"), nameof(AddonSettings.BatteryCriticalPercent), 1, 30, L.T("%")));
        Toggle(nameof(AddonSettings.Bluetooth), L.T("Bluetooth devices"), L.T("Headphones, mice and controllers connecting or disconnecting, with their battery level when available."), "");
        Expander(nameof(AddonSettings.IPhone), L.T("iPhone"),
                 L.T("Your iPhone's notifications and calls in the island, and its battery level. Uses Windows' Phone Link app, which links the iPhone over Bluetooth."), "",
                 IPhoneSetup(),
                 Number(L.T("Low iPhone battery warning at"), nameof(AddonSettings.IPhoneBatteryLowPercent), 11, 50, L.T("%")));
        Toggle(nameof(AddonSettings.UsbDrives), L.T("USB drives"), L.T("USB sticks and external drives plugged in or removed. Click the pop-up to open the drive."), "");
        Toggle(nameof(AddonSettings.Network), L.T("Wi-Fi and internet"), L.T("When the internet drops or comes back, and which network you joined."), "");
        Toggle(nameof(AddonSettings.PrivacyIndicators), L.T("Microphone and camera in use"), L.T("An orange (microphone) or green (camera) dot next to the island while an app uses them."), "");
        Toggle(nameof(AddonSettings.LockKeys), L.T("Caps Lock and Num Lock"), L.T("A short message when you turn them on or off."), "");
        Toggle(nameof(AddonSettings.PerformanceWarnings), L.T("Heavy load warnings"), L.T("When the CPU or memory stays nearly full, or an NVIDIA graphics card runs hot."), "");

        Section(L.T("Productivity"));
        Expander(nameof(AddonSettings.Calendar), L.T("Calendar"), L.T("Meeting reminders with a Join button for Teams, Zoom, Google Meet and Webex."), "",
                 CalendarFeeds(),
                 Number(L.T("Remind me before a meeting"), nameof(AddonSettings.CalendarReminderMinutes), 1, 60, L.T("min")));
        Expander(null, L.T("Focus timer"), L.T("Lengths for the Pomodoro timer. Start it by clicking the empty island and choosing Focus."), "",
                 Number(L.T("Focus"), nameof(AddonSettings.FocusMinutes), 1, 180, L.T("min")),
                 Number(L.T("Break"), nameof(AddonSettings.BreakMinutes), 1, 60, L.T("min")),
                 Number(L.T("Long break"), nameof(AddonSettings.LongBreakMinutes), 1, 120, L.T("min")),
                 Number(L.T("Long break after"), nameof(AddonSettings.SessionsBeforeLongBreak), 1, 12, L.T("sessions")));
        Toggle(nameof(AddonSettings.Clipboard), L.T("Clipboard"), L.T("A short preview of what you copy. Passwords from password managers are never shown."), "");
        Toggle(nameof(AddonSettings.Screenshots), L.T("Screenshots"), L.T("A thumbnail when a screenshot is saved. Click it to open the image."), "");
        Toggle(nameof(AddonSettings.Downloads), L.T("Downloads"), L.T("Download progress in the island and \"Download complete\" for files saved to your Downloads folder."), "");
        Toggle(nameof(AddonSettings.FileCopy), L.T("File copies"), L.T("Progress of copies and moves in File Explorer."), "");

        Section(L.T("Alerts"));
        Toggle(nameof(AddonSettings.DoNotDisturb), L.T("Follow Do Not Disturb"), L.T("While Windows' Do Not Disturb is on, only alarms and timers pop up, and a moon shows in the island."), "");
        Toggle(nameof(AddonSettings.WindowsUpdate), L.T("Windows Update"), L.T("Tells you when a restart is needed to finish installing updates."), "");

        Section(L.T("Info"));
        Expander(nameof(AddonSettings.Weather), L.T("Weather"), L.T("The temperature in the empty island, and a warning before it starts raining. From Open-Meteo, no account needed."), "",
                 WeatherLocation(),
                 Switch(L.T("Fahrenheit"), L.T("Show °F instead of °C."), nameof(AddonSettings.WeatherFahrenheit)),
                 Switch(L.T("Rain alerts"), L.T("\"Rain in about 15 min\" when it's dry now."), nameof(AddonSettings.RainAlerts)));
        Toggle(nameof(AddonSettings.GameMode), L.T("Game mode"),
               L.T("Keeps the island visible over fullscreen games, with GPU load and temperature. Frame rate isn't available: Windows only shares it with programs running as administrator."),
               "");

        Section(L.T("For developers"));
        Expander(nameof(AddonSettings.IslandApi), L.T("Island API"),
                 L.T("Lets scripts and apps on this PC show their own pop-ups and progress in the island. Web pages can't use it. The island restarts to apply this."),
                 "", ApiExample());

        Layout();
        Search.PlaceholderText = L.T("Search add-ons");
        PreviewInfo.IsOpen = false;
        PreviewAction.Click += (_, _) => _previewAction?.Invoke();
        WatchStatus();
    }

    // ───────────────────────────── sections, search and filter ─────────────────────────────

    /// <summary>One card, its section, and the words search looks at.</summary>
    private sealed record Entry(FrameworkElement Card, string? Property, string Text, Group Group);

    private sealed class Group
    {
        public required TextBlock Header { get; init; }
        public List<Entry> Entries { get; } = new();
    }

    private readonly List<Group> _groups = new();

    private static bool IsOn(string? property) => property is null || Property(property).GetValue(Options) is true;

    /// <summary>Adds the sections to the page, with the add-ons that are on first in each (the order is fixed
    /// while the page is open, so a card doesn't jump away from the mouse when it's switched).</summary>
    private void Layout()
    {
        foreach (var group in _groups)
        {
            Root.Children.Add(group.Header);
            foreach (var entry in group.Entries.OrderBy(e => IsOn(e.Property) ? 0 : 1)) Root.Children.Add(entry.Card);
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var switches = _groups.SelectMany(g => g.Entries).Where(e => e.Property is not null).ToList();
        Summary.Text = L.F("{0} of {1} add-ons are on", switches.Count(e => IsOn(e.Property)), switches.Count);
    }

    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();

    private void OnFilter(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (Root is null || NoMatches is null) return; // during InitializeComponent
        var words = (Search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var any = false;
        foreach (var group in _groups)
        {
            var groupVisible = false;
            foreach (var entry in group.Entries)
            {
                var visible = words.All(w => entry.Text.Contains(w, StringComparison.CurrentCultureIgnoreCase))
                              && Filter.SelectedIndex switch
                              {
                                  1 => entry.Property is not null && IsOn(entry.Property),
                                  2 => entry.Property is not null && !IsOn(entry.Property),
                                  _ => true,
                              };
                entry.Card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                groupVisible |= visible;
            }
            group.Header.Visibility = groupVisible ? Visibility.Visible : Visibility.Collapsed;
            any |= groupVisible;
        }
        NoMatches.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    // ───────────────────────────── previews ─────────────────────────────

    /// <summary>Add-ons without a pop-up of their own to show.</summary>
    private static readonly HashSet<string> NoPreview = new() { nameof(AddonSettings.IslandApi) };

    private Action? _previewAction;

    private Button PreviewButton(string property)
    {
        var button = new Button { Content = L.T("Preview"), Margin = new Thickness(0, 0, 12, 0) };
        ToolTipService.SetToolTip(button, L.T("Show a sample in the island"));
        button.Click += async (_, _) => await ShowPreview(property);
        return button;
    }

    private async Task ShowPreview(string key)
    {
        if (!SettingsStore.IslandRunning)
        {
            Explain(L.T("The island isn't running"), L.T("Start it to see previews."), L.T("Start island"), () =>
            {
                SettingsStore.StartIsland();
                PreviewInfo.IsOpen = false;
            });
            return;
        }
        // Read the file: the island may have been hidden or shown with the shortcut since this window opened.
        if ((AppSettings.TryLoad() ?? SettingsStore.Current).Paused)
        {
            Explain(L.T("The island is hidden"), L.T("Show it again to see the preview."), L.T("Show island"), async () =>
            {
                PreviewInfo.IsOpen = false;
                SettingsStore.Change(s => s.Paused = false);
                SettingsStore.Flush();
                await Task.Delay(1200); // the island picks the change up, then fades in
                await Task.Run(() => IslandMessages.Send(IslandMessages.PreviewPrefix + key));
            });
            return;
        }
        PreviewInfo.IsOpen = false;
        if (!await Task.Run(() => IslandMessages.Send(IslandMessages.PreviewPrefix + key)))
        {
            Explain(L.T("The island didn't respond"), L.T("Try again in a moment, or restart it from the tray icon."), null, null);
        }
    }

    private void Explain(string title, string message, string? action, Action? onAction)
    {
        PreviewInfo.Title = title;
        PreviewInfo.Message = message;
        PreviewAction.Content = action;
        PreviewAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        _previewAction = onAction;
        PreviewInfo.IsOpen = true;
    }

    /// <summary>The on/off switch, with a Preview button in front of it when the add-on has a pop-up.</summary>
    private FrameworkElement Controls(string property)
    {
        var toggle = BoolSwitch(property);
        toggle.Toggled += (_, _) => UpdateSummary();
        if (NoPreview.Contains(property)) return toggle;
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { PreviewButton(property), toggle } };
    }

    /// <summary>How to link the iPhone, with a button that opens Phone Link.</summary>
    private static SettingsCard IPhoneSetup()
    {
        var steps = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = L.T("1. Open Phone Link, choose iPhone and follow the steps (it pairs over Bluetooth).\n2. On the iPhone, under Settings → Bluetooth → your PC, turn on Show Notifications and Sync Contacts.\n3. To see only the island's pop-up, turn off banners for Phone Link in Windows Settings → System → Notifications."),
        };
        var open = new Button { Content = L.T("Open Phone Link"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        open.Click += (_, _) => SettingsStore.Open(@"shell:AppsFolder\Microsoft.YourPhone_8wekyb3d8bbwe!App");
        var notifications = new HyperlinkButton { Content = L.T("Notification settings") };
        notifications.Click += (_, _) => SettingsStore.Open("ms-settings:notifications");
        return new SettingsCard
        {
            Header = L.T("Link your iPhone"),
            ContentAlignment = ContentAlignment.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 10,
                Children = { steps, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, notifications } } },
            },
        };
    }

    private static SettingsCard ApiExample()
    {
        var port = SettingsStore.Current.ClaudePort;
        var example =
            $"curl -X POST http://127.0.0.1:{port}/alert -H \"Content-Type: application/json\" -d \"{{\\\"title\\\":\\\"Backup done\\\",\\\"icon\\\":\\\"success\\\"}}\"\n\n" +
            L.T("PowerShell (the helper script is in the app's tools folder):") + "\n" +
            "island.ps1 -Title \"Backup done\" -Icon success\n" +
            "island.ps1 -Title \"Build failed\" -Icon error -Button \"log=Open log\" -Wait\n" +
            "island.ps1 -Progress backup -Title \"Backing up\" -Value 0.4";
        var text = new TextBlock
        {
            Text = example,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
        };
        var copy = new Button { Content = L.T("Copy example") };
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(example);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        };
        var docs = new HyperlinkButton { Content = L.T("Full API description"), NavigateUri = new Uri("https://github.com/aangeraakt/DI4W#island-api") };
        return new SettingsCard
        {
            Header = L.T("Examples"),
            ContentAlignment = ContentAlignment.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { text, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copy, docs } } },
            },
        };
    }

    // ───────────────────────────── building blocks ─────────────────────────────

    private void Section(string title) =>
        _groups.Add(new Group { Header = new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SectionHeader"] } });

    private void Add(FrameworkElement card, string? property, string header, string description)
    {
        var group = _groups[^1];
        group.Entries.Add(new Entry(card, property, $"{group.Header.Text} {header} {description}", group));
    }

    /// <summary>An add-on with just a switch; it opens to show its quiet times, when it has pop-ups.</summary>
    private void Toggle(string property, string header, string description, string glyph)
    {
        if (!NoQuiet.Contains(property))
        {
            Expander(property, header, description, glyph);
            return;
        }
        Add(new SettingsCard
        {
            Header = header,
            Description = Describe(property, description),
            HeaderIcon = new FontIcon { Glyph = glyph },
            Content = Controls(property),
        }, property, header, description);
    }

    // ───────────────────────────── quiet times ─────────────────────────────

    /// <summary>Add-ons without pop-ups of their own (a moon, a live view, the API) have no quiet times.</summary>
    private static readonly HashSet<string> NoQuiet = new()
    {
        nameof(AddonSettings.DoNotDisturb), nameof(AddonSettings.GameMode), nameof(AddonSettings.IslandApi),
        nameof(AddonSettings.IPhone), // its notifications come through Phone Link, which has its own settings
    };

    private static AddonQuiet QuietOf(string key) =>
        Options.Quiet.TryGetValue(key, out var q) ? q : new AddonQuiet();

    /// <summary>Saves a changed copy (the change may run twice, see SettingsStore.Change, so it sets a whole copy).</summary>
    private static void SaveQuiet(string key, AddonQuiet quiet)
    {
        var copy = quiet.Copy();
        SettingsStore.Change(s =>
        {
            if (copy.IsEmpty) s.Addons.Quiet.Remove(key);
            else s.Addons.Quiet[key] = copy.Copy();
        });
    }

    private static SettingsCard QuietCard(string key)
    {
        var quiet = QuietOf(key).Copy();

        // Hours: [x] Quiet between [22:00] and [08:00]
        var hours = new CheckBox { Content = L.T("Quiet between"), IsChecked = quiet.HoursOn, MinWidth = 0 };
        var from = new TimePicker { ClockIdentifier = "24HourClock", MinuteIncrement = 5, Time = Parse(quiet.From, 22), IsEnabled = quiet.HoursOn };
        var to = new TimePicker { ClockIdentifier = "24HourClock", MinuteIncrement = 5, Time = Parse(quiet.To, 8), IsEnabled = quiet.HoursOn };
        hours.Click += (_, _) =>
        {
            quiet.HoursOn = hours.IsChecked == true;
            from.IsEnabled = to.IsEnabled = quiet.HoursOn;
            SaveQuiet(key, quiet);
        };
        from.TimeChanged += (_, e) =>
        {
            quiet.From = e.NewTime.ToString(@"hh\:mm");
            SaveQuiet(key, quiet);
        };
        to.TimeChanged += (_, e) =>
        {
            quiet.To = e.NewTime.ToString(@"hh\:mm");
            SaveQuiet(key, quiet);
        };
        var hoursRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { hours, from, new TextBlock { Text = L.T("and"), VerticalAlignment = VerticalAlignment.Center }, to },
        };

        // Apps: chips with a remove button, and a drop-down of the apps that are open now.
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var add = new DropDownButton { Content = L.T("Add an open app") };
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        add.Flyout = menu;

        void RenderApps()
        {
            chips.Children.Clear();
            foreach (var app in quiet.Apps.ToList())
            {
                var chip = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Children = { new TextBlock { Text = app }, new FontIcon { Glyph = "", FontSize = 10 } },
                    },
                    Padding = new Thickness(10, 4, 8, 5),
                };
                ToolTipService.SetToolTip(chip, L.T("Remove"));
                chip.Click += (_, _) =>
                {
                    quiet.Apps.Remove(app);
                    SaveQuiet(key, quiet);
                    RenderApps();
                };
                chips.Children.Add(chip);
            }
        }

        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var name in OpenApps().Where(n => !quiet.Apps.Contains(n, StringComparer.OrdinalIgnoreCase)))
            {
                var item = new MenuFlyoutItem { Text = name };
                item.Click += (_, _) =>
                {
                    quiet.Apps.Add(name);
                    SaveQuiet(key, quiet);
                    RenderApps();
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = L.T("No other apps are open"), IsEnabled = false });
        };
        RenderApps();

        var appsRow = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L.T("Also quiet while one of these apps is in front:"), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { chips, add } },
            },
        };

        return new SettingsCard
        {
            Header = L.T("Quiet times"),
            Description = L.T("Its pop-ups stay away then. You'll still find them under Recent in the island."),
            ContentAlignment = ContentAlignment.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 12, Children = { hoursRow, appsRow } },
        };

        static TimeSpan Parse(string text, int fallbackHour) => TimeSpan.TryParse(text, out var t) ? t : TimeSpan.FromHours(fallbackHour);
    }

    /// <summary>Apps with a window open now (process names), for the quiet-times list.</summary>
    private static List<string> OpenApps()
    {
        var self = Environment.ProcessId;
        return System.Diagnostics.Process.GetProcesses()
            .Where(p =>
            {
                try
                {
                    return p.Id != self && p.MainWindowHandle != IntPtr.Zero && p.ProcessName is not ("DynamicIsland" or "explorer" or "TextInputHost" or "ApplicationFrameHost");
                }
                catch
                {
                    return false;
                }
            })
            .Select(p => p.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ───────────────────────────── live status from the island ─────────────────────────────

    private readonly Dictionary<string, TextBlock> _statusLines = new();
    private FileSystemWatcher? _statusWatcher;

    /// <summary>The description plus a status line the island fills in ("Set your city below").</summary>
    private object Describe(string? key, string description)
    {
        if (key is null) return description;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        _statusLines[key] = status;
        return new StackPanel { Children = { new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap }, status } };
    }

    private void WatchStatus()
    {
        ShowStatus();
        Directory.CreateDirectory(AppSettings.Folder);
        _statusWatcher = new FileSystemWatcher(AppSettings.Folder, Path.GetFileName(IslandStatus.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _statusWatcher.Changed += (_, _) => DispatcherQueue.TryEnqueue(ShowStatus);
        _statusWatcher.Renamed += (_, _) => DispatcherQueue.TryEnqueue(ShowStatus);
        Unloaded += (_, _) => _statusWatcher?.Dispose();
    }

    private void ShowStatus()
    {
        // A status file left behind by an island that isn't running would be stale; show nothing then.
        var status = SettingsStore.IslandRunning ? IslandStatus.TryLoad() : null;
        foreach (var (key, line) in _statusLines)
        {
            var state = status?.Addons.GetValueOrDefault(key);
            var enabled = Property(key).GetValue(Options) is true;
            line.Text = state?.Message ?? "";
            line.Visibility = enabled && !string.IsNullOrEmpty(state?.Message) ? Visibility.Visible : Visibility.Collapsed;
            line.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                state?.Problem == true ? "SystemFillColorCautionBrush" : "TextFillColorTertiaryBrush"];
        }
    }

    /// <summary>A card that opens to show options; with a property it also has an on/off switch.</summary>
    private void Expander(string? property, string header, string description, string glyph, params SettingsCard[] items)
    {
        var expander = new SettingsExpander
        {
            Header = header,
            Description = Describe(property, description),
            HeaderIcon = new FontIcon { Glyph = glyph },
        };
        if (property is not null) expander.Content = Controls(property);
        foreach (var item in items) expander.Items.Add(item);
        if (property is not null && !NoQuiet.Contains(property)) expander.Items.Add(QuietCard(property));
        Add(expander, property, header, description);
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
        var add = new Button { Content = L.T("Add"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

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
                ToolTipService.SetToolTip(remove, L.T("Remove"));
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
            SettingsStore.Change(s =>
            {
                if (!s.Addons.CalendarFeeds.Contains(url)) s.Addons.CalendarFeeds.Add(url);
            });
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
            Text = L.T("Paste a calendar link (.ics). Outlook: Settings → Calendar → Shared calendars → Publish a calendar → copy the ICS link. Google Calendar: Settings → your calendar → Secret address in iCal format. Anyone with the link can see the calendar, so keep it private."),
        };
        var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { input, add } };

        return new SettingsCard
        {
            Header = L.T("Calendar links"),
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
        var box = new TextBox { Text = Options.WeatherLocation, PlaceholderText = L.T("e.g. Amsterdam"), MinWidth = 220 };
        void Save() => SettingsStore.Change(s => s.Addons.WeatherLocation = box.Text.Trim());
        box.LostFocus += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) Save();
        };
        return new SettingsCard
        {
            Header = L.T("City"),
            Description = L.T("Leave empty to use your Windows location (Settings → Privacy & security → Location → let desktop apps access your location)."),
            Content = box,
        };
    }
}
