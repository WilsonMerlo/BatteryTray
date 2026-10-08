using HidSharp;

namespace BatteryTray.Core.Providers.Keychron;

/// <summary>
/// Teclados Keychron con el firmware propio que agrega el comando de batería 0xBA
/// (por ahora el K8 HE "sunset"). Con el firmware de fábrica no responden y no aparecen.
///
/// Se habla con la interfaz raw HID de VIA (uso 0xFF60:0x61):
///  - del teclado, cuando está por cable;
///  - del receptor Keychron Link, que reenvía el pedido al teclado por 2.4 GHz.
/// Solo se envían comandos de lectura (0xBA y el 0x01 de VIA).
/// </summary>
public sealed class KeychronHidProvider : IBatteryProvider
{
    const int KeychronVid = 0x3434;
    const uint RawHidUsage = 0xFF60_0061;
    static readonly TimeSpan ResponseTimeout = TimeSpan.FromMilliseconds(600);

    /// <summary>Receptores Keychron Link conocidos (2.4 GHz).</summary>
    static readonly HashSet<int> ReceiverPids = [0xD030];

    static readonly Dictionary<int, string> KnownNames = new()
    {
        [0x0E80] = "Keychron K8 HE",
        [0x0E81] = "Keychron K8 HE",
        [0x0E82] = "Keychron K8 HE",
    };

    /// <summary>Nombre del producto visto por cable, para usarlo cuando llega por el receptor.</summary>
    readonly Dictionary<int, string> _namesSeen = [];

    /// <summary>Teclados que alguna vez respondieron por el receptor (si dejan de responder, están apagados).</summary>
    readonly Dictionary<string, BatteryReading> _seenViaReceiver = [];

    public string Name => "Keychron (HID)";

