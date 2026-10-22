using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace UtiliTech.Services;

public enum StepState { Done, Pending, Warning, NotApplicable }

/// <summary>Uma etapa do checklist de pós-formatação, já avaliada.</summary>
public record ChecklistStep(string Title, string Description, StepState State, string Detail, string Page, string Action)
{
    public string StateText => State switch
    {
        StepState.Done => "Feito",
        StepState.Pending => "Falta",
        StepState.Warning => "Atenção",
        _ => "Não se aplica",
    };
    public string Color => State switch
    {
        StepState.Done => Palette.Good,
        StepState.Pending => Palette.Accent,
        StepState.Warning => Palette.Warn,
        _ => Palette.Muted,
    };
    public string Glyph => State switch
    {
        StepState.Done => "",    // visto
        StepState.Pending => "", // círculo
        StepState.Warning => "", // aviso
        _ => "",                 // traço
    };
    public bool HasAction => State is StepState.Pending or StepState.Warning && Page.Length > 0 && Action.Length > 0;
}

/// <summary>O que foi lido do PC para montar o checklist (separado da avaliação para poder testar).</summary>
public record ChecklistFacts
{
    public string Activation { get; init; } = "";
    public int ProblemDevices { get; init; }
    /// <summary>Programas obrigatórios (RequiredApps) que não estão instalados.</summary>
    public List<string> MissingApps { get; init; } = new();
    /// <summary>Outro Office instalado que não é o 365 (ex.: "Microsoft Office LTSC Professional Plus 2021"); vazio = nenhum.</summary>
    public string OtherOffice { get; init; } = "";
    public string ComputerName { get; init; } = "";
    /// <summary>Nome novo que só vale depois de reiniciar (null = nenhum).</summary>
    public string? PendingName { get; init; }
    public bool PartOfDomain { get; init; }
    public string MachineDomain { get; init; } = "";
    /// <summary>Domínio informado na tela Nome e domínio (vazio = não usa domínio).</summary>
    public string ConfiguredDomain { get; init; } = "";
    /// <summary>Ingresso no domínio feito, esperando reiniciar.</summary>
    public bool JoinPending { get; init; }
    public DateTime? LastGroupPolicy { get; init; }
    public bool RebootPending { get; init; }
    /// <summary>Pior veredito dos discos (Bom / Atenção / Ruim; vazio = nenhum disco lido).</summary>
    public string WorstDisk { get; init; } = "";
    /// <summary>Relatório ou planilha deste PC guardado hoje no histórico de atendimentos.</summary>
    public bool ReportSavedToday { get; init; }
}

/// <summary>
/// Checklist de pós-formatação: o que falta para entregar o PC (Windows ativado, drivers, programas,
/// nome e domínio, políticas, reinício, discos e relatório). Tudo verificado sozinho, sem marcar à mão.
/// </summary>
public static class PostFormatChecklist
{
    /// <summary>Nome que o Windows dá na instalação (DESKTOP-1A2B3C4, LAPTOP-..., WIN-...).</summary>
    static readonly Regex DefaultName = new("^(DESKTOP|LAPTOP|WIN)-[A-Z0-9]{5,12}$", RegexOptions.IgnoreCase);

    public static bool IsDefaultName(string name) => DefaultName.IsMatch(name.Trim());

    const string Office365 = "Microsoft 365";

    /// <summary>
    /// Programas que o checklist exige, com o trecho do nome em "Aplicativos instalados" (alternativas separadas por |).
    /// Office: só conta o 365 (Microsoft 365 Apps / Office 365); Office 2016/2019/2021/LTSC não.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Match)> RequiredApps = new[]
    {
        ("Google Chrome", "Google Chrome"),
        ("Mozilla Firefox", "Mozilla Firefox"),
        (Office365, "Microsoft 365|Office 365"),
    };

    /// <summary>Quais dos programas obrigatórios não aparecem na lista de instalados.</summary>
    public static List<string> Missing(IReadOnlyCollection<string> installedNames) =>
        RequiredApps.Where(a => !a.Match.Split('|').Any(m => installedNames.Any(n => n.Contains(m, StringComparison.OrdinalIgnoreCase))))
                    .Select(a => a.Name).ToList();

