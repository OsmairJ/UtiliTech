namespace UtiliTech.Tests;

/// <summary>Avaliação das etapas do checklist de pós-formatação a partir do que foi lido do PC.</summary>
public class ChecklistTests
{
    static readonly DateTime Agora = new(2026, 10, 22, 16, 0, 0);

    /// <summary>PC pronto para entregar: tudo feito.</summary>
    static ChecklistFacts Pronto() => new()
    {
        Activation = "Ativado",
        ProblemDevices = 0,
        ComputerName = "PC-SUPORTE-01",
        PartOfDomain = true,
        MachineDomain = "empresa.local",
        ConfiguredDomain = "empresa.local",
        LastGroupPolicy = Agora.AddHours(-2),
        WorstDisk = "Bom",
        ReportSavedToday = true,
    };

    static ChecklistStep Etapa(ChecklistFacts f, string titulo) =>
        PostFormatChecklist.Evaluate(f, Agora).Single(s => s.Title == titulo);

    [Fact]
    public void PC_pronto_tem_tudo_feito()
    {
        var etapas = PostFormatChecklist.Evaluate(Pronto(), Agora);
        Assert.Equal(9, etapas.Count);
        Assert.All(etapas, e => Assert.Equal(StepState.Done, e.State));
        Assert.All(etapas, e => Assert.False(e.HasAction));
    }

    [Fact]
    public void Ordem_das_etapas()
    {
        Assert.Equal(new[] { "Windows ativado", "Drivers", "Programas", "Nome do computador", "Domínio", "Políticas de grupo",
                "Reiniciar", "Discos", "Relatório do atendimento" },
            PostFormatChecklist.Evaluate(Pronto(), Agora).Select(e => e.Title));
    }

    [Fact]
    public void Windows_nao_ativado_falta()
    {
        var e = Etapa(Pronto() with { Activation = "Notificação (não ativado)" }, "Windows ativado");
        Assert.Equal(StepState.Pending, e.State);
        Assert.Equal("Notificação (não ativado)", e.Detail);
        Assert.True(e.HasAction);
    }

    [Fact]
    public void Drivers_com_problema_faltam_e_levam_para_a_tela_de_drivers()
    {
        var e = Etapa(Pronto() with { ProblemDevices = 3 }, "Drivers");
        Assert.Equal(StepState.Pending, e.State);
        Assert.Contains("3 dispositivo", e.Detail);
        Assert.Equal("drivers", e.Page);
    }

    [Fact]
    public void Programas_exigidos_sao_Chrome_Firefox_e_Microsoft_365()
    {
        Assert.Equal(new[] { "Google Chrome", "Mozilla Firefox", "Microsoft 365" }, PostFormatChecklist.RequiredApps.Select(a => a.Name));
        var e = Etapa(Pronto(), "Programas");
        Assert.Equal("Google Chrome, Mozilla Firefox e Microsoft 365 instalados", e.Detail);
    }

    [Fact]
    public void Detecta_pelos_nomes_de_Aplicativos_instalados()
    {
        var instalados = new List<string>
        {
            "Google Chrome", "Mozilla Firefox (x64 pt-BR)", "Microsoft 365 Apps for enterprise - pt-br", "7-Zip 24.08 (x64)",
        };
        Assert.Empty(PostFormatChecklist.Missing(instalados));
        Assert.Empty(PostFormatChecklist.Missing(new[] { "Google Chrome", "Mozilla Firefox (x64 en-US)", "Microsoft Office 365 ProPlus - pt-br" }));
    }

    [Fact]
    public void Office_que_nao_e_365_nao_conta_e_aparece_no_aviso()
    {
        var instalados = new List<string>
        {
            "Google Chrome", "Mozilla Firefox (x64 pt-BR)", "Microsoft Office LTSC Professional Plus 2021 - pt-br",
            "Office 16 Click-to-Run Extensibility Component",
        };
        Assert.Equal(new[] { "Microsoft 365" }, PostFormatChecklist.Missing(instalados));
        Assert.Equal("Microsoft Office LTSC Professional Plus 2021", PostFormatChecklist.OtherOffice(instalados));

        var e = Etapa(Pronto() with
        {
            MissingApps = PostFormatChecklist.Missing(instalados),
            OtherOffice = PostFormatChecklist.OtherOffice(instalados),
        }, "Programas");
        Assert.Equal(StepState.Pending, e.State);
        Assert.Equal("Falta: Microsoft 365 (este PC tem o Microsoft Office LTSC Professional Plus 2021, que não é o 365)", e.Detail);
    }

    [Fact]
    public void Programas_que_faltam_aparecem_pelo_nome()
    {
        var faltando = PostFormatChecklist.Missing(new[] { "7-Zip 24.08 (x64)" });
        Assert.Equal(3, faltando.Count);
        var e = Etapa(Pronto() with { MissingApps = faltando }, "Programas");
        Assert.Equal("Falta: Google Chrome, Mozilla Firefox e Microsoft 365", e.Detail);
        Assert.True(e.HasAction);
    }

