using DynamicIsland.Core;
using DynamicIsland.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DynamicIsland.SettingsApp.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var version = typeof(AboutPage).Assembly.GetName().Version;
        VersionCard.Description = $"Version {version?.ToString(3)}";
        SettingsFileCard.Description = AppSettings.FilePath;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var running = SettingsStore.IslandRunning;
        Status.Text = running ? "Running" : "Not running";
        StartButton.Visibility = running || SettingsStore.IslandExe is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        SettingsStore.StartIsland();
        await Task.Delay(1500);
        UpdateStatus();
    }

    private void OnOpenSettingsFile(object sender, RoutedEventArgs e) => SettingsStore.Open(AppSettings.FilePath);

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Directory);
        SettingsStore.Open(Log.Directory);
    }
}
