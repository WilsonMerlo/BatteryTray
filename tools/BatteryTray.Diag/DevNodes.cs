using Windows.Devices.Enumeration;

namespace BatteryTray.Diag;

/// <summary>Un nodo del árbol de dispositivos PnP (lo que ves en el Administrador de dispositivos).</summary>
sealed record DevNode(
    string InstanceId,
    string Name,
    bool? Present,
    Guid? ContainerId,
    string? ClassName,
    string? Service,
    string? BusReportedName,
    string? Manufacturer,
    object? Battery);

/// <summary>
/// Foto de todos los devnodes con las propiedades que nos interesan.
/// Se toma una sola vez y la usan las secciones USB y Bluetooth.
/// </summary>
static class DevNodes
{
    // Nivel de batería que publica la pila Bluetooth de Windows (es lo que muestra
    // Configuración > Bluetooth). No tiene nombre canónico, por eso va como {GUID} PID.
    public const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    const string PresentKey = "System.Devices.Present";
    const string ContainerKey = "System.Devices.ContainerId";
    const string ManufacturerKey = "System.Devices.Manufacturer";
    const string ClassKey = "{A45C254E-DF1C-4EFD-8020-67D146A850E0} 9";          // DEVPKEY_Device_Class
    const string ServiceKey = "{A45C254E-DF1C-4EFD-8020-67D146A850E0} 6";        // DEVPKEY_Device_Service
    const string BusReportedKey = "{540B947E-8B40-45BC-A8A2-6A0B894CBDA2} 4";    // DEVPKEY_Device_BusReportedDeviceDesc

    public static async Task<IReadOnlyList<DevNode>> SnapshotAsync()
    {
        string[] props = [PresentKey, ContainerKey, ManufacturerKey, ClassKey, ServiceKey, BusReportedKey, BatteryKey];
        var all = await DeviceInformation.FindAllAsync("", props, DeviceInformationKind.Device);

        return all.Select(d => new DevNode(
            d.Id,
            d.Name,
            Get(d, PresentKey) as bool?,
            Get(d, ContainerKey) as Guid?,
            Get(d, ClassKey) as string,
            Get(d, ServiceKey) as string,
            Get(d, BusReportedKey) as string,
            Get(d, ManufacturerKey) as string,
            Get(d, BatteryKey)))
            .ToList();
    }

    /// <summary>
    /// Busca la propiedad sin distinguir mayúsculas: según la versión de Windows,
    /// la clave vuelve con el GUID en otro formato que el pedido.
    /// </summary>
    static object? Get(DeviceInformation d, string key)
    {
        if (d.Properties.TryGetValue(key, out var v)) return v;
        foreach (var kv in d.Properties)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }
}
