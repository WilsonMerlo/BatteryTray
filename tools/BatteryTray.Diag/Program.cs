using System.Text;
using BatteryTray.Diag;

Console.OutputEncoding = Encoding.UTF8;

var options = Options.Parse(args);
if (options.ShowHelp)
{
    Options.PrintHelp();
    return 0;
}

// Windows.Gaming.Input enumera los mandos en segundo plano: lo despertamos ya para que
// cuando lleguemos a esa sección la lista esté completa.
GamingSection.WarmUp();

var path = options.OutputPath ?? Path.Combine(Environment.CurrentDirectory, $"diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
using var o = new Output(path);

o.Line($"BatteryTray.Diag — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
o.Line($"Windows {Environment.OSVersion.Version}  |  .NET {Environment.Version}  |  proceso 64 bits: {Environment.Is64BitProcess}");
o.Line("Solo lectura: no se envía ningún comando a los dispositivos.");

IReadOnlyList<DevNode> nodes = [];
await Step("árbol de dispositivos", async () => nodes = await DevNodes.SnapshotAsync());
await Step("HID", () => { HidSection.Run(o); return Task.CompletedTask; });
await Step("USB", () => { UsbSection.Run(o, nodes); return Task.CompletedTask; });
await Step("Bluetooth", () => BluetoothSection.RunAsync(o, nodes, options.ReadGatt));
await Step("mandos", () => GamingSection.RunAsync(o));
if (options.BleScanSeconds > 0)
    await Step("escaneo BLE", () => BleScanSection.RunAsync(o, options.BleScanSeconds));

o.Line();
o.Line($"Reporte guardado en: {o.FilePath}");
return 0;

// Cada sección es independiente: si una explota, se registra y se sigue con las demás.
async Task Step(string name, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (Exception ex)
    {
        o.Line();
        o.Line($"!! Falló la sección \"{name}\": {Errors.Describe(ex)}");
    }
}
