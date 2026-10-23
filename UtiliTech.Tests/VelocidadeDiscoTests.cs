using System.IO;

namespace UtiliTech.Tests;

/// <summary>Teste de velocidade do disco: cálculo, veredito e um teste real pequeno numa pasta temporária.</summary>
public class VelocidadeDiscoTests
{
    static SpeedResult R(double seqRead, double randRead, double seqWrite = 400, double randWrite = 30) =>
        new("C:", seqRead, seqWrite, randRead, randWrite, randRead * 1e6 / 4096, randWrite * 1e6 / 4096);

    [Theory]
    [InlineData(3500, 60, "Muito rápido · típico de SSD NVMe")]
    [InlineData(540, 35, "Rápido · típico de SSD SATA")]
    [InlineData(150, 0.8, "Lento · típico de HD mecânico")]
    [InlineData(520, 1.5, "Lento · típico de HD mecânico")]   // sequencial bom mas 4K de HD (HD híbrido/RAID)
    [InlineData(200, 12, "Abaixo do esperado para SSD")]
    [InlineData(30, 6, "Muito lento")]
    public void Veredito(double seqRead, double randRead, string esperado)
    {
        Assert.Equal(esperado, DiskSpeed.Classify(R(seqRead, randRead)).Label);
    }

    [Fact]
    public void HD_recomenda_trocar_por_SSD()
    {
        var (_, cor, conselho) = DiskSpeed.Classify(R(150, 0.8));
        Assert.Equal(Palette.Warn, cor);
        Assert.Contains("Trocar por SSD", conselho);
    }

    [Fact]
    public void MBps_em_base_1000()
    {
        Assert.Equal(100, DiskSpeed.MBps(500_000_000, TimeSpan.FromSeconds(5)), 6);
        Assert.Equal(0, DiskSpeed.MBps(1, TimeSpan.Zero));
    }

    [Fact]
    public void Posicao_aleatoria_alinhada_e_dentro_do_arquivo()
    {
        var rng = new Random(1);
        long tamanho = 256L << 20;
        for (int i = 0; i < 1000; i++)
        {
            long off = DiskSpeed.RandomOffset(rng, tamanho);
            Assert.Equal(0, off % 4096);
            Assert.InRange(off, 0, tamanho - 4096);
        }
    }

    [Fact]
    public void Precisa_de_folga_de_1GB()
    {
        Assert.True(DiskSpeed.HasRoom(3L << 30, 1L << 30));
        Assert.False(DiskSpeed.HasRoom((1L << 30) + 100, 1L << 30));
    }

    [Fact]
    public void Resumo_para_o_relatorio()
    {
        Assert.Equal("C: leitura 540 MB/s, gravação 400 MB/s, 4K leitura 35,0 MB/s, 4K gravação 30,0 MB/s", R(540, 35).Summary);
    }

    [Fact]
    public async Task Teste_real_pequeno_apaga_o_arquivo()
    {
        string pasta = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        try
        {
            var drive = new SpeedDrive(pasta, "teste", 0, 0);
            var mensagens = new List<string>();
            var r = await DiskSpeed.RunAsync(drive, 16L << 20, new Progress<string>(mensagens.Add), CancellationToken.None, randomSeconds: 0.3);
            Assert.True(r.SeqRead > 0 && r.SeqWrite > 0 && r.RandRead > 0 && r.RandWrite > 0);
            Assert.True(r.RandReadIops > 0);
            Assert.False(File.Exists(Path.Combine(pasta, DiskSpeed.TestFileName)));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public async Task Parar_no_meio_tambem_apaga_o_arquivo()
    {
        string pasta = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        try
        {
            using var cts = new CancellationTokenSource();
            var progresso = new SyncProgress(_ => cts.Cancel()); // cancela na primeira mensagem
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                DiskSpeed.RunAsync(new SpeedDrive(pasta, "", 0, 0), 64L << 20, progresso, cts.Token, 0.3));
            Assert.False(File.Exists(Path.Combine(pasta, DiskSpeed.TestFileName)));
        }
        finally { Directory.Delete(pasta, true); }
    }

    sealed class SyncProgress(Action<string> action) : IProgress<string>
    {
        public void Report(string value) => action(value);
    }
}
