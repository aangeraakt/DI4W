using DynamicIsland.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace DynamicIsland.Addons.SystemAddons;

/// <summary>
/// The battery level of a Bluetooth device, when it shares one: headsets publish it as a device property,
/// Bluetooth LE devices (and an iPhone linked with Phone Link) through the GATT battery service.
/// </summary>
public static class BluetoothBattery
{
    /// <summary>Battery % for hands-free headsets.</summary>
    public const string PropertyKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public static async Task<int?> ReadAsync(string id, Guid? container, bool lowEnergy, string name)
    {
        try
        {
            if (container is { } c)
            {
                var nodes = await DeviceInformation.FindAllAsync(
                    $"System.Devices.ContainerId:=\"{{{c}}}\"", new[] { PropertyKey }, DeviceInformationKind.Device);
                foreach (var node in nodes)
                {
                    if (node.Properties.TryGetValue(PropertyKey, out var v) && v is byte level) return level;
                }
            }

            if (lowEnergy)
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
            Log.Info($"No Bluetooth battery level for {name}: {ex.Message}");
        }
        return null;
    }
}
