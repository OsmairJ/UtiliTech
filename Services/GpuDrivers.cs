using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UtiliTech.Services;

public enum GpuVendor { Nvidia, Amd, Intel, Other }

public record GpuInfo(string Name, GpuVendor Vendor, string WindowsDriverVersion, DateTime? DriverDate, bool HasVendorDriver)
{
    /// <summary>Versão no formato do fabricante (NVIDIA: 32.0.15.6094 → 560.94).</summary>
    public string VendorVersion => Vendor == GpuVendor.Nvidia ? GpuDrivers.NvidiaVersion(WindowsDriverVersion) : WindowsDriverVersion;
}

public record NvidiaDriver(string Version, string Name, string ReleaseDate, string DownloadUrl, long Size);

/// <summary>Cartão de uma placa de vídeo na tela Drivers.</summary>
public class GpuCard : Observable
{
    public GpuInfo Info { get; init; } = null!;
    public string Name => Info.Name;
    public string Detail { get; init; } = "";
    public string SitePage { get; init; } = "";

    NvidiaDriver? _latest;
    public NvidiaDriver? Latest { get => _latest; set => SetField(ref _latest, value); }

    string _status = "";
    public string Status { get => _status; set => SetField(ref _status, value); }

    string _statusColor = Palette.Muted;
    public string StatusColor { get => _statusColor; set => SetField(ref _statusColor, value); }

    string _actionText = "";
    public string ActionText { get => _actionText; set => SetField(ref _actionText, value); }

    bool _actionVisible;
    public bool ActionVisible { get => _actionVisible; set => SetField(ref _actionVisible, value); }

    bool _busy;
    public bool Busy { get => _busy; set => SetField(ref _busy, value); }

    double _progress;
    public double Progress { get => _progress; set => SetField(ref _progress, value); }
}

/// <summary>
/// Driver da placa de vídeo direto do fabricante: a NVIDIA tem consulta automática da versão mais nova
/// (o mesmo serviço da página "Download de drivers"); AMD e Intel usam as ferramentas oficiais deles,
/// que reconhecem a placa e instalam o driver certo.
/// </summary>
public static class GpuDrivers
{
    public const string AmdPage = "https://www.amd.com/pt/support/download/drivers.html";
    public const string NvidiaPage = "https://www.nvidia.com/pt-br/drivers/";
    public const string IntelDsaWingetId = "Intel.IntelDriverAndSupportAssistant";
    public const string IntelDsaPage = "https://www.intel.com.br/content/www/br/pt/support/intel-driver-support-assistant.html";

    public static List<GpuInfo> Detect()
    {
        var list = new List<GpuInfo>();
        foreach (var v in Wmi.Query("SELECT Name, PNPDeviceID, DriverVersion, DriverDate FROM Win32_VideoController"))
        {
            string pnp = Wmi.Str(v, "PNPDeviceID").ToUpperInvariant();
            var vendor = pnp.Contains("VEN_10DE") ? GpuVendor.Nvidia
                : pnp.Contains("VEN_1002") || pnp.Contains("VEN_1022") ? GpuVendor.Amd
                : pnp.Contains("VEN_8086") ? GpuVendor.Intel
                : GpuVendor.Other;
            string name = Wmi.Str(v, "Name");
            bool generic = name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || name.Contains("Vídeo Básico", StringComparison.OrdinalIgnoreCase);
            list.Add(new GpuInfo(name, vendor, Wmi.Str(v, "DriverVersion"), Wmi.Date(v, "DriverDate"), !generic));
        }
        // Placa dedicada primeiro
        return list.OrderBy(g => g.Vendor switch { GpuVendor.Nvidia => 0, GpuVendor.Amd => 1, GpuVendor.Intel => 2, _ => 3 }).ToList();
    }

