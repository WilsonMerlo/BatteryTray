using BatteryTray.Core.Providers.Keychron;

namespace BatteryTray.Core.Tests;

public class KeychronBatteryProtocolTests
{
    static byte[] Report(params byte[] head)
    {
        var r = new byte[32];
        head.CopyTo(r, 0);
        return r;
    }

    [Fact]
    public void Pedido_lleva_report_id_y_comando()
    {
        var req = KeychronBatteryProtocol.Request(KeychronBatteryProtocol.CmdBattery);
        Assert.Equal(33, req.Length);
        Assert.Equal(0x00, req[0]);
        Assert.Equal(0xCA, req[1]);
    }

    [Fact]
    public void Parsea_bateria_inalambrica_medida()
    {
        // 76 %, flags = medición vigente, 3920 mV (0x0F50), transporte 2.4 GHz
        var s = KeychronBatteryProtocol.ParseBattery(Report(0xCA, 1, 76, 0x08, 0x50, 0x0F, 0x04));

        Assert.NotNull(s);
        Assert.Equal(76, s.Percent);
        Assert.True(s.Measured);
        Assert.False(s.Charging);
        Assert.False(s.UsbPower);
        Assert.Equal(3920, s.Millivolts);
        Assert.Equal(KeychronBatteryProtocol.TransportP2P4, s.Transport);
    }

    [Fact]
    public void Parsea_carga_por_cable()
    {
        // flags = cargando + USB con energía, sin medición vigente
        var s = KeychronBatteryProtocol.ParseBattery(Report(0xCA, 1, 100, 0x05, 0x68, 0x10, 0x01))!;

        Assert.True(s.Charging);
        Assert.True(s.UsbPower);
        Assert.False(s.Measured);
        Assert.False(s.Full);
    }

    [Theory]
    [InlineData(0xFF)]   // VIA "comando desconocido": firmware de fábrica
    [InlineData(0xA1)]
    public void Ignora_respuestas_de_otros_comandos(byte first)
    {
        Assert.Null(KeychronBatteryProtocol.ParseBattery(Report(first, 1, 50, 0x08)));
    }

    [Fact]
    public void Ignora_version_de_formato_desconocida()
    {
        Assert.Null(KeychronBatteryProtocol.ParseBattery(Report(0xCA, 2, 50, 0x08)));
    }

    [Fact]
    public void Identidad_inalambrica_trae_vid_y_pid()
    {
        // Respuesta de VIA 0x01 por 2.4 GHz: [01, ver hi, ver lo, VID hi, VID lo, PID hi, PID lo, ...]
        var id = KeychronBatteryProtocol.ParseWirelessIdentity(Report(0x01, 0x00, 0x0C, 0x34, 0x34, 0x0E, 0x80, 0x01, 0x11));
        Assert.Equal(new KeychronIdentity(0x3434, 0x0E80), id);

        // Por cable esos bytes vienen en cero (lo que vimos con el firmware actual).
        Assert.Null(KeychronBatteryProtocol.ParseWirelessIdentity(Report(0x01, 0x00, 0x0C)));
    }
}
