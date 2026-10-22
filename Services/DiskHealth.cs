using System.IO;
using System.Management;

namespace UtiliTech.Services;

public class DiskReport
{
    public string Name { get; set; } = "";
    public string Verdict { get; set; } = "Bom";
    public string VerdictColor { get; set; } = Palette.Good;
    public List<InfoItem> Items { get; } = new();
    public List<string> Reasons { get; } = new();
    public string ReasonsText => string.Join("\n", Reasons.Select(r => "• " + r));
}

public record VolumeInfo(string Name, string Detail, double UsedPercent, string BarColor, long FreeBytes = 0, long TotalBytes = 0);

public static class DiskHealth
{
    const string StorageScope = @"root\Microsoft\Windows\Storage";

    public static List<DiskReport> Collect()
    {
        var list = new List<DiskReport>();
        foreach (var d in Wmi.Query("SELECT * FROM MSFT_PhysicalDisk", StorageScope))
            list.Add(Analyze(d));
        return list;
    }

    static DiskReport Analyze(ManagementObject d)
    {
        var r = new DiskReport { Name = Wmi.Str(d, "FriendlyName") };
        int media = (int)Wmi.Long(d, "MediaType");
        int health = (int)Wmi.Long(d, "HealthStatus");

        r.Items.Add(new("Tipo", $"{MediaName(media)} · {BusName((int)Wmi.Long(d, "BusType"))}"));
        r.Items.Add(new("Capacidade", Fmt.DiskSize(Wmi.Long(d, "Size"))));
        r.Items.Add(new("Número de série", Wmi.Str(d, "SerialNumber")));
        r.Items.Add(new("Firmware", Wmi.Str(d, "FirmwareVersion")));
        r.Items.Add(new("Status no Windows", health switch { 0 => "Saudável", 1 => "Aviso", 2 => "Não saudável", _ => "Desconhecido" }));

        var bad = new List<string>();
        var warn = new List<string>();
        if (health == 2) bad.Add("O Windows marca este disco como NÃO saudável");
        if (health == 1) warn.Add("O Windows emitiu um aviso para este disco");

        ManagementBaseObject? c = null;
        try
        {
            foreach (ManagementBaseObject x in d.GetRelated("MSFT_StorageReliabilityCounter")) { c = x; break; }
        }
        catch { }

        if (c == null)
        {
            r.Reasons.Add("Contadores S.M.A.R.T. não disponíveis (comum em USB, RAID ou sem administrador)");
        }
        else
        {
            if (Wmi.LongN(c, "Temperature") is long temp && temp > 0)
            {
                long max = Wmi.Long(c, "TemperatureMax");
                r.Items.Add(new("Temperatura", $"{temp} °C" + (max > 0 ? $" (limite {max} °C)" : "")));
                if (temp >= 60) warn.Add($"Temperatura alta ({temp} °C)");
            }
            if (Wmi.LongN(c, "PowerOnHours") is { } hours)
            {
                r.Items.Add(new("Horas ligado", $"{hours:N0} h (~{hours / 24:N0} dias)"));
                if (media == 3 && hours > 35_000) warn.Add($"HD com muitas horas de uso ({hours:N0} h)");
            }
            if (Wmi.LongN(c, "StartStopCycleCount") is { } cycles)
                r.Items.Add(new("Ciclos liga/desliga", $"{cycles:N0}"));
            if (Wmi.LongN(c, "Wear") is { } wear && media == 4)
            {
                r.Items.Add(new("Desgaste do SSD", $"{wear}% usado (≈ {100 - wear}% de vida restante)"));
                if (wear >= 90) bad.Add($"SSD com {wear}% da vida útil gasta");
                else if (wear >= 70) warn.Add($"SSD com {wear}% da vida útil gasta");
            }
            long readUnc = Wmi.Long(c, "ReadErrorsUncorrected");
            long writeUnc = Wmi.Long(c, "WriteErrorsUncorrected");
            r.Items.Add(new("Erros não corrigidos", $"leitura {readUnc:N0} · escrita {writeUnc:N0}"));
            if (readUnc > 0) bad.Add($"{readUnc:N0} erro(s) de leitura não corrigidos");
            if (writeUnc > 0) bad.Add($"{writeUnc:N0} erro(s) de escrita não corrigidos");
        }

        if (bad.Count > 0) { r.Verdict = "Ruim"; r.VerdictColor = Palette.Bad; }
        else if (warn.Count > 0) { r.Verdict = "Atenção"; r.VerdictColor = Palette.Warn; }
        r.Reasons.InsertRange(0, bad.Concat(warn));
        if (r.Reasons.Count == 0) r.Reasons.Add("Nenhum problema encontrado");
        r.Items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Value));
        return r;
    }

    /// <summary>Previsão de falha do driver (atributo SMART "threshold exceeded").</summary>
    public static int PredictedFailures() =>
        Wmi.Query("SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus", @"root\wmi")
           .Count(x => Wmi.Get(x, "PredictFailure") is true);

    public static List<VolumeInfo> Volumes()
    {
        var list = new List<VolumeInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType == DriveType.CDRom) continue;
                double used = d.TotalSize > 0 ? 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize : 0;
                string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? DriveTypeName(d.DriveType) : d.VolumeLabel;
                string color = used >= 95 ? Palette.Bad : used >= 85 ? Palette.Warn : Palette.Accent;
                list.Add(new($"{d.Name.TrimEnd('\\')}  {label}",
                    $"{Fmt.Bytes(d.TotalFreeSpace)} livres de {Fmt.Bytes(d.TotalSize)} · {d.DriveFormat} · {DriveTypeName(d.DriveType)}",
                    used, color, d.TotalFreeSpace, d.TotalSize));
            }
            catch { }
        }
        return list;
    }

    static string DriveTypeName(DriveType t) => t switch
    {
        DriveType.Fixed => "Disco local",
        DriveType.Removable => "Removível",
        DriveType.Network => "Rede",
        _ => t.ToString()
    };

    static string MediaName(int m) => m switch { 3 => "HD", 4 => "SSD", 5 => "SCM", _ => "Tipo desconhecido" };

    static string BusName(int b) => b switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "FireWire", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID",
        9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 15 => "Virtual", 16 => "Storage Spaces",
        17 => "NVMe", 18 => "SCM", 19 => "UFS", _ => "barramento desconhecido"
    };
}
