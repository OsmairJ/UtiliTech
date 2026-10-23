using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace UtiliTech.Services;

/// <summary>Uma placa de rede conectada.</summary>
public record AdapterInfo(string Name, string Description, bool IsWifi, long SpeedBps, string Mac, List<string> Ipv4,
    List<string> Gateways, List<string> Dns, bool Dhcp)
{
    public string Title => $"{Name} · {Description}";
    public string SpeedText => NetworkTools.Speed(SpeedBps);
    public bool HasApipa => Ipv4.Any(NetworkTools.IsApipa);
    /// <summary>Linhas "rótulo: valor" para a tela.</summary>
    public List<InfoItem> Items => new List<InfoItem>
    {
        new("Endereço IP", Ipv4.Count > 0 ? string.Join(", ", Ipv4) + (Dhcp ? " (DHCP)" : " (fixo)") : "sem IP"),
        new("Gateway", string.Join(", ", Gateways)),
        new("DNS", string.Join(", ", Dns)),
        new("MAC", Mac),
        new("Velocidade do link", SpeedText + (NetworkTools.SlowLink(this) ? " · abaixo de 1 Gbps: confira cabo e porta do switch" : "")),
    }.Where(i => i.Value.Length > 0).ToList();
}

/// <summary>Wi-Fi conectado (lido do netsh wlan show interfaces).</summary>
public record WifiInfo(string Ssid, int? Signal, string Channel, string Rate, string Band);

public enum NetStepState { Ok, Fail, Skipped }

/// <summary>Uma etapa do "Testar conexão".</summary>
public record NetTestStep(string Name, NetStepState State, string Detail)
{
    public string Glyph => State switch { NetStepState.Ok => "", NetStepState.Fail => "", _ => "" };
    public string Color => State switch { NetStepState.Ok => Palette.Good, NetStepState.Fail => Palette.Bad, _ => Palette.Muted };
}

/// <summary>Unidade de rede mapeada do usuário (HKCU\Network).</summary>
public record MappedDrive(string Letter, string RemotePath)
{
    public string Title => $"{Letter}: → {RemotePath}";
}

/// <summary>
/// Ferramenta de rede: placas (IP, gateway, DNS, MAC, velocidade), Wi-Fi, teste de conexão em etapas
/// (IP → gateway → DNS → internet) com o diagnóstico em português, ping, rota e unidades mapeadas.
/// </summary>
public static class NetworkTools
{
    static NetworkTools() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Nome usado no teste de DNS e endereço do teste de internet (o mesmo que o Windows usa).</summary>
    public const string DnsTestHost = "www.msftconnecttest.com";
    const string InternetTestUrl = "http://www.msftconnecttest.com/connecttest.txt";

    // ---------- Placas ----------

