namespace UtiliTech.Services;

/// <summary>
/// Carga máxima na CPU (todas as threads, contas de ponto flutuante e inteiros) e, opcionalmente, na RAM
/// (grava e confere padrões em parte da memória livre). Quem decide quando parar é a tela, olhando a temperatura.
/// </summary>
public sealed class StressTest
{
    const int RamChunkLongs = 8 * 1024 * 1024; // 64 MB por bloco

    readonly List<Thread> _threads = new();
    CancellationTokenSource? _cts;
    long _ramErrors;
    long _ramBytes;
    static double _sink; // impede que o compilador descarte as contas

    public bool Running => _cts != null;
    public long RamErrors => Interlocked.Read(ref _ramErrors);
    public long RamBytes => Interlocked.Read(ref _ramBytes);

    public void Start(bool includeRam)
    {
        if (Running) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ramErrors = 0;
        _ramBytes = 0;

        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            int seed = i;
            // Abaixo do normal: a CPU vai a 100%, mas a janela continua respondendo
            var t = new Thread(() => CpuWork(seed, token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Estresse CPU " + i };
            _threads.Add(t);
            t.Start();
        }
        if (includeRam)
        {
            var t = new Thread(() => RamWork(token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Estresse RAM" };
            _threads.Add(t);
            t.Start();
        }
    }

    public void Stop()
    {
        if (_cts == null) return;
        _cts.Cancel();
        foreach (var t in _threads) t.Join(2000);
        _threads.Clear();
        _cts.Dispose();
        _cts = null;
        GC.Collect();
    }

    static void CpuWork(int seed, CancellationToken token)
    {
        double a = 1.0001 + seed, b = 0.9999;
        ulong x = (ulong)seed + 0x9E3779B97F4A7C15UL;
        while (!token.IsCancellationRequested)
        {
            for (int i = 0; i < 4_000_000; i++)
            {
                a = a * b + 0.000001;
                b = Math.Sqrt(a) * 0.999 + 0.0001;
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
            }
            if (double.IsNaN(a) || a > 1e9) a = 1.0001 + seed;
            Volatile.Write(ref _sink, a + x);
        }
    }

    void RamWork(CancellationToken token)
    {
        // 40% da memória livre, deixando folga para o Windows não começar a paginar
        long target = (long)(MemoryTester.GetStatus().Available * 0.4);
        var chunks = new List<ulong[]>();
        try
        {
            while ((long)chunks.Count * RamChunkLongs * 8 < target && !token.IsCancellationRequested)
            {
                chunks.Add(GC.AllocateUninitializedArray<ulong>(RamChunkLongs));
                Interlocked.Add(ref _ramBytes, (long)RamChunkLongs * 8);
            }
        }
        catch (OutOfMemoryException) { }

        ulong pass = 1;
        while (!token.IsCancellationRequested)
        {
            ulong seed = pass++ * 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < chunks.Count && !token.IsCancellationRequested; i++)
                MemoryTester.Fill(chunks[i], 5, seed, i);
            for (int i = 0; i < chunks.Count && !token.IsCancellationRequested; i++)
            {
                long e = MemoryTester.Verify(chunks[i], 5, seed, i);
                if (e > 0) Interlocked.Add(ref _ramErrors, e);
            }
        }
        chunks.Clear();
    }
}
