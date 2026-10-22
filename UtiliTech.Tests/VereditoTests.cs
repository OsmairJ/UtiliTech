namespace UtiliTech.Tests;

/// <summary>Veredito Bom / Atenção / Ruim dos discos (S.M.A.R.T.) e da bateria.</summary>
public class VereditoTests
{
    static SmartCounters Contadores(long? temp = 35, long? horas = 1000, long? desgaste = 5, long errosLeitura = 0, long errosEscrita = 0) =>
        new(temp, horas, desgaste, errosLeitura, errosEscrita);

    static DiskReport Disco(int media, int status, SmartCounters? c)
    {
        var r = new DiskReport();
        DiskHealth.Judge(r, media, status, c);
        return r;
    }

    [Fact]
    public void Disco_saudavel_e_Bom()
    {
        var r = Disco(DiskHealth.MediaSsd, 0, Contadores());
        Assert.Equal("Bom", r.Verdict);
        Assert.Equal(new[] { "Nenhum problema encontrado" }, r.Reasons);
    }

    [Fact]
    public void Windows_nao_saudavel_e_Ruim()
    {
        var r = Disco(DiskHealth.MediaSsd, 2, Contadores());
        Assert.Equal("Ruim", r.Verdict);
        Assert.Contains(r.Reasons, m => m.Contains("NÃO saudável"));
    }

    [Fact]
    public void Aviso_do_Windows_e_Atencao()
    {
        Assert.Equal("Atenção", Disco(DiskHealth.MediaSsd, 1, Contadores()).Verdict);
    }

    [Theory]
    [InlineData(59, "Bom")]
    [InlineData(60, "Atenção")]
    public void Temperatura_alta_a_partir_de_60(long temp, string esperado)
    {
        Assert.Equal(esperado, Disco(DiskHealth.MediaSsd, 0, Contadores(temp: temp)).Verdict);
    }

    [Theory]
    [InlineData(69, "Bom")]
    [InlineData(70, "Atenção")]
    [InlineData(89, "Atenção")]
    [InlineData(90, "Ruim")]
    public void Desgaste_do_SSD(long desgaste, string esperado)
    {
        Assert.Equal(esperado, Disco(DiskHealth.MediaSsd, 0, Contadores(desgaste: desgaste)).Verdict);
    }

    [Fact]
    public void Desgaste_so_conta_em_SSD()
    {
        Assert.Equal("Bom", Disco(DiskHealth.MediaHdd, 0, Contadores(desgaste: 95)).Verdict);
    }

    [Fact]
    public void HD_com_muitas_horas_e_Atencao_mas_SSD_nao()
    {
        Assert.Equal("Atenção", Disco(DiskHealth.MediaHdd, 0, Contadores(horas: 35_001)).Verdict);
        Assert.Equal("Bom", Disco(DiskHealth.MediaSsd, 0, Contadores(horas: 35_001)).Verdict);
    }

    [Fact]
    public void Erros_nao_corrigidos_sao_Ruim_e_vem_antes_dos_avisos()
    {
        var r = Disco(DiskHealth.MediaSsd, 0, Contadores(temp: 70, errosLeitura: 3));
        Assert.Equal("Ruim", r.Verdict);
        Assert.Contains("leitura", r.Reasons[0]);
        Assert.Contains("Temperatura", r.Reasons[1]);
    }

    [Fact]
    public void Sem_contadores_avisa_e_fica_Bom()
    {
        var r = Disco(DiskHealth.MediaSsd, 0, null);
        Assert.Equal("Bom", r.Verdict);
        Assert.Contains(r.Reasons, m => m.Contains("não disponíveis"));
    }

    static BatteryReport Bateria(long projeto, long atual, long ciclos = 100)
    {
        var r = new BatteryReport { Present = true };
        BatteryInfo.Judge(r, projeto, atual, ciclos);
        return r;
    }

    [Theory]
    [InlineData(50_000, 50_000, "Bom")]
    [InlineData(50_000, 40_000, "Bom")]      // 80%
    [InlineData(50_000, 39_999, "Atenção")]
    [InlineData(50_000, 30_000, "Atenção")]  // 60%
    [InlineData(50_000, 29_999, "Ruim")]
    public void Saude_da_bateria(long projeto, long atual, string esperado)
    {
        Assert.Equal(esperado, Bateria(projeto, atual).Verdict);
    }

    [Fact]
    public void Saude_nao_passa_de_100()
    {
        Assert.Equal(100, Bateria(40_000, 45_000).HealthPercent);
    }

    [Fact]
    public void Sem_capacidade_informada_fica_Desconhecido()
    {
        var r = Bateria(0, 0);
        Assert.Equal("Desconhecido", r.Verdict);
        Assert.Null(r.HealthPercent);
    }

    [Fact]
    public void Muitos_ciclos_entram_nos_motivos()
    {
        Assert.Contains(Bateria(50_000, 45_000, ciclos: 801).Reasons, m => m.Contains("ciclos"));
        Assert.DoesNotContain(Bateria(50_000, 45_000, ciclos: 800).Reasons, m => m.Contains("ciclos"));
    }
}
