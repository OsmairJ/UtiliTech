namespace UtiliTech.Tests;

/// <summary>Lógica que saiu das telas grandes (Nome e domínio, Drivers, Visão geral) para poder ser testada.</summary>
public class TelasGrandesTests
{
    const string Dominio = "empresa.local";
    static readonly MachineDomainInfo ForaDoDominio = new("DESKTOP-7Q2K9LM", "WORKGROUP", false);
    static readonly MachineDomainInfo NoDominio = new("PC-SUPORTE-01", "EMPRESA.LOCAL", true);

    // ---------- Nome e domínio ----------

    [Fact]
    public void Tela_sem_dominio_informado()
    {
        var s = DomainPlan.Describe(ForaDoDominio, "", @"PC\tecnico");
        Assert.Equal(2, s.Items.Count);
        Assert.Equal("Grupo de trabalho", s.Items[1].Label);
        Assert.StartsWith("Informe abaixo o domínio", s.Status);
        Assert.Equal("", s.CredHint);
    }

    [Fact]
    public void Tela_fora_e_dentro_do_dominio()
    {
        var fora = DomainPlan.Describe(ForaDoDominio, Dominio, @"DESKTOP\tecnico");
        Assert.Contains("não está no empresa.local", fora.Status);
        Assert.Equal("Renomear e adicionar ao empresa.local", fora.ApplyText);
        Assert.Contains("ingressar máquinas", fora.CredHint);

        var dentro = DomainPlan.Describe(NoDominio, Dominio, @"EMPRESA\tecnico");
        Assert.Contains("já está no domínio", dentro.Status);
        Assert.Equal("Renomear no domínio", dentro.ApplyText);
        Assert.Equal("Domínio", dentro.Items[1].Label);
    }

    [Fact]
    public void Conferencias_antes_do_AD()
    {
        Assert.Equal(Palette.Bad, DomainPlan.PreCheck("PC 01", ForaDoDominio, Dominio)!.Color);
        var mesmo = DomainPlan.PreCheck("pc-suporte-01", NoDominio, Dominio);
        Assert.Equal("Este já é o nome deste computador.", mesmo!.Title);
        Assert.Null(DomainPlan.PreCheck("DESKTOP-7Q2K9LM", ForaDoDominio, Dominio)); // mesmo nome, mas fora do domínio: pode ingressar
        Assert.Null(DomainPlan.PreCheck("PC-NOVO", NoDominio, Dominio));
    }

    [Fact]
    public void Dica_do_nome()
    {
        Assert.Equal(("", false), DomainPlan.NameHint("  "));
        Assert.Equal(("7 de 15 caracteres", false), DomainPlan.NameHint("PC-NOVO"));
        Assert.True(DomainPlan.NameHint("NOME-COMPRIDO-DEMAIS").IsError);
    }

    [Fact]
    public void Botao_so_libera_nome_conferido()
    {
        Assert.False(DomainPlan.ApplyState("DESKTOP-1", "PC-NOVO", null).Enabled);
        Assert.False(DomainPlan.ApplyState("DESKTOP-1", "PC-NOVO", "PC-OUTRO").Enabled);
        var ok = DomainPlan.ApplyState("DESKTOP-1", " pc-novo ", "PC-NOVO");
        Assert.True(ok.Enabled);
        Assert.Equal("Pronto: DESKTOP-1 → pc-novo. A mudança vale depois de reiniciar.", ok.Hint);
    }

    [Fact]
    public void Confirmacao_de_ingressar_e_de_renomear()
    {
        var ingressar = DomainPlan.Apply(ForaDoDominio, "PC-NOVO", Dominio, "tecnico@empresa.local");
        Assert.Equal("Renomear DESKTOP-7Q2K9LM para PC-NOVO e adicionar ao domínio empresa.local?", ingressar.Title);
        Assert.Equal(3, ingressar.Effects.Count);
        Assert.Contains("entra no domínio", ingressar.Effects[1]);
        Assert.NotNull(ingressar.Warning);
        Assert.Equal("Renomear e ingressar", ingressar.ConfirmText);

        var renomear = DomainPlan.Apply(NoDominio, "PC-NOVO", Dominio, "tecnico@empresa.local");
        Assert.Contains("renomeada no AD", renomear.Effects[1]);
        Assert.Null(renomear.Warning);
        Assert.Equal("Renomear", renomear.ConfirmText);
    }

    [Fact]
    public void Mensagens_do_AD()
    {
        Assert.Equal(Palette.Good, DomainPlan.Free("PC-NOVO", Dominio).Color);
        Assert.Contains("JÁ ESTÁ EM USO", DomainPlan.InUse("PC-NOVO", Dominio).Title);
        Assert.EndsWith("informe usuário e senha no passo 2.", DomainPlan.QueryFailed("Servidor indisponível", false, false).Text);
        Assert.Equal("Servidor indisponível", DomainPlan.QueryFailed("Servidor indisponível", true, false).Text);
        Assert.Equal("✓ Feito: o computador vai se chamar PC-NOVO e já está no domínio empresa.local. Reinicie o PC para concluir.",
            DomainPlan.Done("PC-NOVO", false, Dominio));
    }

    // ---------- Drivers ----------

    static GpuInfo Gpu(GpuVendor v, bool driver, string versao = "31.0.15.3699") =>
        new(v == GpuVendor.Nvidia ? "NVIDIA GeForce GT 730" : "Placa", v, versao, new DateTime(2024, 3, 1), driver);

