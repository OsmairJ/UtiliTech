namespace UtiliTech.Services;

/// <summary>
/// Leitura única dos sensores para o app inteiro (Monitor, teste de estresse, alertas e rodapé do menu).
/// Lê a cada 1 s enquanto alguém pede ritmo rápido (tela aberta, teste rodando) e a cada 5 s no resto do tempo.
/// O evento <see cref="Updated"/> chega numa thread de fundo.
/// </summary>
public static class SensorHub
{
    static readonly object Gate = new();
    static readonly SemaphoreSlim Wake = new(0);
    static Task<HardwareMonitor?>? _opening;
    static CancellationTokenSource? _loop;
    static int _fastRequests;

    public static HardwareMonitor? Monitor { get; private set; }
    public static MonitorSnapshot? Last { get; private set; }
    public static event Action<MonitorSnapshot>? Updated;

    /// <summary>Abre os sensores (uma vez só) e começa a ler.</summary>
    public static Task<HardwareMonitor?> StartAsync()
    {
        lock (Gate) return _opening ??= Task.Run(Open);
    }

    static HardwareMonitor? Open()
    {
        HardwareMonitor m;
        try
        {
            m = new HardwareMonitor();
            m.Open();
        }
        catch { return null; }

        Monitor = m;
        Last = m.Snapshot();
        var cts = new CancellationTokenSource();
        _loop = cts;
        _ = Task.Run(() => Loop(m, cts.Token));
        return m;
    }

    static async Task Loop(HardwareMonitor m, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                m.Update();
                var snap = m.Snapshot();
                Last = snap;
                Updated?.Invoke(snap);
            }
            catch { }
            try { await Wake.WaitAsync(Volatile.Read(ref _fastRequests) > 0 ? 1000 : 5000, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public static void RequestFast()
    {
        Interlocked.Increment(ref _fastRequests);
        Wake.Release();
    }

    public static void ReleaseFast()
    {
        if (Interlocked.Decrement(ref _fastRequests) < 0) Interlocked.Exchange(ref _fastRequests, 0);
    }

    /// <summary>Fecha e abre de novo (ex.: depois de instalar o driver PawnIO).</summary>
    public static Task<HardwareMonitor?> RestartAsync()
    {
        Shutdown();
        return StartAsync();
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            _loop?.Cancel();
            _loop = null;
            Monitor?.Dispose();
            Monitor = null;
            Last = null;
            _opening = null;
        }
    }
}
