using BatteryTray.Core.Config;
using BatteryTray.Core.Providers;
using BatteryTray.Core.Providers.Apple;
using BatteryTray.Core.Providers.Flydigi;
using BatteryTray.Core.Providers.Keychron;
using BatteryTray.Core.Providers.Razer;

namespace BatteryTray.Core;

public static class ProviderFactory
{
    public static IReadOnlyList<IBatteryProvider> Create(AppConfig config)
    {
        var p = config.Providers;
        var list = new List<IBatteryProvider>();
        if (p.Razer.Enabled) list.Add(new RazerHidProvider(p.Razer));
        if (p.Apple.Enabled) list.Add(new AppleBleProvider(p.Apple));
        if (p.Keychron.Enabled) list.Add(new KeychronHidProvider());
        if (p.Flydigi.Enabled) list.Add(new FlydigiHidProvider());
        if (p.GenericHid.Enabled) list.Add(new GenericHidBatteryProvider());
        if (p.GameControllers.Enabled) list.Add(new GameControllerProvider());
        if (p.WindowsBluetooth.Enabled) list.Add(new WindowsBluetoothProvider());
        return list;
    }
}
