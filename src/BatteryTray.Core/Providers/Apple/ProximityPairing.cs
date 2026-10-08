namespace BatteryTray.Core.Providers.Apple;

/// <summary>Estado decodificado de la parte pública (sin cifrar) de un mensaje proximity pairing.</summary>
internal sealed record ApplePodsStatus(
    ushort ModelPid,
    int? Left, bool LeftCharging,
    int? Right, bool RightCharging,
    int? Case, bool CaseCharging,
    int? Single, bool SingleCharging);

/// <summary>
/// Decodificador del mensaje "proximity pairing" (tipo 0x07) que AirPods/Beats publican en el
/// manufacturer data de Apple (company ID 0x004C). Offsets según CAPod (ApplePods.kt, DualApplePods.kt):
///
///   [0]  0x07  tipo de mensaje
///   [1]  0x19  largo (25)
///   [2]  0x01  prefijo
///   [3-4]      modelo (PID en little endian)
///   [5]        status: bit 5 = el izquierdo es el primario (si NO está, izquierda/derecha van invertidos)
///   [6]        batería de los auriculares: un nibble por lado, 0-10 = 0-100 %, 15 = desconocido
///   [7]        nibble alto = flags de carga (bit0/bit1 auriculares, bit2 estuche), nibble bajo = estuche
///   [8]        tapa, [9] color, [10] sufijo
///   [11-26]    parte cifrada (necesita la clave del dispositivo, no la usamos)
///
/// La parte pública solo tiene resolución de 10 %.
/// </summary>
internal static class ProximityPairing
{
    public const ushort AppleCompanyId = 0x004C;
    public const byte MessageType = 0x07;
    const byte MessageLength = 0x19;
    const byte Prefix = 0x01;

    public static ApplePodsStatus? TryDecode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 11 || data[0] != MessageType || data[1] != MessageLength || data[2] != Prefix)
            return null;

        var pid = (ushort)(data[3] | (data[4] << 8));
        var status = data[5];
        var pods = data[6];
        var flags = data[7] >> 4;
        var caseNibble = data[7] & 0x0F;

        var flipped = (status & 0x20) == 0;
        var leftNibble = flipped ? pods >> 4 : pods & 0x0F;
        var rightNibble = flipped ? pods & 0x0F : pods >> 4;
        var leftCharging = (flags & (flipped ? 0x2 : 0x1)) != 0;
        var rightCharging = (flags & (flipped ? 0x1 : 0x2)) != 0;

        return new ApplePodsStatus(
            pid,
            Nibble(leftNibble), leftCharging,
            Nibble(rightNibble), rightCharging,
            Nibble(caseNibble), (flags & 0x4) != 0,
            Single: Nibble(pods & 0x0F), SingleCharging: (flags & 0x1) != 0);
    }

    static int? Nibble(int value) => value == 15 ? null : Math.Min(value, 10) * 10;
}
