namespace UtiliTech.Services;

public record AlertInfo(string Title, string Message, string Color)
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string TimeText => Time.ToString("dd/MM HH:mm:ss");
}

/// <summary>
/// Vigia os sensores em segundo plano e avisa quando CPU/GPU passam do limite ou um disco dá sinal de falha.
/// Cada tipo de aviso se repete no máximo a cada 5 minutos.
/// </summary>
public static class AlertWatcher
{
    static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    static readonly Dictionary<string, DateTime> LastFired = new();
    static readonly List<AlertInfo> HistoryList = new();
    static bool _started;

    /// <summary>Avisos desta sessão, do mais recente para o mais antigo (máx. 50).</summary>
    public static List<AlertInfo> History
    {
        get { lock (HistoryList) return HistoryList.ToList(); }
    }

    /// <summary>Chega numa thread de fundo.</summary>
    public static event Action<AlertInfo>? Raised;

    /// <summary>Desliga os avisos de temperatura enquanto o teste de estresse controla o limite.</summary>
    public static bool Suppressed { get; set; }

    public static void Start()
    {
        if (_started) return;
        _started = true;
        SensorHub.Updated += Check;
        _ = Task.Run(CheckDisks);
    }

    static void Check(MonitorSnapshot s)
    {
        var cfg = AppSettings.Current;
        if (!cfg.AlertsEnabled || Suppressed) return;
        if (s.CpuTemp is { } cpu && cpu >= cfg.CpuAlertLimit)
            Fire("cpu", "Processador quente", $"{s.CpuName} chegou a {cpu:0} °C (limite {cfg.CpuAlertLimit} °C).", Palette.Bad);
        if (s.GpuTemp is { } gpu && gpu >= cfg.GpuAlertLimit)
            Fire("gpu", "Placa de vídeo quente", $"{s.GpuName} chegou a {gpu:0} °C (limite {cfg.GpuAlertLimit} °C).", Palette.Bad);
    }

    static void CheckDisks()
    {
        try
        {
            if (!AppSettings.Current.AlertsEnabled) return;
            foreach (var d in DiskHealth.Collect().Where(d => d.VerdictColor == Palette.Bad))
                Fire("disk-" + d.Name, "Disco com problema", $"{d.Name}: {d.Reasons.FirstOrDefault()}. Faça backup e veja a tela Discos.", Palette.Bad);
            int predicted = DiskHealth.PredictedFailures();
            if (predicted > 0)
                Fire("disk-predict", "Falha de disco prevista", $"O Windows prevê falha em {predicted} disco(s). Faça backup imediatamente.", Palette.Bad);
        }
        catch { }
    }

    static void Fire(string key, string title, string message, string color)
    {
        lock (LastFired)
        {
            if (LastFired.TryGetValue(key, out var last) && DateTime.Now - last < Cooldown) return;
            LastFired[key] = DateTime.Now;
        }
        var alert = new AlertInfo(title, message, color);
        lock (HistoryList)
        {
            HistoryList.Insert(0, alert);
            if (HistoryList.Count > 50) HistoryList.RemoveAt(HistoryList.Count - 1);
        }
        Raised?.Invoke(alert);
    }

    public static void Test() =>
        Raised?.Invoke(new AlertInfo("Teste de aviso", "Os alertas estão funcionando. Eles aparecem aqui, por cima das outras janelas.", Palette.Accent));
}
