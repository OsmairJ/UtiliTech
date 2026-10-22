using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UtiliTech.Services;

/// <param name="Sha256">Hash informado pelo próprio GitHub para o arquivo (campo digest), se houver.</param>
/// <param name="HashUrl">Arquivo UtiliTech.exe.sha256 publicado junto na release, se houver.</param>
public record UpdateInfo(Version Version, string Tag, string DownloadUrl, long Size, string Notes, string? Sha256 = null, string? HashUrl = null);

/// <summary>
/// Atualização pelo GitHub Releases: compara a última release com a versão atual, baixa o UtiliTech.exe,
/// confere o SHA-256 com o publicado na release, troca o arquivo (o Windows deixa renomear um .exe em uso)
/// e reabre o app.
/// </summary>
public static class Updater
{
    const string AssetName = "UtiliTech.exe";
    const string HashAssetName = AssetName + ".sha256";
    const string OldSuffix = ".old";

    public static Version Current
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => "v" + Current.ToString(3);

    static HttpClient Client()
    {
        // Proxy de empresa: manda as credenciais do Windows, como o navegador
        var http = new HttpClient(new HttpClientHandler { DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("UtiliTech/" + Current.ToString(3));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Resultado de cada verificação: (versão nova ou null, mensagem de erro ou null). Chega em thread de fundo.</summary>
    public static event Action<UpdateInfo?, string?>? Checked;

    /// <summary>Procura versão nova; null se já está na última. Lança exceção se o GitHub não responder.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string repo)
    {
        try
        {
            var update = await CheckCoreAsync(repo);
            Checked?.Invoke(update, null);
            return update;
        }
        catch (Exception ex)
        {
            Checked?.Invoke(null, ex.Message);
            throw;
        }
    }

    static async Task<UpdateInfo?> CheckCoreAsync(string repo)
    {
        repo = repo.Trim().Trim('/');
        if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) repo = repo[19..];
        if (repo.Count(c => c == '/') != 1) throw new ArgumentException("Use o formato usuário/repositório (ex.: maria/UtiliTech).");

        using var http = Client();
        using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Repositório sem releases publicadas (ou repositório privado/inexistente).");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) throw new InvalidOperationException($"Versão inválida na release: {tag}");
        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));

        var assets = root.GetProperty("assets").EnumerateArray().ToList();
        JsonElement Asset(string name) =>
            assets.FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase));
        var asset = Asset(AssetName);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException($"A release {tag} não tem o arquivo {AssetName}.");

        if (latest <= Current) return null;

        // O GitHub informa o SHA-256 de cada arquivo ("digest": "sha256:..."); o .sha256 da release é a segunda opção
        string? digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        string? sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
        var hashAsset = Asset(HashAssetName);
        string? hashUrl = hashAsset.ValueKind == JsonValueKind.Undefined ? null : hashAsset.GetProperty("browser_download_url").GetString();

        return new UpdateInfo(latest, tag, asset.GetProperty("browser_download_url").GetString()!,
            asset.GetProperty("size").GetInt64(), root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            sha, hashUrl);
    }

    /// <summary>Baixa, troca o .exe e abre a versão nova. Quem chama deve fechar o app em seguida.</summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double> progress)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Não foi possível localizar o UtiliTech.exe.");
        string downloaded = exe + ".download";

        string actual;
        using (var http = Client())
        {
            using (var resp = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? update.Size;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(downloaded);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read));
                    sha.AppendData(buffer, 0, read);
                    done += read;
                    if (total > 0) progress.Report(done * 100.0 / total);
                }
                actual = Convert.ToHexString(sha.GetHashAndReset());
            }
            if (new FileInfo(downloaded).Length < 1_000_000)
            {
                File.Delete(downloaded);
                throw new InvalidOperationException("O download veio incompleto.");
            }

            string? expected = update.Sha256;
            if (expected == null && update.HashUrl != null)
                expected = ParseHash(await http.GetStringAsync(update.HashUrl));
            if (expected == null)
            {
                File.Delete(downloaded);
                throw new InvalidOperationException($"A release {update.Tag} não informa o SHA-256 do {AssetName}: por segurança a atualização não foi instalada. Baixe pelo site do GitHub.");
            }
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(downloaded);
                Logger.Error($"Atualização {update.Tag} recusada: SHA-256 esperado {expected}, recebido {actual}");
                throw new InvalidOperationException("O arquivo baixado não confere com o publicado na release (SHA-256 diferente). " +
                                                    "Pode ter sido corrompido ou alterado no caminho; nada foi trocado.");
            }
        }
        Logger.Info($"SHA-256 do {update.Tag} conferido: {actual}");

        string old = exe + OldSuffix;
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);            // o .exe em uso pode ser renomeado
        File.Move(downloaded, exe);
        Logger.Info($"Atualizado de {CurrentText} para {update.Tag}");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Lê o hash de um arquivo .sha256 ("hash  nome" ou só o hash); null se não achar.</summary>
    internal static string? ParseHash(string text)
    {
        var m = Regex.Match(text, @"\b[0-9a-fA-F]{64}\b");
        return m.Success ? m.Value : null;
    }

    /// <summary>Apaga o .exe antigo que ficou da última atualização.</summary>
    public static void CleanupPreviousVersion()
    {
        try
        {
            var old = Environment.ProcessPath + OldSuffix;
            if (File.Exists(old)) File.Delete(old);
        }
        catch { }
    }
}
