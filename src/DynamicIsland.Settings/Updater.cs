using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using DynamicIsland.Core;
using DynamicIsland.Updates;

namespace DynamicIsland.SettingsApp;

/// <summary>
/// Downloads and installs an update. Running programs can't overwrite their own files, so the last step is a
/// small PowerShell script that waits for the island and this app to exit, copies the new files over the
/// install folder and starts the island again.
/// </summary>
public static class Updater
{
    private static readonly string WorkFolder = Path.Combine(Path.GetTempPath(), "DI4W-update");

    /// <summary>
    /// The folder that holds DynamicIsland.exe, with this app in its Settings subfolder (the published layout).
    /// Null when running from source, where updating would overwrite build output.
    /// </summary>
    public static string? InstallFolder
    {
        get
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            var settingsFolder = new DirectoryInfo(AppContext.BaseDirectory).Name;
            return settingsFolder.Equals("Settings", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(Path.Combine(parent, "DynamicIsland.exe"))
                ? parent
                : null;
        }
    }

    /// <summary>Downloads, verifies and unpacks the update. Returns the folder with the new files.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancel)
    {
        var folder = Path.Combine(WorkFolder, update.Version.ToString());
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        var zip = Path.Combine(folder, "update.zip");
        using (var response = await UpdateChecker.Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var target = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                progress.Report(update.Size > 0 ? (double)done / update.Size : 0);
            }
        }

        if (new FileInfo(zip).Length != update.Size)
        {
            throw new InvalidDataException("The download is incomplete. Please try again.");
        }
        if (update.Sha256 is { } expected)
        {
            await using var stream = File.OpenRead(zip);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The download doesn't match the release (checksum mismatch), so it wasn't installed.");
            }
        }

        // ExtractToDirectory refuses entries that would land outside the target folder.
        var extracted = Path.Combine(folder, "files");
        ZipFile.ExtractToDirectory(zip, extracted);

        var island = Directory.EnumerateFiles(extracted, "DynamicIsland.exe", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new InvalidDataException("The update package doesn't contain DynamicIsland.exe.");
        var root = Path.GetDirectoryName(island)!;
        if (!File.Exists(Path.Combine(root, "Settings", "DynamicIsland.Settings.exe")))
        {
            throw new InvalidDataException("The update package doesn't contain the settings app.");
        }
        return root;
    }

    /// <summary>Starts the install script, asks the island to quit, and closes this app. The script does the rest.</summary>
    public static void Install(string newFiles, string installFolder)
    {
        var script = Path.Combine(WorkFolder, "install-update.ps1");
        File.WriteAllText(script, InstallScript);

        var psi = new ProcessStartInfo("powershell.exe")
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            // Elevate only when the install folder needs it (e.g. Program Files); otherwise no UAC prompt.
            UseShellExecute = !CanWrite(installFolder),
            CreateNoWindow = true,
        };
        if (psi.UseShellExecute) psi.Verb = "runas";
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                                    "-Source", newFiles, "-Target", installFolder, "-Log", Path.Combine(Log.Directory, "update.log") })
        {
            psi.ArgumentList.Add(arg);
        }
        Process.Start(psi);
        Log.Info($"Installing update from {newFiles} into {installFolder}");

        if (EventWaitHandle.TryOpenExisting(UpdateChecker.IslandQuitEvent, out var quit))
        {
            using (quit) quit.Set();
        }
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private const string InstallScript = """
        # Installs a Dynamic Island update. Started by the settings app; runs hidden.
        param([string]$Source, [string]$Target, [string]$Log)
        function Write-Log($message) {
            New-Item -ItemType Directory -Force (Split-Path $Log) | Out-Null
            Add-Content -Path $Log -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message"
        }

        # Wait for this installation's island and settings app to close (they were asked to), then make sure.
        $targetPrefix = (Resolve-Path $Target).Path.TrimEnd('\') + '\'
        function Get-AppProcesses {
            Get-Process DynamicIsland, DynamicIsland.Settings -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -and $_.Path.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase) }
        }
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-AppProcesses) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
        Get-AppProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500

        # Copy the new files over the old ones. robocopy retries files that are briefly locked.
        robocopy $Source $Target /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) {
            Write-Log "Update failed: robocopy exit code $LASTEXITCODE (from $Source to $Target)"
        } else {
            Write-Log "Updated $Target from $Source"
        }

        Start-Process -FilePath (Join-Path $Target 'DynamicIsland.exe')
        """;
}
