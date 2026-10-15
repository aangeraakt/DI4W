using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class IslandWindow : Window
{
    private const double CanvasWidth = 600;
    private const double CanvasHeight = 260;
    private const double CompactHeight = 34;
    private const double IdleWidth = 190;
    private const double BubbleSize = 34;
    private const double BubbleGap = 8;

    private enum Mode { Idle, Compact, Expanded, Panel, Alert }

    private readonly ActivityManager _manager;
    private readonly AppSettings _settings;

    // Shape springs. Width/height share a slightly bouncy spring; the radius follows a stiffer one.
    private readonly Spring _width = new(IdleWidth, 300, 24);
    private readonly Spring _height = new(CompactHeight, 300, 24);
    private readonly Spring _radius = new(CompactHeight / 2, 380, 30);
    private readonly Spring _bubble = new(0, 360, 22);
    private readonly Spring _hover = new(0, 420, 28);
    private readonly SpringDriver _driver;

    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _housekeeping;

    private Mode _mode = Mode.Idle;
    private object? _shownKey;
    private bool _expanded;
    private bool _hovering;
    private FrameworkElement? _panel;
    private Size _panelSize;
    private Activity? _secondary;
    private Win32.WinEventProc? _foregroundHook; // kept in a field so the GC doesn't collect the callback
    private int _fullscreenTicks;

    /// <summary>Content shown when the idle island is clicked (the timer picker).</summary>
    public Func<(FrameworkElement View, Size Size)>? IdlePanelFactory { get; set; }

    /// <summary>Builds the right-click menu each time it opens.</summary>
    public Func<ContextMenu>? ContextMenuFactory { get; set; }

    /// <summary>The global hide/show shortcut was pressed.</summary>
    public event Action? HotkeyPressed;

    /// <summary>Every window message (msg, wParam, lParam), for add-ons that listen to broadcasts
    /// such as device changes or clipboard updates.</summary>
    public event Action<int, IntPtr, IntPtr>? MessageReceived;

    /// <summary>Where privacy dots go: right of the pill (and the bubble, when shown).</summary>
    public Panel IndicatorPanel => Indicators;

    /// <summary>Set by the game add-on so a fullscreen game doesn't hide the island.</summary>
    public bool KeepVisibleInFullscreen { get; set; }

    private const int HideHotkeyId = 1;
    private const int ExpandHotkeyId = 2;

    /// <summary>Closes a view opened with the keyboard shortcut (there's no mouse to move away).</summary>
    private readonly DispatcherTimer _keyboardCollapse = new() { Interval = TimeSpan.FromSeconds(8) };

    public IslandWindow(ActivityManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        InitializeComponent();

        _driver = new SpringDriver(ApplyFrame, _width, _height, _radius, _bubble, _hover);

        Pill.Background = Bubble.Background = Ui.PillBackground;
        if (Palette.Light)
        {
            // A hairline edge and the pill's own color under the shadow, so a light pill doesn't melt into white pages.
            Shadow.Background = Ui.PillBackground;
            Shadow.Opacity = 0.35;
            Pill.BorderBrush = Bubble.BorderBrush = Ui.Brush("#22000000");
            Pill.BorderThickness = Bubble.BorderThickness = new Thickness(1);
        }

        ApplyScale();
        ApplyFrame();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.MakeToolWindow(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            // Re-assert topmost the moment any other window comes to the front, instead of up to a second later.
            _foregroundHook = (_, _, _, _, _, _, _) => KeepOnTop();
            Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                  _foregroundHook, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        };
        Loaded += (_, _) => PositionOnScreen();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionOnScreen, DispatcherPriority.Background);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PositionOnScreen, DispatcherPriority.Background);

        Pill.MouseEnter += OnPillEnter;
        Pill.MouseLeave += OnPillLeave;
        Pill.MouseLeftButtonUp += OnPillClick;
        Pill.MouseRightButtonUp += OnPillRightClick;
        Bubble.MouseLeftButtonUp += (_, e) =>
        {
            if (_secondary is not null) _manager.Pin(_secondary);
            e.Handled = true;
        };
        Bubble.MouseRightButtonUp += OnPillRightClick;

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            Collapse();
        };

        _keyboardCollapse.Tick += (_, _) =>
        {
            _keyboardCollapse.Stop();
            if (!_hovering) Collapse();
        };

        _housekeeping = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _housekeeping.Tick += (_, _) => Housekeeping();
        _housekeeping.Start();

        // Battery saver: drop the soft shadow (re-rendered by the CPU on every frame) and check less often.
        _shadowEffect = Shadow.Effect;
        Power.Changed += ApplyPower;
        ApplyPower();

        // Hidden (by the user, or for a fullscreen app): alerts go to Recent instead of piling up, and the island
        // comes back collapsed; the mouse can't still be over it, so a hover that never got its "leave" is reset.
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _hovering = false;
                _expanded = false;
                _collapseTimer.Stop();
                _keyboardCollapse.Stop();
                if (_panel is not null) ClosePanel();
            }
            _manager.Suppressed = !IsVisible;
            Refresh();
        };

        _manager.Changed += Refresh;
        Refresh();

        // Closing the island window (installer upgrade/uninstall, Windows shutting down) quits the whole app
        // cleanly, so the tray icon, hook port and settings are released properly.
        Closing += (_, _) =>
        {
            if (_quitting) return;
            _quitting = true;
            Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        };
    }

    private bool _quitting;
    private readonly Effect? _shadowEffect;

    private void ApplyPower()
    {
        Shadow.Effect = Power.Saving ? null : _shadowEffect;
        _housekeeping.Interval = Power.Interval(TimeSpan.FromSeconds(1), 2);
    }

    // ───────────────────────────── public API ─────────────────────────────

    public void OpenPanel(FrameworkElement view, Size size)
    {
        _panel = view;
        _panelSize = size;
        Refresh();
    }

    public void ClosePanel()
    {
        _panel = null;
        Refresh();
    }

    /// <summary>Small status items shown in the empty pill (left: Do Not Disturb moon, right: weather).</summary>
    public void SetIdleContent(FrameworkElement? leading, FrameworkElement? trailing)
    {
        if (ReferenceEquals(IdleLeadingHost.Content, leading) && ReferenceEquals(IdleTrailingHost.Content, trailing)) return;
        IdleLeadingHost.Content = leading;
        IdleTrailingHost.Content = trailing;
        if (_mode == Mode.Idle) _shownKey = null; // re-run the reveal so new content fades in
        Refresh();
    }

    private bool HasIdleContent => IdleLeadingHost.Content is not null || IdleTrailingHost.Content is not null;

    public void Collapse()
    {
        _expanded = false;
        _panel = null;
        Refresh();
    }

    /// <summary>Registers (or replaces) the global hide/show shortcut. Empty or invalid text just removes it.</summary>
    public void RegisterHideHotkey(string? shortcut) => RegisterHotkey(HideHotkeyId, shortcut, "Hide");

    /// <summary>The shortcut that opens/closes the expanded view, so the island can be used without a mouse.</summary>
    public void RegisterExpandHotkey(string? shortcut) => RegisterHotkey(ExpandHotkeyId, shortcut, "Expand");

    private void RegisterHotkey(int id, string? shortcut, string purpose)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Win32.UnregisterHotKey(hwnd, id);
        if (!Hotkey.TryParse(shortcut, out var modifiers, out var key)) return;

        if (Win32.RegisterHotKey(hwnd, id, modifiers | Win32.MOD_NOREPEAT, key))
        {
            Log.Info($"{purpose} shortcut: {shortcut}");
        }
        else
        {
            Log.Error($"Hotkey registration failed for {shortcut} (error {Marshal.GetLastWin32Error()}); another app probably uses it");
        }
    }

    /// <summary>Opens the current activity (or the timer picker when idle), or closes it again.</summary>
    public void ToggleExpanded()
    {
        if (!IsVisible) return; // hidden (paused, or a fullscreen app): it would open unseen and pop up open later
        if (_expanded || _panel is not null)
        {
            Collapse();
            return;
        }
        if (_manager.Active.Count > 0)
        {
            _expanded = true;
            Refresh();
        }
        else if (IdlePanelFactory is not null)
        {
            var (view, size) = IdlePanelFactory();
            OpenPanel(view, size);
        }
        // Without a mouse over it, it would stay open forever; close after a while like a hover-out would.
        _collapseTimer.Stop();
        _keyboardCollapse.Stop();
        _keyboardCollapse.Start();
    }

    public void ApplyScale()
    {
        RootScale.ScaleX = RootScale.ScaleY = _settings.Scale;
        Width = CanvasWidth * _settings.Scale;
        Height = CanvasHeight * _settings.Scale;
        PositionOnScreen();
        ApplyFrame();
    }

    // ───────────────────────────── state → shape ─────────────────────────────

    private void Refresh()
    {
        var alert = _manager.CurrentAlert;
        var active = _manager.Active;
        var primary = active.Count > 0 ? active[0] : null;
        _secondary = active.Count > 1 ? active[1] : null;

        if (primary is null) _expanded = false;

        Mode mode;
        Size target;
        object key;

        if (alert is not null)
        {
            mode = Mode.Alert;
            target = alert.Size;
            key = alert;
            AlertLayer.Content = alert.Content;
        }
        else if (_panel is not null)
        {
            mode = Mode.Panel;
            target = _panelSize;
            key = _panel;
            ExpandedLayer.Content = _panel;
        }
        else if (_expanded && primary is not null)
        {
            mode = Mode.Expanded;
            target = primary.ExpandedSize;
            key = primary.Expanded;
            ExpandedLayer.Content = primary.Expanded;
        }
        else if (primary is not null)
        {
            mode = Mode.Compact;
            target = new Size(primary.CompactWidth, CompactHeight);
            key = primary;
            if (!ReferenceEquals(LeadingHost.Content, primary.Leading))
            {
                LeadingHost.Content = null;
                TrailingHost.Content = null;
                LeadingHost.Content = primary.Leading;
                TrailingHost.Content = primary.Trailing;
            }
        }
        else
        {
            mode = Mode.Idle;
            target = new Size(IdleWidth, CompactHeight);
            key = Mode.Idle;
        }

        // Bubble only next to the compact pill.
        var showBubble = mode == Mode.Compact && _secondary is not null;
        if (showBubble && !ReferenceEquals(BubbleHost.Content, _secondary!.Bubble))
        {
            BubbleHost.Content = _secondary.Bubble;
        }
        _bubble.Target = showBubble ? 1 : 0;
        Bubble.IsHitTestVisible = showBubble;

        _width.Target = target.Width;
        _height.Target = target.Height;
        _radius.Target = Math.Min(target.Height / 2, 42);
        _hover.Target = _hovering && mode is Mode.Idle or Mode.Compact ? 1 : 0;

        if (mode != _mode || !Equals(key, _shownKey))
        {
            SwitchLayers(mode, target);
            _mode = mode;
            _shownKey = key;
        }

        _driver.Kick();
        UpdateHoverTarget();
        UpdateAccessibleName();
    }

    private Activity? _hoverTarget;

    /// <summary>Tells the activity in the compact pill when the mouse is over it (music shows its buttons then).</summary>
    private void UpdateHoverTarget()
    {
        var target = _hovering && _mode == Mode.Compact && _manager.Active.Count > 0 ? _manager.Active[0] : null;
        if (target == _hoverTarget) return;
        _hoverTarget?.OnHover(false);
        target?.OnHover(true);
        _hoverTarget = target;
    }

    private void SwitchLayers(Mode mode, Size size)
    {
        FrameworkElement? show = mode switch
        {
            Mode.Compact => CompactLayer,
            Mode.Expanded or Mode.Panel => ExpandedLayer,
            Mode.Alert => AlertLayer,
            Mode.Idle when HasIdleContent => IdleLayer,
            _ => null,
        };

        foreach (var layer in new FrameworkElement[] { IdleLayer, CompactLayer, ExpandedLayer, AlertLayer })
        {
            if (layer == show) continue;
            if (layer.Opacity > 0 || layer.HasAnimatedProperties) FadeOut(layer);
        }

        if (show is null) return;

        // Content is laid out at its final size from the start; the animating pill clips it.
        show.Width = size.Width;
        show.Height = size.Height;
        Reveal(show, mode is Mode.Compact or Mode.Idle ? 0.06 : 0.14);
    }

    private static void FadeOut(FrameworkElement layer)
    {
        layer.IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        // Once invisible, hide it for real: screen readers would otherwise still read its old text.
        fade.Completed += (_, _) =>
        {
            if (!layer.IsHitTestVisible) layer.Visibility = Visibility.Hidden;
        };
        layer.BeginAnimation(OpacityProperty, fade);
    }

    private static void Reveal(FrameworkElement layer, double delaySeconds)
    {
        layer.IsHitTestVisible = true;
        layer.Visibility = Visibility.Visible;
        var delay = TimeSpan.FromSeconds(delaySeconds);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = delay,
            EasingFunction = ease,
        });

        var scale = new ScaleTransform(0.9, 0.9);
        layer.RenderTransformOrigin = new Point(0.5, 0.3);
        layer.RenderTransform = scale;
        var grow = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(380))
        {
            BeginTime = delay,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        // The blur-in is drawn by the CPU (transparent windows aren't GPU-drawn); skip it on battery saver.
        if (Power.Saving)
        {
            layer.Effect = null;
            return;
        }
        var blur = new BlurEffect { Radius = 10, RenderingBias = RenderingBias.Performance };
        layer.Effect = blur;
        var unblur = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = ease };
        unblur.Completed += (_, _) =>
        {
            if (layer.Effect == blur) layer.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
    }

    private void ApplyFrame()
    {
        var hover = _hover.Value;
        var w = Math.Max(20, _width.Value + hover * 10);
        var h = Math.Max(20, _height.Value + hover * 3);
        var r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        var left = (CanvasWidth - w) / 2;
        var top = _settings.TopOffset;

        // When the bubble is visible, shift the pill left so pill + bubble stay centered as a group.
        var bubbleShift = Math.Max(0, _bubble.Value) * (BubbleSize + BubbleGap) / 2;
        left -= bubbleShift;

        foreach (var b in new[] { Pill, Shadow })
        {
            b.Width = w;
            b.Height = h;
            b.CornerRadius = new CornerRadius(r);
            Canvas.SetLeft(b, left);
            Canvas.SetTop(b, top);
        }
        Pill.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);

        Canvas.SetLeft(Bubble, left + w + BubbleGap);
        Canvas.SetTop(Bubble, top + (h - BubbleSize) / 2);
        var bs = Math.Max(0, _bubble.Value);
        BubbleScale.ScaleX = BubbleScale.ScaleY = bs;
        Bubble.Opacity = Math.Clamp(bs * 1.4, 0, 1);

        // Privacy dots sit after the pill (and the bubble while it's out), level with the compact row.
        Canvas.SetLeft(Indicators, left + w + BubbleGap + bs * (BubbleSize + BubbleGap));
        Canvas.SetTop(Indicators, top + (CompactHeight - Indicators.ActualHeight) / 2);
    }

    // ───────────────────────────── input ─────────────────────────────

    private void OnPillEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        _collapseTimer.Stop();
        _manager.HoldAlert = true;
        _manager.AlertHovered();
        Refresh();
    }

    private void OnPillLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        _manager.HoldAlert = false;
        if (_expanded || _panel is not null) _collapseTimer.Start();
        Refresh();
    }

    private void OnPillClick(object sender, MouseButtonEventArgs e)
    {
        switch (_mode)
        {
            case Mode.Alert:
                var alert = _manager.CurrentAlert;
                alert?.OnClick?.Invoke();
                _manager.DismissAlert();
                break;
            case Mode.Compact:
                _expanded = true;
                Refresh();
                break;
            case Mode.Idle when IdlePanelFactory is not null:
                var (view, size) = IdlePanelFactory();
                OpenPanel(view, size);
                break;
        }
    }

    private void OnPillRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ContextMenuFactory is null) return;
        var menu = ContextMenuFactory();
        menu.PlacementTarget = Pill;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ───────────────────────────── window plumbing ─────────────────────────────

    /// <summary>
    /// Snaps the window to the top-center of its display (main display, the chosen one, or the one with the mouse).
    /// Works in physical pixels so it stays correct after the display scale changes (WPF's SystemParameters keep
    /// the startup DPI's values).
    /// </summary>
    private void PositionOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        // WndProc rewrites the coordinates to the anchor, so any move request lands in the right place.
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private (int X, int Y) Anchor(int pixelWidth)
    {
        var display = Displays.Choose(_settings.Monitor);
        // Top of the work area: below a taskbar (or other bar) docked at the top, at the very top otherwise.
        return (display.Bounds.Left + (display.Bounds.Width - pixelWidth) / 2, display.Work.Top);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        MessageReceived?.Invoke(msg, wParam, lParam);
        switch (msg)
        {
            case Win32.WM_WINDOWPOSCHANGING:
            {
                // The island is pinned: whoever moves it (DPI change, Win+arrow, shell, other apps) it stays
                // at the top-center, and it can't be pushed out of the topmost band.
                var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
                var width = pos.cx;
                if ((pos.flags & Win32.SWP_NOSIZE) != 0 || width <= 0)
                {
                    Win32.GetWindowRect(hwnd, out var r);
                    width = r.Width;
                }
                (pos.x, pos.y) = Anchor(width);
                pos.flags &= ~Win32.SWP_NOMOVE;
                if ((pos.flags & Win32.SWP_NOZORDER) == 0) pos.hwndInsertAfter = Win32.HWND_TOPMOST;
                Marshal.StructureToPtr(pos, lParam, false);
                break;
            }
            case Win32.WM_HOTKEY when wParam.ToInt32() == HideHotkeyId:
                HotkeyPressed?.Invoke();
                handled = true;
                break;
            case Win32.WM_HOTKEY when wParam.ToInt32() == ExpandHotkeyId:
                ToggleExpanded();
                handled = true;
                break;
            case Win32.WM_DISPLAYCHANGE:
            case Win32.WM_SETTINGCHANGE:
                Dispatcher.BeginInvoke(PositionOnScreen, DispatcherPriority.Background);
                break;
        }
        return IntPtr.Zero;
    }

    // ───────────────────────────── accessibility ─────────────────────────────

    private object? _announced;

    /// <summary>
    /// Gives screen readers a description of what the island shows ("Timer, 4:32", "Download complete,
    /// photos.zip") and announces new alerts, which otherwise only appear visually.
    /// </summary>
    private void UpdateAccessibleName()
    {
        var primary = _manager.Active.Count > 0 ? _manager.Active[0] : null;
        var text = _mode switch
        {
            Mode.Alert => Words(AlertLayer.Content),
            Mode.Expanded or Mode.Panel => Words(ExpandedLayer.Content),
            Mode.Compact when primary is not null => Join(primary.Name, primary.AccessibleDetail ?? "", Words(LeadingHost.Content), Words(TrailingHost.Content)),
            _ => Join(L.T("Dynamic Island"), Words(IdleLeadingHost.Content), Words(IdleTrailingHost.Content)),
        };
        if (Announcer.Text != text) Announcer.Text = text;

        if (_mode == Mode.Alert && !ReferenceEquals(_announced, _manager.CurrentAlert))
        {
            _announced = _manager.CurrentAlert;
            UIElementAutomationPeer.CreatePeerForElement(Announcer)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private static string Join(params string[] parts) => string.Join(", ", parts.Where(p => p.Length > 0));

    /// <summary>The readable text inside a view: text blocks and tooltips, without icon-font glyphs.</summary>
    private static string Words(object? root) => string.Join(", ", ViewText.Texts(root));

    private void KeepOnTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && Visibility == Visibility.Visible) Win32.ReassertTopmost(hwnd);
    }

    private void Housekeeping()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        UpdateAccessibleName(); // keeps changing text (countdowns) current for screen readers

        // Require two checks in a row so a window that covers the screen for a moment doesn't make it blink away.
        _fullscreenTicks = _settings.HideInFullscreen && !KeepVisibleInFullscreen && Win32.IsFullscreenAppRunning()
            ? _fullscreenTicks + 1
            : 0;
        var hide = _fullscreenTicks >= 2;
        var visible = Visibility == Visibility.Visible;
        if (hide && visible) Hide();
        else if (!hide && !visible && !App.Paused) Show();

        if (!hide)
        {
            Win32.ReassertTopmost(hwnd);
            // Snap back if the position drifted (a missed display change), and follow the mouse to another
            // display when the island is set to.
            Win32.GetWindowRect(hwnd, out var r);
            if ((r.Left, r.Top) != Anchor(r.Width)) PositionOnScreen();
        }
    }
}
