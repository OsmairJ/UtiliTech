using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace UtiliTech.Services;

public class CatalogApp : Observable
{
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    /// <summary>Trecho do nome em "Aplicativos instalados" (alternativas separadas por |).</summary>
    public string Match { get; init; } = "";
    /// <summary>Instalador local na pasta Instaladores (aceita curinga, ex.: pjeoffice*.exe). Vazio = winget.</summary>
    public string? File { get; init; }
    /// <summary>Parâmetros para o instalador local (ex.: /S para silencioso).</summary>
    public string? Args { get; init; }
    /// <summary>Página oficial para baixar o instalador local.</summary>
    public string? Url { get; init; }

    public bool IsLocal => !string.IsNullOrEmpty(File);
    public string Source => IsLocal ? $"{Category} · pendrive" : Category;

    bool _selected;
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }

    string _status = "";
    public string Status { get => _status; set => SetField(ref _status, value); }

    string _statusColor = Palette.Muted;
    public string StatusColor { get => _statusColor; set => SetField(ref _statusColor, value); }

    public AppEntry ToEntry() => new(Name, Id, Category, Match, File, Args, Url);

    public static CatalogApp From(AppEntry e) => new()
    {
        Name = e.Name, Id = e.Id, Category = e.Category, Match = e.Match, File = e.File, Args = e.Args, Url = e.Url
    };
}

/// <summary>Item da lista do técnico, como fica gravado em apps.json.</summary>
public record AppEntry(string Name, string Id, string Category, string Match,
    string? File = null, string? Args = null, string? Url = null);

public record SearchResult(string Name, string Id, string Version);

public record InstallResult(bool Success, string Message);

public static class AppInstaller
{
    // Códigos de saída do winget (APPINSTALLER_CLI_ERROR_*)
    const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    const int NoApplicationsFound = unchecked((int)0x8A150014);
    const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    const int RebootRequired = unchecked((int)0x8A150109);

    const string ListFileName = "apps.json";

    /// <summary>Lista inicial, usada até o técnico salvar a própria lista.</summary>
    public static List<CatalogApp> DefaultCatalog() => new()
    {
        App("Google Chrome", "Google.Chrome", "Navegador", "Google Chrome"),
        App("Mozilla Firefox", "Mozilla.Firefox", "Navegador", "Mozilla Firefox"),
        App("7-Zip", "7zip.7zip", "Compactador", "7-Zip"),
        App("WinRAR", "RARLab.WinRAR", "Compactador", "WinRAR"),
        App("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit", "Escritório", "Adobe Acrobat"),
        App("LibreOffice", "TheDocumentFoundation.LibreOffice", "Escritório", "LibreOffice"),
        App("VLC media player", "VideoLAN.VLC", "Mídia", "VLC media player"),
        App("AnyDesk", "AnyDesk.AnyDesk", "Acesso remoto", "AnyDesk"),
        App("TeamViewer", "TeamViewer.TeamViewer", "Acesso remoto", "TeamViewer"),
        App("Zoom", "Zoom.Zoom", "Comunicação", "Zoom"),
        App("NAPS2 (digitalização)", "Cyanfish.NAPS2", "Digitalização", "NAPS2"),
        App("Java (JRE 8)", "Oracle.JavaRuntimeEnvironment", "Runtime", "Java 8"),
        App("Visual C++ 2015-2022 x64", "Microsoft.VCRedist.2015+.x64", "Runtime", "C++ 2015-2022 Redistributable (x64)|C++ v14 Redistributable (x64)"),
        App(".NET Desktop Runtime 8", "Microsoft.DotNet.DesktopRuntime.8", "Runtime", "Windows Desktop Runtime - 8."),
        App("Notepad++", "Notepad++.Notepad++", "Utilitário", "Notepad++"),
        App("CrystalDiskInfo", "CrystalDewWorld.CrystalDiskInfo", "Diagnóstico", "CrystalDiskInfo"),
        // Instaladores locais: ficam na pasta Instaladores, ao lado do UtiliTech.exe no pendrive
        // (os instaladores da Lexmark ficam em Manutenção → Impressoras → Lexmark do pendrive)
        Local("PJe Office Pro", "Jurídico", "pjeoffice*.exe", "PJe Office Pro|PJeOffice Pro", "https://pje-office.pje.jus.br/pro/"),
    };

    static CatalogApp App(string name, string id, string category, string match) =>
        new() { Name = name, Id = id, Category = category, Match = match };

    static CatalogApp Local(string name, string category, string file, string match, string? url = null) =>
        new() { Name = name, Category = category, File = file, Match = match, Url = url };

    /// <summary>Pasta dos instaladores locais, ao lado do .exe.</summary>
    public static string InstallersFolder => Path.Combine(AppContext.BaseDirectory, "Instaladores");

