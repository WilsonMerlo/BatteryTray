using System.Text;
using BatteryTray.Core;
using BatteryTray.Core.Config;

// Consola de prueba de la fase 1: consulta todos los proveedores y muestra una tabla.
//   BatteryTray.Cli                 una consulta (espera unos segundos a que lleguen los advertisements BLE)
//   BatteryTray.Cli --watch         consulta cada PollIntervalSeconds hasta Ctrl+C
//   BatteryTray.Cli --wait 15       segundos de escucha BLE antes de la primera consulta (default 10)
//   BatteryTray.Cli -v              log detallado también en consola

Console.OutputEncoding = Encoding.UTF8;

var watch = args.Contains("--watch");
var verbose = args.Contains("-v") || args.Contains("--verbose");
var waitSeconds = 10;
var waitIndex = Array.IndexOf(args, "--wait");
if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out var w))
    waitSeconds = Math.Max(0, w);

Log.Init(AppPaths.LogDir);
Log.Verbose = true;          // la consola de prueba siempre deja el log detallado en el archivo
Log.ToConsole = verbose;

var config = ConfigStore.Load(AppPaths.ConfigFile);
Console.WriteLine($"Configuración: {AppPaths.ConfigFile}");
Console.WriteLine($"Log:           {AppPaths.LogDir}");

using var monitor = new BatteryMonitor(ProviderFactory.Create(config), config);
monitor.LowBattery += (_, d) => Console.WriteLine($"\n⚠  Batería baja: {d.Name} {d.Level}%");

monitor.StartProviders();
if (waitSeconds > 0)
{
    Console.WriteLine($"Escuchando {waitSeconds} s para recibir los advertisements BLE...");
    await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
}

if (!watch)
{
    await monitor.PollAsync();
    Print(monitor.Devices);
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
monitor.Updated += (_, devices) => Print(devices);
monitor.StartPolling();
Console.WriteLine($"Consultando cada {Math.Max(15, config.PollIntervalSeconds)} s. Ctrl+C para salir.");
try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }

static void Print(IReadOnlyList<BatteryReading> devices)
{
    Console.WriteLine();
    Console.WriteLine($"── {DateTime.Now:HH:mm:ss} ─ {devices.Count} dispositivo(s) ──────────────────────────────────────────────");
    Console.WriteLine($"{"Dispositivo",-30} {"Nivel",6}  {"Carga",-11} {"Conectado",-10} {"Lectura",-9} Fuente");
    foreach (var d in devices)
    {
        var level = d.Level is int l ? $"{l}%" : "—";
        var charging = d.Charging switch
        {
            ChargingState.Charging => "cargando",
            ChargingState.NotCharging => "no",
            _ => "?",
        };
        var time = d.Level is null ? "—" : d.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Console.WriteLine($"{Trim(d.Name, 30),-30} {level,6}  {charging,-11} {(d.Connected ? "sí" : "no"),-10} {time,-9} {d.Source}");

        if (d.Components.Count > 0)
            Console.WriteLine("    " + string.Join("  |  ", d.Components.Select(c =>
                $"{c.Name} {(c.Level is int cl ? $"{cl}%" : "—")}{(c.Charging == ChargingState.Charging ? " (cargando)" : "")}")));
        if (d.Note is not null)
            Console.WriteLine($"    {d.Note}");
        Console.WriteLine($"    id: {d.Id}");
    }
}

static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
