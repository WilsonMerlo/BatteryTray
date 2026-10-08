using Windows.Gaming.Input;

namespace BatteryTray.Diag;

/// <summary>
/// Mandos vistos por Windows.Gaming.Input y su BatteryReport.
/// La lista se llena de forma asíncrona después del primer acceso, por eso hay un WarmUp
/// que se llama al principio del programa.
/// </summary>
static class GamingSection
{
    public static void WarmUp()
    {
        _ = RawGameController.RawGameControllers;
        _ = Gamepad.Gamepads;
    }

    public static async Task RunAsync(Output o)
    {
        o.Section("4. MANDOS (Windows.Gaming.Input)");

        // Margen para que termine la enumeración inicial.
        await Task.Delay(TimeSpan.FromSeconds(2));

        var raws = RawGameController.RawGameControllers;
        var gamepads = Gamepad.Gamepads;
        o.Line($"RawGameControllers: {raws.Count}   Gamepads (Xbox/XInput): {gamepads.Count}");

        foreach (var rc in raws)
        {
            var isGamepad = gamepads.Any(gp => RawGameController.FromGameController(gp)?.NonRoamableId == rc.NonRoamableId);
            o.Line();
            o.Line($"{KnownVendors.Mark(rc.HardwareVendorId)} \"{rc.DisplayName}\"  VID 0x{rc.HardwareVendorId:X4}  PID 0x{rc.HardwareProductId:X4}  " +
                   $"[{KnownVendors.Name(rc.HardwareVendorId)}]  inalámbrico={rc.IsWireless}  esGamepad={isGamepad}");
            o.Line($"    botones={rc.ButtonCount} ejes={rc.AxisCount} switches={rc.SwitchCount}");
            o.Line($"    batería: {Describe(rc.TryGetBatteryReport())}");
            o.Line($"    id: {rc.NonRoamableId}");
        }
    }

    static string Describe(Windows.Devices.Power.BatteryReport? r)
    {
        if (r is null) return "sin reporte";
        var pct = r.RemainingCapacityInMilliwattHours is int rem && r.FullChargeCapacityInMilliwattHours is int full && full > 0
            ? $"{rem * 100.0 / full:0}%"
            : "?%";
        return $"{pct}  estado={r.Status}  restante={r.RemainingCapacityInMilliwattHours?.ToString() ?? "?"} " +
               $"lleno={r.FullChargeCapacityInMilliwattHours?.ToString() ?? "?"} diseño={r.DesignCapacityInMilliwattHours?.ToString() ?? "?"} " +
               $"tasa={r.ChargeRateInMilliwatts?.ToString() ?? "?"} (mWh/mW)";
    }
}
