using System.Diagnostics;
using System.Management;
using System.Net;
using System.Text.RegularExpressions;

namespace UtiliTech.Services;

/// <summary>Uma impressora instalada no PC.</summary>
public class PrinterInfo : Observable
{
    public string Name { get; init; } = "";
    public string Driver { get; init; } = "";
    public string Port { get; init; } = "";
    /// <summary>IP/nome da impressora (porta TCP/IP) ou servidor (impressora compartilhada); vazio se local.</summary>
    public string Host { get; init; } = "";
    public bool IsDefault { get; init; }
    public bool Offline { get; init; }
    /// <summary>Conexão a uma impressora compartilhada (\\servidor\impressora).</summary>
    public bool IsShared { get; init; }
    public int Jobs { get; init; }

    public bool IsVirtual => PrinterTools.IsVirtual(Port, Driver);
    public bool CanSetDefault => !IsDefault;
    public bool HasJobs => Jobs > 0;
    public bool IsGhost => PrinterTools.IsGhost(Name);

    public string Detail => string.Join(" · ", new[]
    {
        Driver,
        Host.Length > 0 ? (IsShared ? $"servidor {Host}" : $"IP {Host}") : Port,
        Jobs > 0 ? $"{Jobs} documento(s) na fila" : "",
    }.Where(s => s.Length > 0));

    public string Badges => string.Join("  ", new[]
    {
        IsDefault ? "PADRÃO" : "",
        IsGhost ? "CÓPIA (fila fantasma?)" : "",
        Offline ? "OFFLINE" : "",
        IsVirtual ? "virtual" : "",
    }.Where(s => s.Length > 0));

    public string BadgeColor => IsGhost || Offline ? Palette.Warn : IsDefault ? Palette.Accent : Palette.Muted;

    string _reach = "";
    /// <summary>Resultado do ping no IP/servidor ("responde" / "não responde").</summary>
    public string Reach { get => _reach; set => SetField(ref _reach, value); }

    string _reachColor = Palette.Muted;
    public string ReachColor { get => _reachColor; set => SetField(ref _reachColor, value); }
}

/// <summary>
/// Impressoras: lista (porta, IP, fila, padrão, offline, cópias fantasmas), página de teste, padrão,
/// limpar fila, remover, instalar por IP e conectar impressora compartilhada.
/// </summary>
public static class PrinterTools
{
    // ---------- Lista ----------

    public static List<PrinterInfo> List()
    {
        var ports = Wmi.Query("SELECT Name, HostAddress FROM Win32_TCPIPPrinterPort")
            .ToDictionary(p => Wmi.Str(p, "Name"), p => Wmi.Str(p, "HostAddress"), StringComparer.OrdinalIgnoreCase);
        var jobs = Wmi.Query("SELECT Name FROM Win32_PrintJob")
            .GroupBy(j => JobPrinter(Wmi.Str(j, "Name")), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return Wmi.Query("SELECT Name, DriverName, PortName, Default, WorkOffline, ServerName FROM Win32_Printer")
            .Select(p =>
            {
                string name = Wmi.Str(p, "Name"), port = Wmi.Str(p, "PortName"), server = Wmi.Str(p, "ServerName").TrimStart('\\');
                return new PrinterInfo
                {
                    Name = name,
                    Driver = Wmi.Str(p, "DriverName"),
                    Port = port,
                    Host = server.Length > 0 ? server : ports.GetValueOrDefault(port, ""),
                    IsShared = server.Length > 0,
                    IsDefault = Wmi.Get(p, "Default") is true,
                    Offline = Wmi.Get(p, "WorkOffline") is true,
                    Jobs = jobs.GetValueOrDefault(name),
                };
            })
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.IsVirtual).ThenBy(p => p.Name)
            .ToList();
    }

    /// <summary>Win32_PrintJob.Name vem como "Nome da impressora, 12": tira o número do trabalho.</summary>
    public static string JobPrinter(string jobName)
    {
        int i = jobName.LastIndexOf(", ", StringComparison.Ordinal);
        return i > 0 ? jobName[..i] : jobName;
    }

    /// <summary>"HP Universal (Cópia 1)" / "(Copy 2)": o Windows cria quando o driver é instalado de novo por cima.</summary>
    public static bool IsGhost(string name) => Regex.IsMatch(name, @"\((Cópia|Copia|Copy) \d+\)", RegexOptions.IgnoreCase);

