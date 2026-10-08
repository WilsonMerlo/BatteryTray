namespace BatteryTray.Core.Providers.Flydigi;

/// <param name="Percent">0-100, o null si el mando no informa el nivel.</param>
internal sealed record FlydigiInfo(int? Percent, ChargingState Charging, byte RawBattery);

/// <summary>
/// Protocolo "V2" de los mandos Flydigi (Vader 5 Pro, Apex 5/6), tomado del driver de SDL
/// (src/joystick/hidapi/SDL_hidapi_flydigi.c, licencia zlib). Solo se usa GET_INFO, que es de
/// lectura: no se "adquiere" el mando, así que no interfiere con Space Station ni con los juegos.
///
///   pedido:    [report] 5A A5 01 02
///   respuesta: [report] 5A A5 01 ... ; byte 11 (contando desde 5A) = batería:
///              nibble alto = estado (0 a batería, 1 cargando, 2 cargado), nibble bajo = nivel
///
/// El Vader 5 Pro y el Apex 6 usan reports sin número (report ID 0); el Apex 5 usa el ID 0x03.
/// </summary>
internal static class FlydigiProtocol
{
    public const int VendorId = 0x37D7;
    public const ushort VendorUsagePage = 0xFFA0;
    const byte Magic1 = 0x5A;
    const byte Magic2 = 0xA5;
    const byte CmdGetInfo = 0x01;
    const byte NumberedReportId = 0x03;

    /// <param name="Scale">Porcentaje por cada paso del nivel (el Vader informa 0-5, el Apex 6 0-10).</param>
    internal sealed record Model(string Name, bool UnnumberedReports, int Scale);

    static readonly Dictionary<int, Model> Models = new()
    {
        [0x2401] = new("Flydigi Vader 5 Pro", true, 20),
        [0x2501] = new("Flydigi Apex 5", false, 20),
        [0x2502] = new("Flydigi Apex 6", true, 10),
    };

    /// <summary>Modelos desconocidos de Flydigi: se asume lo mismo que el Vader 5 Pro.</summary>
    public static Model Find(int pid) =>
        Models.TryGetValue(pid, out var m) ? m : new Model($"Mando Flydigi 0x{pid:X4}", true, 20);

    /// <param name="outputLength">Largo del output report del dispositivo (incluye el report ID).</param>
    public static byte[] InfoRequest(Model model, int outputLength)
    {
        var buffer = new byte[Math.Max(outputLength, 6)];
        buffer[0] = model.UnnumberedReports ? (byte)0 : NumberedReportId;
        buffer[1] = Magic1;
        buffer[2] = Magic2;
        buffer[3] = CmdGetInfo;
        buffer[4] = 2;
        return buffer;
    }

    /// <param name="report">Input report tal como llega, con o sin report ID adelante.</param>
    public static FlydigiInfo? ParseInfo(ReadOnlySpan<byte> report, Model model)
    {
        if (report.Length > 0 && report[0] != Magic1) report = report[1..];   // sacar el report ID
        if (report.Length < 31 || report[0] != Magic1 || report[1] != Magic2 || report[2] != CmdGetInfo) return null;

        var raw = report[11];
        var status = raw >> 4;
        var level = raw & 0x0F;
        return status switch
        {
            0 => new FlydigiInfo(Math.Min(100, level * model.Scale), ChargingState.NotCharging, raw),
            1 => new FlydigiInfo(Math.Min(100, level * model.Scale), ChargingState.Charging, raw),
            2 => new FlydigiInfo(100, ChargingState.NotCharging, raw),
            _ => new FlydigiInfo(null, ChargingState.Unknown, raw),
        };
    }
}
