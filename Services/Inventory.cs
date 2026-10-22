using System.IO;

namespace UtiliTech.Services;

/// <summary>Uma máquina no inventário: os dados mais recentes dela no histórico e o resumo dos atendimentos.</summary>
public record InventoryMachine(
    string NumeroSerie, string Computador, string Fabricante, string Modelo, string Processador, string Memoria,
    string Armazenamento, string Windows, string Rede, string Discos, string Bateria,
    DateTime Primeiro, DateTime Ultimo, int Atendimentos, string Clientes, string UltimaOS, string UltimoTecnico);

/// <summary>
/// Inventário em lote: uma linha por máquina atendida (identificada pelo nº de série, ou pelo nome se não houver),
/// montado a partir do histórico de atendimentos. Fica em atendimentos\inventario.xlsx no pendrive.
/// </summary>
public static class Inventory
{
    /// <summary>Agrupa os atendimentos por máquina. Cada campo vem do atendimento mais novo que o tenha preenchido.</summary>
    public static List<InventoryMachine> Machines(IEnumerable<AttendanceRecord> records) =>
        records.GroupBy(AttendanceHistory.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var byDate = g.OrderByDescending(r => r.Data).ToList();
                var last = byDate[0];
                string Latest(Func<AttendanceRecord, string> field) =>
                    byDate.Select(field).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
                string LatestList(Func<AttendanceRecord, List<string>> field) =>
                    string.Join("; ", byDate.Select(field).FirstOrDefault(l => l.Count > 0) ?? new());
                return new InventoryMachine(
                    last.NumeroSerie, last.Computador, Latest(r => r.Fabricante), Latest(r => r.Modelo), Latest(r => r.Processador),
                    Latest(r => r.Memoria), Latest(r => r.Armazenamento), Latest(r => r.Windows), Latest(r => r.Rede),
                    LatestList(r => r.Discos), Latest(r => r.Bateria),
                    byDate[^1].Data, last.Data, byDate.Count,
                    string.Join(", ", byDate.Select(r => r.Cliente).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)),
                    last.OrdemServico, last.Tecnico);
            })
            .OrderByDescending(m => m.Ultimo)
            .ToList();

    static readonly string[] MachineHeader =
    {
        "Nº de série", "Computador", "Fabricante", "Modelo", "Processador", "Memória", "Armazenamento", "Windows", "Rede (MAC · IP)",
        "Saúde dos discos", "Bateria", "Primeiro atendimento", "Último atendimento", "Atendimentos", "Cliente(s)", "Última OS", "Último técnico",
    };

    static readonly string[] AttendanceHeader =
    {
        "Data", "Cliente", "OS", "Técnico", "Computador", "Nº de série", "Modelo", "Saúde dos discos", "Bateria",
        "Serviços feitos", "Observações", "Pasta",
    };

    /// <summary>
    /// Grava a planilha (abas Máquinas e Atendimentos). Escreve num arquivo temporário e troca no fim:
    /// se o inventario.xlsx estiver aberto no Excel, lança IOException e o arquivo antigo fica como estava.
    /// </summary>
    public static void Save(string path, List<AttendanceRecord> records)
    {
        var machines = Machines(records);
        var tables = new List<(string, string[], IEnumerable<string[]>)>
        {
            ("Máquinas", MachineHeader, machines.Select(m => new[]
            {
                m.NumeroSerie, m.Computador, m.Fabricante, m.Modelo, m.Processador, m.Memoria, m.Armazenamento, m.Windows, m.Rede,
                m.Discos, m.Bateria, m.Primeiro.ToString("dd/MM/yyyy"), m.Ultimo.ToString("dd/MM/yyyy HH:mm"), m.Atendimentos.ToString(),
                m.Clientes, m.UltimaOS, m.UltimoTecnico,
            })),
            ("Atendimentos", AttendanceHeader, records.OrderByDescending(r => r.Data).Select(r => new[]
            {
                r.Data.ToString("dd/MM/yyyy HH:mm"), r.Cliente, r.OrdemServico, r.Tecnico, r.Computador, r.NumeroSerie, r.Modelo,
                string.Join("; ", r.Discos), r.Bateria, string.Join("; ", r.Servicos), r.Observacoes, Path.GetFileName(r.Folder),
            })),
        };

        string temp = path + ".novo" + Path.GetExtension(path);
        try
        {
            SpreadsheetExporter.SaveTables(temp, tables);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { }
        }
    }
}
