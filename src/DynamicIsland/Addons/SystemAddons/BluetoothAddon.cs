using System.Windows;
using DynamicIsland.Core;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>(4) "AirPods connected · 80%" when a paired Bluetooth device connects or disconnects.</summary>
public sealed class BluetoothAddon : Addon
{
    private const string IsConnected = "System.Devices.Aep.IsConnected";
    private const string IsPaired = "System.Devices.Aep.IsPaired";
    private const string Category = "System.Devices.Aep.Category";
    private const string ContainerId = "System.Devices.Aep.ContainerId";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // battery % for hands-free headsets

    // Classic Bluetooth and Bluetooth LE association endpoints.
    private const string Selector =
        "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR " +
        "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";

    private sealed record Device(string Name, bool Connected, bool Paired, string[] Categories, Guid? Container, bool LowEnergy);

    private readonly Dictionary<string, Device> _devices = new();
    private DeviceWatcher? _watcher;
    private bool _ready;

    public BluetoothAddon(AddonContext context) : base(context) { }

    public override string Name => "Bluetooth";
    protected override bool IsEnabled(AddonSettings o) => o.Bluetooth;

    protected override void Start()
    {
        _devices.Clear();
        _ready = false;
        _watcher = DeviceInformation.CreateWatcher(Selector, new[] { IsConnected, IsPaired, Category, ContainerId },
                                                   DeviceInformationKind.AssociationEndpoint);
        _watcher.Added += (_, d) => Context.Ui(() => Added(d));
        _watcher.Updated += (_, u) => Context.Ui(() => Updated(u));
        _watcher.Removed += (_, u) => Context.Ui(() => _devices.Remove(u.Id));
        _watcher.EnumerationCompleted += (_, _) => Context.Ui(() => _ready = true); // don't announce what's already connected
        _watcher.Start();
    }

    protected override void Stop()
    {
        if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted }) _watcher.Stop();
        _watcher = null;
    }

    private void Added(DeviceInformation info)
    {
        var device = new Device(
            info.Name,
            Get<bool>(info.Properties, IsConnected),
            Get<bool>(info.Properties, IsPaired),
            info.Properties.TryGetValue(Category, out var c) && c is string[] cats ? cats : Array.Empty<string>(),
            info.Properties.TryGetValue(ContainerId, out var g) && g is Guid guid ? guid : null,
            info.Id.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase));
        _devices[info.Id] = device;
        if (_ready && device.Connected && device.Paired) _ = Announce(info.Id, device, connected: true);
    }

    private void Updated(DeviceInformationUpdate update)
    {
        if (!_devices.TryGetValue(update.Id, out var device)) return;
        if (!update.Properties.TryGetValue(IsConnected, out var value) || value is not bool connected) return;
        if (connected == device.Connected) return;

        device = device with { Connected = connected };
        _devices[update.Id] = device;
        if (_ready && device.Paired && !string.IsNullOrWhiteSpace(device.Name)) _ = Announce(update.Id, device, connected);
    }

    private async Task Announce(string id, Device device, bool connected)
    {
        int? battery = connected ? await ReadBattery(id, device) : null;
        var subtitle = connected ? battery is { } b ? $"Connected · {b}% battery" : "Connected" : "Disconnected";
        var icon = Ui.GlyphCircle(GlyphFor(device), connected ? Ui.Blue : Ui.ButtonBg, 46, 20);
        Show(new IslandAlert
        {
            Content = Ui.AlertView(icon, device.Name, subtitle),
            Size = new Size(360, 76),
            Duration = TimeSpan.FromSeconds(3),
        });
    }

    private static string GlyphFor(Device d)
    {
        var all = string.Join(" ", d.Categories) + " " + d.Name;
        bool Has(string s) => all.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("Audio") || Has("Headphone") || Has("Headset") || Has("Speaker") || Has("Buds") || Has("Pods")) return "";
        if (Has("Mouse")) return "";
        if (Has("Keyboard")) return "";
        if (Has("Gaming") || Has("Gamepad") || Has("Controller")) return "";
        if (Has("Phone")) return "";
        return "";
    }

    /// <summary>Best effort: headsets publish it as a device property, LE devices through the GATT battery service.</summary>
    private static async Task<int?> ReadBattery(string id, Device device)
    {
        try
        {
            if (device.Container is { } container)
            {
                var nodes = await DeviceInformation.FindAllAsync(
                    $"System.Devices.ContainerId:=\"{{{container}}}\"", new[] { BatteryKey }, DeviceInformationKind.Device);
                foreach (var node in nodes)
                {
                    if (node.Properties.TryGetValue(BatteryKey, out var v) && v is byte level) return level;
                }
            }

            if (device.LowEnergy)
            {
                using var le = await BluetoothLEDevice.FromIdAsync(id);
                if (le is null) return null;
                var services = await le.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached);
                foreach (var service in services.Services)
                {
                    using (service)
                    {
                        var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached);
                        foreach (var ch in chars.Characteristics)
                        {
                            var read = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (read.Status == GattCommunicationStatus.Success && read.Value.Length > 0)
                            {
                                using var reader = Windows.Storage.Streams.DataReader.FromBuffer(read.Value);
                                return reader.ReadByte();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Info($"No Bluetooth battery level for {device.Name}: {ex.Message}");
        }
        return null;
    }

    private static T? Get<T>(IReadOnlyDictionary<string, object> props, string key) =>
        props.TryGetValue(key, out var v) && v is T t ? t : default;
}