    [Fact]
    public void Cartao_da_placa()
    {
        var intelSem = GpuDrivers.Card(Gpu(GpuVendor.Intel, false));
        Assert.EndsWith("SEM DRIVER DO FABRICANTE", intelSem.Detail);
        Assert.Equal(Palette.Warn, intelSem.StatusColor);
        Assert.Equal("Instalar assistente da Intel", intelSem.ActionText);

        var amd = GpuDrivers.Card(Gpu(GpuVendor.Amd, true));
        Assert.Equal("AMD · driver 31.0.15.3699 de 01/03/2024", amd.Detail);
        Assert.Equal(GpuDrivers.AmdPage, amd.SitePage);
        Assert.True(amd.ActionVisible);

        var nvidia = GpuDrivers.Card(Gpu(GpuVendor.Nvidia, true));
        Assert.StartsWith("Consultando", nvidia.Status);
        Assert.False(nvidia.ActionVisible);
    }

    [Fact]
    public void NVIDIA_mais_nova_oferece_instalar()
    {
        var card = GpuDrivers.Card(Gpu(GpuVendor.Nvidia, true, "31.0.15.3699")); // = 536.99
        GpuDrivers.ApplyLatest(card, new NvidiaDriver("552.22", "GeForce Game Ready", "16/04/2024", "https://exemplo", 600L << 20));
        Assert.True(card.ActionVisible);
        Assert.Equal("Baixar e instalar 552.22", card.ActionText);
        Assert.StartsWith("Versão nova disponível: 552.22, lançado em 16/04/2024", card.Status);

        var emDia = GpuDrivers.Card(Gpu(GpuVendor.Nvidia, true, "31.0.15.5222")); // = 552.22
        GpuDrivers.ApplyLatest(emDia, new NvidiaDriver("552.22", "GeForce", "", "https://exemplo", 1));
        Assert.False(emDia.ActionVisible);
        Assert.Equal("✓ Driver em dia (o mais novo é o 552.22)", emDia.Status);

        var semDriver = GpuDrivers.Card(Gpu(GpuVendor.Nvidia, false));
        GpuDrivers.ApplyLatest(semDriver, new NvidiaDriver("552.22", "GeForce", "", "https://exemplo", 1));
        Assert.True(semDriver.ActionVisible);
    }

    [Fact]
    public void Resumos_da_tela_de_drivers()
    {
        var agora = new DateTime(2026, 10, 23, 11, 30, 0);
        var lista = new List<ProblemDevice>
        {
            new("Dispositivo desconhecido", "PCI\\1", 28, "", "", ""),
            new("Bluetooth", "USB\\2", 22, "", "", ""), // desativado: não é defeito
        };
        Assert.Equal("1 dispositivo(s) com problema · 1 desativado(s) · verificado às 11:30", DriverManager.ProblemSummary(lista, agora));
        Assert.Equal("Tudo certo no Gerenciador de Dispositivos · verificado às 11:30", DriverManager.ProblemSummary(new List<ProblemDevice>(), agora));

        var r = DriverManager.InstallSummary(new DriverInstallResult(3, 1, true, new()));
        Assert.Equal(("3 instalado(s), 1 com falha · reinicie o PC para concluir", Palette.Warn), r);
        Assert.Equal(Palette.Good, DriverManager.InstallSummary(new DriverInstallResult(2, 0, false, new())).Color);
    }

    // ---------- Visão geral ----------

    [Theory]
    [InlineData("Positivo Informatica SA", "Positivo Master D810", "Positivo Master D810")]
    [InlineData("Dell Inc.", "Latitude 5490", "Dell Inc. Latitude 5490")]
    [InlineData("LENOVO", "ThinkPad E14", "LENOVO ThinkPad E14")]
    [InlineData("HP", "HP ProDesk 400 G4", "HP ProDesk 400 G4")]
    [InlineData("", "", "PC-01")]
    public void Modelo_sem_repetir_a_marca(string fabricante, string modelo, string esperado)
    {
        Assert.Equal(esperado, OverviewText.DisplayModel(fabricante, modelo, "PC-01"));
    }

    [Fact]
    public void Nome_curto_da_CPU()
    {
        Assert.Equal("Intel Core i7-6700 @ 3.40GHz", OverviewText.ShortCpuName("Intel(R) Core(TM) i7-6700 CPU @ 3.40GHz"));
        Assert.Equal("AMD Ryzen 5 5500U with Radeon Graphics", OverviewText.ShortCpuName("AMD Ryzen 5 5500U with Radeon Graphics"));
    }

    [Theory]
    [InlineData("load", 37, 0, 100)]
    [InlineData("power", 20, 0, 50)]
    [InlineData("power", 95, 0, 120)]
    [InlineData("temp", 65, 20, 90)]
    [InlineData("temp", 97, 20, 110)]
    public void Escala_do_grafico(string metrica, double pico, double min, double max)
    {
        Assert.Equal((min, max), OverviewText.ChartRange(metrica, pico));
    }

    [Fact]
    public void Textos_e_disco()
    {
        Assert.Equal("65 °C", OverviewText.Temp(64.6f));
        Assert.Equal("—", OverviewText.Temp(null));
        Assert.Equal("32 %", OverviewText.Pct(32.4f));
        Assert.Equal(25, OverviewText.UsedPercent(200, 150));
        Assert.Equal(0, OverviewText.UsedPercent(0, 0));
    }
}
