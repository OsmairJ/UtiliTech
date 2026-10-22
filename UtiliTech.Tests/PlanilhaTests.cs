using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace UtiliTech.Tests;

/// <summary>Exportação da planilha (.xlsx/.csv) feita sem biblioteca.</summary>
public class PlanilhaTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Nome_da_coluna(int indice, string esperado)
    {
        Assert.Equal(esperado, SpreadsheetExporter.ColumnName(indice));
    }

    [Theory]
    [InlineData("simples", "simples")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("diz \"oi\"", "\"diz \"\"oi\"\"\"")]
    [InlineData("linha\nnova", "\"linha\nnova\"")]
    public void Celula_do_csv(string valor, string esperado)
    {
        Assert.Equal(esperado, SpreadsheetExporter.Csv(valor));
    }

    [Fact]
    public void Xml_escapa_e_tira_caracteres_de_controle()
    {
        Assert.Equal("a &lt; b &amp; c", SpreadsheetExporter.Xml("a < b & c"));
        Assert.Equal("ab", SpreadsheetExporter.Xml("a\u0001b"));
    }

    static (Atendimento, List<InfoSection>, List<DiskReport>, BatteryReport) Dados()
    {
        var at = new Atendimento("Cliente <Teste> & Cia", "OS-123", "Técnico", "Obs; com \"aspas\"");
        var secoes = new List<InfoSection> { new("Sistema", new() { new("Modelo", "Notebook X"), new("Windows", "11 Pro") }) };
        var disco = new DiskReport { Name = "SSD 256 GB" };
        DiskHealth.Judge(disco, DiskHealth.MediaSsd, 0, new SmartCounters(40, 100, 3, 0, 0));
        var bateria = new BatteryReport { Present = true };
        BatteryInfo.Judge(bateria, 50_000, 45_000, 120);
        return (at, secoes, new() { disco }, bateria);
    }

    [Fact]
    public void Xlsx_abre_como_zip_com_xml_valido()
    {
        var (at, secoes, discos, bateria) = Dados();
        string path = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}.xlsx");
        try
        {
            SpreadsheetExporter.Save(path, at, secoes, discos, bateria, "Sem erros");
            using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
            var sheets = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/")).ToList();
            Assert.True(sheets.Count >= 3, $"esperava 3+ abas, veio {sheets.Count}");
            foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
            {
                using var s = e.Open();
                XDocument.Load(s); // lança exceção se o XML estiver quebrado
            }
            string tudo = string.Concat(sheets.Select(e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }));
            Assert.Contains("Cliente &lt;Teste&gt; &amp; Cia", tudo);
            Assert.Contains("SSD 256 GB", tudo);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Csv_tem_bom_e_separador_ponto_e_virgula()
    {
        var (at, secoes, discos, bateria) = Dados();
        string path = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}.csv");
        try
        {
            SpreadsheetExporter.Save(path, at, secoes, discos, bateria, null);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            string texto = File.ReadAllText(path);
            Assert.Contains("Modelo;Notebook X", texto);
            Assert.Contains("\"Obs; com \"\"aspas\"\"\"", texto);
        }
        finally { File.Delete(path); }
    }
}
