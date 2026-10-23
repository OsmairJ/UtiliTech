namespace UtiliTech.Tests;

/// <summary>Eventos simplificados: classificação, códigos de tela azul, junção de incidentes e resumo.</summary>
public class EventosTests
{
    static readonly DateTime T = new(2026, 10, 5, 9, 11, 0);

    static RawEvent Raw(string provider, int id, int level = 2, object?[]? props = null, string msg = "", DateTime? time = null) =>
        new(time ?? T, provider, id, level, props ?? Array.Empty<object?>(), msg);

    [Theory]
    [InlineData("0x0000007e (0xffffffffc0000005, 0xfffff80412345678)", 0x7Eu)]
    [InlineData("0x00000124 (0x0, ...)", 0x124u)]
    [InlineData("O computador foi reiniciado após uma verificação de erros. A verificação de erros foi: 0x000000d1 (...)", 0xD1u)]
    public void Codigo_da_tela_azul(string texto, uint codigo)
    {
        Assert.Equal(codigo, EventTools.ParseBugCheck(texto));
    }

    [Fact]
    public void Explica_o_codigo()
    {
        Assert.StartsWith("0xD1 DRIVER_IRQL_NOT_LESS_OR_EQUAL: driver", EventTools.ExplainBugCheck(0xD1));
        Assert.Contains("WHEA_UNCORRECTABLE_ERROR", EventTools.ExplainBugCheck(0x124));
        Assert.Contains("código pouco comum", EventTools.ExplainBugCheck(0xDEAD));
    }

    [Fact]
    public void Tela_azul_do_relatorio_de_erros()
    {
        var e = EventTools.Classify(Raw("Microsoft-Windows-WER-SystemErrorReporting", 1001, props: new object?[] { "0x0000003b (0xc0000005, ...)" }));
        Assert.Equal(EventKind.BlueScreen, e!.Kind);
        Assert.StartsWith("0x3B SYSTEM_SERVICE_EXCEPTION", e.Detail);
    }

    [Fact]
    public void Kernel_Power_41_sem_codigo_e_desligamento_inesperado()
    {
        var e = EventTools.Classify(Raw("Microsoft-Windows-Kernel-Power", 41, 1, new object?[] { 0u }));
        Assert.Equal(EventKind.UnexpectedShutdown, e!.Kind);
        var azul = EventTools.Classify(Raw("Microsoft-Windows-Kernel-Power", 41, 1, new object?[] { 209u })); // 0xD1
        Assert.Equal(EventKind.BlueScreen, azul!.Kind);
        Assert.Contains("0xD1", azul.Detail);
    }

    [Fact]
    public void EventLog_6008_mostra_a_hora_que_parou()
    {
        var e = EventTools.Classify(Raw("EventLog", 6008, props: new object?[] { "00:17:46", "05/10/2026" }));
        Assert.Equal(EventKind.UnexpectedShutdown, e!.Kind);
        Assert.Equal("O PC parou por volta das 00:17:46 05/10/2026", e.Detail);
    }

    [Theory]
    [InlineData("disk", 7, 2, true)]
    [InlineData("disk", 153, 3, true)]
    [InlineData("disk", 7, 4, false)]                         // informativo: não conta
    [InlineData("Microsoft-Windows-Ntfs", 98, 4, false)]      // "volume íntegro": não é erro
    [InlineData("Microsoft-Windows-Ntfs", 55, 2, true)]
    [InlineData("Microsoft-Windows-Kernel-Boot", 153, 4, false)] // mesmo número, outra origem
    [InlineData("Microsoft-Windows-WindowsUpdateClient", 19, 4, false)]
    public void Erros_de_disco_so_da_origem_certa(string provider, int id, int level, bool disco)
    {
        var e = EventTools.Classify(Raw(provider, id, level, msg: "O dispositivo \\Device\\Harddisk0\\DR0 tem um bloco inválido."));
        Assert.Equal(disco, e?.Kind == EventKind.Disk);
    }

    [Fact]
    public void Video_e_WHEA()
    {
        Assert.Equal(EventKind.Video, EventTools.Classify(Raw("Display", 4101, 3))!.Kind);
        Assert.Equal("Erro de hardware grave", EventTools.Classify(Raw("Microsoft-Windows-WHEA-Logger", 18, 2))!.Title);
        Assert.Equal("Erro de hardware corrigido", EventTools.Classify(Raw("Microsoft-Windows-WHEA-Logger", 19, 3))!.Title);
        Assert.Null(EventTools.Classify(Raw("Microsoft-Windows-WHEA-Logger", 19, 4)));
    }

