using System.Drawing.Imaging;
using BatteryTray.Core;
using BatteryTray.Core.Config;

namespace BatteryTray.App;

/// <summary>
/// BatteryTray.exe --preview salida.png
/// Consulta los dispositivos reales y guarda una imagen con la ventanita (tema oscuro y claro)
/// y ejemplos del ícono. Sirve para revisar el diseño sin abrir la bandeja.
/// </summary>
internal static class Preview
{
    public static int Run(string outputPath)
    {
        var config = ConfigStore.Load(AppPaths.ConfigFile);
        using var monitor = new BatteryMonitor(ProviderFactory.Create(config), config);
        monitor.StartProviders();
        Thread.Sleep(TimeSpan.FromSeconds(12));
        monitor.PollAsync().GetAwaiter().GetResult();
        var devices = monitor.Devices;

        using var popup = new DevicePopup();
        popup.SetData(devices, DateTimeOffset.Now, config.LowBatteryThreshold, config.TrayDevice, refreshing: false);
        using var dark = popup.Render(Theme.Dark);
        using var light = popup.Render(Theme.Light);

        (int? Level, bool Charging)[] samples = [(67, false), (100, false), (35, false), (15, false), (45, true), (null, false)];
        const int iconSize = 32, gap = 16;
        var width = dark.Width + light.Width + gap * 3;
        var height = Math.Max(dark.Height, light.Height) + iconSize * 2 + gap * 4;

        using var canvas = new Bitmap(width, height);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(0x60, 0x60, 0x60));
            g.DrawImage(dark, gap, gap);
            g.DrawImage(light, dark.Width + gap * 2, gap);

            var y = Math.Max(dark.Height, light.Height) + gap * 2;
            for (var row = 0; row < 2; row++)
            {
                var darkTaskbar = row == 0;
                using (var bar = new SolidBrush(darkTaskbar ? Color.FromArgb(0x1C, 0x1C, 0x1C) : Color.FromArgb(0xEE, 0xEE, 0xEE)))
                    g.FillRectangle(bar, gap, y - 4, samples.Length * (iconSize + gap) + gap, iconSize + 8);
                for (var i = 0; i < samples.Length; i++)
                {
                    using var icon = TrayIconRenderer.RenderBitmap(iconSize, samples[i].Level, samples[i].Charging, config.LowBatteryThreshold, darkTaskbar);
                    g.DrawImage(icon, gap * 2 + i * (iconSize + gap), y);
                }
                y += iconSize + gap;
            }
        }
        canvas.Save(outputPath, ImageFormat.Png);
        Console.WriteLine($"Vista previa guardada en {outputPath} ({devices.Count} dispositivo(s))");
        return 0;
    }
}
