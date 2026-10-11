using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using DynamicIsland.Core;
using DynamicIsland.Updates;
using Microsoft.Win32;

namespace DynamicIsland.SettingsApp;

/// <summary>
/// Downloads and installs an update. Running programs can't overwrite their own files, so the last step is a
/// small PowerShell script that waits for the island and this app to exit and then either runs the new
/// installer (for MSI installs) or copies the new files over the old ones with a backup to roll back to.
/// </summary>
public static class Updater
{
    /// <summary>Written by the installer (.msi) so updates go through the installer too.</summary>
    private const string InstallerKey = @"Software\DynamicIsland4Windows";

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

    /// <summary>True when this copy was installed with the .msi installer (it records its folder in the registry).</summary>
    public static bool InstalledWithMsi
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(InstallerKey);
            return key?.GetValue("InstallFolder") is string folder && InstallFolder is { } here
                   && Path.GetFullPath(folder).TrimEnd('\\').Equals(here.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The installer for MSI installs (when the release has one), otherwise the zip.</summary>
    public static ReleaseAsset? PickAsset(UpdateInfo update) =>
        InstalledWithMsi && update.Msi is not null ? update.Msi : update.Zip ?? update.Msi;

    /// <summary>Downloads and verifies the update. Returns the .msi, or the folder with the unpacked new files.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancel)
    {
        var asset = PickAsset(update) ?? throw new InvalidDataException("This release has no Windows package.");
        var folder = Path.Combine(WorkFolder, update.Version.ToString());
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        var file = Path.Combine(folder, asset.Name);
        await Download(asset, file, progress, cancel);
        if (file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return file;

        // ExtractToDirectory refuses entries that would land outside the target folder.
        var extracted = Path.Combine(folder, "files");
        ZipFile.ExtractToDirectory(file, extracted);
        var island = Directory.EnumerateFiles(extracted, "DynamicIsland.exe", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new InvalidDataException("The update package doesn't contain DynamicIsland.exe.");
        var root = Path.GetDirectoryName(island)!;
        if (!File.Exists(Path.Combine(root, "Settings", "DynamicIsland.Settings.exe")))
        {
            throw new InvalidDataException("The update package doesn't contain the settings app.");
        }
        return root;
    }

    private static async Task Download(ReleaseAsset asset, string target, IProgress<double> progress, CancellationToken cancel)
    {
        using (var response = await UpdateChecker.Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var output = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                progress.Report(asset.Size > 0 ? (double)done / asset.Size : 0);
            }
        }

        if (new FileInfo(target).Length != asset.Size)
        {
            throw new InvalidDataException("The download is incomplete. Please try again.");
        }
        if (asset.Sha256 is { } expected)
        {
            await using var stream = File.OpenRead(target);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The download doesn't match the release (checksum mismatch), so it wasn't installed.");
            }
        }
    }

    /// <summary>Starts the install script, asks the island to quit, and closes this app. The script does the rest.</summary>
    /// <param name="payload">The downloaded .msi, or the folder with the new files.</param>
    public static void Install(string payload, string installFolder, Version version)
    {
        var script = Path.Combine(WorkFolder, "install-update.ps1");
        File.WriteAllText(script, InstallScript);

        var isMsi = payload.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo("powershell.exe")
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            // Copying into a protected folder (e.g. Program Files) needs admin rights; ask only then.
            // The installer is per-user and needs none.
            UseShellExecute = !isMsi && !CanWrite(installFolder),
            CreateNoWindow = true,
        };
        if (psi.UseShellExecute) psi.Verb = "runas";
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                                    "-Payload", payload, "-Target", installFolder, "-Version", version.ToString(),
                                    "-Log", Path.Combine(Log.Directory, "update.log") })
        {
            psi.ArgumentList.Add(arg);
        }
        Process.Start(psi);
        Log.Info($"Installing update {version} from {payload} into {installFolder}");

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
        param([string]$Payload, [string]$Target, [string]$Version, [string]$Log)
        function Write-Log($message) {
            New-Item -ItemType Directory -Force (Split-Path $Log) | Out-Null
            Add-Content -Path $Log -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message"
        }
        $island = Join-Path $Target 'DynamicIsland.exe'

        # Start the island as the signed-in user, never as administrator (even if this script was elevated).
        function Start-Island { Start-Process -FilePath explorer.exe -ArgumentList "`"$island`"" }

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

        if ($Payload -like '*.msi') {
            # Installed with the installer: let it upgrade, so Windows' "Installed apps" stays correct.
            $msiLog = Join-Path (Split-Path $Log) 'update-msi.log'
            $p = Start-Process msiexec.exe -ArgumentList "/i `"$Payload`" /qb- /norestart /l*v `"$msiLog`"" -Wait -PassThru
            if ($p.ExitCode -in 0, 3010) { Write-Log "Updated to $Version with the installer" }
            else { Write-Log "Installer failed with code $($p.ExitCode); see $msiLog. The previous version is still installed." }
            Start-Island
            exit
        }

        # Copy-over update with a backup, so a failed copy never leaves a half-updated mix of files.
        $backup = Join-Path (Split-Path $Payload -Parent) 'backup'
        robocopy $Target $backup /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) {
            Write-Log "Update to $Version cancelled: couldn't back up $Target (robocopy $LASTEXITCODE)"
            Start-Island
            exit
        }

        robocopy $Payload $Target /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        $copied = $LASTEXITCODE -lt 8
        $installed = (Get-Item $island -ErrorAction SilentlyContinue).VersionInfo.ProductVersion
        if ($copied -and $installed -like "$Version*") {
            Write-Log "Updated $Target to $Version"
        } else {
            robocopy $backup $Target /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            Write-Log "Update to $Version failed (copy ok: $copied, found version: $installed); restored the previous version"
        }
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
        Start-Island
        """;
}
