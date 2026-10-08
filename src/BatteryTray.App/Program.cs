using BatteryTray.Core;
using BatteryTray.Core.Config;

namespace BatteryTray.App;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Log.Init(AppPaths.LogDir);
        Application.ThreadException += (_, e) => Log.Error("Excepción no controlada en la interfaz", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Excepción no controlada", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Tarea con excepción no observada", e.Exception);
            e.SetObserved();
        };

        // Modo de prueba: dibuja la ventanita y los íconos a un PNG y sale.
        if (args.Length >= 2 && args[0] == "--preview")
            return Preview.Run(args[1]);

        // Una sola instancia por usuario.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\BatteryTray.App", out var isFirst);
        if (!isFirst)
        {
            Log.Info("Ya hay una instancia corriendo; salgo");
            return 0;
        }

        Log.Info($"BatteryTray iniciado ({Environment.ProcessPath})");
        Application.Run(new TrayApplicationContext());
        return 0;
    }
}
