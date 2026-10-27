using HidSharp;

namespace BatteryTray.Core.Providers.Razer;

/// <summary>
/// Batería de los BlackShark V2 Pro (2023 y Xbox) por su receptor 2.4 GHz.
/// El receptor reenvía las consultas al headset; si está apagado o fuera de alcance no hay
/// respuesta y se devuelve la última lectura como desconectada.
/// </summary>
public sealed class RazerHeadsetProvider : IBatteryProvider
{
    const int RazerVid = 0x1532;
    const int Attempts = 3;
    const int RemoteSettleMs = 35;
    static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(200);

    readonly Dictionary<string, BatteryReading> _seen = [];

    public string Name => "Razer headset (HID)";

    public Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct) => Task.Run(() => Query(ct), ct);

    IReadOnlyList<BatteryReading> Query(CancellationToken ct)
    {
        var readings = new Dictionary<string, BatteryReading>();
        foreach (var dev in DeviceList.Local.GetHidDevices(RazerVid).Where(IsCommandInterface))
        {
            ct.ThrowIfCancellationRequested();
            var reading = QueryDevice(dev, ct);
            if (reading is null) continue;
            readings[reading.Id] = reading;
            _seen[reading.Id] = reading;
        }

        // Respondía y ahora no: headset apagado, fuera de alcance o enchufado por cable.
        foreach (var (id, last) in _seen)
            if (!readings.ContainsKey(id))
                readings[id] = last with { Connected = false, Level = null, Note = "Sin respuesta (¿headset apagado o conectado por cable?)" };

        return readings.Values.ToList();
    }

    BatteryReading? QueryDevice(HidDevice dev, CancellationToken ct)
    {
        var model = RazerHeadsetProtocol.Models[dev.ProductID];
        var product = SafeProductName(dev) ?? model;
        var id = "razer:" + product.Trim().ToLowerInvariant().Replace(' ', '-');

        HidStream? stream = null;
        try
        {
            if (!dev.TryOpen(out stream)) return null;

            // Las dos consultas en una sola sesión con el receptor: menos paquetes por el aire.
            var values = QueryBytes(stream, ct, RazerHeadsetProtocol.CmdBattery, RazerHeadsetProtocol.CmdCharging);
            if (!values.TryGetValue(RazerHeadsetProtocol.CmdBattery, out var level))
            {
                Log.Debug($"Razer {product} (0x{dev.ProductID:X4}): el headset no responde");
                return null;
            }
            var charging = !values.TryGetValue(RazerHeadsetProtocol.CmdCharging, out var c) ? ChargingState.Unknown
                : c == 0 ? ChargingState.NotCharging
                : ChargingState.Charging;

            var percent = Math.Clamp((int)level, 0, 100);
            Log.Debug($"Razer {product} (0x{dev.ProductID:X4}): {percent}%, carga {charging}");
            return new BatteryReading
            {
                Id = id,
                Name = product,
                Source = Name,
                Kind = DeviceKind.Headset,
                Level = percent,
                Charging = charging,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log.Debug($"Razer headset: error con 0x{dev.ProductID:X4}: {ex.Message}");
            return null;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <summary>
    /// Consulta varios registros con el reenvío al headset habilitado una sola vez. Como
    /// blackshark-control, un "remoto" de más despierta el enlace si está dormido. Si el primero
    /// no responde (headset apagado), no se insiste con los demás.
    /// </summary>
    static Dictionary<byte, byte> QueryBytes(HidStream stream, CancellationToken ct, params byte[] commands)
    {
        var values = new Dictionary<byte, byte>();
        Drain(stream);
        Send(stream, RazerHeadsetProtocol.RemoteRequest(true));
        try
        {
            foreach (var cmd in commands)
            {
                for (var attempt = 0; attempt < Attempts && !values.ContainsKey(cmd); attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    Send(stream, RazerHeadsetProtocol.RemoteRequest(true));
                    stream.Write(RazerHeadsetProtocol.QueryRequest(cmd));
                    if (ReadReply(stream, cmd) is byte value) values[cmd] = value;
                }
                if (values.Count == 0) break;
            }
        }
        finally
        {
            Send(stream, RazerHeadsetProtocol.RemoteRequest(false));
        }
        return values;
    }

    static void Send(HidStream stream, byte[] report)
    {
        stream.Write(report);
        Thread.Sleep(RemoteSettleMs);
    }

    static byte? ReadReply(HidStream stream, byte cmd)
    {
        var buffer = new byte[RazerHeadsetProtocol.ReportLength];
        var deadline = DateTime.UtcNow + ReplyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            stream.ReadTimeout = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
            int n;
            try { n = stream.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { break; }
            var value = RazerHeadsetProtocol.ParseReply(buffer.AsSpan(0, n), cmd);
            if (value is not null) return value;
        }
        return null;
    }

    /// <summary>Descarta reports viejos para no confundirlos con la respuesta.</summary>
    static void Drain(HidStream stream)
    {
        var buffer = new byte[RazerHeadsetProtocol.ReportLength];
        stream.ReadTimeout = 20;
        try
        {
            while (true) stream.Read(buffer, 0, buffer.Length);
        }
        catch (TimeoutException)
        {
        }
    }

    static bool IsCommandInterface(HidDevice dev)
    {
        if (!RazerHeadsetProtocol.Models.ContainsKey(dev.ProductID)) return false;
        try
        {
            return dev.GetMaxOutputReportLength() == RazerHeadsetProtocol.ReportLength
                && dev.GetReportDescriptor().DeviceItems
                    .SelectMany(i => i.Usages.GetAllValues())
                    .Any(u => (u >> 16) == RazerHeadsetProtocol.VendorUsagePage);
        }
        catch
        {
            return false;
        }
    }

    static string? SafeProductName(HidDevice d)
    {
        try { return d.GetProductName(); }
        catch { return null; }
    }

    public void Dispose() { }
}
