namespace BatteryTray.Diag;

sealed class Options
{
    public int BleScanSeconds { get; private set; } = 15;
    public bool ReadGatt { get; private set; } = true;
    public string? OutputPath { get; private set; }
    public bool ShowHelp { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--ble" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s):
                    o.BleScanSeconds = Math.Max(0, s);
                    i++;
                    break;
                case "--no-gatt":
                    o.ReadGatt = false;
                    break;
                case "--out" when i + 1 < args.Length:
                    o.OutputPath = args[++i];
                    break;
                case "-h" or "--help" or "/?":
                    o.ShowHelp = true;
                    break;
                default:
                    Console.WriteLine($"Argumento desconocido: {args[i]}");
                    o.ShowHelp = true;
                    break;
            }
        }
        return o;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            BatteryTray.Diag — diagnóstico de periféricos (solo lectura, no envía nada a los dispositivos)

            Uso: BatteryTray.Diag [opciones]

              --ble <segundos>   Duración del escaneo BLE de advertisements (default 15, 0 = no escanear)
              --no-gatt          No intentar leer el servicio de batería GATT de los dispositivos BLE
              --out <archivo>    Ruta del reporte (default: diag-AAAAMMDD-HHMMSS.txt en la carpeta actual)
              -h, --help         Esta ayuda
            """);
    }
}
