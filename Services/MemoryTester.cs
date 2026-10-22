using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;

namespace UtiliTech.Services;

public record MemoryTestStatus(double Percent, string Stage, long Errors);
public record MemoryTestResult(long TestedBytes, int Passes, long Errors, TimeSpan Elapsed, bool Cancelled);

/// <summary>
/// Teste de RAM dentro do Windows: reserva parte da memória livre, grava padrões e confere.
/// Não alcança a área usada pelo próprio Windows; para teste completo use o Diagnóstico de Memória (mdsched).
/// </summary>
public static class MemoryTester
{
    const int ChunkLongs = 32 * 1024 * 1024; // 256 MB por bloco

    public static readonly string[] PatternNames = { "Zeros", "Uns", "Xadrez 0xAA", "Xadrez 0x55", "Endereço", "Aleatório" };

    public static Task<MemoryTestResult> RunAsync(long targetBytes, int passes, IProgress<MemoryTestStatus> progress,
        Action<string> log, CancellationToken ct) =>
        Task.Run(() => Run(targetBytes, passes, progress, log, ct));

    static MemoryTestResult Run(long targetBytes, int passes, IProgress<MemoryTestStatus> progress, Action<string> log, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var chunks = new List<ulong[]>();
        long allocated = 0;
        log($"Reservando {Fmt.Bytes(targetBytes)} de memória...");
        try
        {
            while (allocated < targetBytes && !ct.IsCancellationRequested)
            {
                int n = (int)Math.Min(ChunkLongs, (targetBytes - allocated) / 8);
                if (n < 1024) break;
                chunks.Add(GC.AllocateUninitializedArray<ulong>(n));
                allocated += (long)n * 8;
            }
        }
        catch (OutOfMemoryException)
        {
            log("Memória insuficiente para reservar tudo; seguindo com o que foi possível.");
        }
        log($"Testando {Fmt.Bytes(allocated)} em {chunks.Count} blocos, {passes} passada(s), {Environment.ProcessorCount} threads.");

        long errors = 0, done = 0;
        long totalSteps = Math.Max(1, (long)passes * PatternNames.Length * chunks.Count * 2);
        int passesDone = 0;
        bool cancelled = false;
        var po = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };

        void Step(string stage) =>
            progress.Report(new MemoryTestStatus(Interlocked.Increment(ref done) * 100.0 / totalSteps, stage, Interlocked.Read(ref errors)));

        try
        {
            for (int pass = 1; pass <= passes; pass++)
            {
                for (int p = 0; p < PatternNames.Length; p++)
                {
                    ulong seed = (ulong)(pass * 7919 + p * 104729) | 1;
                    string stage = $"Passada {pass}/{passes} · {PatternNames[p]}";
                    int pattern = p;
                    // Grava tudo antes de conferir, para os dados ficarem um tempo parados na RAM
                    Parallel.For(0, chunks.Count, po, i => { Fill(chunks[i], pattern, seed, i); Step(stage + " · gravando"); });
                    Parallel.For(0, chunks.Count, po, i =>
                    {
                        long e = Verify(chunks[i], pattern, seed, i);
                        if (e > 0)
                        {
                            Interlocked.Add(ref errors, e);
                            log($"ERRO: {e:N0} valor(es) incorreto(s) no bloco {i} (padrão {PatternNames[pattern]}, passada {pass})");
                        }
                        Step(stage + " · conferindo");
                    });
                }
                passesDone = pass;
                log($"Passada {pass} concluída · erros até agora: {Interlocked.Read(ref errors):N0}");
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            log("Teste interrompido pelo usuário.");
        }

        chunks.Clear();
        GC.Collect();
        return new MemoryTestResult(allocated, passesDone, errors, sw.Elapsed, cancelled);
    }

    static ulong Constant(int p) => p switch
    {
        0 => 0UL,
        1 => ulong.MaxValue,
        2 => 0xAAAAAAAAAAAAAAAAUL,
        _ => 0x5555555555555555UL
    };

    // SplitMix64: valor pseudoaleatório derivado só do índice, então gravar e conferir podem rodar em paralelo
    static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    // internal: também usados pelo teste de estresse (carga de RAM)
    internal static void Fill(ulong[] a, int p, ulong seed, int chunk)
    {
        switch (p)
        {
            case < 4:
                Array.Fill(a, Constant(p));
                break;
            case 4:
            {
                ulong b = (ulong)chunk << 32;
                for (int i = 0; i < a.Length; i++) a[i] = (b + (ulong)i) ^ seed;
                break;
            }
            default:
            {
                ulong b = seed ^ ((ulong)chunk << 40);
                for (int i = 0; i < a.Length; i++) a[i] = Mix(b + (ulong)i);
                break;
            }
        }
    }

    internal static long Verify(ulong[] a, int p, ulong seed, int chunk)
    {
        long e = 0;
        switch (p)
        {
            case < 4:
            {
                ulong v = Constant(p);
                for (int i = 0; i < a.Length; i++) if (a[i] != v) e++;
                break;
            }
            case 4:
            {
                ulong b = (ulong)chunk << 32;
                for (int i = 0; i < a.Length; i++) if (a[i] != ((b + (ulong)i) ^ seed)) e++;
                break;
            }
            default:
            {
                ulong b = seed ^ ((ulong)chunk << 40);
                for (int i = 0; i < a.Length; i++) if (a[i] != Mix(b + (ulong)i)) e++;
                break;
            }
        }
        return e;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    public static (long Total, long Available) GetStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? ((long)m.ullTotalPhys, (long)m.ullAvailPhys) : (0, 0);
    }

    /// <summary>Abre o Diagnóstico de Memória do Windows (ele mesmo pergunta se reinicia agora).</summary>
    public static void ScheduleWindowsDiagnostic() =>
        Process.Start(new ProcessStartInfo("mdsched.exe") { UseShellExecute = true });

    /// <summary>Último resultado do Diagnóstico de Memória do Windows, lido do log Sistema.</summary>
    public static string? LastWindowsDiagnostic()
    {
        var query = new EventLogQuery("System", PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']]]") { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        using var ev = reader.ReadEvent();
        if (ev == null) return null;
        string verdict = ev.Id switch
        {
            1201 => "nenhum erro encontrado",
            1202 => "ERROS DE HARDWARE encontrados — troque/teste os pentes",
            _ => ev.FormatDescription() ?? $"evento {ev.Id}"
        };
        return $"{ev.TimeCreated:dd/MM/yyyy HH:mm} — {verdict}";
    }
}
