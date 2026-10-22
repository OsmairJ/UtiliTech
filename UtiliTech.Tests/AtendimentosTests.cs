using System.IO;

namespace UtiliTech.Tests;

/// <summary>Histórico de atendimentos, sempre numa pasta temporária criada pelo próprio teste.</summary>
[Collection("Histórico")] // a pasta do histórico é estática: um teste por vez
public sealed class AtendimentosTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}");
    readonly string _anterior;

    public AtendimentosTests()
    {
        _anterior = AttendanceHistory.Folder;
        AttendanceHistory.Folder = Path.Combine(_pasta, "atendimentos");
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose()
    {
        AttendanceHistory.Folder = _anterior;
        try { Directory.Delete(_pasta, true); }
        catch { }
    }

    static List<InfoSection> Secoes(string pc = "PC-SUPORTE-01", string serie = "ABC123") => new()
    {
        new("Computador", new()
        {
            new("Nome do computador", pc), new("Fabricante", "Dell Inc."), new("Modelo", "Latitude 5490"), new("Número de série", serie),
        }),
        new("Windows", new() { new("Sistema", "Microsoft Windows 11 Pro"), new("Versão", "24H2 (build 26100.1)") }),
        new("Processador", new() { new("Modelo", "Intel Core i5") }), // "Modelo" repetido: vale o primeiro (o do computador)
    };

    static AttendanceRecord Registro(string cliente = "Escritório Exemplo", string os = "123", DateTime? quando = null,
        string pc = "PC-SUPORTE-01", string serie = "ABC123")
    {
        var disco = new DiskReport { Name = "SSD 256 GB" };
        DiskHealth.Judge(disco, DiskHealth.MediaSsd, 0, new SmartCounters(40, 100, 3, 0, 0));
        var bateria = new BatteryReport { Present = true };
        BatteryInfo.Judge(bateria, 50_000, 45_000, 100);
        return AttendanceHistory.Build(new Atendimento(cliente, os, "Técnico", "Formatação"), Secoes(pc, serie),
            new() { disco }, bateria, "Memória RAM: sem erros", quando ?? new DateTime(2026, 10, 22, 14, 30, 0));
    }

    string Arquivo(string nome, string conteudo = "x")
    {
        string path = Path.Combine(_pasta, nome);
        File.WriteAllText(path, conteudo);
        return path;
    }

    [Fact]
    public void Monta_o_registro_a_partir_do_relatorio()
    {
        var r = Registro();
        Assert.Equal("PC-SUPORTE-01", r.Computador);
        Assert.Equal("Latitude 5490", r.Modelo);
        Assert.Equal("ABC123", r.NumeroSerie);
        Assert.Equal("Microsoft Windows 11 Pro 24H2 (build 26100.1)", r.Windows);
        Assert.Equal(new[] { "SSD 256 GB: Bom" }, r.Discos);
        Assert.Equal("Bom (90% de saúde)", r.Bateria);
    }

    [Fact]
    public void Guarda_json_e_copia_do_arquivo()
    {
        string pasta = AttendanceHistory.Save(Registro(), Arquivo("Relatorio.html", "<html>relatório</html>"));

        Assert.StartsWith(AttendanceHistory.Folder, pasta);
        Assert.Contains("2026-10-22_1430_PC-SUPORTE-01", pasta);
        Assert.True(File.Exists(Path.Combine(pasta, "atendimento.json")));
        Assert.Equal("<html>relatório</html>", File.ReadAllText(Path.Combine(pasta, "Relatorio.html")));

        var lido = Assert.Single(AttendanceHistory.LoadAll());
        Assert.Equal("Escritório Exemplo", lido.Cliente);
        Assert.Equal(pasta, lido.Folder);
        Assert.Equal("Relatório", Assert.Single(lido.Files).Label);
    }

    [Fact]
    public void Relatorio_e_planilha_do_mesmo_atendimento_ficam_juntos()
    {
        string a = AttendanceHistory.Save(Registro(), Arquivo("Relatorio.html"));
        string b = AttendanceHistory.Save(Registro(quando: new DateTime(2026, 10, 22, 16, 0, 0)), Arquivo("Relatorio.xlsx"));

        Assert.Equal(a, b);
        var r = Assert.Single(AttendanceHistory.LoadAll());
        Assert.Equal(new[] { "Relatório", "Planilha" }, r.Files.Select(f => f.Label));
    }

    [Fact]
    public void Outro_cliente_outro_dia_ou_outro_PC_viram_atendimentos_separados()
    {
        AttendanceHistory.Save(Registro(), null);
        AttendanceHistory.Save(Registro(cliente: "Outro cliente"), null);
        AttendanceHistory.Save(Registro(quando: new DateTime(2026, 10, 23, 9, 0, 0)), null);
        AttendanceHistory.Save(Registro(pc: "PC-RECEPCAO", serie: "XYZ999"), null);

        Assert.Equal(4, AttendanceHistory.LoadAll().Count);
    }

    [Fact]
    public void Mesmo_numero_de_serie_e_a_mesma_maquina_mesmo_com_outro_nome()
    {
        string a = AttendanceHistory.Save(Registro(pc: "DESKTOP-ABC"), null);
        string b = AttendanceHistory.Save(Registro(pc: "PC-SUPORTE-01"), null); // renomeado depois de formatar
        Assert.Equal(a, b);
    }

    [Fact]
    public void Lista_do_mais_novo_para_o_mais_antigo_e_pula_json_estragado()
    {
        AttendanceHistory.Save(Registro(quando: new DateTime(2026, 1, 5, 10, 0, 0)), null);
        AttendanceHistory.Save(Registro(quando: new DateTime(2026, 10, 1, 10, 0, 0)), null);
        var ruim = Path.Combine(AttendanceHistory.Folder, "estragado");
        Directory.CreateDirectory(ruim);
        File.WriteAllText(Path.Combine(ruim, "atendimento.json"), "{ isso não é json");

        var lista = AttendanceHistory.LoadAll();
        Assert.Equal(2, lista.Count);
        Assert.True(lista[0].Data > lista[1].Data);
    }

    [Theory]
    [InlineData("123", "Escritório Exemplo · OS 123")]
    [InlineData("OS-777", "Escritório Exemplo · OS-777")]
    [InlineData("", "Escritório Exemplo")]
    public void Titulo_nao_repete_OS(string os, string esperado)
    {
        Assert.Equal(esperado, Registro(os: os).Title);
    }

    [Fact]
    public void Pasta_do_atendimento_para_o_salvar_como()
    {
        // Novo atendimento: pasta ainda não existe
        string nova = AttendanceHistory.FolderFor(Registro());
        Assert.Contains("2026-10-22_1430_PC-SUPORTE-01", nova);
        Assert.False(Directory.Exists(nova));

        // O diálogo cria a pasta e o arquivo é salvo direto nela: o Save usa essa mesma pasta (sem criar "_2")
        Directory.CreateDirectory(nova);
        string relatorio = Path.Combine(nova, "Relatorio.html");
        File.WriteAllText(relatorio, "<html/>");
        Assert.Equal(nova, AttendanceHistory.Save(Registro(), relatorio, nova));
        Assert.Single(Directory.GetDirectories(AttendanceHistory.Folder));

        // Depois, a planilha do mesmo atendimento abre na mesma pasta
        Assert.Equal(nova, AttendanceHistory.FolderFor(Registro(quando: new DateTime(2026, 10, 22, 17, 0, 0))));
        Assert.Equal("Relatório", Assert.Single(Assert.Single(AttendanceHistory.LoadAll()).Files).Label);
    }

    [Fact]
    public void Arquivo_apagado_some_da_lista()
    {
        string pasta = AttendanceHistory.Save(Registro(), Arquivo("Relatorio.html"));
        File.Delete(Path.Combine(pasta, "Relatorio.html"));
        Assert.Empty(Assert.Single(AttendanceHistory.LoadAll()).Files);
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("escritorio", 1)]        // sem acento acha "Escritório"
    [InlineData("ESCRITÓRIO exemplo", 1)]
    [InlineData("xyz999", 1)]            // nº de série
    [InlineData("recepção", 1)]          // nome do PC sem acento
    [InlineData("latitude", 3)]
    [InlineData("os-777", 1)]
    [InlineData("22/10/2026", 3)]
    [InlineData("inexistente", 0)]
    public void Busca(string termo, int esperado)
    {
        var lista = new List<AttendanceRecord>
        {
            Registro(),
            Registro(cliente: "Padaria Central", os: "OS-777"),
            Registro(cliente: "Clínica", pc: "PC-RECEPCAO", serie: "XYZ999"),
        };
        Assert.Equal(esperado, AttendanceHistory.Search(lista, termo).Count);
    }
}