    /// <summary>Caminho do instalador local (o mais recente, se o nome tiver curinga), ou null se não estiver lá.</summary>
    public static string? FindLocalInstaller(CatalogApp app)
    {
        if (!app.IsLocal) return null;
        foreach (var folder in new[] { InstallersFolder, AppContext.BaseDirectory })
        {
            if (!Directory.Exists(folder)) continue;
            var match = new DirectoryInfo(folder).GetFiles(app.File!)
                .OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (match != null) return match.FullName;
        }
        return null;
    }

    public static async Task<InstallResult> InstallLocalAsync(CatalogApp app)
    {
        var path = FindLocalInstaller(app);
        if (path == null) return new(false, "Instalador não encontrado");

        var psi = new ProcessStartInfo(path, app.Args ?? "") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        Process? p;
        try { p = Process.Start(psi); }
        catch (Win32Exception ex) { return new(false, "Não abriu: " + ex.Message); }
        if (p == null) return new(false, "Não abriu o instalador");

        using (p)
        {
            await p.WaitForExitAsync();
            return p.ExitCode switch
            {
                0 => new(true, "Instalado"),
                3010 or 1641 => new(true, "Instalado (reinicie o PC)"),
                var c => new(false, $"Falhou (código {c})")
            };
        }
    }

    // Ao lado do .exe primeiro (a lista vai junto no pendrive); se a pasta não aceitar escrita, no AppData
    static string[] ListPaths() => new[]
    {
        Path.Combine(AppContext.BaseDirectory, ListFileName),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UtiliTech", ListFileName),
    };

