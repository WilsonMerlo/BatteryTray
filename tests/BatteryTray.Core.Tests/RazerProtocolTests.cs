using BatteryTray.Core.Providers.Razer;

namespace BatteryTray.Core.Tests;

public class RazerProtocolTests
{
    [Fact]
    public void Pedido_de_bateria_tiene_el_formato_de_OpenRazer()
    {
        var buffer = RazerProtocol.BuildRequest(0x1F, RazerProtocol.ClassMisc, RazerProtocol.CmdBatteryLevel, 0x02);

        Assert.Equal(91, buffer.Length);
        Assert.Equal(0x00, buffer[0]);          // Report ID
        var r = buffer.AsSpan(1);
        Assert.Equal(0x00, r[0]);               // status
        Assert.Equal(0x1F, r[1]);               // transaction id
        Assert.Equal(0x02, r[5]);               // data size
        Assert.Equal(0x07, r[6]);               // command class
        Assert.Equal(0x80, r[7]);               // command id
        // CRC = 0x02 ^ 0x07 ^ 0x80 (el resto son ceros; el transaction id no entra en el CRC)
        Assert.Equal(0x02 ^ 0x07 ^ 0x80, r[88]);
        Assert.Equal(0x00, r[89]);
    }

    [Fact]
    public void Parsea_respuesta_y_convierte_nivel()
    {
        var buffer = new byte[91];
        var r = buffer.AsSpan(1);
        r[0] = RazerProtocol.StatusSuccess;
        r[1] = 0x1F;
        r[5] = 0x02;
        r[6] = 0x07;
        r[7] = 0x80;
        r[9] = 0xFF;                            // arguments[1]

        var resp = RazerProtocol.Parse(buffer);

        Assert.True(resp.IsSuccess);
        Assert.True(resp.Matches(0x07, 0x80));
        Assert.False(resp.Matches(0x07, 0x84));
        Assert.Equal(100, RazerProtocol.ToPercent(resp.Arguments[1]));
        Assert.Equal(50, RazerProtocol.ToPercent(128));
    }

    [Fact]
    public void Tabla_incluye_el_Viper_V3_Pro_SE()
    {
        Assert.Equal(0x1F, RazerDeviceTable.Known[0x00DF].TransactionId);
        Assert.Equal(0x1F, RazerDeviceTable.Known[0x00DE].TransactionId);
        Assert.False(RazerDeviceTable.Known[0x0062].HasCharging);
    }
}