    [Fact]
    public void Um_travamento_conta_uma_vez()
    {
        var eventos = new[]
        {
            EventTools.Classify(Raw("Microsoft-Windows-Kernel-Power", 41, 1, new object?[] { 0u }, time: T))!,
            EventTools.Classify(Raw("EventLog", 6008, props: new object?[] { "09:05:00", "05/10/2026" }, time: T.AddSeconds(10)))!,
            EventTools.Classify(Raw("Microsoft-Windows-WER-SystemErrorReporting", 1001, props: new object?[] { "0x00000124 (...)" }, time: T.AddMinutes(1)))!,
            EventTools.Classify(Raw("EventLog", 6008, props: new object?[] { "20:00:00", "07/10/2026" }, time: T.AddDays(2)))!,
        };
        var lista = EventTools.Deduplicate(eventos);
        Assert.Equal(2, lista.Count);
        Assert.Equal(EventKind.UnexpectedShutdown, lista[0].Kind); // o de 07/10, mais recente primeiro
        Assert.Equal(EventKind.BlueScreen, lista[1].Kind);          // 41 + 6008 + 1001 viram uma tela azul
        Assert.Contains("0x124", lista[1].Detail);
        Assert.StartsWith("O PC parou por volta das 09:05:00 05/10/2026 · 0x124", lista[1].Detail);
        Assert.Equal(T, lista[1].Time);
    }

    [Fact]
    public void Desligamento_inesperado_mostra_quando_parou()
    {
        var eventos = new[]
        {
            EventTools.Classify(Raw("Microsoft-Windows-Kernel-Power", 41, 1, new object?[] { 0u }, time: T))!,
            EventTools.Classify(Raw("EventLog", 6008, props: new object?[] { "00:17:46", "05/10/2026" }, time: T.AddSeconds(10)))!,
        };
        var e = Assert.Single(EventTools.Deduplicate(eventos));
        Assert.Equal("Desligou sem desligar o Windows", e.Title);
        Assert.StartsWith("O PC parou por volta das 00:17:46 05/10/2026 · Queda de energia", e.Detail);
        // na ordem contrária (6008 antes do 41) dá o mesmo
        var e2 = Assert.Single(EventTools.Deduplicate(eventos.Reverse()));
        Assert.Equal(e.Detail, e2.Detail);
    }

    [Fact]
    public void Erros_de_disco_nao_sao_juntados()
    {
        var disco = EventTools.Classify(Raw("disk", 7, 2))!;
        Assert.Equal(3, EventTools.Deduplicate(new[] { disco, disco with { Time = T.AddMinutes(1) }, disco with { Time = T.AddMinutes(2) } }).Count);
    }

    [Fact]
    public void Resumo_com_o_que_fazer()
    {
        var eventos = new List<PcEvent>
        {
            new(T, EventKind.BlueScreen, "Tela azul", EventTools.ExplainBugCheck(0xD1)),
            new(T.AddDays(1), EventKind.BlueScreen, "Tela azul", EventTools.ExplainBugCheck(0xD1)),
            new(T, EventKind.Disk, "Bloco defeituoso no disco", ""),
        };
        var r = EventTools.Summarize(eventos, minidumps: 2);
        Assert.Equal(new[] { EventKind.BlueScreen, EventKind.UnexpectedShutdown, EventKind.Disk, EventKind.Video, EventKind.Hardware }, r.Select(s => s.Kind));
        var azul = r[0];
        Assert.Equal("2 vezes", azul.CountText);
        Assert.Equal(T.AddDays(1), azul.Last);
        Assert.Contains("Mais comum: 0xD1", azul.Advice);
        Assert.Contains("2 minidump(s)", azul.Advice);
        Assert.Equal("Abrir Drivers", azul.ActionText);
        Assert.Equal("nenhum", r[1].CountText);
        Assert.False(r[1].HasAction);
        Assert.Equal(Palette.Good, r[1].Color);
        Assert.Equal("Abrir Discos", r[2].ActionText);
    }
}
