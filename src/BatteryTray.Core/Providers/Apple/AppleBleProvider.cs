using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BatteryTray.Core.Config;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BatteryTray.Core.Providers.Apple;

/// <summary>
/// AirPods y Beats: Windows no expone su batería, pero ellos la anuncian por BLE en el mensaje
/// "proximity pairing". Un watcher escucha todo el tiempo y guarda el último mensaje de cada modelo;
/// la consulta periódica solo lee ese estado.
///
/// Como cada auricular anuncia con una dirección aleatoria que rota, no se puede asociar el
/// mensaje a "tus" AirPods por dirección. Se asocia por modelo: por defecto solo se muestran
/// los modelos que tenés emparejados con esta PC (el PID de Windows coincide con el del mensaje).
/// </summary>
public sealed partial class AppleBleProvider : IBatteryProvider
{
    sealed record Observation(ApplePodsStatus Status, short Rssi, DateTimeOffset At);

    sealed record PairedApple(string Name, ushort Pid, bool Connected);

    readonly AppleConfig _config;
    readonly ConcurrentDictionary<ushort, Observation> _seen = new();
    readonly Dictionary<Guid, ushort?> _pidByContainer = [];
    BluetoothLEAdvertisementWatcher? _watcher;

    public AppleBleProvider(AppleConfig config) => _config = config;

    public string Name => "Apple (BLE)";

    public void Start() => EnsureWatcher();

