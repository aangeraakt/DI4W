using System.Text.RegularExpressions;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DynamicIsland.SettingsApp.Pages;

public sealed partial class AboutPage : Page
{
    private readonly bool _loading = true;
    private UpdateInfo? _update;
    private CancellationTokenSource? _download;

    public AboutPage()
    {
        InitializeComponent();
        VersionCard.Description = $"Version {UpdateChecker.CurrentVersion}";
        SettingsFileCard.Description = AppSettings.FilePath;
        AutoCheck.IsOn = SettingsStore.Current.AutoCheckUpdates;
        UpdateStatus();
        _loading = false;

        Loaded += async (_, _) => await CheckAsync();
        Unloaded += (_, _) => _download?.Cancel();
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

    // ───────────────────────────── updates ─────────────────────────────

    private void OnAutoCheck(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsStore.Change(s => s.AutoCheckUpdates = AutoCheck.IsOn);
    }

    private async void OnCheck(object sender, RoutedEventArgs e) => await CheckAsync();

    private async Task CheckAsync()
    {
        if (_download is not null) return; // busy installing
        CheckButton.IsEnabled = false;
        Checking.IsActive = true;
        try
        {
            _update = await UpdateChecker.CheckAsync();
            if (_update is null)
            {
                UpdateCard.Description = $"You're up to date. Last checked {DateTime.Now:t}.";
                UpdateBar.IsOpen = false;
                return;
            }

            UpdateCard.Description = $"Version {_update.Version} is available.";
            UpdateBar.Title = $"Version {_update.Version} is available";
            var size = Updater.PickAsset(_update)?.Size ?? 0;
            UpdateBar.Message = $"You have {UpdateChecker.CurrentVersion}. Download size {size / 1048576.0:0.#} MB.";
            UpdateBar.Severity = InfoBarSeverity.Informational;
            ReleaseNotes.Text = PlainText(_update.Notes);
            ReleaseNotes.Visibility = ReleaseNotes.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotesLink.NavigateUri = new Uri(_update.PageUrl);
            DownloadProgress.Visibility = Visibility.Collapsed;

            if (Updater.InstallFolder is null)
            {
                InstallButton.IsEnabled = false;
                InstallButton.Content = "Only available in the published app";
            }
            UpdateBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            UpdateCard.Description = $"Couldn't check for updates: {ex.Message}";
        }
        finally
        {
            CheckButton.IsEnabled = true;
            Checking.IsActive = false;
        }
    }

    /// <summary>Release notes are Markdown; show them as readable plain text (the full page is one click away).</summary>
    private static string PlainText(string markdown)
    {
        var text = Regex.Replace(markdown, @"\[([^\]]+)\]\([^)]*\)", "$1");          // [text](url) -> text
        text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s*", "", RegexOptions.Multiline);  // headings
        text = Regex.Replace(text, @"(\*\*|__|`)", "");                                // bold, code
        text = Regex.Replace(text, @"^\s*[-*]\s+", "• ", RegexOptions.Multiline);      // bullets
        return text.Trim();
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_update is null || Updater.InstallFolder is not { } installFolder) return;

        _download = new CancellationTokenSource();
        InstallButton.IsEnabled = CheckButton.IsEnabled = false;
        InstallButton.Content = "Downloading…";
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        try
        {
            var progress = new Progress<double>(p => DownloadProgress.Value = p);
            var files = await Updater.DownloadAsync(_update, progress, _download.Token);

            InstallButton.Content = "Installing…";
            SettingsStore.Flush();
            Updater.Install(files, installFolder, _update.Version); // closes this app; the island restarts on the new version
        }
        catch (OperationCanceledException)
        {
            // Left the page mid-download; nothing to clean up beyond the temp folder, which the next try replaces.
        }
        catch (Exception ex)
        {
            Log.Error("Update failed", ex);
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "The update couldn't be installed";
            UpdateBar.Message = ex is System.ComponentModel.Win32Exception
                ? "Administrator permission is needed to update this folder."
                : ex.Message;
            InstallButton.Content = "Try again";
            InstallButton.IsEnabled = CheckButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _download?.Dispose();
            _download = null;
        }
    }
}
