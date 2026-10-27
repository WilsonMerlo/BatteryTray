using BatteryTray.Core.Providers.Razer;

namespace BatteryTray.Core.Tests;

public class RazerHeadsetProtocolTests
{
    /// <summary>Reports reales del receptor del BlackShark V2 Pro Xbox (1532:0A49), primeros 24 bytes.</summary>
    static byte[] Report(string hex)
    {
        var r = new byte[RazerHeadsetProtocol.ReportLength];
        Convert.FromHexString(hex).CopyTo(r, 0);
        return r;
    }

    const string BatteryReply = "020E504908D5EEB34E0104002101012004000000A8100020";   // 32 %
    const string ChargingReply = "020E504908D680B44E0104002A0101000000000000000000";  // sin cargar
    const string Unsolicited = "020D504901C0E56E6F0103000E800020010000002FA30000";

    [Fact]
    public void Pedido_de_bateria()
    {
        var req = RazerHeadsetProtocol.QueryRequest(RazerHeadsetProtocol.CmdBattery);
        Assert.Equal(64, req.Length);
        Assert.Equal(Convert.FromHexString("028008000050410800032100"), req[..12]);
    }

    [Fact]
    public void Pedido_de_remoto()
    {
        Assert.Equal(Convert.FromHexString("028007000050410E0002E101"), RazerHeadsetProtocol.RemoteRequest(true)[..12]);
        Assert.Equal(0, RazerHeadsetProtocol.RemoteRequest(false)[11]);
    }

    [Fact]
    public void Decodifica_respuestas_reales()
    {
        Assert.Equal((byte)32, RazerHeadsetProtocol.ParseReply(Report(BatteryReply), RazerHeadsetProtocol.CmdBattery));
        Assert.Equal((byte)0, RazerHeadsetProtocol.ParseReply(Report(ChargingReply), RazerHeadsetProtocol.CmdCharging));
    }

    [Fact]
    public void Ignora_reports_que_no_son_la_respuesta()
    {
        Assert.Null(RazerHeadsetProtocol.ParseReply(Report(Unsolicited), RazerHeadsetProtocol.CmdBattery));
        Assert.Null(RazerHeadsetProtocol.ParseReply(Report(ChargingReply), RazerHeadsetProtocol.CmdBattery));
    }
}
