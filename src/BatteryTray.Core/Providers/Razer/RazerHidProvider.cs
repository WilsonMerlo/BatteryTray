using System.Globalization;
using BatteryTray.Core.Config;
using BatteryTray.Core.Hid;
using HidSharp;

namespace BatteryTray.Core.Providers.Razer;

/// <summary>
/// Batería de cualquier periférico Razer que hable el protocolo de feature reports de 90 bytes.
///
/// - Modelos de la tabla de OpenRazer: se consultan directo.
/// - Modelos desconocidos: se prueban los transaction IDs conocidos y se recuerda el que funcionó
///   (o que no tiene batería), así la prueba se hace una sola vez por sesión.
///
/// Con Synapse abierto las dos apps comparten el canal; por eso se valida que la respuesta
/// corresponda al comando pedido y se reintenta si no.
/// </summary>
public sealed class RazerHidProvider : IBatteryProvider
{
    const int RazerVid = 0x1532;
    const int Attempts = 3;

    readonly RazerConfig _config;
    readonly Dictionary<int, RazerDeviceInfo> _overrides;
    readonly Dictionary<int, RazerDeviceInfo?> _probed = [];   // null = probado y sin batería

    public RazerHidProvider(RazerConfig config)
    {
        _config = config;
        _overrides = config.ExtraDevices
            .Select(ParseOverride)
            .Where(o => o is not null)
            .ToDictionary(o => o!.Value.Pid, o => o!.Value.Info);
    }

    public string Name => "Razer (HID)";

