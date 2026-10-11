using CommunityToolkit.WinUI.Controls;
using DynamicIsland.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DynamicIsland.SettingsApp.Pages;

public sealed partial class NotificationsPage : Page
{
    private readonly bool _loading = true;

    public NotificationsPage()
    {
        InitializeComponent();
        NotificationsEnabled.IsOn = SettingsStore.Current.NotificationsEnabled;
        Calls.IsOn = SettingsStore.Current.Addons.Calls;
        RenderChatApps();
        RenderApps();
        UpdateEnabled();
        var access = SettingsStore.IslandRunning ? IslandStatus.TryLoad()?.NotificationAccess : null;
        AccessBar.IsOpen = access is not null && access != "Allowed";
        _loading = false;
    }

    private async void OnOpenPrivacy(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-notifications"));

    private void OnEnabled(object sender, RoutedEventArgs e)
    {
        UpdateEnabled();
        if (!_loading) SettingsStore.Change(s => s.NotificationsEnabled = NotificationsEnabled.IsOn);
    }

    private void OnCalls(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsStore.Change(s => s.Addons.Calls = Calls.IsOn);
    }

    private void UpdateEnabled()
    {
        if (AppsExpander is null) return;
        var on = NotificationsEnabled.IsOn;
        AppsExpander.IsEnabled = CallsCard.IsEnabled = on;
        foreach (var card in ChatAppsPanel.Children.OfType<Control>()) card.IsEnabled = on;
    }

    /// <summary>One switch per built-in chat app; it adds or removes the app from the notification list.</summary>
    private void RenderChatApps()
    {
        foreach (var app in ChatApps.All)
        {
            var toggle = new ToggleSwitch { IsOn = IsListed(app.Name) };
            toggle.Toggled += (_, _) => SettingsStore.Change(s =>
            {
                s.NotificationApps.RemoveAll(a => a.Equals(app.Name, StringComparison.OrdinalIgnoreCase));
                if (toggle.IsOn) s.NotificationApps.Add(app.Name);
            });
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 16, Height = 16,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColor(app.Color)),
            };
            // Brand-colored dot + name as the header, like the island's fallback icon.
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 12,
                Children = { dot, new TextBlock { Text = app.Name, VerticalAlignment = VerticalAlignment.Center } },
            };
            ChatAppsPanel.Children.Add(new SettingsCard
            {
                Header = header,
                Description = app.HasCalls ? "Messages and calls" : "New mail",
                Content = toggle,
            });
        }
    }

    private static bool IsListed(string name) =>
        SettingsStore.Current.NotificationApps.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Windows.UI.Color ParseColor(string hex)
    {
        var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return Windows.UI.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private async void OnOpenWindowsSettings(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:notifications"));

    private void OnNewAppKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) OnAdd(sender, e);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var name = NewApp.Text.Trim();
        NewApp.Text = "";
        if (name.Length == 0) return;
        if (SettingsStore.Current.NotificationApps.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        SettingsStore.Change(s =>
        {
            if (!s.NotificationApps.Contains(name, StringComparer.OrdinalIgnoreCase)) s.NotificationApps.Add(name);
        });
        RenderApps();
    }

    private void RenderApps()
    {
        AppsExpander.Items.Clear();
        // Chat apps have their own switches above; this list is for everything else.
        foreach (var app in SettingsStore.Current.NotificationApps.Where(a => ChatApps.Find(a) is null))
        {
            var remove = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                Padding = new Thickness(8),
            };
            ToolTipService.SetToolTip(remove, $"Remove {app}");
            remove.Click += (_, _) =>
            {
                SettingsStore.Change(s => s.NotificationApps.Remove(app));
                RenderApps();
            };
            AppsExpander.Items.Add(new SettingsCard { Header = app, Content = remove });
        }
    }
}
