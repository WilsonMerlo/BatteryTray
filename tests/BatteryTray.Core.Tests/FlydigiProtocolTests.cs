using BatteryTray.Core.Providers.Flydigi;

namespace BatteryTray.Core.Tests;

public class FlydigiProtocolTests
{
    static readonly FlydigiProtocol.Model Vader = FlydigiProtocol.Find(0x2401);

    /// <summary>Respuesta GET_INFO armada según SDL: 5A A5 01 ..., batería en el byte 11.</summary>
    static byte[] InfoReply(byte battery, bool withReportId)
    {
        var r = new byte[32];
        r[0] = 0x5A; r[1] = 0xA5; r[2] = 0x01; r[11] = battery;
        return withReportId ? [0x00, .. r] : r;
    }

    [Fact]
    public void Pedido_del_Vader_usa_report_sin_numero()
    {
        var req = FlydigiProtocol.InfoRequest(Vader, 33);
        Assert.Equal(33, req.Length);
        Assert.Equal(new byte[] { 0x00, 0x5A, 0xA5, 0x01, 0x02 }, req[..5]);
    }

    [Fact]
    public void Pedido_del_Apex5_usa_report_id_3()
    {
        var req = FlydigiProtocol.InfoRequest(FlydigiProtocol.Find(0x2501), 33);
        Assert.Equal(0x03, req[0]);
    }

    [Theory]
    [InlineData(0x04, 80, ChargingState.NotCharging)]   // a batería, nivel 4 de 5
    [InlineData(0x12, 40, ChargingState.Charging)]      // cargando, nivel 2
    [InlineData(0x20, 100, ChargingState.NotCharging)]  // carga completa
    public void Decodifica_bateria(byte raw, int percent, ChargingState charging)
    {
        foreach (var withId in new[] { false, true })
        {
            var info = FlydigiProtocol.ParseInfo(InfoReply(raw, withId), Vader)!;
            Assert.Equal(percent, info.Percent);
            Assert.Equal(charging, info.Charging);
        }
    }

    [Fact]
    public void Apex6_usa_pasos_de_10()
    {
        var info = FlydigiProtocol.ParseInfo(InfoReply(0x07, false), FlydigiProtocol.Find(0x2502))!;
        Assert.Equal(70, info.Percent);
    }

    [Fact]
    public void Ignora_reports_de_estado_y_otros_comandos()
    {
        var state = new byte[32];
        state[0] = 0x5A; state[1] = 0xA5; state[2] = 0xEF;   // report de entrada del mando
        Assert.Null(FlydigiProtocol.ParseInfo(state, Vader));
        Assert.Null(FlydigiProtocol.ParseInfo(new byte[10], Vader));
    }
}
