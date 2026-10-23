namespace UtiliTech.Tests;

/// <summary>Avaliação da tela Segurança a partir do que foi lido do PC.</summary>
public class SegurancaTests
{
    static readonly DateTime Agora = new(2026, 10, 23, 10, 0, 0);

    /// <summary>PC em ordem.</summary>
    static SecurityFacts Seguro() => new()
    {
        DefenderEnabled = true,
        RealTime = true,
        SignaturesUpdated = Agora.AddHours(-12),
        LastQuickScan = Agora.AddDays(-1),
        Antivirus = new() { new("Windows Defender", true, true) },
        Firewall = new() { ["Domain"] = true, ["Private"] = true, ["Public"] = true },
        BitLocker = new() { new("C:", 1, 1, 100) },
        Uefi = true,
        SecureBoot = true,
        TpmPresent = true,
        TpmReady = true,
        TpmVersion = "2.0",
        Uac = true,
        LastUpdateInstalled = Agora.AddDays(-3),
    };

    static SecurityItem Item(SecurityFacts f, string key) => SecurityCheck.Evaluate(f, Agora).Single(i => i.Key == key);

    [Fact]
    public void PC_seguro_fica_tudo_OK()
    {
        var itens = SecurityCheck.Evaluate(Seguro(), Agora);
        Assert.Equal(new[] { "av", "fw", "bl", "sb", "tpm", "uac", "wu" }, itens.Select(i => i.Key));
        Assert.All(itens, i => Assert.Equal(SecState.Ok, i.State));
    }

    [Fact]
    public void Assinaturas_velhas_pedem_atualizacao()
    {
        var i = Item(Seguro() with { SignaturesUpdated = Agora.AddDays(-10) }, "av");
        Assert.Equal(SecState.Warning, i.State);
        Assert.Contains("10 dias", i.Advice);
    }

    [Fact]
    public void Tempo_real_desligado_e_problema()
    {
        Assert.Equal(SecState.Bad, Item(Seguro() with { RealTime = false }, "av").State);
    }

    [Fact]
    public void Outro_antivirus_ligado_vale()
    {
        var f = Seguro() with { DefenderEnabled = false, RealTime = false, Antivirus = new() { new("Windows Defender", false, true), new("Antivírus Corporativo", true, true) } };
        var i = Item(f, "av");
        Assert.Equal(SecState.Ok, i.State);
        Assert.StartsWith("Antivírus Corporativo ativo", i.Detail);
    }

    [Fact]
    public void Sem_antivirus_nenhum_e_problema()
    {
        var i = Item(Seguro() with { DefenderEnabled = false, RealTime = false, Antivirus = new() { new("Windows Defender", false, false) } }, "av");
        Assert.Equal(SecState.Bad, i.State);
    }

    [Fact]
    public void Firewall_desligado_em_um_perfil()
    {
        var i = Item(Seguro() with { Firewall = new() { ["Domain"] = true, ["Private"] = false, ["Public"] = true } }, "fw");
        Assert.Equal(SecState.Bad, i.State);
        Assert.Equal("Desligado no perfil Particular", i.Detail);
    }

    [Theory]
    [InlineData(1, 1, SecState.Ok, "C: protegida")]
    [InlineData(0, 2, SecState.Warning, "C: criptografando (40%)")]
    [InlineData(0, 1, SecState.Warning, "C: criptografada, proteção suspensa")]
    [InlineData(0, 0, SecState.Warning, "C: sem BitLocker")]
    public void BitLocker(int protecao, int conversao, SecState esperado, string texto)
    {
        var i = Item(Seguro() with { BitLocker = new() { new("C:", protecao, conversao, 40) } }, "bl");
        Assert.Equal(esperado, i.State);
        Assert.Equal(texto, i.Detail);
    }

    [Fact]
    public void Secure_Boot_desligado_e_Legacy()
    {
        Assert.Equal("UEFI, mas com o Secure Boot desligado", Item(Seguro() with { SecureBoot = false }, "sb").Detail);
        var legacy = Item(Seguro() with { Uefi = false, SecureBoot = null }, "sb");
        Assert.Equal(SecState.Warning, legacy.State);
        Assert.Contains("mbr2gpt", legacy.Advice);
    }

    [Fact]
    public void TPM()
    {
        Assert.Equal(SecState.Warning, Item(Seguro() with { TpmPresent = false, TpmReady = null }, "tpm").State);
        Assert.Contains("1.2", Item(Seguro() with { TpmVersion = "1.2" }, "tpm").Advice);
        Assert.Equal(SecState.Warning, Item(Seguro() with { TpmReady = false }, "tpm").State);
    }

    [Fact]
    public void UAC_desligado_e_atualizacao_antiga()
    {
        Assert.Equal(SecState.Bad, Item(Seguro() with { Uac = false }, "uac").State);
        var wu = Item(Seguro() with { LastUpdateInstalled = Agora.AddDays(-60) }, "wu");
        Assert.Equal(SecState.Warning, wu.State);
        Assert.Contains("há 60 dias", wu.Detail);
    }

    [Fact]
    public void Sem_leitura_fica_nao_verificado()
    {
        var itens = SecurityCheck.Evaluate(new SecurityFacts(), Agora);
        Assert.All(itens, i => Assert.Equal(SecState.Unknown, i.State));
    }

    [Theory]
    [InlineData(397568u, true, true)]   // Defender ligado e atualizado (0x61100)
    [InlineData(393472u, false, true)]  // 0x60100: desligado
    [InlineData(397584u, true, false)]  // 0x61110: desatualizado
    public void Estado_do_antivirus(uint estado, bool ligado, bool atualizado)
    {
        var a = SecurityCheck.DecodeProductState("AV", estado);
        Assert.Equal(ligado, a.Enabled);
        Assert.Equal(atualizado, a.UpToDate);
    }

    [Theory]
    [InlineData("10/22/2026", 2026, 10, 22)]
    [InlineData("20261022", 2026, 10, 22)]
    [InlineData("1/5/2026", 2026, 1, 5)]
    public void Data_das_atualizacoes(string texto, int ano, int mes, int dia)
    {
        Assert.Equal(new DateTime(ano, mes, dia), SecurityCheck.ParseInstalledOn(texto));
    }

    [Fact]
    public void Data_invalida_vira_null()
    {
        Assert.Null(SecurityCheck.ParseInstalledOn(""));
        Assert.Null(SecurityCheck.ParseInstalledOn("ontem"));
    }
}
