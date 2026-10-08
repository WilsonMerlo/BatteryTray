namespace BatteryTray.Diag;

/// <summary>
/// Vendor IDs USB conocidos, solo para hacer el reporte más legible.
/// Los marcados con ★ son los que nos interesan para tus periféricos.
/// </summary>
static class KnownVendors
{
    static readonly Dictionary<int, string> Names = new()
    {
        [0x1532] = "Razer",
        [0x3434] = "Keychron",
        [0x045E] = "Microsoft",
        [0x05AC] = "Apple",
        [0x04B4] = "Cypress/Infineon (Flydigi antiguos)",
        [0x37D7] = "Flydigi",
        [0x046D] = "Logitech",
        [0x1038] = "SteelSeries",
        [0x0B05] = "ASUS",
        [0x8087] = "Intel",
        [0x0BDA] = "Realtek",
        [0x0CF3] = "Qualcomm Atheros",
        [0x13D3] = "IMC Networks",
        [0x0E8D] = "MediaTek",
        [0x1D6B] = "Linux Foundation (hub raíz)",
        [0x0451] = "Texas Instruments",
        [0x2109] = "VIA Labs (hub)",
        [0x05E3] = "Genesys Logic (hub)",
        [0x1A86] = "QinHeng (CH340/CH9329)",
        [0x0D8C] = "C-Media (audio USB)",
        [0x3367] = "Endgame Gear",
    };

    static readonly HashSet<int> Interesting = [0x1532, 0x3434, 0x045E, 0x04B4, 0x37D7];

    public static string Name(int vid) => Names.TryGetValue(vid, out var n) ? n : "?";

    public static string Mark(int vid) => Interesting.Contains(vid) ? "★" : " ";
}
