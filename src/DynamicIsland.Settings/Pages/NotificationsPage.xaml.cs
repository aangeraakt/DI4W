using CommunityToolkit.WinUI.Controls;
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
        AppsExpander.IsEnabled = NotificationsEnabled.IsOn;
        RenderApps();
        _loading = false;
    }

    private void OnEnabled(object sender, RoutedEventArgs e)
    {
        if (AppsExpander is not null) AppsExpander.IsEnabled = NotificationsEnabled.IsOn;
        if (!_loading) SettingsStore.Change(s => s.NotificationsEnabled = NotificationsEnabled.IsOn);
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
        SettingsStore.Change(s => s.NotificationApps.Add(name));
        RenderApps();
    }

    private void RenderApps()
    {
        AppsExpander.Items.Clear();
        foreach (var app in SettingsStore.Current.NotificationApps)
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
