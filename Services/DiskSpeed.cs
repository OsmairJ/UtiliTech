using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace UtiliTech.Services;

/// <summary>Resultado do teste de velocidade (MB/s em base 1000, como o CrystalDiskMark).</summary>
public record SpeedResult(string Drive, double SeqRead, double SeqWrite, double RandRead, double RandWrite, double RandReadIops, double RandWriteIops)
{
    public string Summary => $"{Drive} leitura {SeqRead:0} MB/s, gravação {SeqWrite:0} MB/s, 4K leitura {RandRead:0.0} MB/s, 4K gravação {RandWrite:0.0} MB/s";
}

/// <summary>Unidade que pode ser testada.</summary>
public record SpeedDrive(string Root, string Label, long Free, long Total)
{
    public string Letter => Root.TrimEnd('\\');
    public string Title => $"{Letter} {Label}".Trim() + $" · {Fmt.Bytes(Free)} livres de {Fmt.Bytes(Total)}";
    public override string ToString() => Title;
}

/// <summary>
/// Teste de velocidade do disco: grava um arquivo temporário com dados aleatórios e mede leitura/gravação sequencial
/// (blocos de 1 MiB) e aleatória 4K (QD1), sem o cache do Windows (FILE_FLAG_NO_BUFFERING). O arquivo é apagado no fim.
/// </summary>
public static class DiskSpeed
{
    public const string TestFileName = "UtiliTech-teste-velocidade.tmp";
    const int SeqBlock = 1 << 20;   // 1 MiB
    const int RandBlock = 4096;     // 4 KiB
    const FileOptions NoBuffering = (FileOptions)0x20000000;

    public record TestSize(string Label, long Bytes);

    public static readonly IReadOnlyList<TestSize> Sizes = new TestSize[]
    {
        new("256 MB", 256L << 20), new("1 GB", 1L << 30), new("4 GB", 4L << 30),
    };

    /// <summary>Discos internos e externos com sistema de arquivos (sem CD, rede ou unidade virtual).</summary>
    public static List<SpeedDrive> Drives()
    {
        var list = new List<SpeedDrive>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                list.Add(new SpeedDrive(d.Name, d.VolumeLabel, d.AvailableFreeSpace, d.TotalSize));
            }
            catch { }
        }
        return list;
    }

    /// <summary>Precisa do tamanho do teste + 1 GB de folga (para não encher o disco do cliente).</summary>
    public static bool HasRoom(long free, long testSize) => free >= testSize + (1L << 30);

    /// <summary>MB/s em base 1000.</summary>
    public static double MBps(long bytes, TimeSpan elapsed) => elapsed.TotalSeconds <= 0 ? 0 : bytes / 1e6 / elapsed.TotalSeconds;

    /// <summary>Posição aleatória alinhada em 4 KiB dentro do arquivo (exigência da leitura sem cache).</summary>
    public static long RandomOffset(Random rng, long fileSize) => rng.NextInt64(fileSize / RandBlock) * RandBlock;

    public static Task<SpeedResult> RunAsync(SpeedDrive drive, long size, IProgress<string> progress, CancellationToken cancel,
        double randomSeconds = 5) =>
        Task.Run(() => Run(drive.Root, size, progress, cancel, randomSeconds), cancel);

    static unsafe SpeedResult Run(string root, long size, IProgress<string> progress, CancellationToken cancel, double randomSeconds)
    {
        string path = Path.Combine(root, TestFileName);
        void* mem = NativeMemory.AlignedAlloc(SeqBlock, 4096);
        try
        {
            var seq = new Span<byte>(mem, SeqBlock);
            Random.Shared.NextBytes(seq); // dados aleatórios: SSD que comprime não "trapaceia"
            using var h = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                NoBuffering | FileOptions.WriteThrough, preallocationSize: size);

            // 1. Gravação sequencial (cria o arquivo de teste)
            var sw = Stopwatch.StartNew();
            for (long off = 0; off < size; off += SeqBlock)
            {
                cancel.ThrowIfCancellationRequested();
                RandomAccess.Write(h, seq, off);
                if (off % (64L * SeqBlock) == 0) progress.Report($"Gravação sequencial... {off * 100 / size}%");
            }
            double seqWrite = MBps(size, sw.Elapsed);

            // 2. Leitura sequencial
            sw.Restart();
            for (long off = 0; off < size; off += SeqBlock)
            {
                cancel.ThrowIfCancellationRequested();
                RandomAccess.Read(h, seq, off);
                if (off % (64L * SeqBlock) == 0) progress.Report($"Leitura sequencial... {off * 100 / size}%");
            }
            double seqRead = MBps(size, sw.Elapsed);

            // 3 e 4. Aleatório 4K, uma operação por vez, por alguns segundos
            var rng = new Random(1234);
            (double mbps, double iops) Random4K(bool write)
            {
                long ops = 0;
                var t = Stopwatch.StartNew();
                var limit = TimeSpan.FromSeconds(randomSeconds);
                var buf = new Span<byte>(mem, RandBlock);
                while (t.Elapsed < limit)
                {
                    long off = RandomOffset(rng, size);
                    if (write) RandomAccess.Write(h, buf, off); else RandomAccess.Read(h, buf, off);
                    if (++ops % 256 == 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        progress.Report($"{(write ? "Gravação" : "Leitura")} aleatória 4K... {(int)(t.Elapsed.TotalSeconds * 100 / randomSeconds)}%");
                    }
                }
                return (MBps(ops * RandBlock, t.Elapsed), ops / t.Elapsed.TotalSeconds);
            }
            var rr = Random4K(write: false);
            var rw = Random4K(write: true);
            progress.Report("Apagando o arquivo de teste...");
            return new SpeedResult(root.TrimEnd('\\'), seqRead, seqWrite, rr.mbps, rw.mbps, rr.iops, rw.iops);
        }
        finally
        {
            NativeMemory.AlignedFree(mem);
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>Que tipo de disco os números parecem e o que dizer ao cliente.</summary>
    public static (string Label, string Color, string Advice) Classify(SpeedResult r)
    {
        bool hddLike = r.RandRead < 5; // HD mecânico faz ~0,5 a 2 MB/s em 4K; qualquer SSD passa de 15
        if (r.SeqRead >= 1500)
            return ("Muito rápido · típico de SSD NVMe", Palette.Good, r.RandRead < 20 ? "Sequencial ótimo, mas 4K baixo: confira se o driver do armazenamento está atualizado." : "");
        if (r.SeqRead >= 350 && !hddLike)
            return ("Rápido · típico de SSD SATA", Palette.Good, "");
        if (hddLike)
            return ("Lento · típico de HD mecânico", Palette.Warn,
                "O 4K é o que mais pesa no dia a dia (abrir o Windows e programas). Trocar por SSD deixa o PC muito mais rápido.");
        if (r.SeqRead >= 80)
            return ("Abaixo do esperado para SSD", Palette.Warn,
                "SSD lento: pode estar quase cheio, em porta/cabo SATA ruim, com firmware antigo ou se desgastando (veja Discos).");
        return ("Muito lento", Palette.Bad,
            "Pendrive USB 2.0, disco com defeito ou cabo ruim. Se for o disco do Windows, confira o S.M.A.R.T. em Discos.");
    }
}
