using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace UtiliTech.Tests;

/// <summary>Inventário em lote a partir do histórico (pasta temporária criada pelo teste).</summary>
[Collection("Histórico")] // mesma pasta estática do histórico que AtendimentosTests
public sealed class InventarioTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}");
    readonly string _anterior;

    public InventarioTests()
    {
        _anterior = AttendanceHistory.Folder;
        AttendanceHistory.Folder = Path.Combine(_pasta, "atendimentos");
    }

    public void Dispose()
    {
        AttendanceHistory.Folder = _anterior;
        try { Directory.Delete(_pasta, true); }
        catch { }
    }

    static List<InfoSection> Secoes(string pc, string serie, string cpu = "Intel Core i5-8350U") => new()
    {
        new("Computador", new() { new("Nome do computador", pc), new("Fabricante", "Dell Inc."), new("Modelo", "Latitude 5490"), new("Número de série", serie) }),
        new("Sistema operacional", new() { new("Sistema", "Microsoft Windows 11 Pro"), new("Versão", "24H2 (build 26100.1)") }),
        new("Processador", new() { new("Modelo", cpu), new("Núcleos / threads", "4 / 8") }),
        new("Memória RAM", new() { new("Instalada", "16 GB DDR4"), new("Slots", "2 de 2 ocupados") }),
        new("Armazenamento", new()
        {
            new("SAMSUNG MZVLB256", "256 GB · SCSI · S/N S4DXNX0K"),
            new("Unidade C:", "NTFS · 120 GB livres de 237 GB"),
        }),
        new("Rede", new() { new("Intel Ethernet I219-LM", "IP 10.0.0.15 (DHCP) · MAC 00:11:22:33:44:55 · Gateway 10.0.0.1 · DNS 10.0.0.2") }),
    };

    static AttendanceRecord Registro(string cliente, DateTime quando, string pc = "PC-SUPORTE-01", string serie = "ABC123",
        string cpu = "Intel Core i5-8350U") =>
        AttendanceHistory.Build(new Atendimento(cliente, "1", "Técnico", ""), Secoes(pc, serie, cpu), new(), new BatteryReport(), null, quando);

    [Fact]
    public void Atendimento_guarda_processador_memoria_disco_e_rede()
    {
        var r = Registro("Cliente", new DateTime(2026, 10, 22, 10, 0, 0));
        Assert.Equal("Intel Core i5-8350U", r.Processador);
        Assert.Equal("16 GB DDR4", r.Memoria);
        Assert.Equal("SAMSUNG MZVLB256 (256 GB)", r.Armazenamento); // a partição "Unidade C:" fica de fora
        Assert.Equal("MAC 00:11:22:33:44:55 · IP 10.0.0.15", r.Rede);
    }

    [Fact]
    public void Uma_linha_por_maquina_com_os_dados_mais_novos()
    {
        var lista = new List<AttendanceRecord>
        {
            Registro("Escritório A", new DateTime(2026, 3, 1, 9, 0, 0), pc: "DESKTOP-OLD1", cpu: "CPU antiga"),
            Registro("Escritório A", new DateTime(2026, 10, 22, 9, 0, 0), pc: "PC-SUPORTE-01"),      // mesma máquina, renomeada
            Registro("Clínica B", new DateTime(2026, 9, 5, 14, 0, 0), pc: "PC-RECEPCAO", serie: "XYZ999"),
        };
        var maquinas = Inventory.Machines(lista);

        Assert.Equal(2, maquinas.Count);
        var a = maquinas[0]; // mais recente primeiro
        Assert.Equal("ABC123", a.NumeroSerie);
        Assert.Equal("PC-SUPORTE-01", a.Computador);
        Assert.Equal("Intel Core i5-8350U", a.Processador);
        Assert.Equal(2, a.Atendimentos);
        Assert.Equal(new DateTime(2026, 3, 1, 9, 0, 0), a.Primeiro);
        Assert.Equal("Escritório A", a.Clientes);
        Assert.Equal("XYZ999", maquinas[1].NumeroSerie);
    }

    [Fact]
    public void Campo_vazio_no_atendimento_novo_usa_o_do_anterior()
    {
        var antigo = Registro("A", new DateTime(2026, 1, 1, 9, 0, 0));
        var novo = Registro("A", new DateTime(2026, 10, 1, 9, 0, 0));
        novo.Processador = ""; // ex.: atendimento gravado por uma versão antiga do app
        Assert.Equal("Intel Core i5-8350U", Assert.Single(Inventory.Machines(new[] { antigo, novo })).Processador);
    }

    [Fact]
    public void Sem_numero_de_serie_agrupa_pelo_nome()
    {
        var lista = new List<AttendanceRecord>
        {
            Registro("A", new DateTime(2026, 5, 1, 9, 0, 0), pc: "PC-01", serie: ""),
            Registro("A", new DateTime(2026, 6, 1, 9, 0, 0), pc: "pc-01", serie: ""),
            Registro("A", new DateTime(2026, 6, 1, 9, 0, 0), pc: "PC-02", serie: ""),
        };
        Assert.Equal(2, Inventory.Machines(lista).Count);
    }

    [Fact]
    public void Guardar_atendimento_refaz_o_inventario()
    {
        AttendanceHistory.Save(Registro("Escritório A", new DateTime(2026, 10, 22, 9, 0, 0)), null);
        AttendanceHistory.Save(Registro("Clínica B", new DateTime(2026, 10, 22, 11, 0, 0), pc: "PC-RECEPCAO", serie: "XYZ999"), null);

        Assert.True(File.Exists(AttendanceHistory.InventoryPath));
        using var zip = ZipFile.OpenRead(AttendanceHistory.InventoryPath);
        string Ler(string nome) { using var r = new StreamReader(zip.GetEntry(nome)!.Open()); return r.ReadToEnd(); }
        Assert.Contains("Máquinas", Ler("xl/workbook.xml"));
        Assert.Contains("Atendimentos", Ler("xl/workbook.xml"));
        var maquinas = XDocument.Parse(Ler("xl/worksheets/sheet1.xml"));
        Assert.Equal(3, maquinas.Descendants().Count(e => e.Name.LocalName == "row")); // cabeçalho + 2 máquinas
        Assert.Contains("XYZ999", Ler("xl/worksheets/sheet1.xml"));
        Assert.Empty(Directory.GetFiles(AttendanceHistory.Folder, "*.novo*")); // não sobra arquivo temporário
    }

    [Fact]
    public void Inventario_aberto_no_Excel_nao_derruba_e_avisa()
    {
        AttendanceHistory.Save(Registro("Escritório A", new DateTime(2026, 10, 22, 9, 0, 0)), null);
        byte[] antes = File.ReadAllBytes(AttendanceHistory.InventoryPath);
        using (File.Open(AttendanceHistory.InventoryPath, FileMode.Open, FileAccess.Read, FileShare.None)) // como o Excel faz
        {
            string? erro = AttendanceHistory.UpdateInventory();
            Assert.Contains("Feche o inventario.xlsx", erro);
            // o atendimento continua sendo guardado mesmo com a planilha travada
            AttendanceHistory.Save(Registro("Clínica B", new DateTime(2026, 10, 22, 11, 0, 0), pc: "PC-RECEPCAO", serie: "XYZ999"), null);
        }
        Assert.Equal(antes, File.ReadAllBytes(AttendanceHistory.InventoryPath)); // a planilha antiga ficou inteira
        Assert.Equal(2, AttendanceHistory.LoadAll().Count);
        Assert.Null(AttendanceHistory.UpdateInventory()); // fechada, atualiza
    }
}
