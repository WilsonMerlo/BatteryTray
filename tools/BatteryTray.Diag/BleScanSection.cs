using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace BatteryTray.Diag;

/// <summary>
/// Escucha advertisements BLE durante N segundos y vuelca los de Apple (company ID 0x004C)
/// del tipo "proximity pairing" (0x07), que es donde los AirPods anuncian su batería.
/// No se decodifica nada todavía (eso es la fase 1): queremos ver los bytes crudos y el
/// model ID de tus AirPods 4 ANC.
/// </summary>
static class BleScanSection
{
    const ushort AppleCompanyId = 0x004C;
    const byte ProximityPairing = 0x07;

    sealed record Seen(ulong Address, string Hex, short Rssi, int Count);

    public static async Task RunAsync(Output o, int seconds)
    {
        o.Section($"5. ESCANEO BLE — advertisements de Apple \"proximity pairing\" ({seconds} s)");
        o.Line("Para que aparezcan: sacá los AirPods del estuche o abrí la tapa cerca de la PC.");

        var radios = (await Windows.Devices.Radios.Radio.GetRadiosAsync())
            .Where(r => r.Kind == Windows.Devices.Radios.RadioKind.Bluetooth).ToList();
        if (radios.Count == 0 || radios.All(r => r.State != Windows.Devices.Radios.RadioState.On))
        {
            o.Line("!! La radio Bluetooth está apagada o no existe: no se puede escanear. Prendela y volvé a correr.");
            return;
        }

        var apple = new ConcurrentDictionary<string, Seen>();       // clave: payload hex
        var otherApple = new ConcurrentDictionary<byte, int>();     // tipo de mensaje Apple → cantidad
        var devices = new ConcurrentDictionary<ulong, byte>();      // direcciones vistas (todas)

        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, e) =>
        {
            devices.TryAdd(e.BluetoothAddress, 0);
            foreach (var md in e.Advertisement.ManufacturerData)
            {
                if (md.CompanyId != AppleCompanyId) continue;
                var data = ToBytes(md.Data);
                if (data.Length == 0) continue;
                if (data[0] != ProximityPairing)
                {
                    otherApple.AddOrUpdate(data[0], 1, (_, c) => c + 1);
                    continue;
                }
                var hex = Convert.ToHexString(data);
                var isNew = !apple.ContainsKey(hex);
                apple.AddOrUpdate(hex,
                    _ => new Seen(e.BluetoothAddress, hex, e.RawSignalStrengthInDBm, 1),
                    (_, s) => s with { Count = s.Count + 1, Rssi = e.RawSignalStrengthInDBm });
                if (isNew)
                    o.Status($"  nuevo proximity pairing de {e.BluetoothAddress:X12} RSSI {e.RawSignalStrengthInDBm} dBm");
            }
        };

        var stopped = new TaskCompletionSource<BluetoothError>();
        watcher.Stopped += (_, e) => stopped.TrySetResult(e.Error);

        watcher.Start();
        for (int s = seconds; s > 0 && watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started; s--)
        {
            Console.Write($"\r  escuchando... {s,3} s  (dispositivos BLE vistos: {devices.Count})   ");
            await Task.Delay(1000);
        }
        Console.WriteLine();
        if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            watcher.Stop();
        var error = await Task.WhenAny(stopped.Task, Task.Delay(2000)) == stopped.Task ? stopped.Task.Result : BluetoothError.Success;
        if (error != BluetoothError.Success)
            o.Line($"El escaneo se detuvo con error: {error}");

        o.Line($"Dispositivos BLE distintos vistos: {devices.Count}");
        o.Line($"Otros mensajes de Apple (tipo→cantidad): {(otherApple.IsEmpty ? "ninguno" : string.Join(", ", otherApple.OrderBy(k => k.Key).Select(k => $"0x{k.Key:X2}→{k.Value}")))}");
        o.Line($"Payloads proximity pairing distintos: {apple.Count}");
        foreach (var s in apple.Values.OrderBy(s => s.Address).ThenBy(s => s.Hex))
        {
            var bytes = Convert.FromHexString(s.Hex);
            // byte0 = tipo 0x07, byte1 = largo, byte2 = prefijo, bytes 3-4 = modelo (en el orden en que vienen)
            var model = bytes.Length >= 5 ? $"0x{bytes[3]:X2}{bytes[4]:X2}" : "?";
            o.Line();
            o.Line($"  addr {s.Address:X12}  RSSI {s.Rssi} dBm  visto {s.Count}x  largo {bytes.Length} B  modelo(bytes 3-4)={model}");
            o.Line($"  {SpacedHex(bytes)}");
        }
    }

    static byte[] ToBytes(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    static string SpacedHex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}
