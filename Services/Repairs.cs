using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace UtiliTech.Services;

/// <summary>Um reparo de 1 clique: passos em sequência, com registro do que foi feito.</summary>
public class RepairAction : Observable
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Pergunta antes de executar (null = executa direto).</summary>
    public string? Confirm { get; init; }
    /// <summary>Recebe a função de registro; devolve a mensagem final (sucesso) ou lança exceção.</summary>
    public Func<Action<string>, Task<string>> Run { get; init; } = _ => Task.FromResult("");

    string _status = "";
    public string Status { get => _status; set => SetField(ref _status, value); }

    string _statusColor = Palette.Muted;
    public string StatusColor { get => _statusColor; set => SetField(ref _statusColor, value); }
}

public static class Repairs
{
    static Repairs() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static List<RepairAction> All() => new()
    {
        new()
        {
            Title = "Destravar fila de impressão",
            Description = "Para o spooler, apaga os trabalhos presos na fila e liga de novo. Resolve impressão que não sai ou documento que não some da fila.",
            Run = FixSpooler,
        },
        new()
        {
            Title = "Atualizar políticas de grupo",
            Description = "Baixa de novo as políticas do domínio (gpupdate /force): mapeamentos de rede, impressoras, proxy e configurações da empresa. Use depois de ingressar no domínio ou quando uma política nova não chegou.",
            Run = UpdateGroupPolicy,
        },
        new()
        {
            Title = "Limpar cache DNS",
            Description = "Esquece os endereços de sites guardados. Resolve site que não abre só neste PC ou que abre a página antiga.",
            Run = async log => { await Step(log, "ipconfig", "/flushdns"); return "Cache DNS limpo"; },
        },
        new()
        {
            Title = "Renovar IP",
            Description = "Solta e pede de novo o endereço IP ao roteador (DHCP). A conexão cai por alguns segundos.",
            Run = async log =>
            {
                await Step(log, "ipconfig", "/release");
                var code = await Step(log, "ipconfig", "/renew");
                return code == 0 ? "IP renovado" : "Pedido feito; confira se a rede voltou";
            },
        },
        new()
        {
            Title = "Resetar rede",
            Description = "Recria as configurações de rede do Windows (Winsock e TCP/IP). Para quando nada conecta mesmo com a rede funcionando em outros PCs.",
            Confirm = "Resetar Winsock e TCP/IP?\n\nIP fixo e configurações manuais de rede serão perdidos, e é preciso reiniciar o PC no final.",
            Run = async log =>
            {
                await Step(log, "netsh", "winsock reset");
                await Step(log, "netsh", "int ip reset");
                return "Feito · reinicie o PC";
            },
        },
        new()
        {
            Title = "Resetar Windows Update",
            Description = "Para os serviços de atualização, renomeia o cache (SoftwareDistribution e catroot2) e liga tudo de novo. Para atualização travada ou com erro.",
            Confirm = "Resetar o Windows Update?\n\nO histórico de atualizações exibido no Windows Update vai zerar (as atualizações instaladas continuam). Leva cerca de 1 minuto.",
            Run = ResetWindowsUpdate,
        },
        new()
        {
            Title = "Sincronizar relógio",
            Description = "Acerta data e hora pelo servidor de horário. Relógio errado causa erro de certificado em sites, PJe e assinatura digital.",
            Run = async log =>
            {
                await Step(log, "net", "start w32time");
                var code = await Step(log, "w32tm", "/resync /force");
                return code == 0 ? $"Relógio sincronizado ({DateTime.Now:HH:mm:ss})" : "O servidor de horário não respondeu";
            },
        },
        new()
        {
            Title = "Reiniciar o Explorer",
            Description = "Fecha e abre de novo a barra de tarefas, o menu Iniciar e as pastas. Para quando a barra trava ou os ícones somem.",
            Run = RestartExplorer,
        },
    };

