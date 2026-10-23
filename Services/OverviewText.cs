namespace UtiliTech.Services;

/// <summary>Textos e escalas da Visão geral, sem interface (para testar).</summary>
public static class OverviewText
{
    /// <summary>"Positivo Informatica SA" + "Positivo Master D810" → "Positivo Master D810" (não repete a marca).</summary>
    public static string DisplayModel(string maker, string model, string fallback)
    {
        maker = maker.Trim();
        model = model.Trim();
        if (maker.Length == 0 && model.Length == 0) return fallback;
        string brand = maker.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return brand.Length > 0 && model.StartsWith(brand, StringComparison.OrdinalIgnoreCase) ? model : $"{maker} {model}".Trim();
    }

    /// <summary>"Intel(R) Core(TM) i7-6700 CPU @ 3.40GHz" → "Intel Core i7-6700 @ 3.40GHz".</summary>
    public static string ShortCpuName(string name) =>
        name.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "").Replace("  ", " ").Trim();

    public static string Temp(float? t) => t is { } x ? $"{x:0} °C" : "—";
    public static string Pct(float? v) => v is { } x ? $"{x:0} %" : "—";

    /// <summary>Escala do gráfico: carga 0-100%; consumo até 20% acima do pico (de 10 em 10 W, mínimo 50); temperatura 20-90 °C (110 se passar de 90).</summary>
    public static (double Min, double Max) ChartRange(string metric, double peak) => metric switch
    {
        "load" => (0, 100),
        "power" => (0, Math.Max(50, Math.Ceiling(peak * 1.2 / 10) * 10)),
        _ => (20, peak > 90 ? 110 : 90),
    };

    /// <summary>Percentual usado do disco.</summary>
    public static double UsedPercent(long total, long free) => total <= 0 ? 0 : 100.0 * (total - free) / total;
}
