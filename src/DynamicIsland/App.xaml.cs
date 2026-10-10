using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Activities.Claude;
using DynamicIsland.Activities.Media;
using DynamicIsland.Activities.Notifications;
using DynamicIsland.Activities.Timer;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using H.NotifyIcon;

namespace DynamicIsland;

public partial class App : Application
{
    private const string SettingsProcess = "DynamicIsland.Settings";

    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private bool _restart;
    private TaskbarIcon? _tray;
    private IslandWindow? _island;
    private AppSettings _settings = new();
    private ActivityManager _manager = null!;
    private TimerActivity _timer = null!;
    private NotificationService _notifications = null!;
    private ClaudeHookServer? _hookServer;
    private FileSystemWatcher? _settingsWatcher;
    private DispatcherTimer? _reloadDebounce;

    public static bool Paused { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(false, "DynamicIsland4Windows.SingleInstance");
        // After a self-restart the old process may still be shutting down; give it a moment.
        var wait = e.Args.Contains("--restart") ? TimeSpan.FromSeconds(10) : TimeSpan.Zero;
        try
        {
            _ownsMutex = _singleInstance.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info("Starting Dynamic Island");
        _settings = AppSettings.Load();
        if (_settings.IslandPath != Environment.ProcessPath)
        {
            _settings.IslandPath = Environment.ProcessPath;
            _settings.Save();
        }
        _manager = new ActivityManager();

        // Activities
        _timer = new TimerActivity(_manager, _settings);
        _manager.Register(_timer);

        var claude = new ClaudeActivity(_manager, _settings);
        _manager.Register(claude);

        var mediaService = new MediaService(_settings, Dispatcher);
        var media = new MediaActivity(mediaService, _settings);
        _manager.Register(media);

        // Window
        _island = new IslandWindow(_manager, _settings)
        {
            IdlePanelFactory = () => (TimerPicker.Build(StartTimer), TimerPicker.PanelSize),
            ContextMenuFactory = BuildMenu,
        };
        _island.Show();

        CreateTray();
        WatchSettings();

        // Integrations
        if (_settings.ClaudeEnabled)
        {
            _hookServer = new ClaudeHookServer(_settings.ClaudePort,
                (kind, json) => Dispatcher.BeginInvoke(() => claude.Handle(kind, json)));
            _hookServer.Start();
        }

        _notifications = new NotificationService(_settings, Dispatcher);
        _notifications.Received += ShowNotification;

        if (_settings.MediaEnabled) await mediaService.StartAsync();
        await _notifications.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWatcher?.Dispose();
        _hookServer?.Dispose();
        AudioLevelMeter.Shared.Dispose();
        _tray?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();

        if (_restart && Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
        }
        base.OnExit(e);
    }

    private void StartTimer(TimeSpan duration)
    {
        _timer.Start(duration);
        _island?.ClosePanel();
    }

    private void ShowNotification(IncomingNotification n)
    {
        FrameworkElement icon = n.Logo is not null
            ? Ui.Image(n.Logo, 46, 12)
            : Ui.GlyphCircle(Icons.ChatGlyph, Ui.DiscordBlurple, 46, 20);

        var process = n.App.Replace(" ", "");
        _manager.ShowAlert(new IslandAlert
        {
            Content = Ui.AlertView(icon, n.Title, n.Body),
            Size = new Size(400, string.IsNullOrEmpty(n.Body) || n.Body.Length < 40 ? 76 : 88),
            Duration = TimeSpan.FromSeconds(5),
            OnClick = () => Win32.FocusProcess(process),
        });
    }

    // ───────────────────────────── live settings ─────────────────────────────

    /// <summary>The settings app writes settings.json; pick up its changes without a restart.</summary>
    private void WatchSettings()
    {
        _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            ReloadSettings();
        };

        Directory.CreateDirectory(AppSettings.Folder);
        _settingsWatcher = new FileSystemWatcher(AppSettings.Folder, Path.GetFileName(AppSettings.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        void Changed() => Dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
        _settingsWatcher.Changed += (_, _) => Changed();
        _settingsWatcher.Created += (_, _) => Changed();
        _settingsWatcher.Renamed += (_, _) => Changed();
        _settingsWatcher.EnableRaisingEvents = true;
    }

    private void ReloadSettings()
    {
        var updated = AppSettings.TryLoad();
        if (updated is null) return; // half-written or locked; the next change event retries

        // These are wired up once at startup, so changing them restarts the island.
        var needsRestart = updated.ClaudeEnabled != _settings.ClaudeEnabled
                           || updated.ClaudePort != _settings.ClaudePort
                           || updated.MediaEnabled != _settings.MediaEnabled;

        _settings.CopyFrom(updated);
        _island?.ApplyScale();

        if (needsRestart)
        {
            Log.Info("Restarting to apply settings");
            _restart = true;
            Shutdown();
        }
    }

    // ───────────────────────────── tray + menu ─────────────────────────────

    private void CreateTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "Dynamic Island",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/island.ico")),
            ContextMenu = BuildMenu(),
        };
        _tray.TrayMouseDoubleClick += (_, _) => OpenSettings();
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>One menu used by both the tray icon and right-clicking the island. Rebuilt on open so it is current.</summary>
    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => Populate(menu);
        Populate(menu);
        return menu;
    }

