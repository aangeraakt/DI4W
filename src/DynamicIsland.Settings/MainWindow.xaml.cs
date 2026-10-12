using DynamicIsland.SettingsApp.Pages;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;

namespace DynamicIsland.SettingsApp;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["General"] = typeof(GeneralPage),
        ["Media"] = typeof(MediaPage),
        ["Claude"] = typeof(ClaudePage),
        ["Notifications"] = typeof(NotificationsPage),
        ["Addons"] = typeof(AddonsPage),
        ["About"] = typeof(AboutPage),
    };

    /// <param name="startPage">Tag of the page to open first ("General", "About", …); null for General.</param>
    public MainWindow(string? startPage = null)
    {
        InitializeComponent();
        Translator.Apply(Content); // title bar and navigation
        Title = App.WindowTitle;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "island.ico"));

        var scale = GetDpiScale();
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(720 * scale)));
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.PreferredMinimumWidth = (int)(560 * scale);
        presenter.PreferredMinimumHeight = (int)(480 * scale);
        // Keep the resizable border but drop Windows' own title bar and its min/max/close buttons;
        // the macOS-style traffic lights replace them.
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        CenterOnScreen();

        Lights.CloseRequested += Close;
        Lights.MinimizeRequested += presenter.Minimize;
        Lights.ZoomRequested += () =>
        {
            if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
            else presenter.Maximize();
        };
        Activated += (_, e) => Lights.IsWindowActive = e.WindowActivationState != WindowActivationState.Deactivated;

        // The title bar is a drag area, which would swallow clicks on the lights; carve them out.
        AppTitleBar.SizeChanged += (_, _) => UpdateClickableRegions();
        AppTitleBar.Loaded += (_, _) =>
        {
            UpdateClickableRegions();
            AppTitleBar.XamlRoot.Changed += (_, _) => UpdateClickableRegions();
        };

        Closed += (_, _) => SettingsStore.Flush();
        Nav.SelectedItem = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>()
                               .FirstOrDefault(i => string.Equals(i.Tag as string, startPage, StringComparison.OrdinalIgnoreCase))
                           ?? Nav.MenuItems[0];
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
        {
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
            Translator.Apply(ContentFrame.Content);
        }
    }

    private void UpdateClickableRegions()
    {
        if (Lights.XamlRoot is not { } root) return;
        var scale = root.RasterizationScale;
        var bounds = Lights.TransformToVisual(null).TransformBounds(new Rect(0, 0, Lights.ActualWidth, Lights.ActualHeight));
        // A few pixels of slack around the dots so near-misses still hit a button instead of dragging the window.
        const double slack = 4;
        var rect = new RectInt32((int)((bounds.X - slack) * scale), (int)((bounds.Y - slack) * scale),
                                 (int)((bounds.Width + 2 * slack) * scale), (int)((bounds.Height + 2 * slack) * scale));
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, new[] { rect });
    }

    private double GetDpiScale()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return GetDpiForWindow(hwnd) / 96.0;
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + (area.Width - AppWindow.Size.Width) / 2,
                                      area.Y + (area.Height - AppWindow.Size.Height) / 2));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
