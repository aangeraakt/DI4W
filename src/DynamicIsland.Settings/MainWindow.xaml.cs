using DynamicIsland.SettingsApp.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
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
        ["About"] = typeof(AboutPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = App.WindowTitle;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "island.ico"));

        var scale = GetDpiScale();
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(720 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(560 * scale);
            p.PreferredMinimumHeight = (int)(480 * scale);
        }
        CenterOnScreen();

        Closed += (_, _) => SettingsStore.Flush();
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
        {
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
        }
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