    public Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct) => Task.Run(() => Query(ct), ct);

    IReadOnlyList<BatteryReading> Query(CancellationToken ct)
    {
        var readings = new Dictionary<string, BatteryReading>();
        var interfaces = DeviceList.Local.GetHidDevices(KeychronVid).Where(IsRawHid).ToList();

        foreach (var dev in interfaces)
        {
            ct.ThrowIfCancellationRequested();
            var reading = ReceiverPids.Contains(dev.ProductID) ? QueryReceiver(dev) : QueryKeyboard(dev);
            if (reading is null) continue;
            if (!readings.TryGetValue(reading.Id, out var existing) || IsBetter(reading, existing))
                readings[reading.Id] = reading;
        }

        // Teclado que respondía por el receptor y ahora no: apagado, dormido o fuera de alcance.
        foreach (var (id, last) in _seenViaReceiver)
            if (!readings.ContainsKey(id))
                readings[id] = last with { Connected = false, Level = null, Note = "Sin respuesta por 2.4 GHz (¿apagado o dormido?)" };

        return readings.Values.ToList();
    }

    BatteryReading? QueryKeyboard(HidDevice dev)
    {
        var name = SafeProductName(dev) ?? NameFor(dev.ProductID);
        _namesSeen[dev.ProductID] = name;

        using var stream = TryOpen(dev);
        if (stream is null) return null;
        var status = SendBattery(stream);
        if (status is null) return null;   // firmware de fábrica: no conoce el comando

        Log.Debug($"Keychron {name} (cable): {status}");
        return ToReading(dev.ProductID, name, status, viaReceiver: false);
    }

    BatteryReading? QueryReceiver(HidDevice dev)
    {
        using var stream = TryOpen(dev);
        if (stream is null) return null;

        var status = SendBattery(stream);
        if (status is null) return null;

        // El VID/PID del teclado viene en la respuesta del comando 0x01 por vía inalámbrica.
        var identity = Exchange(stream, KeychronBatteryProtocol.CmdViaProtocolVersion, r => KeychronBatteryProtocol.ParseWirelessIdentity(r));
        var pid = identity?.Pid ?? 0;
        var name = NameFor(pid);

        Log.Debug($"Keychron {name} (receptor 0x{dev.ProductID:X4}): {status}");
        var reading = ToReading(pid, name, status, viaReceiver: true);
        _seenViaReceiver[reading.Id] = reading;
        return reading;
    }

    BatteryReading ToReading(int pid, string name, KeychronBatteryStatus status, bool viaReceiver)
    {
        // El voltaje solo se mide en modo inalámbrico: por cable no hay nivel vigente
        // (el monitor conserva el último nivel conocido).
        int? level = status.Measured ? status.Percent : null;
        var charging = status.Charging ? ChargingState.Charging
            : status.Full || !status.UsbPower ? ChargingState.NotCharging
            : ChargingState.Unknown;

        return new BatteryReading
        {
            Id = $"keychron:{pid:X4}",
            Name = name,
            Source = Name,
            Kind = DeviceKind.Keyboard,
            Level = level,
            Charging = charging,
            Connected = true,
            Note = status.Measured ? null
                : viaReceiver ? "Midiendo batería…"
                : "Por cable el teclado no mide la batería: se muestra la última lectura inalámbrica",
        };
    }

    static KeychronBatteryStatus? SendBattery(HidStream stream) =>
        Exchange(stream, KeychronBatteryProtocol.CmdBattery, r => KeychronBatteryProtocol.ParseBattery(r));

    /// <summary>
    /// Envía un comando y espera la respuesta que empiece con el mismo id. Otras apps (SignalRGB,
    /// Launcher) pueden estar usando la misma interfaz, así que se descartan los reports ajenos.
    /// </summary>
    static T? Exchange<T>(HidStream stream, byte command, Func<byte[], T?> parse) where T : class
    {
        try
        {
            stream.Write(KeychronBatteryProtocol.Request(command));
            var deadline = DateTime.UtcNow + ResponseTimeout;
            var buffer = new byte[stream.Device.GetMaxInputReportLength()];
            while (DateTime.UtcNow < deadline)
            {
                stream.ReadTimeout = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                int n;
                try { n = stream.Read(buffer, 0, buffer.Length); }
                catch (TimeoutException) { break; }
                if (n < 2 || buffer[1] != command) continue;
                var parsed = parse(buffer.AsSpan(1, n - 1).ToArray());
                if (parsed is not null) return parsed;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Log.Debug($"Keychron: error de E/S con 0x{stream.Device.ProductID:X4}: {ex.Message}");
        }
        return null;
    }

    static HidStream? TryOpen(HidDevice dev)
    {
        try
        {
            return dev.TryOpen(out var stream) ? stream : null;
        }
        catch (Exception ex)
        {
            Log.Debug($"Keychron: no se pudo abrir 0x{dev.ProductID:X4}: {ex.Message}");
            return null;
        }
    }

    static bool IsRawHid(HidDevice dev)
    {
        try
        {
            return dev.GetReportDescriptor().DeviceItems.Any(i => i.Usages.GetAllValues().Contains(RawHidUsage));
        }
        catch
        {
            return false;
        }
    }

    string NameFor(int pid) =>
        _namesSeen.TryGetValue(pid, out var seen) ? seen
        : KnownNames.TryGetValue(pid, out var known) ? known
        : pid == 0 ? "Teclado Keychron" : $"Keychron 0x{pid:X4}";

    static string? SafeProductName(HidDevice d)
    {
        try { return d.GetProductName()?.Trim(); }
        catch { return null; }
    }

    /// <summary>Si el teclado responde por cable y por el receptor a la vez, gana la lectura medida.</summary>
    static bool IsBetter(BatteryReading candidate, BatteryReading existing)
    {
        if (candidate.Level.HasValue != existing.Level.HasValue) return candidate.Level.HasValue;
        return candidate.Charging == ChargingState.Charging && existing.Charging != ChargingState.Charging;
    }

    public void Dispose() { }
}
