using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace UtiliTech.Services;

/// <summary>Um atendimento guardado no histórico (atendimento.json dentro da pasta dele).</summary>
public class AttendanceRecord
{
    public DateTime Data { get; set; }
    public string Cliente { get; set; } = "";
    public string OrdemServico { get; set; } = "";
    public string Tecnico { get; set; } = "";
    public string Observacoes { get; set; } = "";
    public string Computador { get; set; } = "";
    public string Fabricante { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string NumeroSerie { get; set; } = "";
    public string Windows { get; set; } = "";
    /// <summary>Veredito de cada disco ("Samsung SSD 980: Bom").</summary>
    public List<string> Discos { get; set; } = new();
    public string Bateria { get; set; } = "";
    public string Testes { get; set; } = "";
    /// <summary>Arquivos guardados na pasta do atendimento (relatório, planilha).</summary>
    public List<string> Arquivos { get; set; } = new();

    /// <summary>Pasta do atendimento (não vai para o JSON).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Folder { get; set; } = "";

    // Textos prontos para a tela
    [System.Text.Json.Serialization.JsonIgnore]
    public string Title => (Cliente.Length > 0 ? Cliente : "Sem cliente") +
        (OrdemServico.Length == 0 ? "" : OrdemServico.StartsWith("OS", StringComparison.OrdinalIgnoreCase) ? $" · {OrdemServico}" : $" · OS {OrdemServico}");
    [System.Text.Json.Serialization.JsonIgnore]
    public string When => Data.ToString("dd/MM/yyyy HH:mm") + (Tecnico.Length > 0 ? $" · {Tecnico}" : "");
    [System.Text.Json.Serialization.JsonIgnore]
    public string Machine => string.Join(" · ", new[] { Computador, $"{Fabricante} {Modelo}".Trim(), NumeroSerie.Length > 0 ? "S/N " + NumeroSerie : "" }
        .Where(s => s.Length > 0));
    [System.Text.Json.Serialization.JsonIgnore]
    public string Health => string.Join(" · ", Discos.Select(d => "Disco " + d).Append(Bateria.Length > 0 ? "Bateria: " + Bateria : "").Where(s => s.Length > 0));
    [System.Text.Json.Serialization.JsonIgnore]
    public List<AttendanceFile> Files => Arquivos.Select(a => new AttendanceFile(a, Path.Combine(Folder, a))).ToList();
}

/// <summary>Arquivo de um atendimento, com o rótulo do botão (Relatório / Planilha).</summary>
public record AttendanceFile(string Name, string Path)
{
    public string Label => System.IO.Path.GetExtension(Name).ToLowerInvariant() switch
    {
        ".html" => "Relatório",
        ".xlsx" => "Planilha",
        ".csv" => "Planilha CSV",
        _ => Name,
    };
}

/// <summary>
/// Histórico de atendimentos no pendrive: atendimentos\AAAA-MM-DD_HHmm_NOMEDOPC\ com o atendimento.json e cópias
/// do relatório e da planilha exportados. Relatório e planilha do mesmo PC, cliente e OS no mesmo dia vão para a mesma pasta.
/// </summary>
public static class AttendanceHistory
{
    const string RecordFile = "atendimento.json";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string? _folder;

    /// <summary>Pasta "atendimentos" ao lado do .exe (ou no AppData se a pasta não aceitar escrita).</summary>
    public static string Folder
    {
        get => _folder ??= ResolveFolder();
        internal set => _folder = value; // testes usam uma pasta temporária
    }

