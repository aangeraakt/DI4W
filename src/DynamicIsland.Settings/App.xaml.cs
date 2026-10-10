using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace DynamicIsland.SettingsApp;

public partial class App : Application
{
    public const string WindowTitle = "Dynamic Island Settings";

    private Mutex? _singleInstance;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, "DynamicIsland4Windows.Settings", out var isNew);
        if (!isNew)
        {
            // Already open: bring that window forward instead of opening a second one.
            var existing = FindWindow(null, WindowTitle);
            if (existing != IntPtr.Zero)
            {
                ShowWindow(existing, 9 /* SW_RESTORE */);
                SetForegroundWindow(existing);
            }
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);
}
