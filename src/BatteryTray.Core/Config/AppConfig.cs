namespace BatteryTray.Core.Config;

/// <summary>Configuración persistida en %AppData%\BatteryTray\config.json.</summary>
public sealed class AppConfig
{
    /// <summary>Cada cuánto se consultan los dispositivos (mínimo 15 s).</summary>
    public int PollIntervalSeconds { get; set; } = 120;

    /// <summary>Por debajo de este porcentaje se avisa con una notificación.</summary>
    public int LowBatteryThreshold { get; set; } = 20;

    /// <summary>Mostrar notificación de Windows cuando un dispositivo cruza el umbral.</summary>
    public bool Notifications { get; set; } = true;

    /// <summary>Id del dispositivo cuyo nivel muestra el ícono; null = el nivel más bajo.</summary>
    public string? TrayDevice { get; set; }

    /// <summary>Log detallado (para depurar).</summary>
    public bool VerboseLog { get; set; }

    /// <summary>Nombres personalizados: id del dispositivo → nombre a mostrar.</summary>
    public Dictionary<string, string> Aliases { get; set; } = [];

    /// <summary>Ids de dispositivos que no se quieren ver.</summary>
    public List<string> HiddenDevices { get; set; } = [];

    public ProvidersConfig Providers { get; set; } = new();
}

public sealed class ProvidersConfig
{
    public RazerConfig Razer { get; set; } = new();
    public AppleConfig Apple { get; set; } = new();

    /// <summary>Teclados Keychron con el firmware propio que informa la batería (K8 HE "sunset").</summary>
    public ToggleConfig Keychron { get; set; } = new();
    public ToggleConfig GameControllers { get; set; } = new();
    public ToggleConfig WindowsBluetooth { get; set; } = new();
}

public sealed class ToggleConfig
{
    public bool Enabled { get; set; } = true;
}

public sealed class RazerConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Si un dispositivo Razer no está en la tabla de OpenRazer, probar los transaction IDs
    /// conocidos para descubrir si informa batería.
    /// </summary>
    public bool ProbeUnknownDevices { get; set; } = true;

    /// <summary>Modelos que la tabla no conoce, o correcciones a la tabla.</summary>
    public List<RazerDeviceOverride> ExtraDevices { get; set; } = [];
}

public sealed class RazerDeviceOverride
{
    /// <summary>PID en hexadecimal, por ejemplo "0x00DF".</summary>
    public string Pid { get; set; } = "";

    /// <summary>Transaction ID en hexadecimal, por ejemplo "0x1F".</summary>
    public string TransactionId { get; set; } = "0x1F";

    public bool HasCharging { get; set; } = true;

    public int WaitMs { get; set; } = 60;
}

public sealed class AppleConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Mostrar solo los modelos de AirPods/Beats emparejados con esta PC. Si es false,
    /// se muestran todos los que se escuchen cerca (incluidos los de otras personas).
    /// </summary>
    public bool OnlyPairedModels { get; set; } = true;

    /// <summary>Ignorar advertisements más débiles que esto (dBm).</summary>
    public int MinRssi { get; set; } = -90;
}