    static string ResolveFolder()
    {
        foreach (var dir in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "atendimentos"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UtiliTech", "atendimentos"),
                 })
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".teste");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch { }
        }
        return Path.Combine(Path.GetTempPath(), "UtiliTech-atendimentos");
    }

    /// <summary>Monta o registro a partir dos dados do relatório (sem gravar nada).</summary>
    public static AttendanceRecord Build(Atendimento at, List<InfoSection> sections, List<DiskReport> disks, BatteryReport battery,
        string? tests, DateTime when)
    {
        string Find(string label) =>
            sections.SelectMany(s => s.Items).FirstOrDefault(i => i.Label == label)?.Value?.Trim() ?? "";

        return new AttendanceRecord
        {
            Data = when,
            Cliente = at.Cliente.Trim(),
            OrdemServico = at.OrdemServico.Trim(),
            Tecnico = at.Tecnico.Trim(),
            Observacoes = at.Observacoes.Trim(),
            Computador = Find("Nome do computador") is { Length: > 0 } pc ? pc : Environment.MachineName,
            Fabricante = Find("Fabricante"),
            Modelo = Find("Modelo"),
            NumeroSerie = Find("Número de série"),
            Windows = $"{Find("Sistema")} {Find("Versão")}".Trim(),
            Discos = disks.Select(d => $"{d.Name}: {d.Verdict}").ToList(),
            Bateria = battery.Present ? battery.Verdict + (battery.HealthPercent is { } h ? $" ({h:0}% de saúde)" : "") : "",
            Testes = tests ?? "",
        };
    }

    /// <summary>
    /// Guarda o atendimento e uma cópia do arquivo exportado. Se já existe um atendimento do mesmo PC, cliente e OS
    /// no mesmo dia, usa a pasta dele (atualizando os dados). Devolve a pasta do atendimento.
    /// </summary>
    public static string Save(AttendanceRecord record, string? exportedFile, string? folder = null)
    {
        var existing = LoadAll().FirstOrDefault(r => SameAttendance(r, record));
        folder = existing?.Folder ?? folder ?? NewFolder(record);
        Directory.CreateDirectory(folder);

        record.Folder = folder;
        record.Arquivos = existing?.Arquivos ?? new();
        if (exportedFile != null && File.Exists(exportedFile))
        {
            string name = Path.GetFileName(exportedFile);
            string target = Path.Combine(folder, name);
            if (!string.Equals(Path.GetFullPath(exportedFile), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(exportedFile, target, overwrite: true);
            if (!record.Arquivos.Contains(name, StringComparer.OrdinalIgnoreCase)) record.Arquivos.Add(name);
        }
        File.WriteAllText(Path.Combine(folder, RecordFile), JsonSerializer.Serialize(record, Json), Encoding.UTF8);
        Logger.Info($"Atendimento guardado em {folder}");
        return folder;
    }

    /// <summary>
    /// Pasta onde este atendimento fica: a do atendimento já guardado (mesmo PC, cliente e OS no dia) ou uma nova,
    /// ainda não criada. Usada para o "Salvar como" já abrir no lugar certo.
    /// </summary>
    public static string FolderFor(AttendanceRecord record) =>
        LoadAll().FirstOrDefault(r => SameAttendance(r, record))?.Folder ?? NewFolder(record);

    static bool SameAttendance(AttendanceRecord a, AttendanceRecord b) =>
        a.Data.Date == b.Data.Date &&
        string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Cliente, b.Cliente, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.OrdemServico, b.OrdemServico, StringComparison.OrdinalIgnoreCase);

    /// <summary>O que identifica a máquina: o nº de série (sobrevive à formatação) ou, sem ele, o nome do PC.</summary>
    static string Key(AttendanceRecord r) => r.NumeroSerie.Length > 0 ? r.NumeroSerie : r.Computador;

    static string NewFolder(AttendanceRecord r)
    {
        string name = $"{r.Data:yyyy-MM-dd_HHmm}_{Safe(r.Computador)}";
        string path = Path.Combine(Folder, name);
        for (int i = 2; Directory.Exists(path); i++) path = Path.Combine(Folder, $"{name}_{i}");
        return path;
    }

    static string Safe(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(s.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray()).Trim('-', '.');
        return clean.Length == 0 ? "PC" : clean.Length > 40 ? clean[..40] : clean;
    }

    /// <summary>Todos os atendimentos, do mais novo para o mais antigo. Pastas sem JSON ou com JSON estragado são puladas.</summary>
    public static List<AttendanceRecord> LoadAll()
    {
        var list = new List<AttendanceRecord>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var dir in Directory.GetDirectories(Folder))
        {
            try
            {
                var file = Path.Combine(dir, RecordFile);
                if (!File.Exists(file)) continue;
                if (JsonSerializer.Deserialize<AttendanceRecord>(File.ReadAllText(file)) is not { } r) continue;
                r.Folder = dir;
                r.Arquivos = r.Arquivos.Where(a => File.Exists(Path.Combine(dir, a))).ToList();
                list.Add(r);
            }
            catch (Exception ex) { Logger.Error($"Atendimento ilegível em {dir}", ex); }
        }
        return list.OrderByDescending(r => r.Data).ToList();
    }

    /// <summary>Busca por cliente, OS, nº de série, nome do PC, modelo, técnico ou observações (sem diferenciar acentos e maiúsculas).</summary>
    public static List<AttendanceRecord> Search(IEnumerable<AttendanceRecord> records, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return records.ToList();
        var cmp = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
        const CompareOptions opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return records.Where(r =>
        {
            string all = string.Join(" | ", r.Cliente, r.OrdemServico, r.NumeroSerie, r.Computador, r.Fabricante, r.Modelo,
                r.Tecnico, r.Observacoes, r.Data.ToString("dd/MM/yyyy"));
            return terms.All(t => cmp.IndexOf(all, t, opts) >= 0);
        }).ToList();
    }
}
