using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Addons.SystemAddons;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.Productivity;

/// <summary>
/// (17) Downloads from any browser: size and speed in the pill while downloading, "Download complete" after.
/// Browsers write to a temporary file (Chrome/Edge ".crdownload", Firefox ".part") and rename it when done,
/// so watching the Downloads folder is enough, no browser extension needed.
/// </summary>
public sealed class DownloadsAddon : Addon
{
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly string[] TempTypes = { ".crdownload", ".part", ".partial", ".download" };

    private sealed class Download
    {
        public required string Path { get; set; }
        public long Size { get; set; }
        public double Speed { get; set; }
    }

    private readonly Dictionary<string, Download> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _compact = Ui.Text("", 14, FontWeights.SemiBold, Ui.Blue, tabular: true);
    private readonly StackPanel _list = new() { Margin = new Thickness(20, 16, 22, 14) };
    private readonly AddonActivity _activity;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _tick;

    public DownloadsAddon(AddonContext context) : base(context)
    {
        _activity = new AddonActivity("Downloads", 15,
            Ui.Glyph("", 15, Ui.Blue), _compact, Ui.Glyph("", 13, Ui.Blue), _list,
            compactWidth: 240, expandedSize: new Size(400, 120));
        context.Manager.Register(_activity);
    }

    public override string Key => nameof(AddonSettings.Downloads);
    public override string Name => "Downloads";
    protected override bool IsEnabled(AddonSettings o) => o.Downloads;

    protected override void Start()
    {
        var folder = ScreenshotAddon.GetKnownFolder(DownloadsFolder)
                     ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        _watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        _watcher.Created += (_, e) => Context.Ui(() => Touched(e.FullPath));
        _watcher.Changed += (_, e) => Context.Ui(() => Touched(e.FullPath));
        _watcher.Deleted += (_, e) => Context.Ui(() => Remove(e.FullPath));
        _watcher.Renamed += (_, e) => Context.Ui(() => Renamed(e.OldFullPath, e.FullPath));
        _watcher.EnableRaisingEvents = true;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Update(measureSpeed: true);
        _tick.Start();
    }

    protected override void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _tick?.Stop();
        _tick = null;
        _active.Clear();
        _activity.Hide();
    }

    private static bool IsTemp(string path) => TempTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void Touched(string path)
    {
        if (!IsTemp(path) || _active.ContainsKey(path)) return;
        _active[path] = new Download { Path = path };
        Update();
    }

    private void Renamed(string from, string to)
    {
        if (!_active.Remove(from, out var download))
        {
            Touched(to);
            return;
        }

        if (IsTemp(to))
        {
            // Chrome: "Unconfirmed 1234.crdownload" -> "photo.jpg.crdownload"
            download.Path = to;
            _active[to] = download;
        }
        else
        {
            Finished(to);
        }
        Update();
    }

    private void Remove(string path)
    {
        if (_active.Remove(path)) Update(); // cancelled
    }

    private void Update(bool measureSpeed = false)
    {
        foreach (var d in _active.Values.ToList())
        {
            try
            {
                var size = new FileInfo(d.Path).Length;
                if (measureSpeed) d.Speed = d.Size > 0 ? Math.Max(0, size - d.Size) : 0; // bytes in the last second
                d.Size = size;
            }
            catch (IOException)
            {
                _active.Remove(d.Path); // gone without a rename event
            }
        }

        if (_active.Count == 0)
        {
            _activity.Hide();
            return;
        }

        var total = _active.Values.Sum(d => d.Speed);
        _compact.Text = _active.Count > 1
            ? $"{_active.Count} files"
            : total > 0 ? $"{UsbDriveAddon.FormatSize((long)total)}/s" : UsbDriveAddon.FormatSize(_active.Values.First().Size);

        _list.Children.Clear();
        _list.Children.Add(Ui.Text("Downloading", 14, FontWeights.SemiBold, Ui.Blue));
        foreach (var d in _active.Values.Take(3))
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Ui.Text(DisplayName(d.Path), 14, FontWeights.Normal, Ui.White));
            var detail = Ui.Text($"{UsbDriveAddon.FormatSize(d.Size)}{(d.Speed > 0 ? $" · {UsbDriveAddon.FormatSize((long)d.Speed)}/s" : "")}",
                                 13, FontWeights.Normal, Ui.Grey, tabular: true);
            detail.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(detail, 1);
            row.Children.Add(detail);
            _list.Children.Add(row);
        }
        _activity.Show();
    }

    private void Finished(string path)
    {
        long size = 0;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (IOException)
        {
            // fine without a size
        }

        Show(new IslandAlert
        {
            Content = Ui.AlertView(Ui.GlyphCircle("", Ui.Blue, 46, 20), "Download complete",
                                   $"{Path.GetFileName(path)}{(size > 0 ? $" · {UsbDriveAddon.FormatSize(size)}" : "")}"),
            Size = new Size(380, 76),
            Duration = TimeSpan.FromSeconds(4),
            // Show it in Explorer rather than running it: downloads can be programs.
            OnClick = () => Process.Start("explorer.exe", $"/select,\"{path}\""),
        });
    }

    // "photo.jpg.crdownload" -> "photo.jpg"; Chrome's placeholder "Unconfirmed 1234" -> "New download"
    internal static string DisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase) ? "New download" : name;
    }
}