    private void Populate(ContextMenu menu)
    {
        menu.Items.Clear();

        var timer = new MenuItem { Header = "Start timer" };
        foreach (var m in new[] { 1, 3, 5, 10, 15, 25, 45, 60 })
        {
            timer.Items.Add(Item($"{m} minutes", () => StartTimer(TimeSpan.FromMinutes(m))));
        }
        timer.Items.Add(new Separator());
        timer.Items.Add(Item("Custom…", () =>
        {
            _island?.OpenPanel(TimerPicker.Build(StartTimer), TimerPicker.PanelSize);
        }));
        menu.Items.Add(timer);
        if (_timer.IsActive) menu.Items.Add(Item("Cancel timer", _timer.Cancel));
        menu.Items.Add(new Separator());

        var pause = new MenuItem { Header = "Pause island", IsCheckable = true, IsChecked = Paused };
        pause.Click += (_, _) =>
        {
            Paused = pause.IsChecked;
            if (Paused) _island?.Hide();
            else _island?.Show();
        };
        menu.Items.Add(pause);
        menu.Items.Add(Item("Settings…", OpenSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", Shutdown));
    }

    private static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    // ───────────────────────────── settings app ─────────────────────────────

    private static void OpenSettings()
    {
        if (Process.GetProcessesByName(SettingsProcess).Length > 0)
        {
            Win32.FocusProcess(SettingsProcess);
            return;
        }

        var exe = FindSettingsApp();
        if (exe is null)
        {
            Log.Error("Settings app not found next to the island");
            MessageBox.Show("The settings app (DynamicIsland.Settings.exe) was not found.\n\n" +
                            "Publish both apps with:\n  dotnet publish src/DynamicIsland -c Release -o publish",
                            "Dynamic Island", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not start {exe}", ex);
        }
    }

    /// <summary>Published layout is publish\Settings\…; when running from source, use the newest build of the project.</summary>
    private static string? FindSettingsApp()
    {
        const string exeName = SettingsProcess + ".exe";
        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[] { Path.Combine(baseDir, "Settings", exeName), Path.Combine(baseDir, exeName) })
        {
            if (File.Exists(candidate)) return candidate;
        }

        for (var dir = new DirectoryInfo(baseDir); dir is not null; dir = dir.Parent)
        {
            var bin = Path.Combine(dir.FullName, SettingsProcess, "bin");
            if (!Directory.Exists(bin)) continue;
            return Directory.EnumerateFiles(bin, exeName, SearchOption.AllDirectories)
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .FirstOrDefault();
        }
        return null;
    }
}
