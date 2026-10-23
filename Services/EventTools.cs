using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace UtiliTech.Services;

public enum EventKind { BlueScreen, UnexpectedShutdown, Disk, Video, Hardware }

/// <summary>Um evento já explicado.</summary>
public record PcEvent(DateTime Time, EventKind Kind, string Title, string Detail)
{
    public string KindText => EventTools.KindTitle(Kind);
    public string Color => EventTools.KindColor(Kind);
    public string When => Time.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>Resumo de um tipo de evento no período, com o que fazer.</summary>
public record EventSummary(EventKind Kind, int Count, DateTime? Last, string Advice, string Page)
{
    public string Title => EventTools.KindTitle(Kind);
    public string Color => Count == 0 ? Palette.Good : EventTools.KindColor(Kind);
    public string CountText => Count == 0 ? "nenhum" : Count == 1 ? "1 vez" : $"{Count} vezes";
    public string LastText => Last is { } d ? $"última em {d:dd/MM/yyyy HH:mm}" : "";
    public bool HasAction => Count > 0 && Page.Length > 0;
    public string ActionText => Page switch
    {
        "drivers" => "Abrir Drivers",
        "disks" => "Abrir Discos",
        "memory" => "Testar memória",
        "monitor" => "Abrir Monitor",
        _ => "Abrir",
    };
}

/// <summary>Registro bruto lido do log do sistema (separado da leitura para poder testar).</summary>
public record RawEvent(DateTime Time, string Provider, int Id, int Level, IReadOnlyList<object?> Properties, string Message);

/// <summary>
/// Eventos simplificados: telas azuis (com o código explicado), desligamentos inesperados, erros de disco e do
/// sistema de arquivos, driver de vídeo que travou (TDR) e erros de hardware (WHEA), lidos do log Sistema.
/// </summary>
public static class EventTools
{
    // ---------- Textos ----------

    public static string KindTitle(EventKind k) => k switch
    {
        EventKind.BlueScreen => "Telas azuis",
        EventKind.UnexpectedShutdown => "Desligamentos inesperados",
        EventKind.Disk => "Erros de disco",
        EventKind.Video => "Driver de vídeo travou",
        EventKind.Hardware => "Erros de hardware (WHEA)",
        _ => k.ToString(),
    };

    public static string KindColor(EventKind k) => k switch
    {
        EventKind.BlueScreen or EventKind.Disk => Palette.Bad,
        _ => Palette.Warn,
    };

