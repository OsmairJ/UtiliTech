using System.IO;
using Microsoft.Win32;

namespace UtiliTech.Services;

/// <summary>Um problema encontrado no PC, com a tela que resolve.</summary>
public record PcIssue(bool Serious, string Title, string Detail, string Page, string Action)
{
    public string Color => Serious ? Palette.Bad : Palette.Warn;
    public string Glyph => Serious ? "" : ""; // erro / aviso
    public bool HasAction => Page.Length > 0;
}

/// <summary>O que foi lido do PC para montar a lista (separado da avaliação para poder testar).</summary>
public record IssueFacts
{
    /// <summary>Nome do disco → veredito (Bom / Atenção / Ruim).</summary>
    public List<(string Name, string Verdict)> Disks { get; init; } = new();
    public long SystemFree { get; init; } = -1;
    public long SystemTotal { get; init; } = -1;
    public int ProblemDevices { get; init; }
    /// <summary>Placa de vídeo sem o driver do fabricante (nome), ou vazio.</summary>
    public string GpuWithoutDriver { get; init; } = "";
    public double? BatteryHealth { get; init; }
    public List<SecurityItem> Security { get; init; } = new();
    /// <summary>Contagem dos últimos 30 dias por tipo de evento.</summary>
    public Dictionary<EventKind, int> Events { get; init; } = new();
    public string Activation { get; init; } = "";
    public bool RebootPending { get; init; }
}

/// <summary>
/// "Problemas encontrados" da Visão geral: junta o que as outras ferramentas acham (discos, espaço, drivers, bateria,
/// segurança, eventos, ativação, reinício) e devolve só o que precisa de atenção, mais grave primeiro.
/// </summary>
public static class PcIssues
{
    public const int EventDays = 30;

    public static List<PcIssue> Build(IssueFacts f)
    {
        var list = new List<PcIssue>();

        foreach (var (name, verdict) in f.Disks)
            if (verdict == "Ruim")
                list.Add(new(true, $"Disco com falha: {name}", "O S.M.A.R.T. indica defeito. Faça backup já e planeje a troca.", "disks", "Ver discos"));
            else if (verdict == "Atenção")
                list.Add(new(false, $"Disco pedindo atenção: {name}", "Temperatura, desgaste ou horas de uso altos.", "disks", "Ver discos"));

        if (f.SystemFree >= 0 && f.SystemTotal > 0)
        {
            double pct = f.SystemFree * 100.0 / f.SystemTotal;
            long gb = f.SystemFree / (1L << 30);
            if (pct < 5 || gb < 5)
                list.Add(new(true, "Disco do Windows quase cheio", $"Só {Fmt.Bytes(f.SystemFree)} livres ({pct:0}%): o Windows fica lento e as atualizações falham.", "cleanup", "Limpar"));
            else if (pct < 10 || gb < 15)
                list.Add(new(false, "Pouco espaço no disco do Windows", $"{Fmt.Bytes(f.SystemFree)} livres ({pct:0}%).", "cleanup", "Limpar"));
        }

        if (f.ProblemDevices > 0)
            list.Add(new(false, $"{f.ProblemDevices} dispositivo(s) sem driver ou com erro", "Aparecem com aviso no Gerenciador de Dispositivos.", "drivers", "Ver drivers"));
        if (f.GpuWithoutDriver.Length > 0)
            list.Add(new(false, "Placa de vídeo sem o driver do fabricante", $"{f.GpuWithoutDriver} está com o driver genérico do Windows.", "drivers", "Instalar driver"));

        if (f.BatteryHealth is { } h && h < 60)
            list.Add(new(false, "Bateria gasta", $"Saúde de {h:0}%: a autonomia está bem menor que a original.", "battery", "Ver bateria"));

        foreach (var s in f.Security)
            if (s.State == SecState.Bad || (s.State == SecState.Warning && s.Key is "av" or "wu"))
                list.Add(new(s.State == SecState.Bad, s.Title + (s.State == SecState.Bad ? ": problema" : ": atenção"), s.Detail, "security", "Ver segurança"));

        int E(EventKind k) => f.Events.GetValueOrDefault(k);
        if (E(EventKind.BlueScreen) > 0)
            list.Add(new(true, $"{E(EventKind.BlueScreen)} tela(s) azul(is) nos últimos {EventDays} dias", "O Windows travou com erro. Veja o código e a causa provável.", "events", "Ver eventos"));
        if (E(EventKind.Disk) > 0)
            list.Add(new(true, $"{E(EventKind.Disk)} erro(s) de disco no log", "O Windows registrou falha ao ler/gravar no disco.", "events", "Ver eventos"));
        if (E(EventKind.UnexpectedShutdown) >= 2)
            list.Add(new(false, $"{E(EventKind.UnexpectedShutdown)} desligamentos inesperados", "Queda de energia, botão segurado ou travamento: vale investigar.", "events", "Ver eventos"));
        if (E(EventKind.Video) >= 3)
            list.Add(new(false, $"Driver de vídeo travou {E(EventKind.Video)} vezes", "A tela piscou ou ficou preta e voltou.", "events", "Ver eventos"));
        if (E(EventKind.Hardware) > 0)
            list.Add(new(false, $"{E(EventKind.Hardware)} erro(s) de hardware (WHEA)", "Detectados pelo processador ou pela placa: teste memória e estresse.", "events", "Ver eventos"));

        if (f.Activation.Length > 0 && f.Activation != "Ativado" && f.Activation != "Não foi possível verificar")
            list.Add(new(false, "Windows não ativado", f.Activation, "system", "Ver sistema"));
        if (f.RebootPending)
            list.Add(new(false, "Reinício pendente", "O Windows está esperando reiniciar para terminar atualizações ou mudanças.", "", ""));

        return list.OrderByDescending(i => i.Serious).ToList(); // ordem estável: dentro do mesmo nível, a ordem acima
    }

    /// <summary>Lê tudo (leva alguns segundos: discos, drivers, segurança e o log de eventos). Chamar fora da tela.</summary>
    public static IssueFacts Gather()
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        var battery = BatteryInfo.Collect();
        var gpu = GpuDrivers.Detect().FirstOrDefault(g => !g.HasVendorDriver);
        var events = EventTools.Deduplicate(EventTools.Read(EventDays).Select(EventTools.Classify).OfType<PcEvent>());
        return new IssueFacts
        {
            Disks = DiskHealth.Collect().Select(d => (d.Name, d.Verdict)).ToList(),
            SystemFree = drive.AvailableFreeSpace,
            SystemTotal = drive.TotalSize,
            ProblemDevices = DriverManager.ScanProblems().Count(p => !p.IsDisabled),
            GpuWithoutDriver = gpu?.Name ?? "",
            BatteryHealth = battery.Present ? battery.HealthPercent : null,
            Security = SecurityCheck.Evaluate(SecurityCheck.Gather(), DateTime.Now),
            Events = events.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count()),
            Activation = SystemInfo.Activation(),
            RebootPending = KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") ||
                            KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"),
        };
    }

    static bool KeyExists(string path)
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(path); return k != null; }
        catch { return false; }
    }
}
