using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Addons.SystemAddons;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland.Addons.Info;

/// <summary>
/// (25) While a fullscreen game runs, keep the island visible and show GPU load and temperature (CPU and RAM
/// when expanded). A fullscreen app counts as a game when its GPU 3D load is high, so videos still hide it.
/// Frame rate isn't shown: Windows only exposes it to programs running as administrator.
/// </summary>
public sealed class GameModeAddon : Addon
{
    private readonly SystemLoad.CpuMeter _cpu = new();
    private readonly TextBlock _compact = Ui.Text("", 14, FontWeights.SemiBold, Ui.Green, tabular: true);
    private readonly TextBlock _gpu = Stat(), _temp = Stat(), _cpuText = Stat(), _ram = Stat();
    private readonly AddonActivity _activity;
    private DispatcherTimer? _poll;
    private int _gameTicks;

    public GameModeAddon(AddonContext context) : base(context)
    {
        var grid = new Grid { Margin = new Thickness(22, 16, 22, 14) };
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddStat(grid, 0, "GPU", _gpu);
        AddStat(grid, 1, "GPU temp", _temp);
        AddStat(grid, 2, "CPU", _cpuText);
        AddStat(grid, 3, "Memory", _ram);

        _activity = new AddonActivity("Game", 10,
            Ui.Glyph("", 16, Ui.Green), _compact, Ui.Glyph("", 13, Ui.Green), grid,
            compactWidth: 250, expandedSize: new Size(400, 92));
        context.Manager.Register(_activity);
    }

    public override string Name => "Game mode";
    protected override bool IsEnabled(AddonSettings o) => o.GameMode;

    protected override void Start()
    {
        _cpu.Read();
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => Check();
        _poll.Start();
    }

    protected override void Stop()
    {
        _poll?.Stop();
        _poll = null;
        Leave();
    }

    private void Check()
    {
        var gpu = SystemLoad.GpuPercent();
        var cpu = _cpu.Read();
        var fullscreen = Win32.IsFullscreenAppRunning();
        // Two readings in a row, so a short spike (or a fullscreen video) doesn't count as a game.
        _gameTicks = fullscreen && gpu >= 30 ? _gameTicks + 1 : 0;
        var inGame = _gameTicks >= 2 || (_activity.IsActive && fullscreen);
        if (!inGame)
        {
            Leave();
            return;
        }

        var temp = SystemLoad.GpuTemperature();
        _compact.Text = temp is { } t ? $"GPU {gpu:0}% · {t}°" : $"GPU {gpu:0}%";
        _gpu.Text = $"{gpu:0}%";
        _temp.Text = temp is { } tt ? $"{tt}°C" : "–";
        _cpuText.Text = $"{cpu:0}%";
        _ram.Text = $"{SystemLoad.MemoryPercent()}%";

        Context.Window.KeepVisibleInFullscreen = true;
        _activity.Show();
    }

    private void Leave()
    {
        _gameTicks = 0;
        Context.Window.KeepVisibleInFullscreen = false;
        _activity.Hide();
    }

    private static TextBlock Stat() => Ui.Text("–", 22, FontWeights.Light, Ui.White, tabular: true);

    private static void AddStat(Grid grid, int column, string label, TextBlock value)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var caption = Ui.Text(label, 12, FontWeights.SemiBold, Ui.Green);
        caption.HorizontalAlignment = value.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(caption);
        stack.Children.Add(value);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }
}
