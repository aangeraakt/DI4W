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
            AutostartCard.Description = "Start the island once first, so this app knows where it is.";
        }
        HideInFullscreen.IsOn = s.HideInFullscreen;
        Sounds.IsOn = s.Sounds;

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
}
