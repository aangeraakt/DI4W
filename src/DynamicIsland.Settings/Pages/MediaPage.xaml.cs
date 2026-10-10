using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DynamicIsland.SettingsApp.Pages;

public sealed partial class MediaPage : Page
{
    private readonly bool _loading = true;

    public MediaPage()
    {
        InitializeComponent();
        var s = SettingsStore.Current;
        MediaEnabled.IsOn = s.MediaEnabled;
        AnyPlayer.IsOn = s.MediaAnyPlayer;
        HideAfter.Value = s.MediaHideAfterPauseSeconds;
        UpdateEnabled();
        _loading = false;
    }

    private void UpdateEnabled()
    {
        AnyPlayerCard.IsEnabled = MediaEnabled.IsOn;
        HideAfterCard.IsEnabled = MediaEnabled.IsOn;
    }

    private void OnMediaEnabled(object sender, RoutedEventArgs e)
    {
        UpdateEnabled();
        if (!_loading) SettingsStore.Change(s => s.MediaEnabled = MediaEnabled.IsOn);
    }

    private void OnAnyPlayer(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsStore.Change(s => s.MediaAnyPlayer = AnyPlayer.IsOn);
    }

    private void OnHideAfter(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue)) return;
        SettingsStore.Change(s => s.MediaHideAfterPauseSeconds = (int)Math.Round(args.NewValue));
    }
}
