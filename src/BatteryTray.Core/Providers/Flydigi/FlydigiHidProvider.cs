using HidSharp;

namespace BatteryTray.Core.Providers.Flydigi;

/// <summary>
/// Mandos Flydigi con el protocolo "V2" (Vader 5 Pro, Apex 5/6), por cable o por su receptor 2.4 GHz.
/// Implementado a partir del driver de SDL, todavía sin probar con el mando real.
///
/// Si el receptor está en modo Xbox 360 (XInput) no expone la interfaz de Flydigi: en ese caso
/// el mando solo puede aparecer por el proveedor de mandos de Windows.
/// </summary>
public sealed class FlydigiHidProvider : IBatteryProvider
{
    static readonly TimeSpan ResponseTimeout = TimeSpan.FromMilliseconds(500);

    readonly Dictionary<string, BatteryReading> _seen = [];

    public string Name => "Flydigi (HID)";

    public Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct) => Task.Run(() => Query(ct), ct);

    IReadOnlyList<BatteryReading> Query(CancellationToken ct)
    {
        var readings = new Dictionary<string, BatteryReading>();
        foreach (var dev in DeviceList.Local.GetHidDevices(FlydigiProtocol.VendorId).Where(IsCommandInterface))
        {
            ct.ThrowIfCancellationRequested();
            var reading = QueryDevice(dev);
            if (reading is null) continue;
            readings[reading.Id] = reading;
            _seen[reading.Id] = reading;
        }

        // Respondía y ahora no: mando apagado o fuera del alcance del receptor.
        foreach (var (id, last) in _seen)
            if (!readings.ContainsKey(id))
                readings[id] = last with { Connected = false, Level = null, Note = "Sin respuesta (¿mando apagado?)" };

        return readings.Values.ToList();
    }

    BatteryReading? QueryDevice(HidDevice dev)
    {
        var model = FlydigiProtocol.Find(dev.ProductID);
        HidStream? stream = null;
        try
        {
            if (!dev.TryOpen(out stream)) return null;
            var outputLength = dev.GetMaxOutputReportLength();
            if (outputLength <= 0) return null;

            // Como en SDL, se reintenta: el primer pedido a veces se pierde.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                stream.Write(FlydigiProtocol.InfoRequest(model, outputLength));
                var info = ReadInfo(stream, dev, model);
                if (info is null) continue;

                Log.Debug($"Flydigi {model.Name} (0x{dev.ProductID:X4}): batería 0x{info.RawBattery:X2} = {info.Percent}% {info.Charging}");
                return new BatteryReading
                {
                    Id = $"flydigi:{dev.ProductID:X4}",
                    Name = model.Name,
                    Source = Name,
                    Kind = DeviceKind.Controller,
                    Level = info.Percent,
                    Charging = info.Charging,
                    Note = model.Scale >= 20 ? "El mando informa la batería en pasos de 20 %" : null,
                };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log.Debug($"Flydigi: error con 0x{dev.ProductID:X4}: {ex.Message}");
        }
        finally
        {
            stream?.Dispose();
        }
        return null;
    }

    static FlydigiInfo? ReadInfo(HidStream stream, HidDevice dev, FlydigiProtocol.Model model)
    {
        var buffer = new byte[Math.Max(dev.GetMaxInputReportLength(), 64)];
        var deadline = DateTime.UtcNow + ResponseTimeout;
        while (DateTime.UtcNow < deadline)
        {
            stream.ReadTimeout = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
            int n;
            try { n = stream.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { break; }
            // El mando manda también reports de estado (0xEF): se ignoran hasta la respuesta.
            var info = FlydigiProtocol.ParseInfo(buffer.AsSpan(0, n), model);
            if (info is not null) return info;
        }
        return null;
    }

    /// <summary>La interfaz de comandos de Flydigi usa la página de uso de fabricante 0xFFA0.</summary>
    static bool IsCommandInterface(HidDevice dev)
    {
        try
        {
            return dev.GetReportDescriptor().DeviceItems
                .SelectMany(i => i.Usages.GetAllValues())
                .Any(u => (u >> 16) == FlydigiProtocol.VendorUsagePage);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() { }
}
