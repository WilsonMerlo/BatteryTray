namespace BatteryTray.Core.Providers.Razer;

/// <summary>
/// Protocolo de los receptores 2.4 GHz de los BlackShark V2 Pro (2023 y Xbox), que no usan los
/// feature reports de 90 bytes sino reports de salida/entrada de 64 bytes (report ID 2) en la
/// interfaz de fabricante 0xFF00. Tomado de blackshark-control (AbdullahSAhmad), que a su vez sale
/// del trabajo de OpenRazer (PR #2862) sobre capturas de Synapse. Solo se usan consultas:
///
///   remoto:  [02] 80 07 00 00 'P' 'A' 0E 00 02 E1 on   (habilita el reenvío al headset)
///   pedido:  [02] 80 08 00 00 'P' 'A' 08 00 03 cmd 00 00
///   resp.:   [02] .. byte 12 = cmd, 13 = 01 (ack), 14 = largo, 15.. = datos
///
/// El receptor también manda reports propios (byte 12 = 0x80) que no son respuesta a nada.
/// </summary>
internal static class RazerHeadsetProtocol
{
    public const int ReportLength = 64;
    public const byte ReportId = 0x02;
    public const ushort VendorUsagePage = 0xFF00;

    const byte TypeRemote = 0x02;
    const byte TypeQuery = 0x03;
    const byte CmdRemote = 0xE1;

    public const byte CmdLink = 0x20;
    public const byte CmdBattery = 0x21;
    public const byte CmdCharging = 0x2A;

    /// <summary>Receptores probados o documentados.</summary>
    public static readonly IReadOnlyDictionary<int, string> Models = new Dictionary<int, string>
    {
        [0x0A49] = "BlackShark V2 Pro Xbox",
        [0x0555] = "BlackShark V2 Pro",
    };

    public static byte[] RemoteRequest(bool on)
    {
        var b = Header(length: 0x07, kind: 0x0E);
        b[9] = TypeRemote;
        b[10] = CmdRemote;
        b[11] = on ? (byte)1 : (byte)0;
        return b;
    }

    public static byte[] QueryRequest(byte cmd)
    {
        var b = Header(length: 0x08, kind: 0x08);
        b[9] = TypeQuery;
        b[10] = cmd;
        return b;
    }

    static byte[] Header(byte length, byte kind)
    {
        var b = new byte[ReportLength];
        b[0] = ReportId;
        b[1] = 0x80;
        b[2] = length;
        b[5] = (byte)'P';
        b[6] = (byte)'A';
        b[7] = kind;
        return b;
    }

    /// <summary>Primer byte de datos si el report es la respuesta a <paramref name="cmd"/>.</summary>
    public static byte? ParseReply(ReadOnlySpan<byte> report, byte cmd)
    {
        if (report.Length < 16 || report[0] != ReportId || report[12] != cmd || report[13] != 0x01 || report[14] == 0)
            return null;
        return report[15];
    }
}
