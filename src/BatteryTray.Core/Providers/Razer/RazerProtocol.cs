namespace BatteryTray.Core.Providers.Razer;

/// <summary>
/// Protocolo de feature reports de Razer, según OpenRazer (driver/razercommon.h y .c):
///
///   offset  campo
///   0       status            (0 en el pedido; 2 = éxito en la respuesta)
///   1       transaction id    (depende del modelo: 0x1F, 0x3F, 0xFF...)
///   2-3     remaining packets (big endian, 0)
///   4       protocol type     (0)
///   5       data size         (largo útil de los argumentos)
///   6       command class
///   7       command id
///   8-87    argumentos (80 bytes)
///   88      CRC = XOR de los bytes 2..87
///   89      reservado (0)
///
/// En Windows el report va precedido por el Report ID 0, así que el buffer mide 91 bytes.
/// </summary>
internal static class RazerProtocol
{
    public const int ReportLength = 90;
    public const int BufferLength = ReportLength + 1;

    public const byte StatusNew = 0x00;
    public const byte StatusBusy = 0x01;
    public const byte StatusSuccess = 0x02;
    public const byte StatusFailure = 0x03;
    public const byte StatusTimeout = 0x04;
    public const byte StatusNotSupported = 0x05;

    // razer_chroma_misc_get_battery_level / get_charging_status (razerchromacommon.c)
    public const byte ClassMisc = 0x07;
    public const byte CmdBatteryLevel = 0x80;
    public const byte CmdChargingStatus = 0x84;

    public static byte[] BuildRequest(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var buffer = new byte[BufferLength];        // buffer[0] = Report ID 0
        var r = buffer.AsSpan(1);
        r[1] = transactionId;
        r[5] = dataSize;
        r[6] = commandClass;
        r[7] = commandId;
        r[88] = Crc(r);
        return buffer;
    }

    public static byte Crc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (int i = 2; i < 88; i++)
            crc ^= report[i];
        return crc;
    }

    public static RazerResponse Parse(byte[] buffer)
    {
        var r = buffer.AsSpan(1);
        return new RazerResponse(
            Status: r[0],
            TransactionId: r[1],
            RemainingPackets: (ushort)((r[2] << 8) | r[3]),
            CommandClass: r[6],
            CommandId: r[7],
            Arguments: r.Slice(8, 80).ToArray());
    }

    /// <summary>Nivel 0-255 de OpenRazer a porcentaje.</summary>
    public static int ToPercent(byte raw) => (int)Math.Round(raw * 100.0 / 255.0);
}

internal sealed record RazerResponse(byte Status, byte TransactionId, ushort RemainingPackets,
    byte CommandClass, byte CommandId, byte[] Arguments)
{
    public bool Matches(byte commandClass, byte commandId) =>
        RemainingPackets == 0 && CommandClass == commandClass && CommandId == commandId;

    /// <summary>OpenRazer trata "busy" como éxito: algunos modelos responden así aunque el comando funcionó.</summary>
    public bool IsSuccess => Status is RazerProtocol.StatusSuccess or RazerProtocol.StatusBusy;
}
