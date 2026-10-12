using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DynamicIsland.UI;

/// <summary>Small factory helpers so every view uses the same iOS-like typography, colors and controls.</summary>
public static class Ui
{
    public static readonly FontFamily Font = new("SF Pro Display, Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static readonly SolidColorBrush White = Freeze(new SolidColorBrush(Colors.White));
    public static readonly SolidColorBrush Grey = Brush("#8E8E93");
    public static readonly SolidColorBrush Orange = Brush("#FF9F0A");
    public static readonly SolidColorBrush OrangeDim = Brush("#4A3410");
    public static readonly SolidColorBrush Green = Brush("#30D158");
    public static readonly SolidColorBrush Amber = Brush("#FFD60A");
    public static readonly SolidColorBrush ClaudeOrange = Brush("#D97757");
    public static readonly SolidColorBrush Track = Brush("#3A3A3C");
    public static readonly SolidColorBrush ButtonBg = Brush("#2C2C2E");
    public static readonly SolidColorBrush DiscordBlurple = Brush("#5865F2");
    public static readonly SolidColorBrush Blue = Brush("#0A84FF");
    public static readonly SolidColorBrush Red = Brush("#FF453A");
    public static readonly SolidColorBrush Purple = Brush("#BF5AF2");
    public static readonly SolidColorBrush Teal = Brush("#64D2FF");
    public static readonly SolidColorBrush Indigo = Brush("#5E5CE6");

    public static SolidColorBrush Brush(string hex) =>
        Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    public static TextBlock Text(string text, double size, FontWeight weight, Brush brush, bool tabular = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = Font,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (tabular) Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
        TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Ideal);
        return tb;
    }

    public static TextBlock Glyph(string glyph, double size, Brush brush) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static Path Shape(Geometry geometry, double size, Brush brush) => new()
    {
        Data = geometry,
        Fill = brush,
        Stroke = brush,
        StrokeThickness = 1.6,
        StrokeLineJoin = PenLineJoin.Round,
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>A button with no chrome: the content is the whole button, with hover/press feedback.</summary>
    public static Button Button(object content, Action onClick, Brush? background = null, double width = double.NaN,
                                double height = double.NaN, string? tooltip = null)
    {
        var b = new Button
        {
            Content = content,
            Background = background ?? Brushes.Transparent,
            Width = width,
            Height = height,
            ToolTip = tooltip,
            Style = (Style)Application.Current.FindResource("IslandButton"),
        };
        b.Click += (_, _) => onClick();
        // Screen readers: icon-only buttons need a name; use the tooltip, or the label text.
        var name = tooltip ?? (content as TextBlock)?.Text;
        if (name is not null) System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.SizeChanged += (_, _) =>
        {
            if (b.Template?.FindName("Bg", b) is Border bg)
            {
                bg.CornerRadius = new CornerRadius(Math.Min(b.ActualWidth, b.ActualHeight) / 2);
            }
        };
        return b;
    }

    /// <summary>Round button as used in iOS live activities (timer pause / cancel).</summary>
    public static Button CircleButton(UIElement icon, Brush background, double size, Action onClick, string? tooltip = null) =>
        Button(icon, onClick, background, size, size, tooltip);

    /// <summary>Rounded image (album art / app icons).</summary>
    public static Border Image(ImageSource? source, double size, double radius)
    {
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            Background = source is null ? ButtonBg : new ImageBrush(source) { Stretch = Stretch.UniformToFill },
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(border, BitmapScalingMode.HighQuality);
        return border;
    }

    /// <summary>The standard alert layout: icon, bold title, grey subtitle, optional trailing accessory.</summary>
    public static FrameworkElement AlertView(UIElement icon, string title, string subtitle,
                                             Brush? titleBrush = null, UIElement? accessory = null)
    {
        var grid = new Grid { Margin = new Thickness(14, 12, 18, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Border { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center, Child = icon };
        grid.Children.Add(iconHost);

        var stack = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text(title, 15, FontWeights.SemiBold, titleBrush ?? White));
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var sub = Text(subtitle, 13.5, FontWeights.Normal, Grey);
            sub.TextWrapping = TextWrapping.Wrap;
            sub.MaxHeight = 38;
            sub.Margin = new Thickness(0, 1, 0, 0);
            stack.Children.Add(sub);
        }
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        if (accessory is not null)
        {
            var host = new ContentControl { Content = accessory, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(host, 2);
            grid.Children.Add(host);
        }
        return grid;
    }

    /// <summary>A filled circle with a glyph in the middle (fallback app icon).</summary>
    public static Border GlyphCircle(string glyph, Brush background, double size, double glyphSize) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = background,
        Child = Glyph(glyph, glyphSize, White),
    };

    public static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}

/// <summary>Vector icons drawn to resemble SF Symbols.</summary>
public static class Icons
{
    public static readonly Geometry Play = Parse("M 1,0 L 13,7.5 L 1,15 Z");
    public static readonly Geometry Pause = Parse("M 0,0 H 4.5 V 15 H 0 Z M 8.5,0 H 13 V 15 H 8.5 Z");
    public static readonly Geometry Next = Parse("M 0,0 L 9,6.5 L 0,13 Z M 9,0 L 18,6.5 L 9,13 Z");
    public static readonly Geometry Previous = Parse("M 18,0 L 9,6.5 L 18,13 Z M 9,0 L 0,6.5 L 9,13 Z");
    public static readonly Geometry Close = Parse("M 0,0 L 12,12 M 12,0 L 0,12");
    public static readonly Geometry Check = Parse("M 0,7 L 5,12 L 14,1");