    /// <summary>A NVIDIA numera o driver pelos últimos 5 dígitos da versão do Windows (32.0.15.6094 → 560.94).</summary>
    public static string NvidiaVersion(string windowsVersion)
    {
        var parts = windowsVersion.Split('.');
        if (parts.Length < 4) return windowsVersion;
        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5) return windowsVersion;
        digits = digits[^5..];
        return $"{int.Parse(digits[..3])}.{digits[3..]}";
    }

    // Proxy de empresa: manda as credenciais do Windows, como o navegador
    static HttpClient Client(TimeSpan timeout)
    {
        var handler = new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials };
        var http = new HttpClient(handler) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) UtiliTech");
        return http;
    }

    // ---------- NVIDIA ----------

    /// <summary>Driver Game Ready (DCH, WHQL) mais novo para a placa, pelo serviço de download da NVIDIA.</summary>
    public static async Task<NvidiaDriver> LatestNvidiaAsync(string gpuName)
    {
        using var http = Client(TimeSpan.FromSeconds(40));

        // 1) Lista de produtos: <LookupValue ParentID="série"><Name>GeForce RTX 3060</Name><Value>produto</Value>
        string xml = await http.GetStringAsync("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3");
        var products = Regex.Matches(xml, @"<LookupValue ParentID=""(\d+)""[^>]*>\s*<Name>([^<]+)</Name>\s*<Value>(\d+)</Value>")
            .Select(m => (Series: m.Groups[1].Value, Name: WebUtility.HtmlDecode(m.Groups[2].Value).Trim(), Id: m.Groups[3].Value))
            .ToList();
        if (products.Count == 0) throw new InvalidOperationException("a NVIDIA não devolveu a lista de produtos");

        string wanted = Regex.Replace(gpuName, @"^NVIDIA\s+", "", RegexOptions.IgnoreCase).Trim();
        var product = products.FirstOrDefault(p => p.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (product == default) // nome com sufixos (ex.: "… 8GB"): o produto mais longo contido no nome
            product = products.Where(p => wanted.Contains(p.Name, StringComparison.OrdinalIgnoreCase))
                              .OrderByDescending(p => p.Name.Length).FirstOrDefault();
        if (product == default) throw new InvalidOperationException($"modelo \"{wanted}\" não encontrado no site da NVIDIA");

        // 2) Driver mais novo para o produto (osID 57 = Windows 10/11 64 bits; dch=1 = padrão atual)
        string url = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
                     $"?func=DriverManualLookup&psid={product.Series}&pfid={product.Id}&osID=57&languageCode=1033" +
                     "&isWHQL=1&dch=1&sort1=0&numberOfResults=1";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("IDS", out var ids) || ids.GetArrayLength() == 0)
            throw new InvalidOperationException("a NVIDIA não informou driver para este modelo");
        var info = ids[0].GetProperty("downloadInfo");
        string Get(string p) => info.TryGetProperty(p, out var e) ? WebUtility.UrlDecode(e.GetString() ?? "") : "";
        long.TryParse(Regex.Replace(Get("DownloadURLFileSize"), @"\D", ""), out long size);
        return new NvidiaDriver(Get("Version"), Get("Name"), Get("ReleaseDateTime"), Get("DownloadURL"), size);
    }

    public static bool IsNewer(string latest, string installed) =>
        Version.TryParse(latest, out var a) && Version.TryParse(installed, out var b) && a > b;

    /// <summary>Baixa o driver, confere que é assinado pela NVIDIA e instala em silêncio. Devolve o código do instalador.</summary>
    public static async Task<int> InstallNvidiaAsync(NvidiaDriver driver, IProgress<double> progress, Action<string> log)
    {
        if (!driver.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            !new Uri(driver.DownloadUrl).Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("endereço de download inesperado: " + driver.DownloadUrl);

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "UtiliTech");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Path.GetFileName(new Uri(driver.DownloadUrl).LocalPath));

        log($"Baixando {driver.Name} {driver.Version}...");
        using (var http = Client(TimeSpan.FromMinutes(60)))
        using (var resp = await http.GetAsync(driver.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? driver.Size;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(file);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress.Report(done * 100.0 / total);
            }
        }

        try
        {
            log("Conferindo a assinatura digital...");
            string signer = SignerOf(file);
            if (!signer.Contains("NVIDIA Corporation", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"o arquivo não está assinado pela NVIDIA ({signer})");
            log("Assinado por: " + signer);

            log("Instalando em silêncio (a tela pode piscar algumas vezes)...");
            using var p = Process.Start(new ProcessStartInfo(file, "-s -noreboot") { UseShellExecute = false, CreateNoWindow = true })
                          ?? throw new InvalidOperationException("não foi possível abrir o instalador");
            await p.WaitForExitAsync();
            log($"Instalador terminou com código {p.ExitCode}");
            return p.ExitCode;
        }
        finally
        {
            try { File.Delete(file); }
            catch { }
        }
    }

    // ---------- AMD / Intel ----------

    public static Task<InstallResult> InstallIntelAssistantAsync() => AppInstaller.InstallAsync(IntelDsaWingetId);

    // ---------- Assinatura digital (WinVerifyTrust) ----------

    /// <summary>Quem assinou o arquivo; lança exceção se a assinatura não for válida.</summary>
    static string SignerOf(string file)
    {
        int result = VerifySignature(file);
        if (result != 0) throw new InvalidOperationException($"assinatura digital inválida (0x{result:X8})");
#pragma warning disable SYSLIB0057
        using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
        return cert.GetNameInfo(X509NameType.SimpleName, false);
    }

    static int VerifySignature(string file)
    {
        var fileInfo = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = file };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE (rede de empresa pode bloquear a consulta de revogação)
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO
    {
        public int cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public int cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public int dwUIChoice;
        public int fdwRevocationChecks;
        public int dwUnionChoice;
        public IntPtr pFile;
        public int dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public int dwProvFlags;
        public int dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    // ---------- Cartão da placa na tela Drivers (sem interface, para testar) ----------

    /// <summary>Monta o cartão da placa: fabricante, driver atual, site e o que fazer (NVIDIA consulta a versão depois).</summary>
    public static GpuCard Card(GpuInfo g)
    {
        string vendor = g.Vendor switch { GpuVendor.Nvidia => "NVIDIA", GpuVendor.Amd => "AMD", GpuVendor.Intel => "Intel", _ => "Outro fabricante" };
        string driver = g.HasVendorDriver
            ? $"driver {g.VendorVersion}" + (g.DriverDate is { } d ? $" de {d:dd/MM/yyyy}" : "")
            : "SEM DRIVER DO FABRICANTE";
        var card = new GpuCard
        {
            Info = g,
            Detail = $"{vendor} · {driver}",
            SitePage = g.Vendor switch
            {
                GpuVendor.Nvidia => NvidiaPage,
                GpuVendor.Amd => AmdPage,
                GpuVendor.Intel => IntelDsaPage,
                _ => "https://www.catalog.update.microsoft.com",
            },
        };
        switch (g.Vendor)
        {
            case GpuVendor.Nvidia:
                card.Status = "Consultando a versão mais nova na NVIDIA...";
                break;
            case GpuVendor.Amd:
                card.Status = "A AMD não informa a versão mais nova para outros programas. A detecção automática do site reconhece a placa e baixa o driver certo.";
                card.ActionText = "Detectar no site da AMD";
                card.ActionVisible = true;
                break;
            case GpuVendor.Intel:
                card.Status = (g.HasVendorDriver ? "" : "Sem driver: o Windows Update abaixo costuma ter um. ") +
                              "Para a versão mais nova, use o assistente oficial da Intel.";
                card.StatusColor = g.HasVendorDriver ? Palette.Muted : Palette.Warn;
                card.ActionText = "Instalar assistente da Intel";
                card.ActionVisible = true;
                break;
            default:
                card.Status = "Use a busca do Windows Update abaixo.";
                break;
        }
        return card;
    }

    /// <summary>Resultado da consulta à NVIDIA: oferece instalar se não há driver ou se o do site é mais novo.</summary>
    public static void ApplyLatest(GpuCard card, NvidiaDriver latest)
    {
        card.Latest = latest;
        string when = latest.ReleaseDate.Length > 0 ? $", lançado em {latest.ReleaseDate}" : "";
        if (!card.Info.HasVendorDriver || IsNewer(latest.Version, card.Info.VendorVersion))
        {
            card.Status = $"Versão nova disponível: {latest.Version}{when} · {Fmt.Bytes(latest.Size)}";
            card.StatusColor = Palette.Accent;
            card.ActionText = $"Baixar e instalar {latest.Version}";
            card.ActionVisible = true;
        }
        else
        {
            card.Status = $"✓ Driver em dia (o mais novo é o {latest.Version}{when})";
            card.StatusColor = Palette.Good;
            card.ActionVisible = false;
        }
    }
}
