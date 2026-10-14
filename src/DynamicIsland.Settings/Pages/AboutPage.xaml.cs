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
        VersionCard.Description = L.F("Version {0}", UpdateChecker.CurrentVersion);
        SettingsFileCard.Description = AppSettings.FilePath;
        AutoCheck.IsOn = SettingsStore.Current.AutoCheckUpdates;
        CrashOffers.IsOn = SettingsStore.Current.OfferCrashReports;
        UpdateStatus();
        _loading = false;

        Loaded += async (_, _) => await CheckAsync();
        Unloaded += (_, _) => _download?.Cancel();
    }

    private void UpdateStatus()
    {
        var running = SettingsStore.IslandRunning;
        Status.Text = running ? L.T("Running") : L.T("Not running");
        StartButton.Visibility = running || SettingsStore.IslandExe is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        SettingsStore.StartIsland();
        await Task.Delay(1500);
        UpdateStatus();
    }

    private async void OnWelcome(object sender, RoutedEventArgs e) => await App.MainWindow!.ShowWelcomeAsync();

    // ───────────────────────────── backup ─────────────────────────────

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Dynamic Island settings {DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add(L.T("Settings file"), new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            SettingsStore.Flush();
            File.Copy(AppSettings.FilePath, file.Path, overwrite: true);
            ShowBackup(InfoBarSeverity.Success, L.F("Saved to {0}", file.Path));
        }
        catch (Exception ex)
        {
            ShowBackup(InfoBarSeverity.Error, L.F("Couldn't save the file: {0}", ex.Message));
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        AppSettings? imported;
        try
        {
            var json = await File.ReadAllTextAsync(file.Path);
            // Any JSON would read as "all defaults"; ours always has the add-ons section.
            imported = json.Contains("\"Addons\"") ? System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) : null;
        }
        catch (Exception ex)
        {
            imported = null;
            DynamicIsland.Core.Log.Info($"Settings import failed: {ex.Message}");
        }
        if (imported is null)
        {
            ShowBackup(InfoBarSeverity.Error, L.T("That isn't a Dynamic Island settings file."));
            return;
        }

        SettingsStore.Flush();
        AppSettings.Update(latest =>
        {
            // This PC's own facts stay: where the island is installed, and that the tour was seen.
            var islandPath = latest.IslandPath;
            latest.CopyFrom(imported);
            latest.IslandPath = islandPath;
            latest.WelcomeShown = true;
        });
        App.Restart("About"); // every page shows the imported values; the island picks them up by itself
    }

    private void ShowBackup(InfoBarSeverity severity, string message)
    {
        BackupBar.Severity = severity;
        BackupBar.Message = message;
        BackupBar.IsOpen = true;
    }

    private void OnOpenSettingsFile(object sender, RoutedEventArgs e) => SettingsStore.Open(AppSettings.FilePath);

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Directory);
        SettingsStore.Open(Log.Directory);
    }

    // ───────────────────────────── report a problem ─────────────────────────────

    private async void OnReport(object sender, RoutedEventArgs e) => await ReportAsync(null);

    /// <summary>The report form; with <paramref name="crash"/> (from crash.txt) the crash details are filled in too.</summary>
    public async Task ReportAsync(string? crash)
    {
        var title = new TextBox { Header = L.T("Title"), PlaceholderText = L.T("Short summary, e.g. \"Weather doesn't show\"") };
        // In English: the issue is read by the developer. "Crash: System.NullReferenceException"
        if (crash is not null) title.Text = "Crash: " + (crash.Split('\n').Skip(1).FirstOrDefault()?.Split(':')[0].Trim() ?? "");
        var body = new TextBox
        {
            Header = L.T("Report (you can change anything)"),
            Text = ProblemReport.Build(crash),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 320,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(body, ScrollBarVisibility.Auto);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = L.T("Report a problem"),
            Content = new StackPanel { Spacing = 12, Width = 560, Children = { title, body } },
            PrimaryButtonText = L.T("Open on GitHub"),
            SecondaryButtonText = L.T("Copy"),
            CloseButtonText = L.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.None) return;

        var report = body.Text;
        if (choice == ContentDialogResult.Secondary || report.Length > ProblemReport.MaxBodyInLink)
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(report);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        if (choice == ContentDialogResult.Primary)
        {
            // Long reports don't fit in a link: open an empty issue and let the user paste (it's on the clipboard).
            var text = report.Length > ProblemReport.MaxBodyInLink
                ? "<!-- The report was too long for a link and has been copied: paste it here (Ctrl+V). -->"
                : report;
            SettingsStore.Open($"{ProblemReport.NewIssueUrl}?title={Uri.EscapeDataString(title.Text.Trim())}&body={Uri.EscapeDataString(text)}");
        }
    }

    // ───────────────────────────── updates ─────────────────────────────

    private void OnCrashOffers(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsStore.Change(s => s.OfferCrashReports = CrashOffers.IsOn);
    }

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
                UpdateCard.Description = L.F("You're up to date. Last checked {0:t}.", DateTime.Now);
                UpdateBar.IsOpen = false;
                return;
            }

            UpdateCard.Description = L.F("Version {0} is available.", _update.Version);
            UpdateBar.Title = L.F("Version {0} is available", _update.Version);
            var size = Updater.PickAsset(_update)?.Size ?? 0;
            UpdateBar.Message = L.F("You have {0}. Download size {1:0.#} MB.", UpdateChecker.CurrentVersion, size / 1048576.0);
            UpdateBar.Severity = InfoBarSeverity.Informational;
            ReleaseNotes.Text = PlainText(_update.Notes);
            ReleaseNotes.Visibility = ReleaseNotes.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotesLink.NavigateUri = new Uri(_update.PageUrl);
            DownloadProgress.Visibility = Visibility.Collapsed;

            if (Updater.InstallFolder is null)
            {
                InstallButton.IsEnabled = false;
                InstallButton.Content = L.T("Only available in the published app");
            }
            UpdateBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            UpdateCard.Description = L.F("Couldn't check for updates: {0}", ex.Message);
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
        InstallButton.Content = L.T("Downloading…");
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        try
        {
            var progress = new Progress<double>(p => DownloadProgress.Value = p);
            var files = await Updater.DownloadAsync(_update, progress, _download.Token);

            InstallButton.Content = L.T("Installing…");
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
            UpdateBar.Title = L.T("The update couldn't be installed");
            UpdateBar.Message = ex is System.ComponentModel.Win32Exception
                ? L.T("Administrator permission is needed to update this folder.")
                : ex.Message;
            InstallButton.Content = L.T("Try again");
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