    /// <summary>Códigos de tela azul mais comuns, com nome e explicação para o técnico.</summary>
    static readonly Dictionary<uint, (string Name, string Hint)> BugChecks = new()
    {
        [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", "driver acessou memória que não podia: atualize drivers e teste a RAM"),
        [0x1A] = ("MEMORY_MANAGEMENT", "erro de memória: teste a RAM"),
        [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", "driver com defeito"),
        [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", "driver ou programa do sistema com defeito: atualize drivers de vídeo e antivírus"),
        [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", "RAM com defeito ou driver: teste a RAM"),
        [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "driver com defeito (muitas vezes vídeo, rede ou armazenamento)"),
        [0x7F] = ("UNEXPECTED_KERNEL_MODE_TRAP", "hardware: RAM, superaquecimento ou overclock"),
        [0x9F] = ("DRIVER_POWER_STATE_FAILURE", "driver não respondeu ao suspender/religar: atualize drivers (rede, vídeo, chipset)"),
        [0xC2] = ("BAD_POOL_CALLER", "driver com defeito"),
        [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "driver com defeito (muitas vezes rede ou Wi-Fi)"),
        [0xEF] = ("CRITICAL_PROCESS_DIED", "processo essencial do Windows parou: disco com erro ou sistema corrompido (sfc /scannow)"),
        [0xF4] = ("CRITICAL_OBJECT_TERMINATION", "disco ou cabo SATA com falha"),
        [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", "não conseguiu ler do disco: disco ou cabo com falha"),
        [0x77] = ("KERNEL_STACK_INPAGE_ERROR", "não conseguiu ler do disco: disco com falha"),
        [0x116] = ("VIDEO_TDR_FAILURE", "placa de vídeo ou driver de vídeo"),
        [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "placa de vídeo ou driver de vídeo"),
        [0x119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", "driver de vídeo"),
        [0x124] = ("WHEA_UNCORRECTABLE_ERROR", "erro de hardware (CPU, RAM, placa-mãe) ou superaquecimento"),
        [0x133] = ("DPC_WATCHDOG_VIOLATION", "driver travou (muitas vezes armazenamento/SSD: atualize o firmware e o driver)"),
        [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", "driver incompatível ou RAM"),
        [0x13A] = ("KERNEL_MODE_HEAP_CORRUPTION", "driver com defeito"),
        [0x154] = ("UNEXPECTED_STORE_EXCEPTION", "disco/SSD com falha"),
        [0x1E0] = ("ATTEMPTED_SWITCH_FROM_DPC", "driver com defeito"),
    };

    public static string ExplainBugCheck(uint code) =>
        BugChecks.TryGetValue(code, out var b) ? $"0x{code:X} {b.Name}: {b.Hint}" : $"0x{code:X}: código pouco comum (pesquise o código ou abra o minidump)";

    /// <summary>"0x0000007e (0xfffff..., ...)" → 0x7E. Null se não achar.</summary>
    public static uint? ParseBugCheck(string? text)
    {
        if (text == null) return null;
        var m = Regex.Match(text, @"0x([0-9a-fA-F]{1,16})");
        return m.Success && ulong.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong v) ? (uint)v : null;
    }

    // ---------- Classificar ----------

    static bool Is(string provider, params string[] names) => names.Any(n => provider.Equals(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>Transforma o registro bruto em evento explicado; null se não for um dos que interessam.</summary>
    public static PcEvent? Classify(RawEvent e)
    {
        bool problem = e.Level is >= 1 and <= 3; // crítico, erro ou aviso
        string P(int i) => i < e.Properties.Count ? e.Properties[i]?.ToString() ?? "" : "";

        if (Is(e.Provider, "Microsoft-Windows-WER-SystemErrorReporting", "BugCheck") && e.Id == 1001)
        {
            uint? code = ParseBugCheck(P(0)) ?? ParseBugCheck(e.Message);
            return new PcEvent(e.Time, EventKind.BlueScreen, "Tela azul", code is { } c ? ExplainBugCheck(c) : "código não informado");
        }
        if (Is(e.Provider, "Microsoft-Windows-Kernel-Power") && e.Id == 41)
        {
            uint bug = uint.TryParse(P(0), out var b) ? b : 0;
            return bug != 0
                ? new PcEvent(e.Time, EventKind.BlueScreen, "Tela azul (reinício após travar)", ExplainBugCheck(bug))
                : new PcEvent(e.Time, EventKind.UnexpectedShutdown, "Desligou sem desligar o Windows",
                    "Queda de energia, botão de ligar segurado ou travamento total (sem tela azul registrada)");
        }
        if (Is(e.Provider, "EventLog") && e.Id == 6008)
            return new PcEvent(e.Time, EventKind.UnexpectedShutdown, "O desligamento anterior foi inesperado",
                P(0).Length > 0 ? $"O PC parou por volta das {P(0)} {P(1)}".Trim() : "Queda de energia ou travamento");
        if (Is(e.Provider, "disk") && e.Id is 7 or 11 or 51 or 153 && problem)
            return new PcEvent(e.Time, EventKind.Disk, e.Id switch
            {
                7 => "Bloco defeituoso no disco",
                11 => "Erro na controladora do disco",
                51 => "Erro ao ler/gravar a memória virtual no disco",
                _ => "Disco demorou e o Windows repetiu a operação",
            }, Short(e.Message));
        if (Is(e.Provider, "Ntfs", "Microsoft-Windows-Ntfs") && e.Id is 55 or 98 or 137 or 140 && problem)
            return new PcEvent(e.Time, EventKind.Disk, "Sistema de arquivos (NTFS) com erro", Short(e.Message) + " · rode chkdsk /f");
        if (Is(e.Provider, "Display") && e.Id == 4101)
            return new PcEvent(e.Time, EventKind.Video, "O driver de vídeo parou de responder e foi recuperado", "Tela piscou/preta por alguns segundos");
        if (Is(e.Provider, "Microsoft-Windows-WHEA-Logger") && e.Id is 1 or 17 or 18 or 19 or 47 && problem)
            return new PcEvent(e.Time, EventKind.Hardware, e.Id == 18 || e.Level == 1 ? "Erro de hardware grave" : "Erro de hardware corrigido",
                Short(e.Message));
        return null;
    }

    static string Short(string message)
    {
        string first = message.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return first.Length > 180 ? first[..180] + "…" : first;
    }

    /// <summary>
    /// Um mesmo travamento gera vários eventos (tela azul 1001 + Kernel-Power 41 + EventLog 6008 no boot seguinte).
    /// Junta os do mesmo tipo de incidente que aconteceram com até 15 minutos de diferença, ficando com o mais explicado.
    /// </summary>
    public static List<PcEvent> Deduplicate(IEnumerable<PcEvent> events)
    {
        var result = new List<PcEvent>();
        foreach (var e in events.OrderBy(e => e.Time))
        {
            bool crash = e.Kind is EventKind.BlueScreen or EventKind.UnexpectedShutdown;
            int i = crash ? result.FindLastIndex(r => r.Kind is EventKind.BlueScreen or EventKind.UnexpectedShutdown &&
                                                      (e.Time - r.Time).Duration() <= TimeSpan.FromMinutes(15)) : -1;
            if (i < 0) { result.Add(e); continue; }
            // A hora em que o PC parou (EventLog 6008) vale mais que a do boot seguinte: entra no detalhe
            string? stopped = new[] { e, result[i] }.Select(x => x.Detail.Split(" · ")[0]).FirstOrDefault(d => d.StartsWith("O PC parou"));
            // Tela azul explica melhor que "desligou"; entre duas telas azuis, a que tem o nome do código
            if (e.Kind == EventKind.BlueScreen && (result[i].Kind != EventKind.BlueScreen || result[i].Detail.StartsWith("código não")))
                result[i] = e with { Time = result[i].Time };
            else if (result[i].Title == "O desligamento anterior foi inesperado" && e.Kind == EventKind.UnexpectedShutdown)
                result[i] = e with { Time = result[i].Time }; // o 41 explica melhor que o 6008
            if (stopped != null && !result[i].Detail.Contains(stopped))
                result[i] = result[i] with { Detail = result[i].Detail.StartsWith("O PC parou") ? result[i].Detail : $"{stopped} · {result[i].Detail}" };
        }
        return result.OrderByDescending(e => e.Time).ToList();
    }

    public static List<EventSummary> Summarize(List<PcEvent> events, int minidumps)
    {
        EventSummary S(EventKind k, string advice, string page)
        {
            var list = events.Where(e => e.Kind == k).ToList();
            return new EventSummary(k, list.Count, list.Count > 0 ? list.Max(e => e.Time) : null, list.Count > 0 ? advice : "", page);
        }
        var blue = events.Where(e => e.Kind == EventKind.BlueScreen).ToList();
        string top = blue.GroupBy(e => e.Detail).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "";
        return new()
        {
            S(EventKind.BlueScreen, (top.Length > 0 ? $"Mais comum: {top}. " : "") +
                (minidumps > 0 ? $"{minidumps} minidump(s) em C:\\Windows\\Minidump para analisar (WinDbg ou BlueScreenView). " : "") +
                "Atualize os drivers e teste a memória.", "drivers"),
            S(EventKind.UnexpectedShutdown, "Pergunte se faltou energia ou se seguraram o botão. Se não, confira temperatura (Monitor), fonte e bateria.", "monitor"),
            S(EventKind.Disk, "Disco pode estar falhando: veja o S.M.A.R.T. em Discos e faça backup. Para NTFS, rode chkdsk /f.", "disks"),
            S(EventKind.Video, "Atualize o driver da placa de vídeo (Drivers → Placa de vídeo). Se continuar, confira a temperatura da GPU.", "drivers"),
            S(EventKind.Hardware, "Erro detectado pelo próprio processador/placa: teste a memória e faça o teste de estresse.", "memory"),
        };
    }

    // ---------- Leitura ----------

    /// <summary>Lê o log Sistema dos últimos dias, só com as origens e números que interessam.</summary>
    public static List<RawEvent> Read(int days)
    {
        long ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        string time = $"TimeCreated[timediff(@SystemTime) <= {ms}]";
        string[] filters =
        {
            "Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting' or @Name='BugCheck'] and EventID=1001",
            "Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41",
            "Provider[@Name='EventLog'] and EventID=6008",
            "Provider[@Name='disk'] and (EventID=7 or EventID=11 or EventID=51 or EventID=153)",
            "Provider[@Name='Ntfs' or @Name='Microsoft-Windows-Ntfs'] and (EventID=55 or EventID=98 or EventID=137 or EventID=140) and (Level=1 or Level=2 or Level=3)",
            "Provider[@Name='Display'] and EventID=4101",
            "Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (Level=1 or Level=2 or Level=3)",
        };
        var list = new List<RawEvent>();
        foreach (var f in filters)
        {
            try
            {
                using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, $"*[System[{f} and {time}]]"));
                for (EventRecord? r = reader.ReadEvent(); r != null && list.Count < 2000; r = reader.ReadEvent())
                    using (r)
                    {
                        string msg = "";
                        try { msg = r.FormatDescription() ?? ""; } catch { }
                        list.Add(new RawEvent(r.TimeCreated ?? DateTime.MinValue, r.ProviderName ?? "", r.Id, r.Level ?? 4,
                            r.Properties.Select(p => p.Value).ToList(), msg));
                    }
            }
            catch (Exception ex) { Logger.Error($"Eventos: {f}", ex); }
        }
        return list;
    }

    public static List<FileInfo> Minidumps()
    {
        try
        {
            var dir = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump"));
            return dir.Exists ? dir.GetFiles("*.dmp").OrderByDescending(f => f.LastWriteTime).ToList() : new();
        }
        catch { return new(); }
    }

    public static (List<PcEvent> Events, List<EventSummary> Summary, List<FileInfo> Dumps) Analyze(int days)
    {
        var events = Deduplicate(Read(days).Select(Classify).OfType<PcEvent>());
        var dumps = Minidumps().Where(f => f.LastWriteTime >= DateTime.Now.AddDays(-days)).ToList();
        return (events, Summarize(events, dumps.Count), dumps);
    }
}
