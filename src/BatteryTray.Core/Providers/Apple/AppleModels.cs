namespace BatteryTray.Core.Providers.Apple;

/// <param name="Dual">true = dos auriculares + estuche; false = un solo nivel (vincha, cuello, etc.).</param>
internal sealed record AppleModel(string Name, bool Dual, DeviceKind Kind);

/// <summary>
/// Modelos de AirPods/Beats según el "device model" del mensaje proximity pairing.
///
/// La clave es el PID Bluetooth de Apple (el que aparece en Windows como VID&amp;0001004C_PID&amp;xxxx):
/// en el advertisement viene en little endian, por eso CAPod lo escribe al revés
/// (por ejemplo AirPods 4 ANC: bytes "1B 20" en el aire = PID 0x201B en Windows).
/// Lista tomada de CAPod (d4rken-org/capod, pods/core/apple/ble/devices).
/// </summary>
internal static class AppleModels
{
    static readonly Dictionary<ushort, AppleModel> Models = new()
    {
        [0x2002] = new("AirPods (1.ª gen.)", true, DeviceKind.Earbuds),
        [0x200F] = new("AirPods (2.ª gen.)", true, DeviceKind.Earbuds),
        [0x2013] = new("AirPods (3.ª gen.)", true, DeviceKind.Earbuds),
        [0x2019] = new("AirPods 4", true, DeviceKind.Earbuds),
        [0x201B] = new("AirPods 4 (ANC)", true, DeviceKind.Earbuds),
        [0x2036] = new("AirPods (gen. 5)", true, DeviceKind.Earbuds),
        [0x2030] = new("AirPods (gen. 5, estuche inalámbrico)", true, DeviceKind.Earbuds),
        [0x200E] = new("AirPods Pro", true, DeviceKind.Earbuds),
        [0x2014] = new("AirPods Pro 2", true, DeviceKind.Earbuds),
        [0x2024] = new("AirPods Pro 2 (USB-C)", true, DeviceKind.Earbuds),
        [0x2027] = new("AirPods Pro 3", true, DeviceKind.Earbuds),
        [0x200A] = new("AirPods Max", false, DeviceKind.Headset),
        [0x201F] = new("AirPods Max (USB-C)", false, DeviceKind.Headset),
        [0x202D] = new("AirPods Max 2", false, DeviceKind.Headset),
        [0x2038] = new("Beats 360", false, DeviceKind.Headset),
        [0x2012] = new("Beats Fit Pro", true, DeviceKind.Earbuds),
        [0x2010] = new("Beats Flex", false, DeviceKind.Earbuds),
        [0x2006] = new("Beats Solo3", false, DeviceKind.Headset),
        [0x2025] = new("Beats Solo 4", false, DeviceKind.Headset),
        [0x2026] = new("Beats Solo Buds", true, DeviceKind.Earbuds),
        [0x200C] = new("Beats Solo Pro", false, DeviceKind.Headset),
        [0x2011] = new("Beats Studio Buds", true, DeviceKind.Earbuds),
        [0x2016] = new("Beats Studio Buds+", true, DeviceKind.Earbuds),
        [0x2017] = new("Beats Studio Pro", false, DeviceKind.Headset),
        [0x2005] = new("BeatsX", false, DeviceKind.Earbuds),
        [0x2003] = new("Powerbeats3", false, DeviceKind.Earbuds),
        [0x200D] = new("Powerbeats4", false, DeviceKind.Earbuds),
        [0x200B] = new("Powerbeats Pro", true, DeviceKind.Earbuds),
        [0x201D] = new("Powerbeats Pro 2", true, DeviceKind.Earbuds),
    };

    /// <summary>Modelos desconocidos se tratan como AirPods "dobles", que es el caso más común.</summary>
    public static AppleModel Find(ushort pid) =>
        Models.TryGetValue(pid, out var m) ? m : new AppleModel($"Apple 0x{pid:X4}", true, DeviceKind.Earbuds);
}
