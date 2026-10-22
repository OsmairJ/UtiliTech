using System.IO;
using System.IO.Compression;

namespace UtiliTech.Tests;

/// <summary>Relatório "antes e depois": comparação do PC e serviços feitos na visita.</summary>
public class AntesDepoisTests
{
    const long GB = 1024L * 1024 * 1024;
    static readonly DateTime Abriu = new(2026, 10, 22, 9, 0, 0);
    static readonly DateTime Agora = new(2026, 10, 22, 11, 30, 0);

    static List<SessionAction> Acoes() => new()
    {
        new(Abriu.AddMinutes(40), "Limpeza", "2,1 GB liberados (Temporários do Windows)"),
        new(Abriu.AddMinutes(10), "Ponto de restauração", "criado antes de: Resetar rede"),
        new(Abriu.AddMinutes(11), "Reparo", "Resetar rede: Feito · reinicie o PC"),
        new(Abriu.AddMinutes(60), "Programa", "Mozilla Firefox: tempo esgotado", false),
    };

    [Fact]
    public void Compara_espaco_livre_e_dispositivos()
    {
        var r = SessionLog.Build(new PcSnapshot(Abriu, 20 * GB, 200 * GB, 3), new PcSnapshot(Agora, 22 * GB + GB / 10, 200 * GB, 0), new());
        Assert.Equal("Espaço livre no disco do Windows", r.BeforeAfter[0].Label);
        Assert.Equal("20 GB → 22,1 GB (+2,1 GB)", r.BeforeAfter[0].Value);
        Assert.Equal("3 → 0", r.BeforeAfter.Single(i => i.Label == "Dispositivos com problema").Value);
        Assert.Equal("22/10 09:00 → 22/10 11:30", r.BeforeAfter.Single(i => i.Label == "Período").Value);
    }

    [Fact]
    public void Espaco_que_diminuiu_e_mudanca_pequena()
    {
        var menos = SessionLog.Build(new PcSnapshot(Abriu, 20 * GB, 200 * GB, 0), new PcSnapshot(Agora, 15 * GB, 200 * GB, 0), new());
        Assert.EndsWith("(−5 GB)", menos.BeforeAfter[0].Value);
        var igual = SessionLog.Build(new PcSnapshot(Abriu, 20 * GB, 200 * GB, 0), new PcSnapshot(Agora, 20 * GB + 10 * 1024 * 1024, 200 * GB, 0), new());
        Assert.EndsWith("(sem mudança)", igual.BeforeAfter[0].Value);
    }

    [Fact]
    public void Sem_dispositivos_com_problema_nao_mostra_a_linha()
    {
        var r = SessionLog.Build(new PcSnapshot(Abriu, 20 * GB, 200 * GB, 0), new PcSnapshot(Agora, 20 * GB, 200 * GB, 0), new());
        Assert.DoesNotContain(r.BeforeAfter, i => i.Label == "Dispositivos com problema");
    }

    [Fact]
    public void Sem_foto_de_antes_fica_so_a_lista_de_servicos()
    {
        var r = SessionLog.Build(null, new PcSnapshot(Agora, 20 * GB, 200 * GB, 0), Acoes());
        Assert.Empty(r.BeforeAfter);
        Assert.Equal(4, r.Actions.Count);
        Assert.False(r.IsEmpty);
        Assert.True(SessionLog.Build(null, null, new()).IsEmpty);
    }

    [Fact]
    public void Servicos_em_ordem_de_hora()
    {
        var r = SessionLog.Build(null, null, Acoes());
        Assert.Equal(new[] { "Ponto de restauração", "Reparo", "Limpeza", "Programa" }, r.Actions.Select(a => a.Category));
        Assert.Equal("10:00 · Programa: Mozilla Firefox: tempo esgotado (falhou)", r.Actions[^1].Line);
    }

    static SessionReport Sessao() =>
        SessionLog.Build(new PcSnapshot(Abriu, 20 * GB, 200 * GB, 2), new PcSnapshot(Agora, 22 * GB, 200 * GB, 0), Acoes());

    static (Atendimento, List<InfoSection>, List<DiskReport>, BatteryReport) Dados() =>
        (new Atendimento("Cliente <Teste>", "OS-9", "Técnico", ""), new List<InfoSection>(), new List<DiskReport>(), new BatteryReport());

    [Fact]
    public void Relatorio_html_tem_servicos_e_antes_e_depois()
    {
        var (at, secoes, discos, bateria) = Dados();
        string html = ReportBuilder.Html(at, secoes, discos, bateria, null, Sessao());
        Assert.Contains("<h2>Serviços feitos nesta visita</h2>", html);
        Assert.Contains("<h2>Antes e depois</h2>", html);
        Assert.Contains("20 GB → 22 GB (+2 GB)", html);
        Assert.Contains("✗</span> Mozilla Firefox: tempo esgotado", html);
        Assert.True(html.IndexOf("Resetar rede", StringComparison.Ordinal) < html.IndexOf("2,1 GB liberados", StringComparison.Ordinal));
    }

    [Fact]
    public void Relatorio_html_sem_sessao_nao_mostra_as_secoes()
    {
        var (at, secoes, discos, bateria) = Dados();
        string html = ReportBuilder.Html(at, secoes, discos, bateria, null, SessionReport.Empty);
        Assert.DoesNotContain("Serviços feitos", html);
        Assert.DoesNotContain("Antes e depois", html);
    }

    [Fact]
    public void Planilha_ganha_aba_Servicos_e_antes_e_depois_no_resumo()
    {
        var (at, secoes, discos, bateria) = Dados();
        string path = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}.xlsx");
        try
        {
            SpreadsheetExporter.Save(path, at, secoes, discos, bateria, null, Sessao());
            using var zip = ZipFile.OpenRead(path);
            string Ler(ZipArchiveEntry e) { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }
            Assert.Contains("Serviços", Ler(zip.GetEntry("xl/workbook.xml")!));
            string tudo = string.Concat(zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/")).Select(Ler));
            Assert.Contains("Antes e depois · Espaço livre no disco do Windows", tudo);
            Assert.Contains("Falhou", tudo);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Historico_guarda_servicos_e_antes_e_depois()
    {
        var (at, secoes, discos, bateria) = Dados();
        var r = AttendanceHistory.Build(at, secoes, discos, bateria, null, Agora, Sessao());
        Assert.Equal(4, r.Servicos.Count);
        Assert.StartsWith("09:10 · Ponto de restauração", r.Servicos[0]);
        Assert.Contains("Espaço livre no disco do Windows: 20 GB → 22 GB (+2 GB)", r.AntesDepois);
    }
}
