using HidSharp;
using HidSharp.Reports;

namespace BatteryTray.Core.Providers;

/// <summary>
/// Proveedor HID genérico: cualquier dispositivo cuyo descriptor declare un campo estándar de
/// batería. Pensado para auriculares, mandos y otros periféricos que no tienen proveedor propio.
///
/// Campos reconocidos:
///  - Generic Device Controls / Battery Strength (0x06:0x20)
///  - Battery System / Relative o Absolute State Of Charge (0x85:0x64, 0x85:0x65)
///  - Battery System / Charging (0x85:0x44)
///
/// Se leen sin mandar comandos: con un GetFeature si el campo está en un feature report, o
/// escuchando un momento los input reports que el dispositivo ya envía por su cuenta.
/// </summary>
public sealed class GenericHidBatteryProvider : IBatteryProvider
{
    const uint BatteryStrength = 0x0006_0020;
    const uint RelativeStateOfCharge = 0x0085_0064;
    const uint AbsoluteStateOfCharge = 0x0085_0065;
    const uint ChargingUsage = 0x0085_0044;

    static readonly HashSet<uint> LevelUsages = [BatteryStrength, RelativeStateOfCharge, AbsoluteStateOfCharge];

    /// <summary>Cuánto se escuchan los input reports de cada dispositivo.</summary>
    static readonly TimeSpan ListenTime = TimeSpan.FromMilliseconds(1500);

    /// <summary>Fabricantes con proveedor propio: se dejan a ese proveedor.</summary>
    static readonly HashSet<int> SkipVendors = [0x1532 /* Razer */, 0x3434 /* Keychron */, 0x37D7 /* Flydigi */];

    public string Name => "HID genérico";

    public Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct) => Task.Run(() => Query(ct), ct);

    IReadOnlyList<BatteryReading> Query(CancellationToken ct)
    {
        var candidates = DeviceList.Local.GetHidDevices()
            .Where(d => !SkipVendors.Contains(d.VendorID))
            .Select(d => (Device: d, Reports: BatteryReports(d)))
            .Where(x => x.Reports.Count > 0)
            .ToList();

        // Se escuchan en paralelo para no sumar 1,5 s por dispositivo.
        var tasks = candidates.Select(c => Task.Run(() => ReadDevice(c.Device, c.Reports, ct), ct)).ToArray();
        Task.WaitAll(tasks, ct);

        return tasks.Select(t => t.Result).OfType<BatteryReading>()
            .GroupBy(r => r.Id)
            .Select(g => g.OrderByDescending(r => r.Level.HasValue).First())
            .ToList();
    }

    /// <summary>Reports (input o feature) del dispositivo que contienen un campo de nivel de batería.</summary>
    static List<Report> BatteryReports(HidDevice dev)
    {
        try
        {
            var desc = dev.GetReportDescriptor();
            return desc.InputReports.Concat(desc.FeatureReports)
                .Where(r => r.DataItems.Any(i => i.Usages.GetAllValues().Any(LevelUsages.Contains)))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    BatteryReading? ReadDevice(HidDevice dev, List<Report> reports, CancellationToken ct)
    {
        HidStream? stream = null;
        try
        {
            if (!dev.TryOpen(out stream)) return null;
            (double? Level, bool? Charging) result = (null, null);

            // 1) Feature reports: se piden directamente.
            foreach (var report in reports.Where(r => r.ReportType == ReportType.Feature))
            {
                var buffer = new byte[Math.Max(report.Length, dev.GetMaxFeatureReportLength())];
                buffer[0] = report.ReportID;
                try { stream.GetFeature(buffer); }
                catch (IOException) { continue; }
                result = Merge(result, Parse(report, buffer));
            }

            // 2) Input reports: se escucha lo que el dispositivo manda por su cuenta.
            var inputs = reports.Where(r => r.ReportType == ReportType.Input).ToDictionary(r => r.ReportID);
            if (result.Level is null && inputs.Count > 0)
            {
                var buffer = new byte[dev.GetMaxInputReportLength()];
                var deadline = DateTime.UtcNow + ListenTime;
                while (result.Level is null && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    stream.ReadTimeout = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    int n;
                    try { n = stream.Read(buffer, 0, buffer.Length); }
                    catch (TimeoutException) { break; }
                    if (n > 0 && inputs.TryGetValue(buffer[0], out var report))
                        result = Merge(result, Parse(report, buffer));
                }
            }

            if (result.Level is null) return null;
            var name = SafeProductName(dev) ?? $"Dispositivo HID {dev.VendorID:X4}:{dev.ProductID:X4}";
            Log.Debug($"HID genérico: {name} ({dev.VendorID:X4}:{dev.ProductID:X4}) = {result.Level:0}%");
            return new BatteryReading
            {
                Id = $"hid:{dev.VendorID:X4}:{dev.ProductID:X4}",
                Name = name,
                Source = Name,
                Kind = GuessKind(dev),
                Level = (int)Math.Round(Math.Clamp(result.Level.Value, 0, 100)),
                Charging = result.Charging switch
                {
                    true => ChargingState.Charging,
                    false => ChargingState.NotCharging,
                    null => ChargingState.Unknown,
                },
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or ObjectDisposedException)
        {
            // Colecciones tomadas en exclusiva por Windows (mouse/teclado del sistema) o dispositivos que se fueron.
            Log.Debug($"HID genérico: {dev.VendorID:X4}:{dev.ProductID:X4}: {ex.Message}");
            return null;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <summary>Extrae nivel (0-100) y estado de carga de un report recibido.</summary>
    static (double? Level, bool? Charging) Parse(Report report, byte[] buffer)
    {
        double? level = null;
        bool? charging = null;
        report.Read(buffer, 0, value =>
        {
            if (!value.IsValid || value.IsNull) return;
            var usages = value.Usages.ToList();
            if (usages.Any(LevelUsages.Contains))
            {
                var item = value.DataItem;
                // Escala lógica del descriptor (p. ej. 0-100 o 0-255) llevada a porcentaje.
                level = item.LogicalRange > 0 ? value.GetFractionalValue() * 100.0 : value.GetLogicalValue();
            }
            else if (usages.Contains(ChargingUsage))
            {
                charging = value.GetLogicalValue() != 0;
            }
        });
        return (level, charging);
    }

    static (double? Level, bool? Charging) Merge((double? Level, bool? Charging) a, (double? Level, bool? Charging) b) =>
        (b.Level ?? a.Level, b.Charging ?? a.Charging);

    static DeviceKind GuessKind(HidDevice dev)
    {
        try
        {
            var usages = dev.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues()).ToList();
            if (usages.Contains(0x0001_0005) || usages.Contains(0x0001_0004)) return DeviceKind.Controller;
            if (usages.Contains(0x0001_0002)) return DeviceKind.Mouse;
            if (usages.Contains(0x0001_0006)) return DeviceKind.Keyboard;
            if (usages.Any(u => (u >> 16) == 0x000B)) return DeviceKind.Headset;   // página Telephony
        }
        catch
        {
            // Sin descriptor reconstruible.
        }
        return DeviceKind.Unknown;
    }

    static string? SafeProductName(HidDevice d)
    {
        try { return d.GetProductName()?.Trim(); }
        catch { return null; }
    }

    public void Dispose() { }
}