    static async Task<string> FixSpooler(Action<string> log)
    {
        await Step(log, "net", "stop spooler /y");
        var queue = Path.Combine(Environment.SystemDirectory, "spool", "PRINTERS");
        int removed = 0, locked = 0;
        if (Directory.Exists(queue))
            foreach (var f in Directory.GetFiles(queue))
            {
                try { File.Delete(f); removed++; }
                catch { locked++; }
            }
        log($"{removed} arquivo(s) de trabalho removido(s) da fila" + (locked > 0 ? $", {locked} em uso" : ""));
        await Step(log, "net", "start spooler");
        if (ServiceState("Spooler") != "Running") throw new InvalidOperationException("O spooler não voltou a rodar");
        return removed > 0 ? $"Fila limpa ({removed / 2} trabalho(s) removido(s))" : "Spooler reiniciado (fila já estava vazia)";
    }

    static async Task<string> UpdateGroupPolicy(Action<string> log)
    {
        var machine = DomainTools.Current();
        if (!machine.PartOfDomain)
            return "Este PC não está em um domínio: não há políticas de grupo para baixar";

        // O gpupdate pode perguntar se faz logoff/reinicia (S/N); a entrada vai fechada, então ele não espera
        // resposta. Se alguma política só vale no próximo logon/reinício, ele avisa no texto e o técnico decide.
        int code = await Step(log, "gpupdate.exe", "/force /wait:300", timeoutMinutes: 6);
        return code == 0
            ? $"Políticas do domínio {machine.Domain} atualizadas"
            : $"gpupdate terminou com código {code}: o PC está na rede da empresa? (veja o registro)";
    }

    static async Task<string> ResetWindowsUpdate(Action<string> log)
    {
        string[] services = { "wuauserv", "bits", "cryptsvc", "msiserver" };
        foreach (var s in services) await Step(log, "net", $"stop {s} /y");

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var dir in new[] { Path.Combine(windows, "SoftwareDistribution"), Path.Combine(Environment.SystemDirectory, "catroot2") })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                Directory.Move(dir, $"{dir}.bak-{stamp}");
                log($"Renomeado: {dir} → {Path.GetFileName(dir)}.bak-{stamp}");
            }
            catch (Exception ex) { log($"Não foi possível renomear {dir}: {ex.Message}"); }
        }

        foreach (var s in new[] { "cryptsvc", "bits", "wuauserv" }) await Step(log, "net", $"start {s}");
        return "Windows Update resetado · procure atualizações de novo";
    }

    static async Task<string> RestartExplorer(Action<string> log)
    {
        await Step(log, "taskkill", "/f /im explorer.exe");
        // O Windows reabre o Explorer sozinho (AutoRestartShell); se não reabrir, abre aqui
        for (int i = 0; i < 10 && Process.GetProcessesByName("explorer").Length == 0; i++) await Task.Delay(500);
        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            log("Explorer aberto pelo UtiliTech");
        }
        return "Explorer reiniciado";
    }

    static string ServiceState(string name) =>
        Wmi.Query($"SELECT State FROM Win32_Service WHERE Name='{name}'").Select(s => Wmi.Str(s, "State")).FirstOrDefault() ?? "";

    /// <summary>
    /// Roda um comando do Windows, registra a saída linha a linha e devolve o código de saída.
    /// Não espera a saída "fechar": alguns comandos (ex.: gpupdate) deixam processos em segundo plano
    /// segurando a saída, e esperar por ela travaria o app. Passado o limite de tempo, encerra o comando.
    /// </summary>
    internal static async Task<int> Step(Action<string> log, string file, string args, int timeoutMinutes = 10)
    {
        log($"> {file} {args}");
        // Comandos de console (net, ipconfig, netsh) escrevem na página de código OEM (850 em português)
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };
        using var p = new Process { StartInfo = psi };
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) log("  " + e.Data.Trim());
        }
        p.OutputDataReceived += OnLine;
        p.ErrorDataReceived += OnLine;
        if (!p.Start()) throw new InvalidOperationException($"Não foi possível executar {file}");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.StandardInput.Close(); // nenhum comando fica esperando resposta do teclado

        // HasExited olha só o processo; WaitForExitAsync também esperaria a saída fechar (e travaria)
        var limit = DateTime.Now.AddMinutes(timeoutMinutes);
        while (!p.HasExited)
        {
            if (DateTime.Now > limit)
            {
                try { p.Kill(entireProcessTree: true); }
                catch { }
                log($"  (encerrado após {timeoutMinutes} min sem terminar)");
                return -1;
            }
            await Task.Delay(200);
        }
        await Task.Delay(300); // últimas linhas que ainda estavam a caminho
        return p.ExitCode;
    }
}
