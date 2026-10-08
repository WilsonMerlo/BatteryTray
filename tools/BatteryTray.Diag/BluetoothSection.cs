using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace BatteryTray.Diag;

/// <summary>
/// Dispositivos Bluetooth emparejados (clásico y LE) y todo lo que Windows expone de su batería:
///  - la propiedad de batería de la pila BT en los devnodes del mismo "container" (HFP, etc.)
///  - el servicio GATT Battery (0x180F / 0x2A19) en los dispositivos LE
/// </summary>
static class BluetoothSection
{
    const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    const string ContainerKey = "System.Devices.Aep.ContainerId";
    static readonly string[] AepProps = [IsConnectedKey, ContainerKey];

    public static async Task RunAsync(Output o, IReadOnlyList<DevNode> nodes, bool readGatt)
    {
        o.Section("3. BLUETOOTH EMPAREJADO");

        foreach (var radio in (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.Bluetooth))
            o.Line($"Radio Bluetooth: \"{radio.Name}\" estado={radio.State}");

        // --- Clásico (AirPods, auriculares, teclados en modo BT clásico) ---
        var classic = await DeviceInformation.FindAllAsync(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true), AepProps, DeviceInformationKind.AssociationEndpoint);
        o.Line();
        o.Line($"--- Bluetooth clásico: {classic.Count} emparejado(s) ---");
        foreach (var info in classic)
        {
            o.Line();
            try
            {
                using var dev = await BluetoothDevice.FromIdAsync(info.Id);
                o.Line($"★ \"{info.Name}\"  {Connected(info)}  addr={Addr(dev?.BluetoothAddress)}  " +
                       $"clase={dev?.ClassOfDevice.MajorClass}/{dev?.ClassOfDevice.MinorClass}");
            }
            catch (Exception ex)
            {
                o.Line($"★ \"{info.Name}\"  {Connected(info)}  (error al abrir: {Errors.Describe(ex)})");
            }
            RelatedNodes(o, info, nodes);
        }

        // --- LE (mando Xbox, teclados/mouse BLE, a veces también los AirPods) ---
        var le = await DeviceInformation.FindAllAsync(
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), AepProps, DeviceInformationKind.AssociationEndpoint);
        o.Line();
        o.Line($"--- Bluetooth LE: {le.Count} emparejado(s) ---");
        foreach (var info in le)
        {
            o.Line();
            BluetoothLEDevice? dev = null;
            try
            {
                dev = await BluetoothLEDevice.FromIdAsync(info.Id);
                o.Line($"★ \"{info.Name}\"  {Connected(info)}  addr={Addr(dev?.BluetoothAddress)}  " +
                       $"apariencia={dev?.Appearance.Category:X}/{dev?.Appearance.SubCategory:X}");
            }
            catch (Exception ex)
            {
                o.Line($"★ \"{info.Name}\"  {Connected(info)}  (error al abrir: {Errors.Describe(ex)})");
            }
            RelatedNodes(o, info, nodes);
            if (dev is not null)
            {
                if (readGatt) await ReadGattBatteryAsync(o, dev, IsConnected(info));
                dev.Dispose();
            }
        }

        // --- Cualquier devnode con la propiedad de batería, sea del bus que sea ---
        o.Line();
        o.Line("--- Todos los devnodes con propiedad de batería de Windows ---");
        var withBattery = nodes.Where(n => n.Battery is not null).ToList();
        if (withBattery.Count == 0) o.Line("  (ninguno)");
        foreach (var n in withBattery)
            o.Line($"  {n.Battery}%  \"{n.Name}\"  presente={n.Present}  id={n.InstanceId}");
    }

    /// <summary>Devnodes que comparten ContainerId con el endpoint BT (perfil HFP, A2DP, HID, etc.).</summary>
    static void RelatedNodes(Output o, DeviceInformation info, IReadOnlyList<DevNode> nodes)
    {
        if (info.Properties.TryGetValue(ContainerKey, out var c) && c is Guid container)
        {
            o.Line($"    container={container}");
            foreach (var n in nodes.Where(n => n.ContainerId == container).OrderBy(n => n.InstanceId))
            {
                var battery = n.Battery is null ? "" : $"  BATERÍA={n.Battery}%";
                o.Line($"    · {(n.Present == true ? "P" : "-")} \"{n.Name}\"  clase={n.ClassName ?? "?"}{battery}");
                o.Line($"        {n.InstanceId}");
            }
        }
        else
        {
            o.Line("    (sin ContainerId)");
        }
    }

    static async Task ReadGattBatteryAsync(Output o, BluetoothLEDevice dev, bool connected)
    {
        await ReadOnceAsync(BluetoothCacheMode.Cached);
        // Uncached obliga a hablar con el dispositivo: solo si está conectado y con timeout,
        // para no colgarnos esperando a uno apagado.
        if (connected) await ReadOnceAsync(BluetoothCacheMode.Uncached);

        async Task ReadOnceAsync(BluetoothCacheMode mode)
        {
            var label = mode == BluetoothCacheMode.Cached ? "cache " : "en vivo";
            try
            {
                var work = ReadAsync(dev, mode);
                var done = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(6)));
                o.Line($"    GATT Battery ({label}): {(done == work ? await work : "timeout (6 s)")}");
            }
            catch (Exception ex)
            {
                o.Line($"    GATT Battery ({label}): error {Errors.Describe(ex)}");
            }
        }
    }

    static async Task<string> ReadAsync(BluetoothLEDevice dev, BluetoothCacheMode mode)
    {
        var services = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, mode);
        if (services.Status != GattCommunicationStatus.Success)
            return $"servicio no accesible ({services.Status})";
        if (services.Services.Count == 0)
            return "el dispositivo no tiene servicio de batería";

        var results = new List<string>();
        foreach (var svc in services.Services)
        {
            using (svc)
            {
                var chars = await svc.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, mode);
                if (chars.Status != GattCommunicationStatus.Success)
                {
                    results.Add($"característica no accesible ({chars.Status})");
                    continue;
                }
                foreach (var ch in chars.Characteristics)
                {
                    var r = await ch.ReadValueAsync(mode);
                    if (r.Status == GattCommunicationStatus.Success && r.Value.Length > 0)
                    {
                        var bytes = new byte[r.Value.Length];
                        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(r.Value))
                            reader.ReadBytes(bytes);
                        results.Add($"{bytes[0]}%");
                    }
                    else
                    {
                        results.Add($"lectura fallida ({r.Status})");
                    }
                }
            }
        }
        return string.Join(", ", results);
    }

    static bool IsConnected(DeviceInformation info) =>
        info.Properties.TryGetValue(IsConnectedKey, out var v) && v is true;

    static string Connected(DeviceInformation info) => IsConnected(info) ? "CONECTADO" : "desconectado";

    static string Addr(ulong? a) =>
        a is null ? "?" : string.Join(":", BitConverter.GetBytes(a.Value).Take(6).Reverse().Select(b => b.ToString("X2")));
}