    /// <summary>Office que não é o 365 (para avisar o técnico); vazio se não houver.</summary>
    public static string OtherOffice(IEnumerable<string> installedNames) =>
        installedNames.Select(n => n.Trim())
            .Where(n => n.StartsWith("Microsoft Office", StringComparison.OrdinalIgnoreCase) &&
                        !n.Contains("365") && !n.Contains("Click-to-Run", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Split(" - ")[0]) // tira o idioma ("- pt-br")
            .FirstOrDefault() ?? "";

    /// <summary>"A, B e C".</summary>
    static string Join(IEnumerable<string> items)
    {
        var l = items.ToList();
        return l.Count <= 1 ? string.Concat(l) : string.Join(", ", l.Take(l.Count - 1)) + " e " + l[^1];
    }

    public static List<ChecklistStep> Evaluate(ChecklistFacts f, DateTime now)
    {
        var steps = new List<ChecklistStep>();

        // 1. Windows ativado
        steps.Add(f.Activation == "Ativado"
            ? new("Windows ativado", "Licença do Windows ativa.", StepState.Done, "Ativado", "system", "")
            : new("Windows ativado", "Licença do Windows ativa.", StepState.Pending,
                f.Activation.Length > 0 ? f.Activation : "Não foi possível verificar", "system", "Ver em Sistema"));

        // 2. Drivers
        steps.Add(f.ProblemDevices == 0
            ? new("Drivers", "Nenhum dispositivo com problema no Gerenciador de Dispositivos.", StepState.Done,
                "Todos os dispositivos funcionando", "drivers", "")
            : new("Drivers", "Nenhum dispositivo com problema no Gerenciador de Dispositivos.", StepState.Pending,
                $"{f.ProblemDevices} dispositivo(s) sem driver ou com erro", "drivers", "Abrir Drivers"));

        // 3. Programas obrigatórios
        string appsDesc = Join(RequiredApps.Select(a => a.Name)) + ".";
        if (f.MissingApps.Count == 0)
        {
            steps.Add(new("Programas", appsDesc, StepState.Done, Join(RequiredApps.Select(a => a.Name)) + " instalados", "install", ""));
        }
        else
        {
            string detail = "Falta: " + Join(f.MissingApps);
            if (f.MissingApps.Contains(Office365) && f.OtherOffice.Length > 0)
                detail += $" (este PC tem o {f.OtherOffice}, que não é o 365)";
            steps.Add(new("Programas", appsDesc, StepState.Pending, detail, "install", "Abrir Instalar apps"));
        }

        // 4. Nome do computador
        string nameDesc = "Nome definitivo, não o que o Windows inventa na instalação.";
        if (f.PendingName != null && !string.Equals(f.PendingName, f.ComputerName, StringComparison.OrdinalIgnoreCase))
            steps.Add(new("Nome do computador", nameDesc, IsDefaultName(f.PendingName) ? StepState.Pending : StepState.Warning,
                $"Vai se chamar {f.PendingName} depois de reiniciar (hoje: {f.ComputerName})", "domain", "Abrir Nome e domínio"));
        else if (IsDefaultName(f.ComputerName))
            steps.Add(new("Nome do computador", nameDesc, StepState.Pending, $"{f.ComputerName} é o nome padrão do Windows", "domain", "Abrir Nome e domínio"));
        else
            steps.Add(new("Nome do computador", nameDesc, StepState.Done, f.ComputerName, "domain", ""));

        // 5. Domínio
        string domainDesc = "PC no domínio da empresa (o domínio é informado na tela Nome e domínio).";
        bool inDomain = f.PartOfDomain && f.ConfiguredDomain.Length > 0 &&
                        f.MachineDomain.Equals(f.ConfiguredDomain, StringComparison.OrdinalIgnoreCase);
        if (f.ConfiguredDomain.Length == 0)
            steps.Add(new("Domínio", domainDesc, StepState.NotApplicable,
                f.PartOfDomain ? $"No domínio {f.MachineDomain} (nenhum domínio informado no app)" : "Nenhum domínio informado no app",
                "domain", ""));
        else if (inDomain)
            steps.Add(new("Domínio", domainDesc, StepState.Done, $"No domínio {f.MachineDomain}", "domain", ""));
        else if (f.JoinPending)
            steps.Add(new("Domínio", domainDesc, StepState.Warning, $"Ingresso em {f.ConfiguredDomain} feito: falta reiniciar", "domain", "Abrir Nome e domínio"));
        else
            steps.Add(new("Domínio", domainDesc, StepState.Pending,
                f.PartOfDomain ? $"Está em {f.MachineDomain}, não em {f.ConfiguredDomain}" : $"Fora do domínio {f.ConfiguredDomain}",
                "domain", "Abrir Nome e domínio"));

        // 6. Políticas de grupo (só no domínio)
        string gpDesc = "Políticas do domínio aplicadas neste PC (mapeamentos, impressoras, proxy).";
        if (!f.PartOfDomain)
            steps.Add(new("Políticas de grupo", gpDesc, StepState.NotApplicable, "O PC não está em um domínio", "repairs", ""));
        else if (f.LastGroupPolicy is not { } gp)
            steps.Add(new("Políticas de grupo", gpDesc, StepState.Pending, "Nunca aplicadas neste PC", "repairs", "Abrir Reparos"));
        else if (now - gp <= TimeSpan.FromDays(1))
            steps.Add(new("Políticas de grupo", gpDesc, StepState.Done, $"Aplicadas em {gp:dd/MM HH:mm}", "repairs", ""));
        else
            steps.Add(new("Políticas de grupo", gpDesc, StepState.Warning,
                $"Última vez em {gp:dd/MM/yyyy HH:mm} (há {(int)(now - gp).TotalDays} dia(s)): rode Atualizar políticas de grupo", "repairs", "Abrir Reparos"));

        // 7. Reinício
        steps.Add(f.RebootPending || f.JoinPending || f.PendingName != null
            ? new("Reiniciar", "Nada esperando reinício (Windows Update, nome, domínio).", StepState.Warning,
                "O Windows está esperando reiniciar para terminar alterações", "", "")
            : new("Reiniciar", "Nada esperando reinício (Windows Update, nome, domínio).", StepState.Done, "Nenhum reinício pendente", "", ""));

        // 8. Discos
        string diskDesc = "Saúde dos discos (S.M.A.R.T.) antes de entregar.";
        steps.Add(f.WorstDisk switch
        {
            "Bom" => new("Discos", diskDesc, StepState.Done, "Todos os discos em bom estado", "disks", ""),
            "" => new("Discos", diskDesc, StepState.NotApplicable, "Nenhum disco lido", "disks", ""),
            var v => new("Discos", diskDesc, StepState.Warning, $"Disco com veredito {v}: avise o cliente", "disks", "Abrir Discos"),
        });

        // 9. Relatório
        steps.Add(f.ReportSavedToday
            ? new("Relatório do atendimento", "Relatório ou planilha exportado e guardado no histórico.", StepState.Done,
                "Guardado hoje em Atendimentos", "attendance", "")
            : new("Relatório do atendimento", "Relatório ou planilha exportado e guardado no histórico.", StepState.Pending,
                "Exporte o relatório na tela Sistema", "system", "Abrir Sistema"));

        return steps;
    }

    /// <summary>Lê tudo do PC (WMI, registro, histórico). Demora alguns segundos: chamar fora da tela.</summary>
    public static ChecklistFacts Gather()
    {
        var machine = DomainTools.Current();
        var installed = AppInstaller.InstalledNames();
        string serial = Wmi.Query("SELECT SerialNumber FROM Win32_BIOS").Select(b => Wmi.Str(b, "SerialNumber")).FirstOrDefault() ?? "";
        var disks = DiskHealth.Collect();

        return new ChecklistFacts
        {
            Activation = SystemInfo.Activation(),
            ProblemDevices = DriverManager.ScanProblems().Count(p => !p.IsDisabled),
            MissingApps = Missing(installed),
            OtherOffice = OtherOffice(installed),
            ComputerName = machine.Name,
            PendingName = PendingComputerName(machine.Name),
            PartOfDomain = machine.PartOfDomain,
            MachineDomain = machine.Domain,
            ConfiguredDomain = DomainTools.DomainName.Trim(),
            JoinPending = KeyExists(@"SYSTEM\CurrentControlSet\Services\Netlogon\JoinDomain"),
            LastGroupPolicy = LastGroupPolicy(),
            RebootPending = KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") ||
                            KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"),
            WorstDisk = disks.Count == 0 ? "" : disks.Any(d => d.Verdict == "Ruim") ? "Ruim" : disks.Any(d => d.Verdict == "Atenção") ? "Atenção" : "Bom",
            ReportSavedToday = AttendanceHistory.LoadAll().Any(r => r.Data.Date == DateTime.Today &&
                (serial.Length > 0 ? r.NumeroSerie.Equals(serial, StringComparison.OrdinalIgnoreCase)
                                   : r.Computador.Equals(machine.Name, StringComparison.OrdinalIgnoreCase))),
        };
    }

    static bool KeyExists(string path)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key != null;
        }
        catch { return false; }
    }

    /// <summary>Nome gravado para o próximo boot, se for diferente do atual.</summary>
    static string? PendingComputerName(string current)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName");
            return key?.GetValue("ComputerName") is string next && !next.Equals(current, StringComparison.OrdinalIgnoreCase) ? next : null;
        }
        catch { return null; }
    }

    /// <summary>Fim da última aplicação das políticas do computador (EndTimeHi/Lo da extensão principal).</summary>
    static DateTime? LastGroupPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State\Machine\Extension-List\{00000000-0000-0000-0000-000000000000}");
            if (key?.GetValue("EndTimeHi") is not int hi || key.GetValue("EndTimeLo") is not int lo) return null;
            long ft = ((long)hi << 32) | (uint)lo;
            return ft > 0 ? DateTime.FromFileTime(ft) : null;
        }
        catch { return null; }
    }
}
