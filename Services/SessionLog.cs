using System.IO;

namespace UtiliTech.Services;

/// <summary>Uma ação feita no PC nesta sessão (limpeza, reparo, driver, programa...).</summary>
public record SessionAction(DateTime When, string Category, string Text, bool Success = true)
{
    public string Line => $"{When:HH:mm} · {Category}: {Text}" + (Success ? "" : " (falhou)");
}

/// <summary>Foto do PC num momento: espaço livre na unidade do Windows e dispositivos com problema.</summary>
public record PcSnapshot(DateTime When, long FreeBytes, long TotalBytes, int ProblemDevices);

/// <summary>O que vai no relatório: antes × depois e as ações da sessão.</summary>
public record SessionReport(List<InfoItem> BeforeAfter, List<SessionAction> Actions)
{
    public bool IsEmpty => BeforeAfter.Count == 0 && Actions.Count == 0;
    public static SessionReport Empty { get; } = new(new(), new());
}

/// <summary>
/// Registro do atendimento enquanto o app está aberto: a foto do PC quando o app abriu ("antes")
/// e cada ação feita pelas ferramentas. Vai para o relatório, a planilha e o histórico de atendimentos.
/// Não é gravado em disco: é da sessão.
/// </summary>
public static class SessionLog
{
    static readonly object Gate = new();
    static readonly List<SessionAction> _actions = new();

    public static PcSnapshot? Baseline { get; private set; }

    /// <summary>Foto do PC ao abrir o app. Chamar uma vez, fora da tela (lê WMI).</summary>
    public static void CaptureBaseline()
    {
        try { Baseline ??= Snapshot(); }
        catch (Exception ex) { Logger.Error("Foto do PC ao abrir", ex); }
    }

    public static PcSnapshot Snapshot()
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        return new PcSnapshot(DateTime.Now, drive.AvailableFreeSpace, drive.TotalSize, DriverManager.ScanProblems().Count(p => !p.IsDisabled));
    }

    public static void Add(string category, string text, bool success = true)
    {
        lock (Gate) _actions.Add(new SessionAction(DateTime.Now, category, text, success));
        Logger.Info($"Sessão · {category}: {text}" + (success ? "" : " (falhou)"));
    }

    public static List<SessionAction> Actions
    {
        get { lock (Gate) return _actions.ToList(); }
    }

    /// <summary>Monta o relatório da sessão com a foto de agora (lê WMI: chamar fora da tela).</summary>
    public static SessionReport Report()
    {
        PcSnapshot? now = null;
        try { now = Snapshot(); }
        catch (Exception ex) { Logger.Error("Foto do PC para o relatório", ex); }
        return Build(Baseline, now, Actions);
    }

    /// <summary>Antes × depois (só o que mudou ou importa) e as ações em ordem.</summary>
    public static SessionReport Build(PcSnapshot? before, PcSnapshot? after, List<SessionAction> actions)
    {
        var items = new List<InfoItem>();
        if (before != null && after != null)
        {
            long diff = after.FreeBytes - before.FreeBytes;
            string change = Math.Abs(diff) < 50L * 1024 * 1024 ? "sem mudança" : (diff > 0 ? "+" : "−") + Fmt.Bytes(Math.Abs(diff));
            items.Add(new("Espaço livre no disco do Windows",
                $"{Fmt.Bytes(before.FreeBytes)} → {Fmt.Bytes(after.FreeBytes)} ({change})"));
            if (before.ProblemDevices > 0 || after.ProblemDevices > 0)
                items.Add(new("Dispositivos com problema", $"{before.ProblemDevices} → {after.ProblemDevices}"));
            items.Add(new("Período", $"{before.When:dd/MM HH:mm} → {after.When:dd/MM HH:mm}"));
        }
        return new SessionReport(items, actions.OrderBy(a => a.When).ToList());
    }

    /// <summary>Só para os testes.</summary>
    internal static void Reset()
    {
        lock (Gate) _actions.Clear();
        Baseline = null;
    }
}
