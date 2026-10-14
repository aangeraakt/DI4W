using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Activities.Api;
using DynamicIsland.Activities.Claude;
using DynamicIsland.Activities.Focus;
using DynamicIsland.Activities.Stopwatches;
using DynamicIsland.Activities.Media;
using DynamicIsland.Activities.Notifications;
using DynamicIsland.Activities.Timer;
using DynamicIsland.Addons;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using DynamicIsland.Updates;
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
    private FocusActivity _focus = null!;
    private StopwatchActivity _stopwatch = null!;
    private NotificationService _notifications = null!;
    private ClaudeHookServer? _hookServer;
    private AddonHost? _addons;
    private FileSystemWatcher? _settingsWatcher;
    private DispatcherTimer? _reloadDebounce;
    private EventWaitHandle? _quitSignal;
    private DispatcherTimer? _updateTimer;
    private Version? _announcedUpdate;

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
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);
            if (args.IsTerminating) CrashReport.Write(args.ExceptionObject as Exception); // offered for reporting on the next start
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        // The settings app sets this before installing an update, so the island exits cleanly (tray icon, port, mutex).
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, UpdateChecker.IslandQuitEvent);
        ThreadPool.RegisterWaitForSingleObject(_quitSignal, (_, _) => Dispatcher.BeginInvoke(Shutdown),
                                               null, Timeout.Infinite, executeOnlyOnce: true);

        Log.Info($"Starting Dynamic Island {UpdateChecker.CurrentVersion}");
        _settings = AppSettings.Load();
        L.Use(_settings.Language);
        // Before any view is built: the colors are read once.
        Palette.Light = IslandTheme.IsLight(_settings.Theme);
        ApplyMenuTheme();
        if (_settings.IslandPath != Environment.ProcessPath)
        {
            var path = Environment.ProcessPath;
            _settings = AppSettings.Update(s => s.IslandPath = path);
        }
        Power.Start();
        _manager = new ActivityManager { Order = _settings.ActivityOrder };

        // Activities
        _timer = new TimerActivity(_manager, _settings);
        _manager.Register(_timer);

        _focus = new FocusActivity(_manager, _settings);
        _manager.Register(_focus);

        _stopwatch = new StopwatchActivity();
        _manager.Register(_stopwatch);

        var claude = new ClaudeActivity(_manager, _settings);
        _manager.Register(claude);

        var mediaService = new MediaService(_settings, Dispatcher);
        var media = new MediaActivity(mediaService, _settings);
        _manager.Register(media);

        // Window
        _island = new IslandWindow(_manager, _settings)
        {
            IdlePanelFactory = () => (BuildPicker(), TimerPicker.PanelSize),
            ContextMenuFactory = BuildMenu,
        };
        _island.Show();
        _island.HotkeyPressed += () => SetPaused(!Paused);
        _island.MessageReceived += OnWindowMessage;
        _island.RegisterHideHotkey(_settings.HideHotkey);
        _island.RegisterExpandHotkey(_settings.ExpandHotkey);

        _addons = new AddonHost(new AddonContext(_settings, _manager, _island, Dispatcher));
        _addons.Sync();

        CreateTray();
        if (_settings.Paused) SetPaused(true, save: false); // hidden before the restart: stay hidden
        WatchSettings();
        WatchWindowsTheme();
        ScheduleUpdateChecks();
        OfferCrashReport();

        // A fresh install: show the welcome tour in the settings app once the island is up (the app marks it shown).
        if (!_settings.WelcomeShown) _ = Dispatcher.BeginInvoke(() => OpenSettings("--welcome"), DispatcherPriority.ApplicationIdle);

        // Integrations
        // One local server for Claude Code hooks (/claude/...) and the Island API (/alert, /progress).
        var claudeOn = _settings.ClaudeEnabled;
        var api = _settings.Addons.IslandApi ? new IslandApi(_manager, Dispatcher) : null;
        if (claudeOn || api is not null)
        {
            _hookServer = new ClaudeHookServer(_settings.ClaudePort, request =>
            {
                if (request.Path.StartsWith("/claude/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!claudeOn) return LocalResponse.Error(404, "Claude Code is turned off in the settings.");
                    var kind = request.Path["/claude/".Length..].Trim('/').ToLowerInvariant();
                    Dispatcher.BeginInvoke(() => claude.Handle(kind, request.Body));
                    return LocalResponse.Ok;
                }
                if (request.Path == "/") return LocalResponse.Ok;
                return api?.Handle(request);
            });
            _hookServer.Start();
        }

        _notifications = new NotificationService(_settings, Dispatcher);
        _notifications.Received += new NotificationPresenter(_manager, _settings).Show;

        if (_settings.MediaEnabled) await mediaService.StartAsync();
        await _notifications.StartAsync();
        _addons?.SetNotificationAccess(_notifications.Status);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWatcher?.Dispose();
        _quitSignal?.Dispose();
        _hookServer?.Dispose();
        _addons?.Shutdown();
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

    private void StartFocus()
    {
        _focus.Start();
        _island?.ClosePanel();
    }

    private void StartStopwatch()
    {
        _stopwatch.Start();
        _island?.ClosePanel();
    }

    private FrameworkElement BuildPicker(int tab = 0) =>
        TimerPicker.Build(StartTimer, StartFocus, StartStopwatch, _settings.Addons,
                          () => RecentView.Build(_manager.History, () => _island?.ClosePanel()), tab);

    /// <summary>The panel on its Recent tab (from the tray menu).</summary>
    private void ShowRecent()
    {
        if (Paused) SetPaused(false);
        _island?.OpenPanel(BuildPicker(tab: 3), TimerPicker.PanelSize);
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
                           || updated.MediaEnabled != _settings.MediaEnabled
                           || updated.Addons.IslandApi != _settings.Addons.IslandApi
                           || updated.Language != _settings.Language
                           || IslandTheme.IsLight(updated.Theme) != Palette.Light;

        var hotkeyChanged = updated.HideHotkey != _settings.HideHotkey;
        var expandHotkeyChanged = updated.ExpandHotkey != _settings.ExpandHotkey;
        if (updated.Paused != Paused) SetPaused(updated.Paused, save: false);

        _settings.CopyFrom(updated);
        _manager.Order = _settings.ActivityOrder;
        _island?.ApplyScale();
        if (hotkeyChanged) _island?.RegisterHideHotkey(_settings.HideHotkey);
        if (expandHotkeyChanged) _island?.RegisterExpandHotkey(_settings.ExpandHotkey);
        _addons?.Sync();

        if (needsRestart)
        {
            Log.Info("Restarting to apply settings");
            _restart = true;
            Shutdown();
        }
    }

    // ───────────────────────────── crash reports ─────────────────────────────

    /// <summary>
    /// The island crashed last time: say so, and offer the settings app's report form with the details filled in.
    /// Only the user can send it (they see it all in the form first); ignoring the alert forgets the crash.
    /// </summary>
    private void OfferCrashReport()
    {
        if (CrashReport.Read() is null) return;
        if (!_settings.OfferCrashReports)
        {
            CrashReport.Clear();
            return;
        }

        var reporting = false;
        var alert = new IslandAlert
        {
            Content = Ui.AlertView(Ui.GlyphCircle("", Ui.Orange, 46, 20), L.T("Dynamic Island closed unexpectedly"),
                                   L.T("Click to report it. You see everything before anything is sent.")),
            Size = new Size(420, 76),
            Duration = TimeSpan.FromSeconds(10),
            StickyUntilHover = true,
            OnClick = () =>
            {
                reporting = true;
                OpenSettings("--report-crash"); // the settings app clears the crash once it has read it
            },
            OnDismissed = () =>
            {
                if (!reporting) CrashReport.Clear();
            },
        };
        // A little after startup, so it isn't lost among the first activities.
        var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            _manager.ShowAlert(alert);
        };
        delay.Start();
    }

    // ───────────────────────────── theme ─────────────────────────────

    /// <summary>With the theme set to follow Windows, switching Windows between light and dark restarts the
    /// island in the new colors (views are built once, so they can't be recolored in place).</summary>
    private void WatchWindowsTheme()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_settings.Theme != IslandTheme.System || IslandTheme.IsLight(_settings.Theme) == Palette.Light || _restart) return;
                Log.Info("Windows switched between light and dark; restarting in the new colors");
                _restart = true;
                Shutdown();
            });
        };
    }

    /// <summary>The right-click / tray menu in the island's colors.</summary>
    private void ApplyMenuTheme()
    {
        if (!Palette.Light) return;
        Resources["MenuBackground"] = Ui.Brush("#F9F9F9");
        Resources["MenuForeground"] = Ui.Brush("#1C1C1E");
        Resources["MenuSecondary"] = Ui.Brush("#6C6C70");
        Resources["MenuBorder"] = Ui.Brush("#D1D1D6");
        Resources["MenuHover"] = Ui.Brush("#E5E5EA");
    }

    // ───────────────────────────── previews from the settings app ─────────────────────────────

    /// <summary>The settings app's Preview buttons send "preview:Battery" (WM_COPYDATA) to show a sample pop-up.</summary>
    private void OnWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        const int WM_COPYDATA = 0x004A;
        if (msg != WM_COPYDATA || IslandMessages.Read(lParam) is not { } text) return;
        if (text.StartsWith(IslandMessages.PreviewPrefix, StringComparison.Ordinal))
        {
            var key = text[IslandMessages.PreviewPrefix.Length..];
            // Not while handling SendMessage: the settings app waits for it to return.
            Dispatcher.BeginInvoke(() => _addons?.Preview(key));
        }
    }

    // ───────────────────────────── updates ─────────────────────────────

    /// <summary>First check shortly after startup (not to slow it down), then every few hours.</summary>
    private void ScheduleUpdateChecks()
    {
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdates();
        };
        _updateTimer.Start();
    }

    private async Task CheckForUpdates()
    {
        if (!_settings.AutoCheckUpdates) return;
        try
        {
            var update = await UpdateChecker.CheckAsync();
            if (update is null || update.Version == _announcedUpdate) return; // announce each version once per run
            _announcedUpdate = update.Version;
            Log.Info($"Update available: {update.Version}");

            _manager.ShowAlert(new IslandAlert
            {
                Content = Ui.AlertView(Ui.GlyphCircle(Icons.UpdateGlyph, Ui.Blue, 46, 20),
                                       L.T("Update available"), L.F("Version {0} is ready. Click to install.", update.Version)),
                Duration = TimeSpan.FromSeconds(8),
                StickyUntilHover = true,
                OnClick = () => OpenSettings("--page", "About"),
            });
        }
        catch (Exception ex)
        {
            Log.Info($"Update check failed: {ex.Message}");
        }
    }

    // ───────────────────────────── tray + menu ─────────────────────────────

    private void CreateTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = L.T("Dynamic Island"),
            Icon = TrayIcons.Normal,
            ContextMenu = BuildMenu(),
            NoLeftClickDelay = true,
        };
        // One click brings a hidden island back; a double-click opens the settings.
        _tray.TrayLeftMouseUp += (_, _) =>
        {
            if (Paused) SetPaused(false);
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

        // The island itself: open the settings (also a double-click on the tray icon), and show or hide it.
        var settings = Item(L.T("Open settings"), () => OpenSettings(), "");
        settings.FontWeight = FontWeights.SemiBold;
        menu.Items.Add(settings);
        var shortcut = Hotkey.TryParse(_settings.HideHotkey, out _, out _) ? _settings.HideHotkey : "";
        var visibility = Paused
            ? Item(L.T("Show island"), () => SetPaused(false), "")
            : Item(L.T("Hide island"), () => SetPaused(true), "");
        visibility.InputGestureText = shortcut;
        menu.Items.Add(visibility);
        menu.Items.Add(new Separator());

        // Timers.
        var timer = new MenuItem { Header = L.T("Start timer"), Icon = MenuIcon(Icons.TimerGlyph) };
        foreach (var m in new[] { 1, 3, 5, 10, 15, 25, 45, 60 })
        {
            timer.Items.Add(Item(L.F("{0} minutes", m), () => StartTimer(TimeSpan.FromMinutes(m))));
        }
        timer.Items.Add(new Separator());
        timer.Items.Add(Item(L.T("Custom…"), () =>
        {
            if (Paused) SetPaused(false);
            _island?.OpenPanel(BuildPicker(), TimerPicker.PanelSize);
        }));
        menu.Items.Add(timer);
        if (_timer.IsActive) menu.Items.Add(Item(L.T("Cancel timer"), _timer.Cancel, ""));
        menu.Items.Add(_focus.IsActive ? Item(L.T("Stop focus session"), _focus.Stop, "") : Item(L.T("Start focus session"), StartFocus, ""));
        menu.Items.Add(_stopwatch.IsActive ? Item(L.T("Stop stopwatch"), _stopwatch.Stop, "") : Item(L.T("Start stopwatch"), StartStopwatch, ""));
        menu.Items.Add(Item(L.T("Recent alerts…"), ShowRecent, ""));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item(L.T("Check for updates…"), () => OpenSettings("--page", "About"), ""));
        menu.Items.Add(Item(L.T("Quit"), Shutdown, ""));
    }

    private static TextBlock MenuIcon(string glyph) => Ui.Glyph(glyph, 14, Ui.Primary);

    /// <summary>
    /// Hides or shows the island. Used by the menu and the hide shortcut; the menu reads Paused when it opens.
    /// Saved, so a hidden island stays hidden after a restart; the tray tooltip says so.
    /// </summary>
    private void SetPaused(bool paused, bool save = true)
    {
        Paused = paused;
        if (paused) _island?.Hide();
        else _island?.Show();
        if (_tray is not null)
        {
            _tray.Icon = paused ? TrayIcons.Paused : TrayIcons.Normal;
            _tray.ToolTipText = paused ? L.T("Dynamic Island is hidden. Click to show it.") : L.T("Dynamic Island");
        }
        if (save && _settings.Paused != paused) AppSettings.Update(s => s.Paused = paused);
        _settings.Paused = paused;
    }

    private static MenuItem Item(string header, Action onClick, string? glyph = null)
    {
        var item = new MenuItem { Header = header };
        if (glyph is not null) item.Icon = MenuIcon(glyph);
        item.Click += (_, _) => onClick();
        return item;
    }

    // ───────────────────────────── settings app ─────────────────────────────

    /// <param name="args">e.g. "--page", "About" to open a specific page (only when the app isn't open yet).</param>
    private static void OpenSettings(params string[] args)
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
            MessageBox.Show(L.T("The settings app (DynamicIsland.Settings.exe) was not found. Reinstall Dynamic Island, or publish both apps with: dotnet publish src/DynamicIsland -c Release -o publish"),
                            L.T("Dynamic Island"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            Process.Start(psi);
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
