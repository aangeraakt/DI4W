using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;

namespace DynamicIsland.UI;

/// <summary>
/// The tray icon in two states: normal, and paused (faded, with an orange pause badge), so a hidden island
/// is visible at a glance in the taskbar.
/// </summary>
public static class TrayIcons
{
    public static Icon Normal { get; } = Build(paused: false);
    public static Icon Paused { get; } = Build(paused: true);

    private static Icon Build(bool paused)
    {
        // The taskbar's small-icon size for this display scale (16 px at 100%, 20 at 125%, 24 at 150%…).
        var size = Math.Max(16, GetSystemMetrics(SM_CXSMICON));
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/island.ico"))!;
        using var stream = resource.Stream;
        using var source = new Icon(stream, size, size);
        if (!paused) return (Icon)source.Clone();

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            using var faded = new ImageAttributes();
            faded.SetColorMatrix(new ColorMatrix { Matrix33 = 0.45f });
            using var icon = source.ToBitmap();
            g.DrawImage(icon, new Rectangle(0, 0, size, size), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, faded);

            // Pause badge in the bottom-right corner.
            var badge = size * 0.62f;
            var x = size - badge;
            var y = size - badge;
            using var orange = new SolidBrush(Color.FromArgb(0xFF, 0x9F, 0x0A));
            g.FillEllipse(orange, x, y, badge - 0.5f, badge - 0.5f);
            var barWidth = badge * 0.16f;
            var barHeight = badge * 0.46f;
            var barTop = y + (badge - barHeight) / 2;
            var center = x + badge / 2;
            g.FillRectangle(Brushes.White, center - barWidth * 1.5f, barTop, barWidth, barHeight);
            g.FillRectangle(Brushes.White, center + barWidth * 0.5f, barTop, barWidth, barHeight);
        }
        return Icon.FromHandle(bitmap.GetHicon()); // made twice per run; the handle lives as long as the app
    }

    private const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
