using System.Runtime.InteropServices;
using DynamicIsland.Settings;
using Microsoft.UI.Xaml;

namespace DynamicIsland.SettingsApp;

public partial class App : Application
{
    public const string WindowTitle = "Dynamic Island Settings";

    private Mutex? _singleInstance;
    private MainWindow? _window;

    public static MainWindow? MainWindow => ((App)Current)._window;

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

        // "--page About" opens a specific page (the island uses it for "Update available").
        var cmd = Environment.GetCommandLineArgs();
        var pageIndex = Array.IndexOf(cmd, "--page");
        var page = pageIndex >= 0 && pageIndex + 1 < cmd.Length ? cmd[pageIndex + 1] : null;

        // Language first: pages are translated as they are built.
        L.Use(SettingsStore.Current.Language);
        try
        {
            // Also for the controls' own text (dialog buttons, number boxes).
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = L.Language switch { "nl" => "nl-NL", "de" => "de-DE", "fr" => "fr-FR", _ => "en-US" };
        }
        catch
        {
            // Not available in every setup; our own text is translated regardless.
        }

        // The island asks for the tour after a fresh install; also show it if this app is opened first.
        // "--report-crash": the island crashed last time and the user clicked its offer to report it.
        var crash = cmd.Contains("--report-crash") ? CrashReport.Read() : null;
        if (crash is not null)
        {
            CrashReport.Clear();
            page = "About";
        }
        _window = new MainWindow(page, welcome: crash is null && (cmd.Contains("--welcome") || !SettingsStore.Current.WelcomeShown), crash: crash);
        _window.Activate();
    }

    /// <summary>Restarts the settings app on a page (after a language change, so every text is redrawn).</summary>
    public static void Restart(string page)
    {
        var app = (App)Current;
        SettingsStore.Flush();
        app._singleInstance?.ReleaseMutex();
        app._singleInstance?.Dispose();
        app._singleInstance = null;
        var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        psi.ArgumentList.Add("--page");
        psi.ArgumentList.Add(page);
        System.Diagnostics.Process.Start(psi);
        app.Exit();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);
}
