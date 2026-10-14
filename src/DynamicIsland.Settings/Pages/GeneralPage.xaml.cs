using System.Globalization;
using DynamicIsland.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

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
            AutostartCard.Description = L.T("Start the island once first, so this app knows where it is.");
        }
        HideInFullscreen.IsOn = s.HideInFullscreen;
        Sounds.IsOn = s.Sounds;

        HideShortcut.Bind(x => x.HideHotkey, (x, v) => x.HideHotkey = v);
        ExpandShortcut.Bind(x => x.ExpandHotkey, (x, v) => x.ExpandHotkey = v);
        HideShortcut.Recorded += taken => ShortcutWarning.IsOpen = taken;
        ExpandShortcut.Recorded += taken => ShortcutWarning.IsOpen = taken;

        SizeBox.SelectedItem = SizeBox.Items.Cast<ComboBoxItem>()
            .OrderBy(i => Math.Abs(double.Parse((string)i.Tag, CultureInfo.InvariantCulture) - s.Scale))
            .First();
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Theme) ?? ThemeBox.Items[0];
        TopOffset.Value = s.TopOffset;
        TopOffsetLabel.Text = $"{s.TopOffset:0} px";
        FillDisplays(s.Monitor);
        FillOrder(s.ActivityOrder);
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Language)
                                   ?? LanguageBox.Items[0];

        _loading = false;
        UpdatePreview();
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
        UpdatePreview();
    }

    /// <summary>Main display, the display with the mouse, then each connected display.</summary>
    private void FillDisplays(string current)
    {
        var choices = new List<(string Value, string Label)> { ("", L.T("Main display")), (Displays.FollowMouse, L.T("Display with the mouse")) };
        choices.AddRange(Displays.All().Select(d => (d.Device, d.Label)));
        // A display that's not connected right now stays selectable, so the setting isn't silently lost.
        if (current.Length > 0 && choices.All(c => c.Value != current)) choices.Add((current, L.F("{0} (not connected)", current)));

        foreach (var (value, label) in choices)
        {
            DisplayBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
        DisplayBox.SelectedItem = DisplayBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == current);
    }

    private void OnLanguage(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        if (value == SettingsStore.Current.Language) return;
        SettingsStore.Change(s => s.Language = value); // the island restarts itself in the new language
        App.Restart("General");                        // and so does this window, so every text is redrawn
    }

    // ───────────────────────────── activity order ─────────────────────────────

    /// <summary>One row of the order list; the list shows <see cref="ToString"/>.</summary>
    private sealed record OrderItem(string Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly System.Collections.ObjectModel.ObservableCollection<OrderItem> _order = new();

    private void FillOrder(IEnumerable<string> kinds)
    {
        _order.Clear();
        foreach (var kind in ActivityKinds.Ordered(kinds))
        {
            var label = ActivityKinds.All.First(k => k.Kind == kind).Label;
            _order.Add(new OrderItem(kind, L.T(label)));
        }
        OrderList.ItemsSource = _order;
    }

    private void OnOrderChanged(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var kinds = _order.Select(o => o.Kind).ToList();
        SettingsStore.Change(s => s.ActivityOrder = kinds.ToList());
    }

    private void OnResetOrder(object sender, RoutedEventArgs e)
    {
        SettingsStore.Change(s => s.ActivityOrder = new List<string>());
        FillOrder(Array.Empty<string>());
    }

    private void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        SettingsStore.Change(s => s.Theme = value); // the island restarts itself in the new colors
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var s = SettingsStore.Current;
        Preview.Update(s.Scale, s.TopOffset, IslandTheme.IsLight(s.Theme));
    }

    private void OnDisplay(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DisplayBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        SettingsStore.Change(s => s.Monitor = value);
    }

    private void OnTopOffset(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TopOffsetLabel is not null) TopOffsetLabel.Text = $"{e.NewValue:0} px";
        if (_loading) return;
        SettingsStore.Change(s => s.TopOffset = e.NewValue);
        UpdatePreview();
    }
}
