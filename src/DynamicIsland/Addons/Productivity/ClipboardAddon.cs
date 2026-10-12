using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.Productivity;

/// <summary>
/// (15) A brief "Copied" with a preview of the text or image. Respects the markers password managers put
/// on secrets, so copied passwords are never shown.
/// </summary>
public sealed class ClipboardAddon : Addon
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    // Formats that mean "don't show or keep this" (KeePass, 1Password, Bitwarden, Windows itself set these).
    private static readonly string[] SecretMarkers =
    {
        "ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore", "CanIncludeInClipboardHistory",
    };

    private IntPtr _hwnd;
    private DateTime _lastShown;

    /// <summary>Set by the screenshot add-on; an image copied right after a screenshot isn't announced twice.</summary>
    public static DateTime LastScreenshot { get; set; }

    public ClipboardAddon(AddonContext context) : base(context) { }

    public override string Key => nameof(AddonSettings.Clipboard);
    public override string Name => "Clipboard";
    protected override bool IsEnabled(AddonSettings o) => o.Clipboard;

    protected override void Start()
    {
        _hwnd = new WindowInteropHelper(Context.Window).EnsureHandle();
        if (!AddClipboardFormatListener(_hwnd)) throw new InvalidOperationException("Clipboard listener refused");
        Context.Window.MessageReceived += OnMessage;
    }

    protected override void Stop()
    {
        Context.Window.MessageReceived -= OnMessage;
        RemoveClipboardFormatListener(_hwnd);
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        // Read after the copying app has finished writing all its formats.
        if (msg == WM_CLIPBOARDUPDATE) Context.Dispatcher.BeginInvoke(Announce, System.Windows.Threading.DispatcherPriority.Background);
    }

    private async void Announce()
    {
        if (DateTime.UtcNow - _lastShown < TimeSpan.FromMilliseconds(400)) return; // apps often write twice
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || IsSecret(data)) return;

            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                var text = (data.GetData(DataFormats.UnicodeText) as string ?? "").Trim();
                if (text.Length == 0) return;
                var preview = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                ShowCopied(Ui.GlyphCircle("", Ui.ButtonBg, 46, 20), L.T("Copied"), preview);
            }
            else if (Clipboard.ContainsImage())
            {
                await Task.Delay(600); // let the screenshot add-on claim it first
                if (DateTime.UtcNow - LastScreenshot < TimeSpan.FromSeconds(3)) return;
                var image = Clipboard.GetImage();
                if (image is null) return;
                ShowCopied(Ui.Image(image, 46, 8), L.T("Copied image"), $"{image.PixelWidth} × {image.PixelHeight}");
            }
            else if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var name = System.IO.Path.GetFileName(files[0]);
                ShowCopied(Ui.GlyphCircle("", Ui.ButtonBg, 46, 20), L.T("Copied"), files.Length == 1 ? name : L.F("{0} and {1} more", name, files.Length - 1));
            }
        }
        catch (COMException)
        {
            // Another app holds the clipboard open; skip this one.
        }
        catch (Exception ex)
        {
            Log.Info($"Clipboard preview failed: {ex.Message}");
        }
    }

    private static bool IsSecret(IDataObject data)
    {
        foreach (var marker in SecretMarkers)
        {
            if (!data.GetDataPresent(marker)) continue;
            if (marker != "CanIncludeInClipboardHistory") return true;
            // This one is a DWORD: 0 means "keep out of history" (used for passwords).
            if (data.GetData(marker) is System.IO.MemoryStream ms && ms.Length >= 4 && BitConverter.ToInt32(ms.ToArray(), 0) == 0) return true;
        }
        return false;
    }

    private void ShowCopied(FrameworkElement icon, string title, string preview)
    {
        _lastShown = DateTime.UtcNow;
        var view = Ui.AlertView(icon, title, preview);
        Show(new IslandAlert { Content = view, Size = new Size(360, 76), Duration = TimeSpan.FromSeconds(2) });
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