    public async Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct)
    {
        EnsureWatcher();

        var paired = await GetPairedAppleDevicesAsync();
        var readings = new List<BatteryReading>();

        foreach (var p in paired)
        {
            _seen.TryGetValue(p.Pid, out var obs);
            readings.Add(ToReading(p.Pid, obs, p.Connected));
        }

        if (!_config.OnlyPairedModels)
        {
            var pairedPids = paired.Select(p => p.Pid).ToHashSet();
            foreach (var (pid, obs) in _seen)
                if (!pairedPids.Contains(pid))
                    readings.Add(ToReading(pid, obs, connected: false));
        }
        return readings;
    }

    BatteryReading ToReading(ushort pid, Observation? obs, bool connected)
    {
        var model = AppleModels.Find(pid);
        var reading = new BatteryReading
        {
            Id = $"apple:{pid:X4}",
            Name = model.Name,
            Source = Name,
            Kind = model.Kind,
            Connected = connected,
        };

        if (obs is null)
            return reading with { Note = "Sin datos todavía: sacá los auriculares o abrí el estuche cerca de la PC" };

        var s = obs.Status;
        if (!model.Dual)
        {
            return reading with
            {
                Level = s.Single,
                Charging = s.SingleCharging ? ChargingState.Charging : ChargingState.NotCharging,
                Timestamp = obs.At,
            };
        }

        // El nivel general es el del auricular más bajo; si los dos están cargando, "cargando".
        int?[] pods = [s.Left, s.Right];
        var known = pods.Where(l => l.HasValue).Select(l => l!.Value).ToList();
        var allCharging = (s.Left is null || s.LeftCharging) && (s.Right is null || s.RightCharging) && known.Count > 0;

        return reading with
        {
            Level = known.Count > 0 ? known.Min() : null,
            Charging = allCharging ? ChargingState.Charging : ChargingState.NotCharging,
            Timestamp = obs.At,
            Components =
            [
                new BatteryComponent("Izquierdo", s.Left, s.LeftCharging ? ChargingState.Charging : ChargingState.NotCharging),
                new BatteryComponent("Derecho", s.Right, s.RightCharging ? ChargingState.Charging : ChargingState.NotCharging),
                new BatteryComponent("Estuche", s.Case, s.CaseCharging ? ChargingState.Charging : ChargingState.NotCharging),
            ],
        };
    }

    void EnsureWatcher()
    {
        if (_watcher is { Status: BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Stopping })
            return;

        try
        {
            if (_watcher is null)
            {
                // Misma configuración que el diagnóstico de la fase 0, que ya probó recibir los AirPods 4.
                // El filtrado (Apple + tipo 0x07) se hace en OnReceived.
                _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
                _watcher.Received += OnReceived;
                _watcher.Stopped += (_, e) => Log.Warn($"Apple: el escaneo BLE se detuvo ({e.Error})");
            }
            _watcher.Start();
            Log.Info("Apple: escaneo BLE iniciado");
        }
        catch (Exception ex)
        {
            // Típicamente: Bluetooth apagado. Se reintenta en la próxima consulta.
            Log.Warn($"Apple: no se pudo iniciar el escaneo BLE ({ex.GetType().Name} 0x{ex.HResult:X8})");
        }
    }

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        if (e.RawSignalStrengthInDBm < _config.MinRssi) return;

        foreach (var md in e.Advertisement.ManufacturerData)
        {
            if (md.CompanyId != ProximityPairing.AppleCompanyId) continue;
            var data = new byte[md.Data.Length];
            using (var reader = DataReader.FromBuffer(md.Data)) reader.ReadBytes(data);

            var status = ProximityPairing.TryDecode(data);
            if (status is null) continue;

            // Con los auriculares fuera del estuche, el mensaje trae el estuche como desconocido:
            // conservamos el último valor conocido (el estuche no se descarga solo en minutos).
            if (status.Case is null && _seen.TryGetValue(status.ModelPid, out var previous) && previous.Status.Case is not null)
                status = status with { Case = previous.Status.Case, CaseCharging = previous.Status.CaseCharging };

            _seen[status.ModelPid] = new Observation(status, e.RawSignalStrengthInDBm, DateTimeOffset.Now);
            Log.Debug($"Apple: 0x{status.ModelPid:X4} RSSI {e.RawSignalStrengthInDBm} L={status.Left} R={status.Right} " +
                      $"estuche={status.Case} [{Convert.ToHexString(data)}]");
        }
    }

    /// <summary>Dispositivos Apple emparejados por Bluetooth clásico, con su PID y estado de conexión.</summary>
    async Task<List<PairedApple>> GetPairedAppleDevicesAsync()
    {
        const string connectedKey = "System.Devices.Aep.IsConnected";
        const string containerKey = "System.Devices.Aep.ContainerId";

        var result = new List<PairedApple>();
        var endpoints = await DeviceInformation.FindAllAsync(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            [connectedKey, containerKey],
            DeviceInformationKind.AssociationEndpoint);

        foreach (var ep in endpoints)
        {
            if (!ep.Properties.TryGetValue(containerKey, out var c) || c is not Guid container) continue;

            if (!_pidByContainer.TryGetValue(container, out var pid))
            {
                pid = await FindApplePidAsync(container);
                _pidByContainer[container] = pid;   // el PID de un dispositivo emparejado no cambia
            }
            if (pid is null) continue;

            var connected = ep.Properties.TryGetValue(connectedKey, out var v) && v is true;
            result.Add(new PairedApple(ep.Name, pid.Value, connected));
        }
        return result;
    }

    /// <summary>Busca en los devnodes del container un id del tipo ..._VID&amp;0001004C_PID&amp;201B...</summary>
    static async Task<ushort?> FindApplePidAsync(Guid container)
    {
        var nodes = await DeviceInformation.FindAllAsync(
            $"System.Devices.ContainerId:=\"{{{container}}}\"", null, DeviceInformationKind.Device);
        foreach (var n in nodes)
        {
            var m = AppleIdRegex().Match(n.Id);
            if (m.Success) return Convert.ToUInt16(m.Groups[1].Value, 16);
        }
        return null;
    }

    [GeneratedRegex(@"VID&0001004C_PID&([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex AppleIdRegex();

    public void Dispose()
    {
        if (_watcher is null) return;
        _watcher.Received -= OnReceived;
        try { _watcher.Stop(); } catch { /* ya detenido */ }
    }
}
