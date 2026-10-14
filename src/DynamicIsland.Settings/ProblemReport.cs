using System.Text;
using System.Text.RegularExpressions;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.Updates;
using Microsoft.Win32;

namespace DynamicIsland.SettingsApp;

/// <summary>
/// The text for a GitHub issue: versions, set-up, add-ons with problems and the end of today's log.
/// The user sees and can edit all of it before anything leaves the PC, and personal paths are replaced.
/// </summary>
public static partial class ProblemReport
{
    public const string NewIssueUrl = $"https://github.com/{UpdateChecker.Repository}/issues/new";

    /// <summary>GitHub (and browsers) cut off very long links; longer reports go through the clipboard.</summary>
    public const int MaxBodyInLink = 6000;

    /// <param name="crash">The island's last crash (crash.txt), included when reporting one.</param>
    public static string Build(string? crash = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**What happened?**");
        sb.AppendLine("<!-- Describe what you did, what you expected, and what happened instead. -->");
        sb.AppendLine();
        sb.AppendLine();
        if (crash is not null)
        {
            sb.AppendLine("**Crash**");
            sb.AppendLine("```");
            sb.AppendLine(crash.Length > 3000 ? crash[..3000] + "\n…" : crash);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        sb.AppendLine("**System**");
        sb.AppendLine($"- Dynamic Island {UpdateChecker.CurrentVersion} ({InstallType()})");
        sb.AppendLine($"- {WindowsVersion()}, .NET {Environment.Version}");
        sb.AppendLine($"- Displays: {Displays.All().Count}, island on: {DescribeMonitor(SettingsStore.Current.Monitor)}");
        sb.AppendLine($"- Island running: {(SettingsStore.IslandRunning ? "yes" : "no")}");

        var problems = SettingsStore.IslandRunning
            ? IslandStatus.TryLoad()?.Addons.Where(a => a.Value.Problem && a.Value.Message is not null).ToList()
            : null;
        if (problems is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("**Add-ons reporting a problem**");
            foreach (var (key, state) in problems) sb.AppendLine($"- {key}: {state.Message}");
        }

        var log = RecentLog(40);
        if (log.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Recent log**");
            sb.AppendLine("```");
            sb.AppendLine(log);
            sb.AppendLine("```");
        }
        return Redact(sb.ToString());
    }

    private static string InstallType() =>
        Updater.InstalledWithMsi ? "installer" : Updater.InstallFolder is not null ? "zip" : "from source";

    private static string DescribeMonitor(string setting) => setting switch
    {
        "" => "main display",
        Displays.FollowMouse => "display with the mouse",
        _ => setting,
    };

    private static string WindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = int.TryParse(key?.GetValue("CurrentBuild") as string, out var b) ? b : Environment.OSVersion.Version.Build;
            var edition = key?.GetValue("EditionID") as string ?? "";
            var release = key?.GetValue("DisplayVersion") as string ?? "";
            // ProductName still says "Windows 10" on Windows 11; the build number tells them apart.
            return $"Windows {(build >= 22000 ? "11" : "10")} {edition} {release} (build {build})".Replace("  ", " ");
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }

    private static string RecentLog(int lines)
    {
        try
        {
            var file = Path.Combine(Log.Directory, $"{DateTime.Now:yyyy-MM-dd}.log");
            if (!File.Exists(file)) return "";
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\n", all.TakeLast(lines).Select(l => l.TrimEnd('\r')));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Replaces the user folder, user name and e-mail addresses, so the report doesn't identify anyone.</summary>
    public static string Redact(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var shortProfile = Path.GetTempPath().Split(@"\AppData\")[0]; // 8.3 form, e.g. C:\Users\OPGERU~1
        if (shortProfile.Length > 3 && !shortProfile.Equals(profile, StringComparison.OrdinalIgnoreCase))
        {
            text = text.Replace(shortProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        var user = Environment.UserName;
        if (user.Length > 2) text = Regex.Replace(text, Regex.Escape(user), "<user>", RegexOptions.IgnoreCase);
        return EmailPattern().Replace(text, "<email>");
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();
}
