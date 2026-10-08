using BatteryTray.Core.Providers.Apple;

namespace BatteryTray.Core.Tests;

public class ProximityPairingTests
{
    // Capturados en la fase 0 de los AirPods 4 ANC reales (dentro del estuche abierto, cargando).
    const string PodA = "0719011B2035AAB631000462 08EDBB73630555A6194DBE13222408";
    const string PodB = "0719011B2055AAB6310004 1AF70EAA9EF754622FA0AB943DE59B";

    static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    [Theory]
    [InlineData(PodA)]
    [InlineData(PodB)]
    public void Decodifica_AirPods4Anc_reales(string hex)
    {
        var s = ProximityPairing.TryDecode(Hex(hex));

        Assert.NotNull(s);
        Assert.Equal(0x201B, s.ModelPid);
        Assert.Equal(100, s.Left);
        Assert.Equal(100, s.Right);
        Assert.Equal(60, s.Case);
        Assert.True(s.LeftCharging);
        Assert.True(s.RightCharging);
        Assert.False(s.CaseCharging);
        Assert.Equal("AirPods 4 (ANC)", AppleModels.Find(s.ModelPid).Name);
    }

    // Capturados en la fase 1 con los AirPods afuera del estuche: cada auricular anuncia con el
    // bit de "primario" opuesto (0x21 vs 0x01) y los nibbles invertidos (A9 vs 9A).
    [Theory]
    [InlineData("0719011B2021A98F110004358EAE1BF61ABD79EFC7866ECDAE5E41")]
    [InlineData("0719011B20019A8F1100049C5CA850DC80DBB0927700AFA3AE8A37")]
    public void Ambos_auriculares_reales_coinciden_fuera_del_estuche(string hex)
    {
        var s = ProximityPairing.TryDecode(Hex(hex))!;

        Assert.Equal(90, s.Left);
        Assert.Equal(100, s.Right);
        Assert.Null(s.Case);                    // nibble F: el estuche no informa con los auriculares afuera
        Assert.False(s.LeftCharging);
        Assert.False(s.RightCharging);
    }

    [Fact]
    public void Invierte_izquierda_y_derecha_si_el_primario_es_el_derecho()
    {
        // status 0x01: bit 5 apagado → valores invertidos. Nibble alto 3 (30 %) = izquierdo, bajo 8 (80 %) = derecho.
        // flags 0x1 → con valores invertidos, el bit 0 corresponde al derecho.
        var data = Hex("0719011B2001" + "38" + "15" + "31000400000000000000000000000000000000");
        var s = ProximityPairing.TryDecode(data)!;

        Assert.Equal(30, s.Left);
        Assert.Equal(80, s.Right);
        Assert.False(s.LeftCharging);
        Assert.True(s.RightCharging);
        Assert.Equal(50, s.Case);
    }

    [Fact]
    public void Nibble_15_es_desconocido()
    {
        var data = Hex("0719011B2020" + "F5" + "0F" + "31000400000000000000000000000000000000");
        var s = ProximityPairing.TryDecode(data)!;

        Assert.Equal(50, s.Left);
        Assert.Null(s.Right);
        Assert.Null(s.Case);
    }

    [Theory]
    [InlineData("1219011B2035AAB6310004")]   // otro tipo de mensaje
    [InlineData("0719071B2035AAB6310004")]   // prefijo distinto de 0x01
    [InlineData("0719011B20")]               // demasiado corto
    public void Rechaza_mensajes_que_no_son_de_estado(string hex)
    {
        Assert.Null(ProximityPairing.TryDecode(Hex(hex)));
    }
}
