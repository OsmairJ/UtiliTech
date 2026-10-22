using System.Diagnostics;
using System.IO;

namespace UtiliTech.Services;

public class BatteryReport
{
    public bool Present { get; init; }
    public string Verdict { get; set; } = "Desconhecido";
    public string VerdictColor { get; set; } = Palette.Muted;
    public double? HealthPercent { get; set; }
    public List<InfoItem> Items { get; } = new();
    public List<string> Reasons { get; } = new();
}

public static class BatteryInfo
{
    const string WmiRoot = @"root\wmi";

    public static BatteryReport Collect()
    {
        var wb = Wmi.Query("SELECT Name, EstimatedChargeRemaining, EstimatedRunTime FROM Win32_Battery").FirstOrDefault();
        if (wb == null) return new BatteryReport { Present = false };

        var r = new BatteryReport { Present = true };
        var stat = Wmi.Query("SELECT * FROM BatteryStaticData", WmiRoot).FirstOrDefault();
        var full = Wmi.Query("SELECT * FROM BatteryFullChargedCapacity", WmiRoot).FirstOrDefault();
        var cyc = Wmi.Query("SELECT * FROM BatteryCycleCount", WmiRoot).FirstOrDefault();
        var st = Wmi.Query("SELECT * FROM BatteryStatus", WmiRoot).FirstOrDefault();

        string name = stat != null ? Wmi.Str(stat, "DeviceName") : "";
        r.Items.Add(new("Bateria", name.Length > 0 ? name : Wmi.Str(wb, "Name")));
        if (stat != null)
        {
            r.Items.Add(new("Fabricante", Wmi.Str(stat, "ManufactureName")));
            r.Items.Add(new("Número de série", Wmi.Str(stat, "SerialNumber")));
        }
        r.Items.Add(new("Carga atual", $"{Wmi.Long(wb, "EstimatedChargeRemaining")}%"));

        bool? online = st != null ? Wmi.Get(st, "PowerOnline") as bool? : null;
        if (online != null)
        {
            bool charging = st != null && Wmi.Get(st, "Charging") is true;
            r.Items.Add(new("Estado", online == true ? (charging ? "Na tomada, carregando" : "Na tomada") : "Na bateria"));
        }
        long runtime = Wmi.Long(wb, "EstimatedRunTime");
        if (online != true && runtime is > 0 and < 6000)
            r.Items.Add(new("Autonomia estimada", $"{runtime / 60}h {runtime % 60}min"));

        long design = stat != null ? Wmi.Long(stat, "DesignedCapacity") : 0;
        long fullCap = full != null ? Wmi.Long(full, "FullChargedCapacity") : 0;
        if (design > 0) r.Items.Add(new("Capacidade de projeto", $"{design:N0} mWh"));
        if (fullCap > 0) r.Items.Add(new("Capacidade atual (carga completa)", $"{fullCap:N0} mWh"));

        long cycles = cyc != null ? Wmi.Long(cyc, "CycleCount") : 0;
        if (cycles > 0) r.Items.Add(new("Ciclos de carga", $"{cycles:N0}"));

        long mv = st != null ? Wmi.Long(st, "Voltage") : 0;
        if (mv > 0) r.Items.Add(new("Tensão", $"{mv / 1000.0:0.00} V"));

        if (design > 0 && fullCap > 0)
        {
            double health = Math.Min(100, fullCap * 100.0 / design);
            r.HealthPercent = health;
            r.Items.Add(new("Saúde", $"{health:0}% (desgaste {100 - health:0}%)"));
            if (health >= 80) { r.Verdict = "Bom"; r.VerdictColor = Palette.Good; r.Reasons.Add("Bateria em bom estado"); }
            else if (health >= 60) { r.Verdict = "Atenção"; r.VerdictColor = Palette.Warn; r.Reasons.Add($"Desgaste de {100 - health:0}%: autonomia reduzida"); }
            else { r.Verdict = "Ruim"; r.VerdictColor = Palette.Bad; r.Reasons.Add($"Desgaste de {100 - health:0}%: recomenda-se a troca da bateria"); }
        }
        else
        {
            r.Reasons.Add("O Windows não informou a capacidade da bateria. Gere o relatório do powercfg para mais detalhes.");
        }
        if (cycles > 800) r.Reasons.Add($"Muitos ciclos de carga ({cycles:N0})");

        r.Items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Value));
        return r;
    }

    /// <summary>Gera o relatório completo do Windows (powercfg /batteryreport) e devolve o caminho do HTML.</summary>
    public static async Task<string> GeneratePowercfgReportAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"battery-report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        var psi = new ProcessStartInfo("powercfg", $"/batteryreport /output \"{path}\"") { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível executar o powercfg.");
        await p.WaitForExitAsync();
        if (!File.Exists(path)) throw new InvalidOperationException($"O powercfg terminou com código {p.ExitCode} e não gerou o relatório.");
        return path;
    }
}
