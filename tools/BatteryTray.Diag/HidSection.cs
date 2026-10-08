using System.Text.RegularExpressions;
using HidSharp;

namespace BatteryTray.Diag;

/// <summary>
/// Lista todas las interfaces/colecciones HID. Solo lee atributos y el descriptor
/// (que Windows reconstruye desde los "preparsed data"); no abre el dispositivo
/// para lectura/escritura ni envía reports, así que no interfiere con Synapse.
/// </summary>
static partial class HidSection
{
    public static void Run(Output o)
    {
        o.Section("1. DISPOSITIVOS HID (HidSharp)");

        var devices = DeviceList.Local.GetHidDevices().ToList();
        o.Line($"Total de interfaces/colecciones HID: {devices.Count}");
        o.Line("Leyenda: MI = interfaz USB, COL = top-level collection, in/out/feat = largo máximo de report (bytes, incluye Report ID)");

        var groups = devices
            .GroupBy(d => (d.VendorID, d.ProductID))
            .OrderBy(g => g.Key.VendorID).ThenBy(g => g.Key.ProductID);

        foreach (var g in groups)
        {
            var first = g.First();
            o.Line();
            o.Line($"{KnownVendors.Mark(g.Key.VendorID)} VID 0x{g.Key.VendorID:X4}  PID 0x{g.Key.ProductID:X4}  [{KnownVendors.Name(g.Key.VendorID)}]  " +
                   $"producto=\"{Try(first.GetProductName)}\"  fabricante=\"{Try(first.GetManufacturer)}\"  rev=0x{first.ReleaseNumberBcd:X4}  bus={Bus(first.DevicePath)}");

            foreach (var d in g.OrderBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase))
            {
                var mi = MiRegex().Match(d.DevicePath);
                var col = ColRegex().Match(d.DevicePath);
                o.Line($"    - MI={(mi.Success ? mi.Groups[1].Value : "--")} COL={(col.Success ? col.Groups[1].Value : "--")}  " +
                       $"in={Try(() => d.GetMaxInputReportLength())} out={Try(() => d.GetMaxOutputReportLength())} feat={Try(() => d.GetMaxFeatureReportLength())}  " +
                       $"nombre=\"{Try(d.GetFriendlyName)}\"");
                DescribeReports(o, d);
                o.Line($"      path: {d.DevicePath}");
            }
        }
    }

    static void DescribeReports(Output o, HidDevice d)
    {
        try
        {
            var desc = d.GetReportDescriptor();
            var usages = desc.DeviceItems
                .SelectMany(i => i.Usages.GetAllValues())
                .Distinct()
                .Select(UsageName);
            o.Line($"      usage: {string.Join(", ", usages)}");

            static string Reports(IEnumerable<HidSharp.Reports.Report> rs) =>
                string.Join(" ", rs.Select(r => $"#{r.ReportID}({r.Length}B)"));

            var input = Reports(desc.InputReports);
            var output = Reports(desc.OutputReports);
            var feature = Reports(desc.FeatureReports);
            o.Line($"      reports in: [{input}]  out: [{output}]  feat: [{feature}]");
        }
        catch (Exception ex)
        {
            o.Line($"      (no se pudo leer el descriptor: {ex.GetType().Name}: {ex.Message})");
        }
    }

    static string UsageName(uint usage)
    {
        var page = (ushort)(usage >> 16);
        var id = (ushort)usage;
        string? name = (page, id) switch
        {
            (0x01, 0x01) => "Pointer",
            (0x01, 0x02) => "Mouse",
            (0x01, 0x04) => "Joystick",
            (0x01, 0x05) => "Gamepad",
            (0x01, 0x06) => "Keyboard",
            (0x01, 0x07) => "Keypad",
            (0x01, 0x08) => "Multi-axis",
            (0x01, 0x80) => "System Control",
            (0x01, _) => "Generic Desktop",
            (0x06, _) => "Generic Device Controls",
            (0x0B, _) => "Telephony",
            (0x0C, 0x01) => "Consumer Control",
            (0x0C, _) => "Consumer",
            (0x0D, _) => "Digitizer",
            (0x84, _) => "Power Device",
            (0x85, _) => "Battery System",
            _ when page >= 0xFF00 => "Vendor",
            _ => null,
        };
        return $"0x{page:X4}:0x{id:X4}" + (name is null ? "" : $" ({name})");
    }

    /// <summary>Deduce el bus a partir del device path que arma Windows.</summary>
    static string Bus(string path)
    {
        var p = path.ToLowerInvariant();
        if (p.Contains("{00001124-0000-1000-8000-00805f9b34fb}")) return "Bluetooth clásico";
        if (p.Contains("{00001812-0000-1000-8000-00805f9b34fb}")) return "Bluetooth LE (HOGP)";
        if (p.Contains("#vid_")) return "USB";
        return "otro";
    }

    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception ex) { return $"<{ex.GetType().Name}>"; }
    }

    static string Try(Func<int> f)
    {
        try { return f().ToString(); }
        catch { return "?"; }
    }

    [GeneratedRegex(@"[&#]mi_([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex MiRegex();

    [GeneratedRegex(@"[&#]col([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ColRegex();
}