    public const string TimerGlyph = "";   // Stopwatch
    public const string ChatGlyph = "";    // Message
    public const string UpdateGlyph = ""; // Download
    public const string BellGlyph ="";    // Ringer

    private static Geometry Parse(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    /// <summary>Stroke-only icon (close / check).</summary>
    public static Path Stroke(Geometry g, double size, Brush brush, double thickness = 2.2) => new()
    {
        Data = g,
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>iOS-style battery: outline, fill for the charge level, a nub, and a bolt while charging.</summary>
    public static FrameworkElement Battery(double fraction, bool charging, Brush fill, double width = 27)
    {
        var height = width * 0.48;
        var body = new Border
        {
            Width = width, Height = height,
            CornerRadius = new CornerRadius(height * 0.3),
            BorderBrush = Ui.Grey, BorderThickness = new Thickness(1.2),
            Padding = new Thickness(1.6),
            Child = new Border
            {
                Width = Math.Max(2, (width - 6.4) * Math.Clamp(fraction, 0, 1)),
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(height * 0.18),
                Background = fill,
            },
        };
        var nub = new Border
        {
            Width = 2, Height = height * 0.36, Margin = new Thickness(1, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 1, 1, 0), Background = Ui.Grey, VerticalAlignment = VerticalAlignment.Center,
        };
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { body, nub } });
        if (charging)
        {
            grid.Children.Add(new Path
            {
                Data = Bolt, Fill = Ui.White, Stroke = Brushes.Black, StrokeThickness = 0.8,
                Stretch = Stretch.Uniform, Height = height * 0.95, Margin = new Thickness(0, 0, 3, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        return grid;
    }

    private static readonly Geometry Bolt = Parse("M 6,0 L 0,8 H 4 L 2,14 L 9,5 H 5 Z");

    /// <summary>Circular progress arc starting at 12 o'clock, clockwise; <paramref name="progress"/> is the filled part 0..1.</summary>
    public static Path Ring(double progress, double size, double stroke, Brush brush)
    {
        var r = (size - stroke) / 2;
        var p = Math.Clamp(progress, 0.0001, 0.9999);
        var angle = p * 2 * Math.PI;
        var start = new Point(size / 2, stroke / 2);
        var end = new Point(size / 2 + r * Math.Sin(angle), size / 2 - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        return new Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = brush,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = size,
            Height = size,
        };
    }

    /// <summary>The Claude "spark": tapered rays of varying length around a centre.</summary>
    public static Geometry ClaudeSpark { get; } = BuildSpark();

    private static Geometry BuildSpark()
    {
        var g = new StreamGeometry();
        double[] lengths = { 1.0, 0.78, 0.92, 0.7, 0.98, 0.8, 0.9, 0.74, 1.0, 0.82, 0.88, 0.72 };
        using (var ctx = g.Open())
        {
            for (var i = 0; i < lengths.Length; i++)
            {
                var a = i * Math.PI * 2 / lengths.Length + 0.13;
                var len = 50 * lengths[i];
                var w = 5.2;
                var dir = new Vector(Math.Cos(a), Math.Sin(a));
                var perp = new Vector(-dir.Y, dir.X);
                var c = new Point(50, 50);
                var inner = c + dir * 6;
                var outer = c + dir * len;
                ctx.BeginFigure(inner + perp * (w * 0.55), true, true);
                ctx.LineTo(outer + perp * (w * 0.5), true, true);
                ctx.ArcTo(outer - perp * (w * 0.5), new Size(w * 0.5, w * 0.5), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(inner - perp * (w * 0.55), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    public static Path Claude(double size) => new()
    {
        Data = ClaudeSpark,
        Fill = Ui.ClaudeOrange,
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(0.5, 0.5),
    };
}