    [Fact]
    public void Sem_Office_nenhum_nao_menciona_outro_Office()
    {
        Assert.Equal("", PostFormatChecklist.OtherOffice(new[] { "Google Chrome", "Office 16 Click-to-Run Licensing Component" }));
        var e = Etapa(Pronto() with { MissingApps = new() { "Microsoft 365" } }, "Programas");
        Assert.Equal("Falta: Microsoft 365", e.Detail);
    }

    [Theory]
    [InlineData("DESKTOP-7Q2K9LM", true)]
    [InlineData("LAPTOP-AB12CD3", true)]
    [InlineData("WIN-5JQ0M1R2T3U", true)]
    [InlineData("desktop-7q2k9lm", true)]
    [InlineData("PC-SUPORTE-01", false)]
    [InlineData("DESKTOP-SUPORTE", true)]
    [InlineData("DESKTOPSALA1", false)]
    public void Nome_padrao_do_Windows(string nome, bool padrao)
    {
        Assert.Equal(padrao, PostFormatChecklist.IsDefaultName(nome));
    }

    [Fact]
    public void Nome_padrao_falta()
    {
        Assert.Equal(StepState.Pending, Etapa(Pronto() with { ComputerName = "DESKTOP-7Q2K9LM" }, "Nome do computador").State);
    }

    [Fact]
    public void Nome_novo_esperando_reiniciar_e_atencao_e_pede_reinicio()
    {
        var f = Pronto() with { ComputerName = "DESKTOP-7Q2K9LM", PendingName = "PC-SUPORTE-01" };
        var nome = Etapa(f, "Nome do computador");
        Assert.Equal(StepState.Warning, nome.State);
        Assert.Contains("PC-SUPORTE-01 depois de reiniciar", nome.Detail);
        Assert.Equal(StepState.Warning, Etapa(f, "Reiniciar").State);
    }

    [Fact]
    public void Sem_dominio_configurado_nao_se_aplica()
    {
        var f = Pronto() with { ConfiguredDomain = "", PartOfDomain = false, MachineDomain = "WORKGROUP" };
        Assert.Equal(StepState.NotApplicable, Etapa(f, "Domínio").State);
        Assert.Equal(StepState.NotApplicable, Etapa(f, "Políticas de grupo").State);
    }

    [Fact]
    public void Fora_do_dominio_falta()
    {
        var e = Etapa(Pronto() with { PartOfDomain = false, MachineDomain = "WORKGROUP", LastGroupPolicy = null }, "Domínio");
        Assert.Equal(StepState.Pending, e.State);
        Assert.Contains("Fora do domínio empresa.local", e.Detail);
    }

    [Fact]
    public void Dominio_compara_sem_maiusculas()
    {
        Assert.Equal(StepState.Done, Etapa(Pronto() with { MachineDomain = "EMPRESA.LOCAL" }, "Domínio").State);
    }

    [Fact]
    public void Ingresso_feito_esperando_reiniciar()
    {
        var f = Pronto() with { PartOfDomain = false, MachineDomain = "WORKGROUP", JoinPending = true };
        Assert.Equal(StepState.Warning, Etapa(f, "Domínio").State);
        Assert.Equal(StepState.Warning, Etapa(f, "Reiniciar").State);
    }

    [Fact]
    public void Politicas_antigas_ou_nunca_aplicadas()
    {
        Assert.Equal(StepState.Done, Etapa(Pronto() with { LastGroupPolicy = Agora.AddHours(-23) }, "Políticas de grupo").State);
        var antiga = Etapa(Pronto() with { LastGroupPolicy = Agora.AddDays(-3) }, "Políticas de grupo");
        Assert.Equal(StepState.Warning, antiga.State);
        Assert.Contains("há 3 dia(s)", antiga.Detail);
        Assert.Equal(StepState.Pending, Etapa(Pronto() with { LastGroupPolicy = null }, "Políticas de grupo").State);
    }

    [Fact]
    public void Reinicio_pendente_do_Windows_Update()
    {
        var e = Etapa(Pronto() with { RebootPending = true }, "Reiniciar");
        Assert.Equal(StepState.Warning, e.State);
        Assert.False(e.HasAction); // não há tela para abrir: só reiniciar
    }

    [Theory]
    [InlineData("Bom", StepState.Done)]
    [InlineData("Atenção", StepState.Warning)]
    [InlineData("Ruim", StepState.Warning)]
    [InlineData("", StepState.NotApplicable)]
    public void Discos(string pior, StepState esperado)
    {
        Assert.Equal(esperado, Etapa(Pronto() with { WorstDisk = pior }, "Discos").State);
    }

    [Fact]
    public void Relatorio_nao_guardado_hoje_falta_e_leva_para_Sistema()
    {
        var e = Etapa(Pronto() with { ReportSavedToday = false }, "Relatório do atendimento");
        Assert.Equal(StepState.Pending, e.State);
        Assert.Equal("system", e.Page);
    }
}