    public static List<AdapterInfo> Adapters()
    {
        var list = new List<AdapterInfo>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var ip = ni.GetIPProperties();
            var v4 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => $"{a.Address}/{a.PrefixLength}").ToList();
            bool dhcp = false;
            try { dhcp = ip.GetIPv4Properties()?.IsDhcpEnabled ?? false; }
            catch { }
            list.Add(new AdapterInfo(ni.Name, ni.Description, ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, ni.Speed,
                FormatMac(ni.GetPhysicalAddress().GetAddressBytes()), v4,
                ip.GatewayAddresses.Select(g => g.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList(),
                ip.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList(),
                dhcp));
        }
        // Placas com gateway primeiro (as que levam à rede de verdade); virtuais (VPN, Hyper-V) por último
        return list.OrderByDescending(a => a.Gateways.Count > 0).ThenBy(a => a.IsWifi).ToList();
    }

    public static string FormatMac(byte[] b) => b.Length == 0 ? "" : string.Join("-", b.Select(x => x.ToString("X2")));

    /// <summary>IP automático do Windows (169.254.x.x): o PC não conseguiu IP do DHCP.</summary>
    public static bool IsApipa(string cidr) => cidr.StartsWith("169.254.");

    public static string Speed(long bps) => bps switch
    {
        <= 0 => "",
        >= 1_000_000_000 => $"{bps / 1e9:0.#} Gbps",
        _ => $"{bps / 1e6:0} Mbps",
    };

    /// <summary>Cabo de rede abaixo de 1 Gbps (10/100): costuma ser cabo ruim, conector ou porta antiga.</summary>
    public static bool SlowLink(AdapterInfo a) => !a.IsWifi && a.SpeedBps > 0 && a.SpeedBps < 1_000_000_000;

    // ---------- Wi-Fi ----------

    public static WifiInfo? Wifi()
    {
        try { return ParseWlan(RunText("netsh", "wlan show interfaces")); }
        catch { return null; }
    }

    /// <summary>Lê a saída do netsh (português ou inglês). Null se o Wi-Fi não estiver conectado.</summary>
    public static WifiInfo? ParseWlan(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            int sep = line.IndexOf(" : ", StringComparison.Ordinal);
            if (sep < 0) continue;
            string key = line[..sep].Trim(), value = line[(sep + 3)..].Trim();
            values.TryAdd(key, value); // primeira placa
        }
        string Get(params string[] keys) => keys.Select(k => values.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        string state = Get("Estado", "State");
        string ssid = Get("SSID");
        if (ssid.Length == 0 || !(state.StartsWith("conectado", StringComparison.OrdinalIgnoreCase) || state.StartsWith("connected", StringComparison.OrdinalIgnoreCase)))
            return null;
        string signal = Get("Sinal", "Signal").TrimEnd('%').Trim();
        return new WifiInfo(ssid, int.TryParse(signal, out int s) ? s : null, Get("Canal", "Channel"),
            Get("Taxa de recepção (Mbps)", "Receive rate (Mbps)"), Get("Banda", "Band"));
    }

    // ---------- Testar conexão ----------

    /// <summary>IP → gateway → DNS → internet. Cada etapa só roda se a anterior passou.</summary>
    public static async Task<List<NetTestStep>> QuickTestAsync(List<AdapterInfo> adapters)
    {
        var steps = new List<NetTestStep>();
        var main = adapters.FirstOrDefault(a => a.Gateways.Count > 0) ?? adapters.FirstOrDefault();

        // 1. Placa conectada com IP
        if (main == null || main.Ipv4.Count == 0)
            steps.Add(new("Placa de rede com IP", NetStepState.Fail, "Nenhuma placa conectada com endereço IP"));
        else if (main.HasApipa)
            steps.Add(new("Placa de rede com IP", NetStepState.Fail, $"{main.Name}: {main.Ipv4[0]} é o IP automático do Windows (não veio do DHCP)"));
        else
            steps.Add(new("Placa de rede com IP", NetStepState.Ok, $"{main.Name}: {string.Join(", ", main.Ipv4)}"));

        // 2. Gateway
        if (steps[^1].State != NetStepState.Ok || main!.Gateways.Count == 0)
            steps.Add(new("Gateway (roteador)", steps[^1].State == NetStepState.Ok ? NetStepState.Fail : NetStepState.Skipped,
                steps[^1].State == NetStepState.Ok ? "A placa não tem gateway configurado" : "Não testado"));
        else
        {
            var r = await PingAsync(main.Gateways[0], 3);
            steps.Add(new("Gateway (roteador)", r.Received > 0 ? NetStepState.Ok : NetStepState.Fail,
                $"{main.Gateways[0]}: " + r.Summary));
        }

        // 3. DNS (mesmo que o gateway não responda ping: alguns roteadores bloqueiam)
        if (steps[0].State != NetStepState.Ok)
            steps.Add(new("DNS (nomes de sites)", NetStepState.Skipped, "Não testado"));
        else
        {
            var (ok, detail) = await ResolveAsync(DnsTestHost);
            steps.Add(new("DNS (nomes de sites)", ok ? NetStepState.Ok : NetStepState.Fail, detail));
        }

        // 4. Internet
        if (steps[^1].State != NetStepState.Ok)
            steps.Add(new("Internet", NetStepState.Skipped, "Não testado"));
        else
        {
            var (ok, detail) = await InternetAsync();
            steps.Add(new("Internet", ok ? NetStepState.Ok : NetStepState.Fail, detail));
        }
        return steps;
    }

    /// <summary>O que provavelmente está errado, a partir das etapas, e o que fazer.</summary>
    public static string Diagnose(List<NetTestStep> steps, List<AdapterInfo> adapters)
    {
        NetStepState S(int i) => i < steps.Count ? steps[i].State : NetStepState.Skipped;
        if (adapters.Count == 0)
            return "Nenhuma placa de rede conectada: confira o cabo (luz da porta acesa?) ou conecte o Wi-Fi. Se a placa não aparece, veja em Drivers.";
        if (S(0) == NetStepState.Fail)
            return adapters.Any(a => a.HasApipa)
                ? "O PC não recebeu IP do roteador (DHCP). Use Reparos → Renovar IP; se continuar, troque o cabo ou a porta do switch."
                : "A placa está conectada mas sem endereço IP. Use Reparos → Renovar IP ou Resetar rede.";
        if (S(1) == NetStepState.Fail && S(2) == NetStepState.Fail)
            return "O roteador não responde e os nomes não resolvem: problema na rede local (cabo, porta, switch ou Wi-Fi fraco).";
        if (S(2) == NetStepState.Fail)
            return "A rede local funciona, mas o DNS não responde. Use Reparos → Limpar cache DNS e confira o DNS da placa (deve ser o do roteador ou o da empresa).";
        if (S(3) == NetStepState.Fail)
            return "A rede e o DNS funcionam, mas a internet não abre: pode ser o proxy ou o firewall da empresa (normal em rede com proxy autenticado), ou o link de internet caiu.";
        if (S(1) == NetStepState.Fail)
            return "Tudo funcionando. O roteador só não responde ping (comum, por segurança).";
        var slow = adapters.FirstOrDefault(SlowLink);
        if (slow != null)
            return $"Conexão funcionando, mas o cabo está a {slow.SpeedText}: troque o cabo ou a porta para ter 1 Gbps.";
        return "Conexão funcionando: IP, roteador, DNS e internet respondendo.";
    }

    // ---------- Ferramentas ----------

    public record PingResult(int Sent, int Received, long? AverageMs, string? Error)
    {
        public string Summary => Error ?? (Received == 0
            ? $"sem resposta ({Sent} tentativas)"
            : $"{Received} de {Sent} respostas · média {AverageMs} ms" + (Received < Sent ? " · perdeu pacotes" : ""));
    }

    public static async Task<PingResult> PingAsync(string host, int count = 4, Action<string>? log = null)
    {
        var times = new List<long>();
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    times.Add(reply.RoundtripTime);
                    log?.Invoke($"Resposta de {reply.Address}: {reply.RoundtripTime} ms");
                }
                else log?.Invoke($"Sem resposta ({Translate(reply.Status)})");
            }
            catch (PingException ex)
            {
                string msg = ex.InnerException is SocketException ? $"não foi possível encontrar \"{host}\"" : ex.Message;
                log?.Invoke("Erro: " + msg);
                return new PingResult(count, 0, null, msg);
            }
            if (i < count - 1) await Task.Delay(300);
        }
        return new PingResult(count, times.Count, times.Count > 0 ? (long)times.Average() : null, null);
    }

    static string Translate(IPStatus s) => s switch
    {
        IPStatus.TimedOut => "tempo esgotado",
        IPStatus.DestinationHostUnreachable => "destino inalcançável",
        IPStatus.DestinationNetworkUnreachable => "rede inalcançável",
        _ => s.ToString(),
    };

    public static async Task<(bool Ok, string Detail)> ResolveAsync(string host)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var task = Dns.GetHostAddressesAsync(host);
            if (await Task.WhenAny(task, Task.Delay(5000)) != task) return (false, $"{host}: o DNS não respondeu em 5 s");
            var addrs = (await task).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
            return addrs.Count > 0
                ? (true, $"{host} → {string.Join(", ", addrs.Take(3))} ({sw.ElapsedMilliseconds} ms)")
                : (false, $"{host}: sem endereço IPv4");
        }
        catch (SocketException) { return (false, $"{host}: nome não encontrado"); }
    }

    public static async Task<(bool Ok, string Detail)> InternetAsync()
    {
        try
        {
            // Proxy de empresa: manda as credenciais do Windows, como o navegador
            using var http = new HttpClient(new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials })
            { Timeout = TimeSpan.FromSeconds(8) };
            var sw = Stopwatch.StartNew();
            string body = await http.GetStringAsync(InternetTestUrl);
            return body.Contains("Microsoft Connect Test")
                ? (true, $"Site de teste da Microsoft respondeu ({sw.ElapsedMilliseconds} ms)")
                : (false, "A resposta veio diferente: página de login de Wi-Fi ou bloqueio do proxy?");
        }
        catch (TaskCanceledException) { return (false, "Sem resposta em 8 s"); }
        catch (HttpRequestException ex) { return (false, ex.StatusCode is { } c ? $"Erro HTTP {(int)c} (proxy ou firewall?)" : ex.Message); }
    }

    /// <summary>Rastrear rota (tracert -d), com a saída linha a linha no registro.</summary>
    public static Task<int> TraceAsync(string host, Action<string> log) =>
        Repairs.Step(log, "tracert", $"-d -h 20 -w 800 {host}", timeoutMinutes: 3);

    /// <summary>Endereço digitado pelo técnico: só letras, números, ponto, hífen e dois-pontos (vai na linha de comando).</summary>
    public static bool IsValidHost(string host) =>
        host.Length is > 0 and <= 253 && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':');

    // ---------- Unidades mapeadas ----------

    /// <summary>Unidades mapeadas do usuário (persistentes). Lidas do registro: o app roda como administrador e não vê as da sessão.</summary>
    public static List<MappedDrive> MappedDrives()
    {
        var list = new List<MappedDrive>();
        try
        {
            using var net = Registry.CurrentUser.OpenSubKey("Network");
            if (net == null) return list;
            foreach (var letter in net.GetSubKeyNames().OrderBy(l => l))
            {
                using var k = net.OpenSubKey(letter);
                if (k?.GetValue("RemotePath") is string path) list.Add(new MappedDrive(letter.ToUpperInvariant(), path));
            }
        }
        catch { }
        return list;
    }

    /// <summary>Letras livres para mapear (D a Z), sem as usadas por discos, pendrives e unidades já mapeadas.</summary>
    public static List<string> FreeLetters(IEnumerable<MappedDrive> mapped)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var d in DriveInfo.GetDrives()) used.Add(d.Name[..1]); }
        catch { }
        foreach (var m in mapped) used.Add(m.Letter);
        return FreeLetters(used);
    }

    public static List<string> FreeLetters(ISet<string> used) =>
        Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => ((char)c).ToString()).Where(l => !used.Contains(l)).ToList();

    /// <summary>\\servidor\pasta (com subpastas); sem aspas nem caracteres que mudem a linha de comando.</summary>
    public static bool IsValidUncPath(string path) =>
        System.Text.RegularExpressions.Regex.IsMatch(path, @"^\\\\[A-Za-z0-9._-]+(\\[^\\""<>|*?\r\n%&^]+)+$");

    /// <summary>net use X: \\servidor\pasta /persistent:yes (grava para o usuário).</summary>
    public static Task<int> MapDriveAsync(string letter, string path, Action<string> log) =>
        Repairs.Step(log, "net", $"use {letter}: \"{path}\" /persistent:yes", timeoutMinutes: 1);

    /// <summary>O caminho de rede responde? (até 4 s, para servidor fora do ar não travar a tela)</summary>
    public static async Task<bool> ReachableAsync(string uncPath)
    {
        var task = Task.Run(() => Directory.Exists(uncPath));
        return await Task.WhenAny(task, Task.Delay(4000)) == task && task.Result;
    }

    // ---------- comando ----------

    static string RunText(string file, string args)
    {
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        using var p = Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = oem,
        })!;
        string text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return text;
    }
}
