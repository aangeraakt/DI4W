using System.Globalization;
using System.Runtime.InteropServices;
using DynamicIsland.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace DynamicIsland.SettingsApp.Pages;

public sealed partial class GeneralPage : Page
{
    private readonly bool _loading = true;

    public GeneralPage()
    {
        InitializeComponent();
        var s = SettingsStore.Current;

        NotRunning.IsOpen = !SettingsStore.IslandRunning;

        AutostartToggle.IsOn = Autostart.IsEnabled;
        if (SettingsStore.IslandExe is null)
        {
            AutostartCard.IsEnabled = false;
            AutostartCard.Description = "Start the island once first, so this app knows where it is.";
        }
        HideInFullscreen.IsOn = s.HideInFullscreen;
        Sounds.IsOn = s.Sounds;
        ShowShortcut();
        Unloaded += (_, _) => StopRecording();

        SizeBox.SelectedItem = SizeBox.Items.Cast<ComboBoxItem>()
            .OrderBy(i => Math.Abs(double.Parse((string)i.Tag, CultureInfo.InvariantCulture) - s.Scale))
            .First();
        TopOffset.Value = s.TopOffset;
        TopOffsetLabel.Text = $"{s.TopOffset:0} px";

        _loading = false;
    }

    private void OnStartIsland(object sender, RoutedEventArgs e)
    {
        SettingsStore.StartIsland();
        NotRunning.IsOpen = false;
    }

    private void OnAutostart(object sender, RoutedEventArgs e)
    {
        if (_loading || SettingsStore.IslandExe is not { } exe) return;
        Autostart.Set(AutostartToggle.IsOn, exe);
    }

    private void OnHideInFullscreen(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsStore.Change(s => s.HideInFullscreen = HideInFullscreen.IsOn);
    }

    private void OnSounds(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsStore.Change(s => s.Sounds = Sounds.IsOn);
    }

    private void OnSize(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SizeBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        SettingsStore.Change(s => s.Scale = double.Parse(tag, CultureInfo.InvariantCulture));
    }

    private void OnTopOffset(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TopOffsetLabel is not null) TopOffsetLabel.Text = $"{e.NewValue:0} px";
        if (!_loading) SettingsStore.Change(s => s.TopOffset = e.NewValue);
    }

    // ───────────────────────────── hide shortcut ─────────────────────────────

    private bool _recording;

    private void ShowShortcut()
    {
        var current = SettingsStore.Current.HideHotkey;
        var valid = Hotkey.TryParse(current, out _, out _);
        ShortcutButton.Content = _recording ? "Press a shortcut…" : valid ? current.Replace("+", " + ") : "None";
        ClearShortcut.IsEnabled = valid && !_recording;
    }

    // Recording uses a low-level keyboard hook: shortcuts another app (or the island) has registered never
    // reach a normal KeyDown handler, because Windows delivers them to that app first. The hook sees them
    // before that and swallows the recorded combo, so the other app isn't triggered.
    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _hookProc; // kept in a field so the GC doesn't collect the callback

    private void OnRecordShortcut(object sender, RoutedEventArgs e)
    {
        if (_recording) return;
        _recording = true;
        ShortcutWarning.IsOpen = false;
        _hookProc = OnLowLevelKey;
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        ShowShortcut();
    }

    private void OnShortcutLostFocus(object sender, RoutedEventArgs e) => StopRecording();

    private void StopRecording()
    {
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _recording = false;
        ShowShortcut();
    }

    // Keys typed while recording go to the hook, not the button (so Space/Enter don't "click" it again).
    private void OnShortcutKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = _recording;

    private IntPtr OnLowLevelKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && _recording && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
        {
            var key = (uint)Marshal.ReadInt32(data);
            if (HandleRecordedKey(key)) return 1; // swallow
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    /// <summary>Returns true when the key finished (or cancelled) recording and should be swallowed.</summary>
    private bool HandleRecordedKey(uint key)
    {
        uint modifiers = 0;
        if (IsDown(VK_CONTROL)) modifiers |= Hotkey.MOD_CONTROL;
        if (IsDown(VK_MENU)) modifiers |= Hotkey.MOD_ALT;
        if (IsDown(VK_SHIFT)) modifiers |= Hotkey.MOD_SHIFT;
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) modifiers |= Hotkey.MOD_WIN;

        if (key == VK_ESCAPE && modifiers == 0)
        {
            DispatcherQueue.TryEnqueue(StopRecording);
            return true;
        }

        // Wait for a real key; a shortcut needs Ctrl, Alt or Win so it doesn't steal normal typing.
        if (IsModifierKey(key) || (modifiers & ~Hotkey.MOD_SHIFT) == 0) return false;

        var shortcut = Hotkey.Format(modifiers, key);
        DispatcherQueue.TryEnqueue(() =>
        {
            StopRecording();
            if (shortcut == SettingsStore.Current.HideHotkey) return;
            ShortcutWarning.IsOpen = !IsShortcutFree(modifiers, key);
            SettingsStore.Change(s => s.HideHotkey = shortcut);
            ShowShortcut();
        });
        return true;
    }

    private void OnClearShortcut(object sender, RoutedEventArgs e)
    {
        ShortcutWarning.IsOpen = false;
        SettingsStore.Change(s => s.HideHotkey = "");
        ShowShortcut();
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // Shift/Ctrl/Alt (generic and left/right), Win keys and Caps Lock.
    private static bool IsModifierKey(uint key) => key is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>Tries to claim the shortcut for a moment; if another app already has it, this fails.</summary>
    private static bool IsShortcutFree(uint modifiers, uint key)
    {
        const int probeId = 0x7F00;
        if (!RegisterHotKey(IntPtr.Zero, probeId, modifiers, key)) return false;
        UnregisterHotKey(IntPtr.Zero, probeId);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    private const int WH_KEYBOARD_LL = 13;
    private static readonly IntPtr WM_KEYDOWN = 0x0100;
    private static readonly IntPtr WM_SYSKEYDOWN = 0x0104;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_ESCAPE = 0x1B, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
