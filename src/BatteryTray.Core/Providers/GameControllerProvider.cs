using Windows.Gaming.Input;

namespace BatteryTray.Core.Providers;

/// <summary>
/// Cualquier mando que Windows reconozca (Xbox por adaptador inalámbrico, Bluetooth o USB, y otros
/// compatibles) y que informe batería por Windows.Gaming.Input.
///
/// Ojo: los mandos de Xbox informan la batería en pasos gruesos (vacío/bajo/medio/lleno),
/// así que vas a ver valores como 70 % y no el número exacto.
/// </summary>
public sealed class GameControllerProvider : IBatteryProvider
{
    public string Name => "Mandos (Windows.Gaming.Input)";

    /// <summary>La lista de mandos se llena en segundo plano después del primer acceso.</summary>
    public void Start()
    {
        _ = RawGameController.RawGameControllers;
        _ = Gamepad.Gamepads;
    }

    public Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct)
    {
        var readings = new List<BatteryReading>();
        foreach (var rc in RawGameController.RawGameControllers)
        {
            var report = rc.TryGetBatteryReport();
            // Con cable o sin batería: Windows devuelve NotPresent (o nada).
            if (report is null || report.Status == Windows.System.Power.BatteryStatus.NotPresent) continue;

            int? level = report.RemainingCapacityInMilliwattHours is int rem && report.FullChargeCapacityInMilliwattHours is int full && full > 0
                ? (int)Math.Round(Math.Clamp(rem * 100.0 / full, 0, 100))
                : null;

            readings.Add(new BatteryReading
            {
                Id = $"pad:{rc.HardwareVendorId:X4}:{rc.HardwareProductId:X4}:{rc.NonRoamableId}",
                Name = FriendlyName(rc),
                Source = Name,
                Kind = DeviceKind.Controller,
                Level = level,
                Charging = report.Status switch
                {
                    Windows.System.Power.BatteryStatus.Charging => ChargingState.Charging,
                    Windows.System.Power.BatteryStatus.Discharging or Windows.System.Power.BatteryStatus.Idle => ChargingState.NotCharging,
                    _ => ChargingState.Unknown,
                },
            });
        }
        return Task.FromResult<IReadOnlyList<BatteryReading>>(readings);
    }

    static string FriendlyName(RawGameController rc) =>
        rc.HardwareVendorId == 0x045E && rc.DisplayName.Contains("Xbox", StringComparison.OrdinalIgnoreCase)
            ? "Mando Xbox"
            : rc.DisplayName;

    public void Dispose() { }
}
