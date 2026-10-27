using System.Collections.Concurrent;
using BatteryTray.Core.Config;

namespace BatteryTray.Core;

/// <summary>
/// Consulta todos los proveedores en paralelo, combina las lecturas y detecta batería baja.
///
/// Reglas:
///  - Si un proveedor falla (excepción o timeout), sus dispositivos conservan la última lectura.
///  - Si un proveedor responde pero un dispositivo ya no aparece, queda como desconectado
///    con su último nivel conocido.
///  - Si una lectura llega sin nivel (dispositivo dormido, etc.), se conserva el nivel anterior.
///  - Si la consulta anterior de un proveedor sigue colgada (p. ej. una llamada HID que no vuelve),
///    no se lanza otra encima: dos consultas a la vez al mismo receptor se pisan los comandos.
/// </summary>
public sealed class BatteryMonitor : IDisposable
{
    static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Se vuelve a avisar recién cuando el nivel sube este margen por encima del umbral.</summary>
    const int LowBatteryHysteresis = 5;

    readonly IReadOnlyList<IBatteryProvider> _providers;
    readonly AppConfig _config;
    readonly Dictionary<string, BatteryReading> _devices = [];
    readonly HashSet<string> _lowNotified = [];
    readonly SemaphoreSlim _pollLock = new(1, 1);
    readonly ConcurrentDictionary<IBatteryProvider, Task> _running = new();
    CancellationTokenSource? _loopCts;

    public BatteryMonitor(IReadOnlyList<IBatteryProvider> providers, AppConfig config)
    {
        _providers = providers;
        _config = config;
    }

    /// <summary>Después de cada consulta, con la lista completa de dispositivos.</summary>
    public event EventHandler<IReadOnlyList<BatteryReading>>? Updated;

    /// <summary>Una vez por dispositivo cada vez que cruza el umbral hacia abajo.</summary>
    public event EventHandler<BatteryReading>? LowBattery;

    public IReadOnlyList<BatteryReading> Devices
    {
        get
        {
            lock (_devices)
                return _devices.Values
                    .Where(d => !_config.HiddenDevices.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
        }
    }

    public void StartProviders()
    {
        foreach (var p in _providers)
        {
            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo iniciar el proveedor {p.Name}", ex);
            }
        }
    }

    /// <summary>Consulta ya y después cada PollIntervalSeconds.</summary>
    public void StartPolling()
    {
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _ = LoopAsync(_loopCts.Token);
    }

    async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await PollAsync(ct);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, _config.PollIntervalSeconds)));
            while (await timer.WaitForNextTickAsync(ct))
                await PollAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("El ciclo de consulta se detuvo", ex);
        }
    }

    public async Task PollAsync(CancellationToken ct = default)
    {
        await _pollLock.WaitAsync(ct);
        try
        {
            var results = await Task.WhenAll(_providers.Select(p => RunProviderAsync(p, ct)));

            IReadOnlyList<BatteryReading> snapshot;
            List<BatteryReading> low;
            lock (_devices)
            {
                foreach (var (provider, readings) in results)
                    if (readings is not null)
                        Merge(provider, readings);
                low = CheckLowBattery();
                snapshot = Devices;
            }

            foreach (var d in low)
            {
                Log.Info($"Batería baja: {d.Name} {d.Level}%");
                LowBattery?.Invoke(this, d);
            }
            Updated?.Invoke(this, snapshot);
        }
        finally
        {
            _pollLock.Release();
        }
    }

    async Task<(IBatteryProvider Provider, IReadOnlyList<BatteryReading>? Readings)> RunProviderAsync(
        IBatteryProvider provider, CancellationToken ct)
    {
        if (_running.TryGetValue(provider, out var previous) && !previous.IsCompleted)
        {
            Log.Warn($"{provider.Name}: la consulta anterior todavía no terminó, se saltea esta vuelta");
            return (provider, null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProviderTimeout);
        var started = DateTime.UtcNow;
        try
        {
            var query = provider.QueryAsync(timeout.Token);
            _running[provider] = query;
            var readings = await query.WaitAsync(timeout.Token);
            Log.Debug($"{provider.Name}: {readings.Count} dispositivo(s) en {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
            return (provider, readings);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"{provider.Name}: no respondió en {ProviderTimeout.TotalSeconds:0} s");
            return (provider, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"{provider.Name} falló", ex);
            return (provider, null);
        }
    }

    void Merge(IBatteryProvider provider, IReadOnlyList<BatteryReading> readings)
    {
        var seen = new HashSet<string>();
        foreach (var raw in readings)
        {
            if (_config.HiddenDevices.Contains(raw.Id, StringComparer.OrdinalIgnoreCase)) continue;

            var r = _config.Aliases.TryGetValue(raw.Id, out var alias) ? raw with { Name = alias } : raw;
            if (r.Level is null && _devices.TryGetValue(r.Id, out var prev) && prev.Level is not null)
            {
                r = r with
                {
                    Level = prev.Level,
                    Charging = prev.Charging,
                    Components = prev.Components,
                    Timestamp = prev.Timestamp,
                };
            }
            _devices[r.Id] = r;
            seen.Add(r.Id);
        }

        foreach (var (id, d) in _devices.ToList())
            if (d.Source == provider.Name && !seen.Contains(id) && d.Connected)
                _devices[id] = d with { Connected = false };
    }

    List<BatteryReading> CheckLowBattery()
    {
        var threshold = _config.LowBatteryThreshold;
        var low = new List<BatteryReading>();
        foreach (var d in _devices.Values)
        {
            if (d.Level is not int level) continue;
            var fresh = DateTimeOffset.Now - d.Timestamp < TimeSpan.FromMinutes(30);

            if (level <= threshold && d.Charging != ChargingState.Charging && fresh)
            {
                if (_lowNotified.Add(d.Id)) low.Add(d);
            }
            else if (level > threshold + LowBatteryHysteresis || d.Charging == ChargingState.Charging)
            {
                _lowNotified.Remove(d.Id);
            }
        }
        return low;
    }

    /// <summary>Corta el ciclo y espera a que termine la consulta en curso (para no pisarse con otro monitor).</summary>
    public async Task StopAsync()
    {
        _loopCts?.Cancel();
        if (await _pollLock.WaitAsync(ProviderTimeout + TimeSpan.FromSeconds(5)))
            _pollLock.Release();
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        foreach (var p in _providers)
        {
            try { p.Dispose(); }
            catch (Exception ex) { Log.Error($"Error al cerrar {p.Name}", ex); }
        }
    }
}
