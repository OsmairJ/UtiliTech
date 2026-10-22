namespace UtiliTech.Tests;

/// <summary>Ferramenta de rede: leitura do Wi-Fi, diagnóstico do teste de conexão e validações.</summary>
public class RedeTests
{
    const string WlanPt = """

        Há 1 interface no sistema:

            Nome                   : Wi-Fi
            Descrição            : Intel(R) Dual Band Wireless-AC 8260
            GUID                   : 9fdc7504-90d7-46f3-b36b-4f3a28f8d121
            Endereço físico       : 14:ab:c5:5d:68:3a
            Estado                  : conectado
            SSID                   : Escritorio-5G
            BSSID                  : aa:bb:cc:dd:ee:ff
            Tipo de rede           : Infraestrutura
            Banda                  : 5 GHz
            Canal                  : 44
            Taxa de recepção (Mbps)  : 866.7
            Taxa de transmissão (Mbps) : 866.7
            Sinal                  : 35%
        """;

    const string WlanEn = """
            Name                   : Wi-Fi
            State                  : connected
            SSID                   : Office
            BSSID                  : aa:bb:cc:dd:ee:ff
            Band                   : 2.4 GHz
            Channel                : 6
            Receive rate (Mbps)    : 144.4
            Signal                 : 88%
        """;

    const string WlanDesconectado = """
            Nome                   : Wi-Fi
            Estado                  : Desconectado
            Status de rádio           : hardware Ativado
        """;

    [Fact]
    public void Le_o_WiFi_em_portugues()
    {
        var w = NetworkTools.ParseWlan(WlanPt);
        Assert.NotNull(w);
        Assert.Equal("Escritorio-5G", w.Ssid); // não confunde com o BSSID
        Assert.Equal(35, w.Signal);
        Assert.Equal("44", w.Channel);
        Assert.Equal("866.7", w.Rate);
        Assert.Equal("5 GHz", w.Band);
    }

    [Fact]
    public void Le_o_WiFi_em_ingles()
    {
        var w = NetworkTools.ParseWlan(WlanEn);
        Assert.Equal("Office", w!.Ssid);
        Assert.Equal(88, w.Signal);
    }

    [Fact]
    public void WiFi_desconectado_vira_null()
    {
        Assert.Null(NetworkTools.ParseWlan(WlanDesconectado));
        Assert.Null(NetworkTools.ParseWlan(""));
    }

    static AdapterInfo Placa(string ip = "10.0.0.15/24", long velocidade = 1_000_000_000, bool wifi = false, bool gateway = true) =>
        new("Ethernet", "Intel Ethernet", wifi, velocidade, "00-11-22-33-44-55", new() { ip },
            gateway ? new() { "10.0.0.1" } : new(), new() { "10.0.0.2" }, true);

    static List<NetTestStep> Etapas(params NetStepState[] estados)
    {
        string[] nomes = { "Placa de rede com IP", "Gateway (roteador)", "DNS (nomes de sites)", "Internet" };
        return estados.Select((e, i) => new NetTestStep(nomes[i], e, "")).ToList();
    }

    const NetStepState Ok = NetStepState.Ok, Falha = NetStepState.Fail, Pulou = NetStepState.Skipped;

    [Fact]
    public void Tudo_funcionando()
    {
        Assert.StartsWith("Conexão funcionando", NetworkTools.Diagnose(Etapas(Ok, Ok, Ok, Ok), new() { Placa() }));
    }

    [Fact]
    public void Sem_placa_conectada()
    {
        Assert.Contains("confira o cabo", NetworkTools.Diagnose(Etapas(Falha, Pulou, Pulou, Pulou), new()));
    }

    [Fact]
    public void IP_automatico_do_Windows_e_problema_de_DHCP()
    {
        var placa = Placa("169.254.10.20/16");
        Assert.True(placa.HasApipa);
        Assert.Contains("não recebeu IP do roteador (DHCP)", NetworkTools.Diagnose(Etapas(Falha, Pulou, Pulou, Pulou), new() { placa }));
    }

    [Fact]
    public void Roteador_e_DNS_mudos_e_rede_local()
    {
        Assert.Contains("rede local", NetworkTools.Diagnose(Etapas(Ok, Falha, Falha, Pulou), new() { Placa() }));
    }

    [Fact]
    public void So_o_DNS_falhando()
    {
        Assert.Contains("Limpar cache DNS", NetworkTools.Diagnose(Etapas(Ok, Ok, Falha, Pulou), new() { Placa() }));
    }

    [Fact]
    public void Internet_bloqueada_aponta_proxy()
    {
        Assert.Contains("proxy", NetworkTools.Diagnose(Etapas(Ok, Ok, Ok, Falha), new() { Placa() }));
    }

    [Fact]
    public void Roteador_que_nao_responde_ping_nao_e_problema()
    {
        Assert.Contains("só não responde ping", NetworkTools.Diagnose(Etapas(Ok, Falha, Ok, Ok), new() { Placa() }));
    }

    [Fact]
    public void Cabo_a_100_Mbps_e_avisado()
    {
        var lenta = Placa(velocidade: 100_000_000);
        Assert.True(NetworkTools.SlowLink(lenta));
        Assert.Contains("100 Mbps", NetworkTools.Diagnose(Etapas(Ok, Ok, Ok, Ok), new() { lenta }));
        Assert.Contains(lenta.Items, i => i.Label == "Velocidade do link" && i.Value.Contains("confira cabo"));
        Assert.False(NetworkTools.SlowLink(Placa(velocidade: 300_000_000, wifi: true))); // Wi-Fi não conta
    }

    [Theory]
    [InlineData(1_000_000_000, "1 Gbps")]
    [InlineData(2_500_000_000, "2.5 Gbps")]
    [InlineData(100_000_000, "100 Mbps")]
    [InlineData(0, "")]
    public void Velocidade(long bps, string esperado)
    {
        Assert.Equal(esperado.Replace(".", ","), NetworkTools.Speed(bps));
    }

    [Theory]
    [InlineData("192.168.0.1", true)]
    [InlineData("servidor-01", true)]
    [InlineData("www.exemplo.com.br", true)]
    [InlineData("fe80::1", true)]
    [InlineData("", false)]
    [InlineData("host & del c:", false)]   // nada que mude a linha de comando do tracert
    [InlineData("a\"b", false)]
    [InlineData("10.0.0.1 -n 999", false)]
    public void Endereco_digitado(string host, bool valido)
    {
        Assert.Equal(valido, NetworkTools.IsValidHost(host));
    }

    [Fact]
    public void Itens_da_placa()
    {
        var itens = Placa().Items;
        Assert.Equal("10.0.0.15/24 (DHCP)", itens.Single(i => i.Label == "Endereço IP").Value);
        Assert.Equal("1 Gbps", itens.Single(i => i.Label == "Velocidade do link").Value);
        Assert.Equal("00-11-22-33-44-55", NetworkTools.FormatMac(new byte[] { 0, 0x11, 0x22, 0x33, 0x44, 0x55 }));
    }
}
