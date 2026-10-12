using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DynamicIsland.Settings;

namespace DynamicIsland.SettingsApp.Pages;

public sealed partial class ClaudePage : Page
{
    private readonly bool _loading = true;
    private readonly string? _installer = FindInstaller();

    public ClaudePage()
    {
        InitializeComponent();
        var s = SettingsStore.Current;
        ClaudeEnabled.IsOn = s.ClaudeEnabled;
        Port.Value = s.ClaudePort;
        PortCard.IsEnabled = s.ClaudeEnabled;

        if (_installer is null)
        {
            HooksCard.IsEnabled = false;
            HooksCard.Description = L.T("The hook scripts (tools\\install-claude-hooks.ps1) weren't found next to the island.");
        }
        _loading = false;
    }

    private void OnClaudeEnabled(object sender, RoutedEventArgs e)
    {
        PortCard.IsEnabled = ClaudeEnabled.IsOn;
        if (!_loading) SettingsStore.Change(s => s.ClaudeEnabled = ClaudeEnabled.IsOn);
    }

    private void OnPort(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue)) return;
        SettingsStore.Change(s => s.ClaudePort = (int)Math.Round(args.NewValue));
    }

    private async void OnInstall(object sender, RoutedEventArgs e) => await RunInstaller(uninstall: false);

    private async void OnRemove(object sender, RoutedEventArgs e) => await RunInstaller(uninstall: true);

    private async Task RunInstaller(bool uninstall)
    {
        if (_installer is null) return;
        Install.IsEnabled = Remove.IsEnabled = false;
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", _installer })
            {
                psi.ArgumentList.Add(arg);
            }
            if (uninstall) psi.ArgumentList.Add("-Uninstall");

            using var process = Process.Start(psi)!;
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                Show(InfoBarSeverity.Success, uninstall ? L.T("Hooks removed") : L.T("Hooks installed"),
                     uninstall ? L.T("Claude Code no longer reports to the island.") : L.T("Start a new Claude Code session to see it in the island."));
            }
            else
            {
                Show(InfoBarSeverity.Error, L.T("Something went wrong"), error.Trim());
            }
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, L.T("Something went wrong"), ex.Message);
        }
        finally
        {
            Install.IsEnabled = Remove.IsEnabled = true;
        }
    }

    private void Show(InfoBarSeverity severity, string title, string message)
    {
        Result.Severity = severity;
        Result.Title = title;
        Result.Message = message;
        Result.IsOpen = true;
    }

    /// <summary>tools\install-claude-hooks.ps1 next to the island, or in the source tree when running from source.</summary>
    private static string? FindInstaller()
    {
        var starts = new[] { Path.GetDirectoryName(SettingsStore.IslandExe), AppContext.BaseDirectory };
        foreach (var start in starts.OfType<string>())
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var script = Path.Combine(dir.FullName, "tools", "install-claude-hooks.ps1");
                if (File.Exists(script)) return script;
            }
        }
        return null;
    }
}