    /// <summary>Impressoras que não imprimem em papel (PDF, OneNote, XPS, Fax).</summary>
    public static bool IsVirtual(string port, string driver) =>
        port.Equals("nul:", StringComparison.OrdinalIgnoreCase) || port.Equals("PORTPROMPT:", StringComparison.OrdinalIgnoreCase) ||
        port.StartsWith("SHRFAX", StringComparison.OrdinalIgnoreCase) ||
        new[] { "PDF", "OneNote", "XPS", "Fax" }.Any(k => driver.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>Drivers de impressora instalados ("Nome,4,Windows x64" vira "Nome"), sem repetir.</summary>
    public static List<string> Drivers() =>
        Wmi.Query("SELECT Name FROM Win32_PrinterDriver").Select(d => DriverName(Wmi.Str(d, "Name")))
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    public static string DriverName(string wmiName)
    {
        int i = wmiName.IndexOf(',');
        return (i >= 0 ? wmiName[..i] : wmiName).Trim();
    }

    // ---------- Ações ----------

    static ManagementObject? Find(string name) =>
        Wmi.Query($"SELECT * FROM Win32_Printer WHERE Name='{WmiEscape(name)}'").FirstOrDefault();

    public static string WmiEscape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    static uint Invoke(string name, string method)
    {
        using var p = Find(name) ?? throw new InvalidOperationException($"A impressora \"{name}\" não foi encontrada.");
        return Convert.ToUInt32(p.InvokeMethod(method, null) ?? 0u);
    }

    public static string PrintTestPage(string name)
    {
        uint code = Invoke(name, "PrintTestPage");
        return code == 0 ? "Página de teste enviada" : $"O Windows recusou a página de teste (código {code})";
    }

    public static string SetDefault(string name)
    {
        uint code = Invoke(name, "SetDefaultPrinter");
        return code == 0 ? "Definida como padrão" : $"Não foi possível definir como padrão (código {code})";
    }

    /// <summary>Cancela os documentos da fila desta impressora. Devolve quantos saíram.</summary>
    public static int ClearQueue(string name)
    {
        int removed = 0;
        foreach (var job in Wmi.Query("SELECT * FROM Win32_PrintJob"))
        {
            if (!JobPrinter(Wmi.Str(job, "Name")).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            try { job.Delete(); removed++; }
            catch { }
        }
        return removed;
    }

    public static void Remove(string name)
    {
        using var p = Find(name) ?? throw new InvalidOperationException($"A impressora \"{name}\" não foi encontrada.");
        p.Delete();
    }

    public static void OpenQueue(string name) =>
        Process.Start(new ProcessStartInfo("rundll32.exe", $"printui.dll,PrintUIEntry /o /n \"{name}\"") { UseShellExecute = false });

    // ---------- Lexmark do pendrive ----------

    /// <summary>
    /// Pacotes da Lexmark na pasta Instaladores do pendrive (autoextraíveis assinados pela Lexmark). Ao rodar, abrem
    /// o assistente da Lexmark, que procura as impressoras na rede e instala com o tipo de driver escolhido.
    /// </summary>
    public record LexmarkPackage(string Title, string Model, string DriverType, string File)
    {
        public string? Path => System.IO.File.Exists(FullPath) ? FullPath : null;
        string FullPath => System.IO.Path.Combine(AppInstaller.InstallersFolder, File);
        public bool Available => Path != null;
        public string State => Available ? $"Instaladores\\{File}" : $"Falta {File} na pasta Instaladores";
        public string StateColor => Available ? Palette.Muted : Palette.Warn;
    }

    public static readonly IReadOnlyList<LexmarkPackage> LexmarkPackages = new[]
    {
        new LexmarkPackage("Lexmark MX620 · preto e branco", "MX620", "XL", "LM PB W10.exe"),
        new LexmarkPackage("Lexmark CS725 · colorida", "CS725", "PS", "LM COLOR W10.exe"),
    };

    /// <summary>Abre o assistente da Lexmark e espera fechar. Devolve o código de saída.</summary>
    public static async Task<int> RunLexmarkAsync(LexmarkPackage pkg)
    {
        string path = pkg.Path ?? throw new InvalidOperationException(pkg.State);
        using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = AppInstaller.InstallersFolder })
                      ?? throw new InvalidOperationException("O instalador da Lexmark não abriu.");
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    /// <summary>Drivers dos modelos da Lexmark usados na empresa primeiro (instalam direto pelo IP depois da 1ª vez).</summary>
    public static List<string> SortDrivers(IEnumerable<string> drivers) =>
        drivers.OrderBy(d => LexmarkPackages.Any(p => d.Contains(p.Model, StringComparison.OrdinalIgnoreCase)) ? 0
                           : d.Contains("Lexmark", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
               .ThenBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

    // ---------- Instalar ----------

    public static bool IsValidIp(string ip) =>
        Regex.IsMatch(ip, @"^\d{1,3}(\.\d{1,3}){3}$") && IPAddress.TryParse(ip, out var a) &&
        ip.Split('.').All(p => int.Parse(p) <= 255) && !ip.StartsWith("0.") && ip != "255.255.255.255";

    /// <summary>Nome da impressora: sem aspas, barras nem caracteres de controle (vai num comando do PowerShell).</summary>
    public static bool IsValidPrinterName(string name) =>
        name.Trim().Length is > 0 and <= 80 && !name.Any(c => c is '"' or '\\' or '/' or ',' or '`' or '$' || char.IsControl(c));

    /// <summary>\\servidor\impressora</summary>
    public static bool IsValidSharePath(string path) =>
        Regex.IsMatch(path, @"^\\\\[A-Za-z0-9._-]+\\[^\\""`$\r\n]+$");

    /// <summary>Texto entre aspas simples do PowerShell (aspas simples viram duas).</summary>
    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>
    /// Script que ativa o driver (se ainda não está no spooler: os de classe da Microsoft vêm guardados no Windows),
    /// cria a porta TCP/IP (se ainda não existe) e a impressora.
    /// </summary>
    public static string InstallScript(string ip, string name, string driver)
    {
        string port = PsQuote("IP_" + ip), drv = PsQuote(driver);
        return $"$ErrorActionPreference='Stop'; " +
               $"if (-not (Get-PrinterDriver -Name {drv} -ErrorAction SilentlyContinue)) {{ Add-PrinterDriver -Name {drv} }}; " +
               $"if (-not (Get-PrinterPort -Name {port} -ErrorAction SilentlyContinue)) {{ Add-PrinterPort -Name {port} -PrinterHostAddress {PsQuote(ip)} }}; " +
               $"Add-Printer -Name {PsQuote(name.Trim())} -DriverName {drv} -PortName {port}; " +
               $"Write-Output 'Impressora criada'";
    }

    // ---------- Drivers para Instalar pelo IP ----------

    public const string Pcl6Driver = "Microsoft PCL6 Class Driver";
    public const string PsDriver = "Microsoft PS Class Driver";
    public const string IppDriver = "Microsoft IPP Class Driver";

    /// <summary>
    /// Drivers de classe que todo Windows 10/11 traz guardados (prnms005.inf) mas só entram no spooler quando usados.
    /// Servem para impressoras que falam PCL6 ou PostScript, sem baixar nada do fabricante.
    /// </summary>
    static readonly (string Name, string Models)[] BuiltInDrivers =
    {
        (Pcl6Driver, "Brother DCP-L2540DW, Samsung SCX-483x/5x3x e SCX-5835/5935X"),
        (PsDriver, "alternativa PostScript para Brother e Samsung"),
    };

    /// <summary>Um driver na lista de Instalar pelo IP: o nome real e o texto mostrado (com os modelos que atende).</summary>
    public record DriverOption(string Name, string Label, bool BuiltIn)
    {
        public override string ToString() => Label;
    }

    /// <summary>Drivers instalados + os de classe do Windows ainda não ativados, com os modelos compatíveis no texto.</summary>
    public static List<DriverOption> DriverOptions(IEnumerable<string> installed)
    {
        var names = installed.ToList();
        var options = SortDrivers(names).Select(n => new DriverOption(n, Label(n), false)).ToList();
        int at = options.FindIndex(o => !LexmarkPackages.Any(p => o.Name.Contains(p.Model, StringComparison.OrdinalIgnoreCase)));
        if (at < 0) at = options.Count;
        foreach (var (name, models) in BuiltInDrivers.Reverse())
        {
            options.RemoveAll(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            options.Insert(at, new DriverOption(name, $"{name} · {models}" + (names.Contains(name, StringComparer.OrdinalIgnoreCase) ? "" : " (vem no Windows)"), true));
        }
        return options;
    }

    static string Label(string driver) =>
        LexmarkPackages.FirstOrDefault(p => driver.Contains(p.Model, StringComparison.OrdinalIgnoreCase)) is { } p
            ? $"{driver} · {p.Title}"
            : driver.Equals(IppDriver, StringComparison.OrdinalIgnoreCase) ? $"{driver} · genérico para impressoras de rede modernas" : driver;

    /// <summary>
    /// Driver indicado para o modelo informado pela impressora (SNMP). Null se não houver um adequado instalado
    /// (ex.: Lexmark ainda sem o driver do fabricante: use o assistente do pendrive).
    /// </summary>
    public static string? RecommendDriver(string model, IEnumerable<string> available)
    {
        var list = available.ToList();
        string? Find(string part) => list.FirstOrDefault(d => d.Contains(part, StringComparison.OrdinalIgnoreCase));
        if (model.Contains("Lexmark", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(model, @"MX6[2-9]\d", RegexOptions.IgnoreCase)) return Find("MX620") ?? Find("MX622");
            if (model.Contains("CS725", StringComparison.OrdinalIgnoreCase)) return Find("CS725");
            return Find("Lexmark Universal");
        }
        if (model.Contains("Brother", StringComparison.OrdinalIgnoreCase) || model.Contains("Samsung", StringComparison.OrdinalIgnoreCase))
            return Find(Pcl6Driver);
        return Find(IppDriver);
    }

    /// <summary>Qual pacote da Lexmark do pendrive serve para o modelo (quando o driver ainda não está instalado).</summary>
    public static LexmarkPackage? LexmarkFor(string model) =>
        Regex.IsMatch(model, @"Lexmark MX6[2-9]\d", RegexOptions.IgnoreCase) ? LexmarkPackages[0]
        : model.Contains("Lexmark CS725", StringComparison.OrdinalIgnoreCase) ? LexmarkPackages[1] : null;

    // ---------- Identificar a impressora (SNMP) ----------

    /// <summary>
    /// Pergunta o modelo à impressora por SNMP v1 (só leitura, comunidade "public"): hrDeviceDescr.1 e, se não vier,
    /// sysDescr. Null se não responder em 1,5 s. "Lexmark MX622adhe 7018... MXTGM.050.040" vira "Lexmark MX622adhe".
    /// </summary>
    public static async Task<string?> IdentifyAsync(string ip) => (await SnmpInfoAsync(ip))?.Model;

    /// <summary>O que a impressora informa por SNMP: modelo, nome na rede e nº de série.</summary>
    public record SnmpInfo(string Model, string Name, string Serial);

    public static async Task<SnmpInfo?> SnmpInfoAsync(string ip)
    {
        try
        {
            using var udp = new System.Net.Sockets.UdpClient();
            // hrDeviceDescr.1, sysDescr, sysName, prtGeneralSerialNumber.1
            byte[] req = Snmp.GetRequest("1.3.6.1.2.1.25.3.2.1.3.1", "1.3.6.1.2.1.1.1.0", "1.3.6.1.2.1.1.5.0", "1.3.6.1.2.1.43.5.1.1.17.1");
            await udp.SendAsync(req, req.Length, ip, 161);
            var recv = udp.ReceiveAsync();
            if (await Task.WhenAny(recv, Task.Delay(1500)) != recv) return null;
            var v = Snmp.ParseStrings((await recv).Buffer);
            string At(int i) => i < v.Count ? v[i] : "";
            string model = At(0).Length > 0 ? At(0) : At(1);
            return model.Length > 0 ? new SnmpInfo(ShortModel(model), At(2), At(3)) : null;
        }
        catch { return null; }
    }

    // ---------- Procurar impressoras na rede ----------

    /// <summary>Uma impressora achada na rede.</summary>
    public record FoundPrinter(string Ip, string Model, string Name, string Serial, string Driver, bool Installed)
    {
        public string Title => Model.Length > 0 ? Model : "Modelo não informado (SNMP desligado)";
        public string Detail => string.Join(" · ", new[] { Ip, Name, Serial.Length > 0 ? "S/N " + Serial : "" }.Where(s => s.Length > 0));
        public string DriverText => "Driver indicado: " + Driver + (Installed ? " · já instalada neste PC" : "");
    }

    public static bool IsLexmark(string model) => model.Contains("Lexmark", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Endereços da rede da placa (sem rede, broadcast e o próprio PC). Rede maior que /22 é limitada
    /// ao bloco /22 do PC (1.022 endereços), para a busca não varrer milhares de IPs.
    /// </summary>
    public static List<string> HostsInSubnet(string ip, int prefix)
    {
        if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return new();
        prefix = Math.Clamp(prefix, 22, 30);
        byte[] b = addr.GetAddressBytes();
        uint me = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        uint net = me & mask, broadcast = net | ~mask;
        var list = new List<string>();
        for (uint h = net + 1; h < broadcast; h++)
            if (h != me) list.Add($"{h >> 24}.{(h >> 16) & 255}.{(h >> 8) & 255}.{h & 255}");
        return list;
    }

    /// <summary>
    /// Procura impressoras: testa a porta 9100 (impressão direta) em lotes, com tempo curto, e pergunta o modelo
    /// por SNMP (só leitura) a quem responder. Não muda nada nas impressoras.
    /// </summary>
    public static async Task<List<(string Ip, SnmpInfo? Info)>> ScanAsync(IReadOnlyList<string> hosts, IProgress<string>? progress,
        CancellationToken cancel)
    {
        var open = new List<string>();
        const int batch = 128;
        for (int i = 0; i < hosts.Count; i += batch)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report($"Procurando: {Math.Min(i + batch, hosts.Count)} de {hosts.Count} endereços...");
            var tries = hosts.Skip(i).Take(batch).Select(async h =>
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var connect = tcp.ConnectAsync(h, 9100);
                bool ok = await Task.WhenAny(connect, Task.Delay(600, cancel)) == connect && connect.IsCompletedSuccessfully;
                return ok ? h : null;
            });
            open.AddRange((await Task.WhenAll(tries)).OfType<string>());
        }
        progress?.Report($"{open.Count} impressora(s) responderam; perguntando o modelo...");
        var infos = await Task.WhenAll(open.Select(async ip => (ip, await SnmpInfoAsync(ip))));
        return infos.Select(x => (x.ip, x.Item2)).ToList();
    }

    /// <summary>Monta a lista final: sem as Lexmark, com o driver indicado e se já está instalada, ordenada por modelo e IP.</summary>
    public static List<FoundPrinter> NonLexmark(IEnumerable<(string Ip, SnmpInfo? Info)> found, IEnumerable<string> drivers,
        IEnumerable<string> installedHosts)
    {
        var driverList = drivers.ToList();
        var hosts = new HashSet<string>(installedHosts, StringComparer.OrdinalIgnoreCase);
        return found.Where(f => !IsLexmark(f.Info?.Model ?? ""))
            .Select(f => new FoundPrinter(f.Ip, f.Info?.Model ?? "", f.Info?.Name ?? "", f.Info?.Serial ?? "",
                RecommendDriver(f.Info?.Model ?? "", driverList) ?? IppDriver, hosts.Contains(f.Ip)))
            .OrderBy(p => p.Model.Length == 0).ThenBy(p => p.Model)
            .ThenBy(p => Version.TryParse(p.Ip, out var v) ? v : new Version())
            .ToList();
    }

    /// <summary>Tira nº de série, firmware e "version ..." do texto da impressora.</summary>
    public static string ShortModel(string descr)
    {
        string s = Regex.Replace(descr, @"\s+version\s.*$", "", RegexOptions.IgnoreCase);
        s = s.Split(';', ',')[0];
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeWhile(w => !(w.Length >= 8 && w.Any(char.IsDigit) && w.All(char.IsLetterOrDigit) && w.Count(char.IsDigit) >= 5)); // nº de série
        return string.Join(" ", words).Trim();
    }

    public static string ConnectScript(string sharePath) =>
        $"$ErrorActionPreference='Stop'; Add-Printer -ConnectionName {PsQuote(sharePath)}; Write-Output 'Impressora conectada'";

    /// <summary>Roda um script do PowerShell com a saída no registro. Devolve o código de saída.</summary>
    public static Task<int> RunPowerShell(string script, Action<string> log) =>
        Repairs.Step(log, "powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"", timeoutMinutes: 3);
}
