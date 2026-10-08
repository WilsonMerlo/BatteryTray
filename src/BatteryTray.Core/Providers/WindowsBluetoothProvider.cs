using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BatteryTray.Core.Providers;

/// <summary>
/// Proveedor genérico Bluetooth. Junta las dos fuentes de batería que ofrece Windows:
///  1. La propiedad de batería que publica la pila Bluetooth en los devnodes (auriculares con
///     perfil manos libres, algunos teclados): es lo mismo que muestra Configuración.
///  2. El servicio GATT Battery (0x180F) de los dispositivos Bluetooth LE emparejados y conectados.
/// Cada dispositivo se identifica por su ContainerId, así no aparece dos veces si tiene ambas.
/// </summary>
public sealed class WindowsBluetoothProvider : IBatteryProvider
{
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    const string DevContainerKey = "System.Devices.ContainerId";
    const string AepConnectedKey = "System.Devices.Aep.IsConnected";
    const string AepContainerKey = "System.Devices.Aep.ContainerId";
    const string ContainerConnectedKey = "System.Devices.Connected";

    static readonly TimeSpan GattTimeout = TimeSpan.FromSeconds(8);

    public string Name => "Bluetooth (Windows)";

    public async Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct)
    {
        var byContainer = new Dictionary<Guid, BatteryReading>();
        await AddPropertyBatteriesAsync(byContainer);
        await AddGattBatteriesAsync(byContainer, ct);
        return byContainer.Values.ToList();
    }

    async Task AddPropertyBatteriesAsync(Dictionary<Guid, BatteryReading> into)
    {
        var nodes = await DeviceInformation.FindAllAsync("", [BatteryKey, DevContainerKey], DeviceInformationKind.Device);
        foreach (var n in nodes)
        {
            if (Get(n, BatteryKey) is not byte level) continue;
            if (Get(n, DevContainerKey) is not Guid container || into.ContainsKey(container)) continue;

            var (name, connected) = await ContainerInfoAsync(container, n.Name);
            into[container] = new BatteryReading
            {
                Id = $"bt:{container}",
                Name = name,
                Source = Name,
                Level = Math.Clamp((int)level, 0, 100),
                Charging = ChargingState.Unknown,
                Connected = connected,
            };
        }
    }

    async Task AddGattBatteriesAsync(Dictionary<Guid, BatteryReading> into, CancellationToken ct)
    {
        var endpoints = await DeviceInformation.FindAllAsync(
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
            [AepConnectedKey, AepContainerKey],
            DeviceInformationKind.AssociationEndpoint);

        foreach (var ep in endpoints)
        {
            ct.ThrowIfCancellationRequested();
            // Consultar uno desconectado obliga a Windows a intentar conectarlo: no lo hacemos.
            if (Get(ep, AepConnectedKey) is not true) continue;
            if (Get(ep, AepContainerKey) is not Guid container) continue;

            try
            {
                using var dev = await BluetoothLEDevice.FromIdAsync(ep.Id);
                if (dev is null) continue;

                var read = ReadBatteryAsync(dev);
                if (await Task.WhenAny(read, Task.Delay(GattTimeout, ct)) != read)
                {
                    Log.Debug($"Bluetooth: timeout leyendo GATT de \"{ep.Name}\"");
                    continue;
                }
                if (await read is not int level) continue;

                into[container] = new BatteryReading
                {
                    Id = $"bt:{container}",
                    Name = ep.Name,
                    Source = Name,
                    Kind = KindFromAppearance(dev.Appearance),
                    Level = level,
                    Charging = ChargingState.Unknown,
                    Connected = true,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Debug($"Bluetooth: no se pudo leer GATT de \"{ep.Name}\": {ex.Message}");
            }
        }
    }

    static async Task<int?> ReadBatteryAsync(BluetoothLEDevice dev)
    {
        var services = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
        if (services.Status != GattCommunicationStatus.Success) return null;

        foreach (var svc in services.Services)
        {
            using (svc)
            {
                var chars = await svc.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
                if (chars.Status != GattCommunicationStatus.Success) continue;
                foreach (var ch in chars.Characteristics)
                {
                    var r = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (r.Status != GattCommunicationStatus.Success || r.Value.Length == 0) continue;
                    using var reader = DataReader.FromBuffer(r.Value);
                    return Math.Clamp((int)reader.ReadByte(), 0, 100);
                }
            }
        }
        return null;
    }

    static async Task<(string Name, bool Connected)> ContainerInfoAsync(Guid container, string fallbackName)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(
                $"{{{container}}}", [ContainerConnectedKey], DeviceInformationKind.DeviceContainer);
            return (info.Name, Get(info, ContainerConnectedKey) is not false);
        }
        catch
        {
            return (fallbackName, true);
        }
    }

    static DeviceKind KindFromAppearance(BluetoothLEAppearance appearance)
    {
        if (appearance.Category != BluetoothLEAppearanceCategories.HumanInterfaceDevice) return DeviceKind.Unknown;
        var sub = appearance.SubCategory;
        if (sub == BluetoothLEAppearanceSubcategories.Keyboard) return DeviceKind.Keyboard;
        if (sub == BluetoothLEAppearanceSubcategories.Mouse) return DeviceKind.Mouse;
        if (sub == BluetoothLEAppearanceSubcategories.Gamepad || sub == BluetoothLEAppearanceSubcategories.Joystick) return DeviceKind.Controller;
        return DeviceKind.Unknown;
    }

    /// <summary>Lee una propiedad tolerando que Windows devuelva la clave con otro formato de GUID.</summary>
    static object? Get(DeviceInformation d, string key)
    {
        if (d.Properties.TryGetValue(key, out var v)) return v;
        foreach (var kv in d.Properties)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    public void Dispose() { }
}
