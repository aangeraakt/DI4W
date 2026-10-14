using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.Productivity;

/// <summary>
/// (16) Thumbnail of a new screenshot. Watches Pictures\Screenshots, where Win+PrtScn and the Snipping Tool
/// save them. Click opens the image; the folder button shows it in Explorer.
/// </summary>
public sealed class ScreenshotAddon : Addon
{
    private static readonly Guid ScreenshotsFolder = new("b7bede81-df94-4682-a7d8-57a52620b86f");
    private static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private FileSystemWatcher? _watcher;
    private string? _lastFile;

    public ScreenshotAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.Screenshots);
    public override string Name => "Screenshots";
    protected override bool IsEnabled(AddonSettings o) => o.Screenshots;

    /// <summary>The real pop-up shows the screenshot itself; the sample has a camera icon instead.</summary>
    public override void Preview() =>
        ShowSaved(Ui.GlyphCircle("", Ui.ButtonBg, 46, 20), $"{L.T("Screenshot")} {DateTime.Now:yyyy-MM-dd HHmmss}.png", () => { }, null);

    protected override void Start()
    {
        var folder = KnownFolder(ScreenshotsFolder)
                     ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        Directory.CreateDirectory(folder);
        _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
        _watcher.Created += (_, e) => Context.Ui(() => _ = Arrived(e.FullPath));
        _watcher.Renamed += (_, e) => Context.Ui(() => _ = Arrived(e.FullPath));
        _watcher.EnableRaisingEvents = true;
    }

    protected override void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private async Task Arrived(string path)
    {
        if (!ImageTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || path == _lastFile) return;
        _lastFile = path;
        ClipboardAddon.LastScreenshot = DateTime.UtcNow;

        var thumbnail = await LoadThumbnail(path);
        if (thumbnail is null) return;

        ShowSaved(Ui.Image(thumbnail, 46, 8), Path.GetFileName(path),
                  () => Process.Start("explorer.exe", $"/select,\"{path}\""),
                  () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
    }

    /// <summary>The pop-up for a saved screenshot; the settings app's preview passes a camera icon and no actions.</summary>
    private void ShowSaved(FrameworkElement picture, string name, Action showInFolder, Action? open)
    {
        var folderButton = Ui.CircleButton(Ui.Glyph("", 15, Ui.Primary), Ui.ButtonBg, 36,
                                           showInFolder, L.T("Show in folder"));
        Show(new IslandAlert
        {
            Content = Ui.AlertView(picture, L.T("Screenshot saved"), name, accessory: folderButton),
            Size = new Size(380, 76),
            Duration = TimeSpan.FromSeconds(4),
            OnClick = open,
        });
    }

    /// <summary>The capture tool may still be writing; retry for a moment. Loaded fully so the file isn't kept locked.</summary>
    private static async Task<BitmapSource?> LoadThumbnail(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelHeight = 120;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception) when (attempt < 9)
            {
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                Log.Info($"Screenshot thumbnail failed: {ex.Message}");
            }
        }
        return null;
    }

    private static string? KnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
            var path = Marshal.PtrToStringUni(p);
            Marshal.FreeCoTaskMem(p);
            return path;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    internal static string? GetKnownFolder(Guid id) => KnownFolder(id);
}
