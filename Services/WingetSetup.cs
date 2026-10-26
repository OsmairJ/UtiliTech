using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UtiliTech.Services;

/// <summary>
/// Instala o winget sem a Microsoft Store, com os arquivos oficiais da release do microsoft/winget-cli
/// (pacote, dependências e licença). Os arquivos ficam em Instaladores\winget no pendrive; se não estiverem lá,
/// são baixados do GitHub uma vez (conferindo o SHA-256) e guardados para as próximas formatações.
/// </summary>
public static class WingetSetup
{
    public const string Repo = "microsoft/winget-cli";
    public const string BundleName = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
    public const string DependenciesName = "DesktopAppInstaller_Dependencies.zip";
    const string LicenseSuffix = "_License1.xml";

    public record Asset(string Name, string Url, long Size, string? Sha256);

    /// <summary>Onde os arquivos do winget ficam no pendrive.</summary>
    public static string PendriveFolder => Path.Combine(AppInstaller.InstallersFolder, "winget");

    /// <summary>Os três arquivos necessários na release (pacote, dependências e licença); null se faltar algum.</summary>
    internal static List<Asset>? PickAssets(string releaseJson)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => new Asset(
                a.GetProperty("name").GetString() ?? "",
                a.GetProperty("browser_download_url").GetString() ?? "",
                a.GetProperty("size").GetInt64(),
                Updater.ParseDigest(a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null)))
            .ToList();
        var picked = new[]
        {
            assets.FirstOrDefault(a => a.Name.Equals(BundleName, StringComparison.OrdinalIgnoreCase)),
            assets.FirstOrDefault(a => a.Name.Equals(DependenciesName, StringComparison.OrdinalIgnoreCase)),
            assets.FirstOrDefault(a => IsLicense(a.Name)),
        };
        return picked.Any(a => a == null) ? null : picked.Select(a => a!).ToList();
    }

    internal static bool IsLicense(string fileName) => fileName.EndsWith(LicenseSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pasta das dependências dentro do zip para este Windows.</summary>
    internal static string ArchFolder(Architecture arch) => arch switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64",
    };

    /// <summary>A pasta tem pacote, dependências e licença?</summary>
    public static bool HasFiles(string folder) =>
        File.Exists(Path.Combine(folder, BundleName)) &&
        File.Exists(Path.Combine(folder, DependenciesName)) &&
        Directory.GetFiles(folder, "*" + LicenseSuffix).Length > 0;

    /// <summary>Instala o winget (do pendrive ou baixando do GitHub) e confere se ele passou a ser encontrado.</summary>
    public static async Task<InstallResult> InstallAsync(IProgress<string> status)
    {
        string folder;
        try
        {
            if (HasFiles(PendriveFolder))
            {
                folder = PendriveFolder;
                status.Report("Usando os arquivos do winget da pasta Instaladores\\winget");
            }
            else folder = await DownloadAsync(status);
        }
        catch (Exception ex)
        {
            Logger.Error("Download do winget", ex);
            return new(false, "Não foi possível baixar o winget do GitHub: " + ex.Message);
        }

        status.Report("Instalando o winget (pode levar alguns minutos)...");
        string depsDir = Path.Combine(Path.GetTempPath(), "UtiliTech-winget-dependencias");
        string errors;
        try
        {
            if (Directory.Exists(depsDir)) Directory.Delete(depsDir, true);
            ZipFile.ExtractToDirectory(Path.Combine(folder, DependenciesName), depsDir);
            var deps = Directory.GetFiles(Path.Combine(depsDir, ArchFolder(RuntimeInformation.OSArchitecture)), "*.appx");
            var license = Directory.GetFiles(folder, "*" + LicenseSuffix).OrderByDescending(File.GetLastWriteTime).First();
            var (output, stderr) = await PowerShellAsync(InstallScript(Path.Combine(folder, BundleName), deps, license));
            errors = (output + "\n" + stderr).Trim();
        }
        catch (Exception ex)
        {
            Logger.Error("Instalação do winget", ex);
            return new(false, "A instalação do winget falhou: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(depsDir)) Directory.Delete(depsDir, true); } catch { }
        }

        if (errors.Trim().Length > 0) Logger.Info("Instalação do winget: " + errors.Trim());
        if (await AppInstaller.FindWingetAsync() != null)
        {
            Logger.Info("winget instalado pelo UtiliTech");
            return new(true, "winget instalado");
        }
        return new(false, "O winget não apareceu depois da instalação." + (errors.Trim().Length > 0 ? "\n\n" + errors.Trim() : ""));
    }

    /// <summary>
    /// Provisiona o pacote para todos os usuários (inclusive os que ainda vão entrar) e instala para quem está usando agora.
    /// Escreve na saída só os erros, um por linha.
    /// </summary>
    internal static string InstallScript(string bundle, IEnumerable<string> dependencies, string license)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var deps = "@(" + string.Join(",", dependencies.Select(Q)) + ")";
        return $$"""
            $deps = {{deps}}
            try { Add-AppxProvisionedPackage -Online -PackagePath {{Q(bundle)}} -DependencyPackagePath $deps -LicensePath {{Q(license)}} -ErrorAction Stop | Out-Null }
            catch { 'Provisionar: ' + $_.Exception.Message }
            try { Add-AppxPackage -Path {{Q(bundle)}} -DependencyPath $deps -ForceApplicationShutdown -ErrorAction Stop }
            catch { 'Instalar para o usuário: ' + $_.Exception.Message }
            """;
    }

    /// <summary>Baixa os arquivos da última release para o pendrive (ou para a pasta temporária, se o pendrive não aceitar escrita).</summary>
    static async Task<string> DownloadAsync(IProgress<string> status)
    {
        status.Report("Procurando a versão mais nova do winget no GitHub...");
        using var http = Updater.Client();
        http.Timeout = TimeSpan.FromMinutes(30);
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        var assets = PickAssets(json) ?? throw new InvalidOperationException("a release do winget não tem os arquivos esperados.");

        string folder = WritableFolder();
        long total = assets.Sum(a => a.Size), before = 0;
        foreach (var asset in assets)
        {
            string target = Path.Combine(folder, asset.Name);
            if (File.Exists(target) && new FileInfo(target).Length == asset.Size && await HashMatchesAsync(target, asset.Sha256))
            {
                before += asset.Size;
                continue; // já baixado numa tentativa anterior
            }
            await DownloadFileAsync(http, asset, target, done =>
                status.Report($"Baixando o winget do GitHub: {(before + done) / 1_048_576} de {total / 1_048_576} MB"));
            before += asset.Size;
        }
        // Licenças de versões antigas confundem a escolha: fica só a da versão baixada
        var license = assets.First(a => IsLicense(a.Name)).Name;
        foreach (var old in Directory.GetFiles(folder, "*" + LicenseSuffix).Where(f => !Path.GetFileName(f).Equals(license, StringComparison.OrdinalIgnoreCase)))
            try { File.Delete(old); } catch { }
        return folder;
    }

    static string WritableFolder()
    {
        foreach (var folder in new[] { PendriveFolder, Path.Combine(Path.GetTempPath(), "UtiliTech", "winget") })
        {
            try
            {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".teste");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return folder;
            }
            catch { }
        }
        throw new IOException("nenhuma pasta aceitou gravar os arquivos do winget.");
    }

    static async Task DownloadFileAsync(HttpClient http, Asset asset, string target, Action<long> progress)
    {
        string part = target + ".part";
        string actual;
        using (var resp = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(part);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long done = 0, lastReport = 0;
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read));
                sha.AppendData(buffer, 0, read);
                done += read;
                if (done - lastReport > 2_000_000) { progress(done); lastReport = done; }
            }
            actual = Convert.ToHexString(sha.GetHashAndReset());
        }
        if (asset.Sha256 != null && !asset.Sha256.Equals(actual, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidOperationException($"{asset.Name} não confere com o publicado pela Microsoft (SHA-256 diferente).");
        }
        File.Move(part, target, overwrite: true);
    }

    static async Task<bool> HashMatchesAsync(string path, string? expected)
    {
        if (expected == null) return true;
        await using var f = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(f)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Roda um comando do PowerShell sem janela; devolve a saída e os erros.</summary>
    internal static async Task<(string Output, string Errors)> PowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Saída em UTF-8 para os acentos das mensagens de erro do Windows chegarem certos
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                     "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " + command })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return ("", "PowerShell não abriu");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            return (stdout.Result, stderr.Result);
        }
        catch (Exception ex)
        {
            return ("", ex.Message);
        }
    }
}