    /// <summary>Lista do técnico (apps.json) ou a lista padrão, se ainda não houver uma salva.</summary>
    public static List<CatalogApp> LoadCatalog()
    {
        // Também lê a lista salva quando o app ainda se chamava Técnico Toolkit
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TecnicoToolkit", ListFileName);
        foreach (var path in ListPaths().Append(legacy))
        {
            try
            {
                if (!File.Exists(path)) continue;
                var entries = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(path));
                if (entries != null) return entries.Select(CatalogApp.From).ToList();
            }
            catch { }
        }
        return DefaultCatalog();
    }

    /// <summary>Grava a lista e devolve onde ela ficou.</summary>
    public static string SaveCatalog(IEnumerable<CatalogApp> apps)
    {
        var json = JsonSerializer.Serialize(apps.Select(a => a.ToEntry()).ToList(), new JsonSerializerOptions { WriteIndented = true });
        Exception? last = null;
        foreach (var path in ListPaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new IOException("Não foi possível salvar a lista de programas.", last);
    }

    /// <summary>Nomes em "Aplicativos instalados" (registro de desinstalação, 64/32 bits e usuário).</summary>
    public static List<string> InstalledNames()
    {
        var names = new List<string>();
        var sources = new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (root, path) in sources)
        {
            using var key = root.OpenSubKey(path);
            if (key == null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var s = key.OpenSubKey(sub);
                if (s?.GetValue("DisplayName") is string n) names.Add(n);
            }
        }
        return names;
    }

    public static bool IsInstalled(CatalogApp app, List<string> installedNames) =>
        app.Match.Split('|', StringSplitOptions.RemoveEmptyEntries)
           .Any(m => installedNames.Any(n => n.Contains(m, StringComparison.OrdinalIgnoreCase)));

    public static async Task<InstallResult> InstallAsync(string id)
    {
        var (code, _) = await RunWingetAsync("install", "--id", id, "-e", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity");
        return code switch
        {
            null => new(false, "winget não encontrado"),
            0 => new(true, "Instalado"),
            UpdateNotApplicable or PackageAlreadyInstalled => new(true, "Já instalado"),
            RebootRequired => new(true, "Instalado (reinicie o PC)"),
            NoApplicationsFound => new(false, "Não encontrado no winget"),
            int c => new(false, $"Falhou (código 0x{c:X8})")
        };
    }

    /// <summary>Procura programas no repositório do winget. Devolve null se o winget não existir.</summary>
    public static async Task<List<SearchResult>?> SearchAsync(string query)
    {
        var (code, output) = await RunWingetAsync("search", query, "--source", "winget",
            "--accept-source-agreements", "--disable-interactivity");
        return code == null ? null : ParseTable(output);
    }

    // A saída do winget é uma tabela alinhada por espaços: cabeçalho, linha de "-----" e as linhas.
    // As colunas são achadas pela posição das palavras do cabeçalho (funciona em português e inglês).
    static List<SearchResult> ParseTable(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l) // descarta o "spinner" de progresso
            .ToList();
        var results = new List<SearchResult>();
        int sep = lines.FindIndex(l => l.Trim().Length > 10 && l.Trim().All(c => c == '-'));
        if (sep < 1) return results;

        string header = lines[sep - 1];
        var starts = new List<int>();
        for (int i = 0; i < header.Length; i++)
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' ')) starts.Add(i);
        if (starts.Count < 3) return results;

        foreach (var line in lines.Skip(sep + 1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string Col(int k)
            {
                int from = Math.Min(starts[k], line.Length);
                int to = k + 1 < starts.Count ? Math.Min(starts[k + 1], line.Length) : line.Length;
                return line[from..to].Trim();
            }
            string id = Col(1).Split(' ')[0];
            if (id.Length == 0) continue;
            results.Add(new SearchResult(Col(0).TrimEnd('…').Trim(), id, Col(2)));
        }
        return results;
    }

    // ---------- Onde está o winget ----------
    // O atalho "winget" fica no perfil de quem registrou o Instalador de Aplicativo. Ao abrir o UtiliTech como
    // administrador com outra conta, ou logo depois de formatar, esse atalho não existe: aí o winget.exe é
    // procurado dentro do pacote instalado no Windows e, se nem o pacote estiver registrado, ele é registrado.

    const string AppInstallerFamily = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";

    public const string WingetMissingMessage =
        "O winget não foi encontrado neste Windows, nem depois de tentar registrá-lo.\n\n" +
        "Clique em Instalar selecionados de novo para o UtiliTech instalar o winget, ou atualize o \"Instalador de Aplicativo\" " +
        "pela Microsoft Store.";
    static string? _wingetPath;

    /// <summary>Caminho do winget.exe, ou null se não houver. Com <paramref name="register"/>, tenta registrar o pacote.</summary>
    public static async Task<string?> FindWingetAsync(bool register = true)
    {
        if (_wingetPath != null && File.Exists(_wingetPath)) return _wingetPath;
        _wingetPath = FindWingetAlias() ?? await FindWingetInPackageAsync();
        if (_wingetPath == null && register)
        {
            await RunPowerShellAsync($"Add-AppxPackage -RegisterByFamilyName -MainPackage {AppInstallerFamily}");
            _wingetPath = FindWingetAlias() ?? await FindWingetInPackageAsync();
        }
        return _wingetPath;
    }

    static string? FindWingetAlias()
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"));
        foreach (var folder in folders)
        {
            try
            {
                var path = Path.Combine(folder.Trim(), "winget.exe");
                if (File.Exists(path)) return path;
            }
            catch { }
        }
        return null;
    }

    static async Task<string?> FindWingetInPackageAsync()
    {
        var folders = new List<string>();
        // Pastas do pacote para todos os usuários (precisa de administrador, que o UtiliTech já é)
        var output = await RunPowerShellAsync(
            "Get-AppxPackage -AllUsers -Name Microsoft.DesktopAppInstaller | ForEach-Object { $_.InstallLocation }");
        folders.AddRange(output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        try
        {
            var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            folders.AddRange(Directory.GetDirectories(apps, "Microsoft.DesktopAppInstaller_*"));
        }
        catch { } // a pasta WindowsApps costuma negar a listagem
        return PickWinget(folders, File.Exists);
    }

    /// <summary>Entre as pastas do Instalador de Aplicativo, a da versão mais nova que tem winget.exe.</summary>
    internal static string? PickWinget(IEnumerable<string> packageFolders, Func<string, bool> exists) =>
        packageFolders
            .Select(f => Path.Combine(f, "winget.exe"))
            .Where(exists)
            .OrderByDescending(p => PackageVersion(Path.GetFileName(Path.GetDirectoryName(p)!)))
            .FirstOrDefault();

    /// <summary>Versão no nome da pasta do pacote (ex.: Microsoft.DesktopAppInstaller_1.24.25200.0_x64__8wekyb3d8bbwe).</summary>
    internal static Version PackageVersion(string folderName)
    {
        var parts = folderName.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out var v) ? v : new Version(0, 0);
    }

    static async Task<string> RunPowerShellAsync(string command)
    {
        var (output, errors) = await WingetSetup.PowerShellAsync(command);
        if (errors.Trim().Length > 0) Logger.Info("winget (PowerShell): " + errors.Trim());
        return output;
    }

    static async Task<(int? Code, string Output)> RunWingetAsync(params string[] args)
    {
        var winget = await FindWingetAsync();
        if (winget == null) return (null, "");
        var psi = new ProcessStartInfo(winget)
        {
            WorkingDirectory = Path.GetDirectoryName(winget)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process? p;
        try { p = Process.Start(psi); }
        catch (Win32Exception) { p = null; }
        if (p == null) return (null, "");

        using (p)
        {
            // Lê a saída para o buffer não encher e travar o winget
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            return (p.ExitCode, stdout.Result);
        }
    }
}
