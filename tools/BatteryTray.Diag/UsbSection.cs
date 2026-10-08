using System.Text.RegularExpressions;

namespace BatteryTray.Diag;

/// <summary>
/// Todos los dispositivos USB presentes, incluyendo los que NO son HID: receptores que
/// se presentan como mando XInput (xusb22/xboxgip), audio USB (usbaudio), etc.
/// Sirve para ubicar el receptor de Flydigi y el de los BlackShark.
/// </summary>
static partial class UsbSection
{
    public static void Run(Output o, IReadOnlyList<DevNode> nodes)
    {
        o.Section("2. DISPOSITIVOS USB PRESENTES (todas las clases, incluye interfaces de dispositivos compuestos)");
        o.Line("Leyenda: clase/servicio = clase de dispositivo de Windows y driver que lo atiende");

        var usb = nodes
            .Where(n => n.Present != false && n.InstanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            .Select(n => (Node: n, Match: VidPidRegex().Match(n.InstanceId)))
            .Where(x => x.Match.Success)
            .Select(x => (x.Node,
                          Vid: Convert.ToInt32(x.Match.Groups["vid"].Value, 16),
                          Pid: Convert.ToInt32(x.Match.Groups["pid"].Value, 16),
                          Mi: x.Match.Groups["mi"].Success ? x.Match.Groups["mi"].Value : null))
            .ToList();

        foreach (var g in usb.GroupBy(x => (x.Vid, x.Pid)).OrderBy(g => g.Key.Vid).ThenBy(g => g.Key.Pid))
        {
            o.Line();
            o.Line($"{KnownVendors.Mark(g.Key.Vid)} VID 0x{g.Key.Vid:X4}  PID 0x{g.Key.Pid:X4}  [{KnownVendors.Name(g.Key.Vid)}]");
            foreach (var x in g.OrderBy(x => x.Mi ?? ""))
            {
                var n = x.Node;
                o.Line($"    - {(x.Mi is null ? "dispositivo " : $"interfaz MI_{x.Mi}")}  \"{n.Name}\"" +
                       (n.BusReportedName is { Length: > 0 } b && b != n.Name ? $"  (bus: \"{b}\")" : "") +
                       $"  clase={n.ClassName ?? "?"}  servicio={n.Service ?? "?"}");
            }
        }
    }

    [GeneratedRegex(@"VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4})(&MI_(?<mi>[0-9A-F]{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex VidPidRegex();
}