    public Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct) => Task.Run(() => QueryCoreAsync(ct), ct);

    async Task<IReadOnlyList<BatteryReading>> QueryCoreAsync(CancellationToken ct)
    {
        // La interfaz de control es la que acepta feature reports de 90 bytes (+ Report ID).
        var byPid = DeviceList.Local.GetHidDevices(RazerVid)
            .GroupBy(d => d.ProductID)
            .ToList();

        var readings = new Dictionary<string, BatteryReading>();
        foreach (var group in byPid)
        {
            ct.ThrowIfCancellationRequested();
            var control = group
                .Where(d => FeatureLength(d) >= RazerProtocol.BufferLength)
                .OrderBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (control is null) continue;

            var reading = await QueryDeviceAsync(group.Key, control, group.ToList(), ct);
            if (reading is null) continue;

            // El mismo mouse puede aparecer dos veces (receptor + cable): nos quedamos con la mejor lectura.
            if (!readings.TryGetValue(reading.Id, out var existing) || IsBetter(reading, existing))
                readings[reading.Id] = reading;
        }
        return readings.Values.ToList();
    }

    async Task<BatteryReading?> QueryDeviceAsync(int pid, HidDevice control, IReadOnlyList<HidDevice> interfaces, CancellationToken ct)
    {
        var product = SafeProductName(control) ?? $"Razer 0x{pid:X4}";
        var id = "razer:" + product.Trim().ToLowerInvariant().Replace(' ', '-');

        using var channel = HidFeatureChannel.Open(control.DevicePath);

        var info = await ResolveAsync(pid, product, channel, ct);
        if (info is null) return null;   // sin batería

        var level = await SendAsync(channel, info.TransactionId, RazerProtocol.CmdBatteryLevel, info.WaitMs, ct);
        var baseReading = new BatteryReading
        {
            Id = id,
            Name = product,
            Source = Name,
            Kind = GuessKind(interfaces),
        };

        if (level is null || !level.IsSuccess)
        {
            var status = level?.Status.ToString(CultureInfo.InvariantCulture) ?? "sin respuesta";
            Log.Debug($"Razer {product} (0x{pid:X4}): batería no disponible, status {status}");
            return baseReading with
            {
                Connected = false,
                Note = "Sin respuesta del dispositivo (¿apagado, dormido o conectado por otro medio?)",
            };
        }

        var charging = ChargingState.Unknown;
        if (info.HasCharging)
        {
            var c = await SendAsync(channel, info.TransactionId, RazerProtocol.CmdChargingStatus, info.WaitMs, ct);
            if (c is not null && c.IsSuccess)
                charging = c.Arguments[1] != 0 ? ChargingState.Charging : ChargingState.NotCharging;
        }
        else
        {
            charging = ChargingState.NotCharging;
        }

        var percent = RazerProtocol.ToPercent(level.Arguments[1]);
        Log.Debug($"Razer {product} (0x{pid:X4}): raw {level.Arguments[1]} = {percent}%, carga {charging}");
        return baseReading with { Level = percent, Charging = charging, Timestamp = DateTimeOffset.Now };
    }

    /// <summary>Tabla de overrides → tabla de OpenRazer → prueba automática.</summary>
    async Task<RazerDeviceInfo?> ResolveAsync(int pid, string product, HidFeatureChannel channel, CancellationToken ct)
    {
        if (_overrides.TryGetValue(pid, out var o)) return o;
        if (RazerDeviceTable.Known.TryGetValue(pid, out var k)) return k;
        if (_probed.TryGetValue(pid, out var p)) return p;
        if (!_config.ProbeUnknownDevices) return null;

        var sawTimeout = false;
        foreach (var tid in RazerDeviceTable.ProbeTransactionIds)
        {
            var r = await SendAsync(channel, tid, RazerProtocol.CmdBatteryLevel, RazerDeviceTable.DefaultWaitMs, ct);
            if (r is null) continue;
            if (r.Status == RazerProtocol.StatusTimeout) sawTimeout = true;
            if (!r.IsSuccess) continue;

            var c = await SendAsync(channel, tid, RazerProtocol.CmdChargingStatus, RazerDeviceTable.DefaultWaitMs, ct);
            var hasCharging = c is not null && c.IsSuccess;
            // Un dispositivo con cable sin batería a veces "responde" 0: lo descartamos salvo que informe carga.
            if (r.Arguments[1] == 0 && !hasCharging) continue;

            var info = new RazerDeviceInfo(product, tid, hasCharging);
            Log.Info($"Razer {product} (0x{pid:X4}) no está en la tabla; detectado transaction id 0x{tid:X2}");
            _probed[pid] = info;
            return info;
        }

        // Si algún intento dio timeout puede ser que el inalámbrico esté apagado: se vuelve a probar la próxima vez.
        if (!sawTimeout)
        {
            Log.Info($"Razer {product} (0x{pid:X4}): no informa batería");
            _probed[pid] = null;
        }
        return null;
    }

    /// <summary>Envía un comando y espera una respuesta válida para ese comando (con reintentos).</summary>
    static async Task<RazerResponse?> SendAsync(HidFeatureChannel channel, byte tid, byte commandId, int waitMs, CancellationToken ct)
    {
        var request = RazerProtocol.BuildRequest(tid, RazerProtocol.ClassMisc, commandId, dataSize: 0x02);
        RazerResponse? last = null;
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                channel.SetFeature(request);
                await Task.Delay(waitMs, ct);
                var buffer = new byte[RazerProtocol.BufferLength];
                channel.GetFeature(buffer);
                last = RazerProtocol.Parse(buffer);

                if (last.Matches(RazerProtocol.ClassMisc, commandId))
                {
                    if (last.IsSuccess || last.Status is RazerProtocol.StatusNotSupported
                                                        or RazerProtocol.StatusTimeout
                                                        or RazerProtocol.StatusFailure)
                        return last;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug($"Razer: intento {attempt} falló: {ex.Message}");
            }
            await Task.Delay(10 * attempt, ct);
        }
        return last;
    }

    static bool IsBetter(BatteryReading candidate, BatteryReading existing)
    {
        if (candidate.Connected != existing.Connected) return candidate.Connected;
        if (candidate.Level.HasValue != existing.Level.HasValue) return candidate.Level.HasValue;
        // Con cable se conoce el estado de carga real.
        return candidate.Charging == ChargingState.Charging && existing.Charging != ChargingState.Charging;
    }

    static DeviceKind GuessKind(IEnumerable<HidDevice> interfaces)
    {
        bool mouse = false, keyboard = false;
        foreach (var d in interfaces)
        {
            try
            {
                foreach (var usage in d.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues()))
                {
                    if (usage == 0x0001_0002) mouse = true;
                    if (usage == 0x0001_0006) keyboard = true;
                }
            }
            catch
            {
                // Algunas colecciones no tienen descriptor reconstruible; no importa.
            }
        }
        // Los mouse Razer también exponen un "teclado" para las macros, así que el mouse manda.
        return mouse ? DeviceKind.Mouse : keyboard ? DeviceKind.Keyboard : DeviceKind.Unknown;
    }

    static int FeatureLength(HidDevice d)
    {
        try { return d.GetMaxFeatureReportLength(); }
        catch { return 0; }
    }

    static string? SafeProductName(HidDevice d)
    {
        try { return d.GetProductName(); }
        catch { return null; }
    }

    static (int Pid, RazerDeviceInfo Info)? ParseOverride(RazerDeviceOverride o)
    {
        if (!TryParseHex(o.Pid, out var pid) || !TryParseHex(o.TransactionId, out var tid) || tid > 0xFF)
        {
            Log.Warn($"Razer: override inválido (pid \"{o.Pid}\", transactionId \"{o.TransactionId}\"), se ignora");
            return null;
        }
        return (pid, new RazerDeviceInfo("override", (byte)tid, o.HasCharging, Math.Max(1, o.WaitMs)));
    }

    static bool TryParseHex(string s, out int value)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    public void Dispose() { }
}
