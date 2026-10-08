namespace BatteryTray.Core.Providers.Keychron;

/// <summary>Respuesta al comando de batería 0xCA del firmware "sunset" del K8 HE.</summary>
internal sealed record KeychronBatteryStatus(
    int Percent,
    bool Charging,
    bool Full,
    bool UsbPower,
    bool Measured,
    int Millivolts,
    byte Transport);

/// <summary>Teclado que está del otro lado del receptor.</summary>
internal sealed record KeychronIdentity(int Vid, int Pid);

/// <summary>
/// Protocolo raw HID (interfaz de uso 0xFF60:0x61, reports de 32 bytes) del firmware propio del
/// K8 HE. Ver keyboards/keychron/k8_he/ansi/keymaps/sunset/battery_hid.h en la rama sunset-battery.
///
///   pedido:    [0xCA]
///   respuesta: [0xCA, versión, %, flags, mV lo, mV hi, transporte]
///   flags: bit0 cargando, bit1 carga completa, bit2 USB con energía, bit3 medición vigente
/// </summary>
internal static class KeychronBatteryProtocol
{
    public const byte CmdBattery = 0xCA;   // 0xB0-0xBF los intercepta el Keychron Link
    public const byte CmdViaProtocolVersion = 0x01;
    public const int ReportLength = 32;

    public const byte TransportUsb = 0x01;
    public const byte TransportBluetooth = 0x02;
    public const byte TransportP2P4 = 0x04;

    /// <summary>Pedido listo para HidStream.Write: byte 0 = Report ID 0.</summary>
    public static byte[] Request(byte command)
    {
        var buffer = new byte[ReportLength + 1];
        buffer[1] = command;
        return buffer;
    }

    /// <param name="report">Report recibido sin el Report ID (32 bytes).</param>
    public static KeychronBatteryStatus? ParseBattery(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[0] != CmdBattery || report[1] != 1) return null;
        var flags = report[3];
        return new KeychronBatteryStatus(
            Percent: Math.Clamp((int)report[2], 0, 100),
            Charging: (flags & 0x01) != 0,
            Full: (flags & 0x02) != 0,
            UsbPower: (flags & 0x04) != 0,
            Measured: (flags & 0x08) != 0,
            Millivolts: report[4] | (report[5] << 8),
            Transport: report[6]);
    }

    /// <summary>
    /// Por vía inalámbrica, el firmware de Keychron agrega VID, PID y versión a la respuesta del
    /// comando VIA 0x01 (bytes 3-8, big endian). Por cable esos bytes vienen en cero.
    /// </summary>
    public static KeychronIdentity? ParseWirelessIdentity(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[0] != CmdViaProtocolVersion) return null;
        var vid = (report[3] << 8) | report[4];
        var pid = (report[5] << 8) | report[6];
        return vid == 0 ? null : new KeychronIdentity(vid, pid);
    }
}
