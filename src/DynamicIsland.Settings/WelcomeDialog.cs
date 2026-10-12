using DynamicIsland.Settings;
using DynamicIsland.SettingsApp.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DynamicIsland.SettingsApp;

/// <summary>
/// The welcome tour after a fresh install (and from About): what the island is, the two shortcuts, and which
/// add-ons to use. Choices apply right away, so closing early keeps what was picked.
/// </summary>
public sealed class WelcomeDialog : ContentDialog
{
    /// <summary>The add-ons offered in the last step: the ones people notice first.</summary>
    private static readonly (string Property, string Label)[] Picks =
    {
        (nameof(AddonSettings.Volume), "Volume"),
        (nameof(AddonSettings.Brightness), "Brightness"),
        (nameof(AddonSettings.Battery), "Battery"),
        (nameof(AddonSettings.Bluetooth), "Bluetooth devices"),
        (nameof(AddonSettings.UsbDrives), "USB drives"),
        (nameof(AddonSettings.Network), "Wi-Fi and internet"),
        (nameof(AddonSettings.Clipboard), "Clipboard"),
        (nameof(AddonSettings.Screenshots), "Screenshots"),
        (nameof(AddonSettings.Downloads), "Downloads"),
        (nameof(AddonSettings.Calendar), "Calendar"),
        (nameof(AddonSettings.Weather), "Weather"),
        (nameof(AddonSettings.GameMode), "Game mode"),
    };

    private readonly FrameworkElement[] _steps;
    private readonly PipsPager _pips;
    private int _step;

    public WelcomeDialog()
    {
        _steps = new[] { Intro(), Shortcuts(), Addons() };
        _pips = new PipsPager
        {
            NumberOfPages = _steps.Length,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
            PreviousButtonVisibility = PipsPagerButtonVisibility.Collapsed,
            NextButtonVisibility = PipsPagerButtonVisibility.Collapsed,
        };
        _pips.SelectedIndexChanged += (_, _) =>
        {
            if (_pips.SelectedPageIndex != _step) Go(_pips.SelectedPageIndex);
        };

        var host = new Grid { MinHeight = 330 };
        foreach (var step in _steps) host.Children.Add(step);
        Content = new StackPanel { Width = 460, Children = { host, _pips } };

        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"]; // the Windows 11 look
        CloseButtonText = L.T("Skip");
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += (_, e) =>
        {
            if (_step < _steps.Length - 1)
            {
                e.Cancel = true;
                Go(_step + 1);
            }
        };
        SecondaryButtonClick += (_, e) =>
        {
            e.Cancel = true;
            Go(_step - 1);
        };
        Go(0);
    }

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, _steps.Length - 1);
        for (var i = 0; i < _steps.Length; i++) _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
        _pips.SelectedPageIndex = _step;
        Title = _step switch
        {
            0 => L.T("Welcome to Dynamic Island"),
            1 => L.T("Two shortcuts to remember"),
            _ => L.T("Pick what pops up"),
        };
        PrimaryButtonText = _step == _steps.Length - 1 ? L.T("Get started") : L.T("Next");
        SecondaryButtonText = _step == 0 ? "" : L.T("Back"); // no text hides the button
    }

    // ───────────────────────────── steps ─────────────────────────────

    private static FrameworkElement Intro()
    {
        var s = SettingsStore.Current;
        var preview = new IslandPreview { Margin = new Thickness(0, 4, 0, 12) };
        preview.Update(s.Scale, s.TopOffset, IslandTheme.IsLight(s.Theme));
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                preview,
                Paragraph(L.T("The island at the top of your screen shows what's going on, like on an iPhone: music, timers, Claude Code, messages from your chat apps and more.")),
                Tip("", L.T("Point at it to see more, and click it to open it.")),
                Tip("", L.T("Click the empty island to start a timer, focus session or stopwatch.")),
                Tip("", L.T("Right-click it for the menu. The tray icon has the same menu.")),
            },
        };
    }

    private static FrameworkElement Shortcuts()
    {
        var s = SettingsStore.Current;
        return new StackPanel
        {
            Spacing = 16,
            Children =
            {
                Paragraph(L.T("They work from any app.")),
                ShortcutRow(s.HideHotkey, L.T("Hide the island, and bring it back")),
                ShortcutRow(s.ExpandHotkey, L.T("Open what the island shows, without the mouse (screen readers read it out)")),
                Paragraph(L.T("Change them under General."), secondary: true),
            },
        };
    }

    private static FrameworkElement Addons()
    {
        var options = SettingsStore.Current.Addons;
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 0 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < Picks.Length; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var prop = typeof(AddonSettings).GetProperty(Picks[i].Property)!;
            var box = new CheckBox { Content = L.T(Picks[i].Label), IsChecked = (bool)prop.GetValue(options)! };
            box.Click += (_, _) => SettingsStore.Change(x => prop.SetValue(x.Addons, box.IsChecked == true));
            Grid.SetRow(box, i / 2);
            Grid.SetColumn(box, i % 2);
            grid.Children.Add(box);
        }

        var panel = new StackPanel
        {
            Spacing = 12,
            Children = { Paragraph(L.T("Turn on what you'd like to see. Calendar and weather need a link or a city, under Add-ons.")), grid },
        };

        if (SettingsStore.IslandExe is { } exe)
        {
            var autostart = new CheckBox { Content = L.T("Start Dynamic Island with Windows"), IsChecked = Autostart.IsEnabled, Margin = new Thickness(0, 8, 0, 0) };
            autostart.Click += (_, _) => Autostart.Set(autostart.IsChecked == true, exe);
            panel.Children.Add(autostart);
        }
        panel.Children.Add(Paragraph(L.T("There's more under Add-ons, each with a Preview button."), secondary: true));
        return panel;
    }

    // ───────────────────────────── building blocks ─────────────────────────────

    private static TextBlock Paragraph(string text, bool secondary = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (secondary) block.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        return block;
    }

    private static FrameworkElement Tip(string glyph, string text)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var label = Paragraph(text);
        Grid.SetColumn(label, 1);
        var row = new Grid { ColumnSpacing = 12, Children = { icon, label } };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        return row;
    }

    /// <summary>"Ctrl" "Alt" "I" as key caps, then what the shortcut does.</summary>
    private static FrameworkElement ShortcutRow(string hotkey, string what)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var parts = Hotkey.TryParse(hotkey, out _, out _) ? hotkey.Split('+') : new[] { L.T("Not set") };
        foreach (var part in parts)
        {
            keys.Children.Add(new Border
            {
                Padding = new Thickness(10, 4, 10, 5),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1, 1, 1, 2),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Child = new TextBlock { Text = part, FontWeight = FontWeights.SemiBold },
            });
        }
        return new StackPanel { Spacing = 6, Children = { keys, Paragraph(what) } };
    }
}
