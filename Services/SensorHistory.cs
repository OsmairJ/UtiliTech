namespace UtiliTech.Services;

public record SensorSample(DateTime Time, MonitorSnapshot Snapshot);

/// <summary>Últimos 15 minutos de leituras (para o gráfico da Visão geral), desde que o app abriu.</summary>
public static class SensorHistory
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    static readonly List<SensorSample> Samples = new();
    static bool _started;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        SensorHub.Updated += s =>
        {
            lock (Samples)
            {
                Samples.Add(new SensorSample(DateTime.Now, s));
                var limit = DateTime.Now - Window;
                int old = Samples.FindIndex(x => x.Time >= limit);
                if (old > 0) Samples.RemoveRange(0, old);
            }
        };
    }

    public static List<SensorSample> Since(TimeSpan span)
    {
        var from = DateTime.Now - span;
        lock (Samples) return Samples.Where(s => s.Time >= from).ToList();
    }
}
