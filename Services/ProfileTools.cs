using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace UtiliTech.Services;

/// <summary>Um perfil de usuário do PC (pasta em C:\Users).</summary>
public class UserProfileInfo : Observable
{
    public string Sid { get; init; } = "";
    /// <summary>DOMINIO\usuario, ou vazio se a conta não existe mais (perfil órfão).</summary>
    public string Account { get; init; } = "";
    public string Path { get; init; } = "";
    public DateTime? LastUse { get; init; }
    /// <summary>Em uso agora (usuário logado ou com o registro carregado).</summary>
    public bool Loaded { get; init; }
    public bool IsCurrentUser { get; init; }

    public string Name => Account.Length > 0 ? Account : System.IO.Path.GetFileName(Path) + " (conta não existe mais)";
    public bool Orphan => Account.Length == 0;

    public int? DaysUnused(DateTime now) => LastUse is { } d ? (int)(now - d).TotalDays : null;

    string _sizeText = "calculando...";
    public string SizeText { get => _sizeText; set => SetField(ref _sizeText, value); }

    public long Size { get; set; } = -1;

    /// <summary>Texto da linha: último uso e estado.</summary>
    public string Detail { get; set; } = "";
    public string Flag { get; set; } = "";
    public string FlagColor { get; set; } = Palette.Muted;
    public bool CanDelete => !Loaded && !IsCurrentUser;
}

/// <summary>Conta local do Windows (não as do domínio).</summary>
public record LocalAccount(string Name, bool Disabled, bool Admin)
{
    public string Detail => (Disabled ? "desativada" : "ativa") + (Admin ? " · administrador" : "");
    public string Color => Disabled ? Palette.Muted : Admin ? Palette.Warn : Palette.Text;
}

/// <summary>
/// Perfis de usuário: lista (dono, último uso, tamanho, em uso), apaga perfil antigo pelo Windows
/// (Win32_UserProfile.Delete: pasta + registro) e copia os dados de um perfil para outro.
/// </summary>
public static class ProfileTools
{
    /// <summary>Perfil parado há mais que isso é "antigo".</summary>
    public const int OldDays = 90;

    // ---------- Listar ----------

    public static List<UserProfileInfo> Profiles()
    {
        string me = WindowsIdentity.GetCurrent().User?.Value ?? "";
        var list = new List<UserProfileInfo>();
        foreach (var p in Wmi.Query("SELECT SID, LocalPath, Loaded, Special, LastUseTime FROM Win32_UserProfile"))
        {
            if (Wmi.Get(p, "Special") is true) continue;
            string sid = Wmi.Str(p, "SID"), path = Wmi.Str(p, "LocalPath");
            if (path.Length == 0 || IsSystemSid(sid)) continue;
            list.Add(new UserProfileInfo
            {
                Sid = sid,
                Account = AccountName(sid),
                Path = path,
                Loaded = Wmi.Get(p, "Loaded") is true,
                IsCurrentUser = sid == me,
                LastUse = LastUse(Wmi.Date(p, "LastUseTime"), sid, path),
            });
        }
        return list.OrderByDescending(p => p.LastUse ?? DateTime.MinValue).ToList();
    }

    /// <summary>Contas de serviço (S-1-5-18/19/20) e as que não são de gente.</summary>
    public static bool IsSystemSid(string sid) => sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20" || !sid.StartsWith("S-1-5-21-");

    static string AccountName(string sid)
    {
        try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
        catch { return ""; } // conta apagada ou de domínio fora de alcance
    }

    /// <summary>
    /// O Windows só preenche LastUseTime de quem está logado; para os outros usa a hora em que o perfil foi
    /// descarregado (registro ProfileList) e a última gravação do NTUSER.DAT. Vale a mais recente.
    /// </summary>
    static DateTime? LastUse(DateTime? wmi, string sid, string path)
    {
        var dates = new List<DateTime>();
        if (wmi is { } w && w.Year > 2000) dates.Add(w);
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
            if (FileTime(k?.GetValue("LocalProfileUnloadTimeHigh"), k?.GetValue("LocalProfileUnloadTimeLow")) is { } u) dates.Add(u);
            if (FileTime(k?.GetValue("LocalProfileLoadTimeHigh"), k?.GetValue("LocalProfileLoadTimeLow")) is { } l) dates.Add(l);
        }
        catch { }
        try
        {
            var nt = new FileInfo(System.IO.Path.Combine(path, "NTUSER.DAT"));
            if (nt.Exists) dates.Add(nt.LastWriteTime);
        }
        catch { }
        return dates.Count > 0 ? dates.Max() : null;
    }

    public static DateTime? FileTime(object? high, object? low)
    {
        if (high is not int h || low is not int l) return null;
        long ft = ((long)(uint)h << 32) | (uint)l;
        if (ft <= 0) return null;
        try { return DateTime.FromFileTime(ft); }
        catch { return null; }
    }

    /// <summary>Preenche Detail/Flag de cada perfil (último uso, em uso, antigo, órfão).</summary>
    public static void Describe(UserProfileInfo p, DateTime now)
    {
        int? days = p.DaysUnused(now);
        p.Detail = p.Path + " · " + (p.LastUse is { } d ? $"último uso {d:dd/MM/yyyy}" + (days switch { > 1 => $" (há {days} dias)", 1 => " (ontem)", _ => " (hoje)" }) : "último uso desconhecido");
        (p.Flag, p.FlagColor) = p.IsCurrentUser ? ("VOCÊ", Palette.Accent)
            : p.Loaded ? ("EM USO", Palette.Accent)
            : p.Orphan ? ("CONTA APAGADA", Palette.Warn)
            : days >= OldDays ? ($"PARADO HÁ {days} DIAS", Palette.Warn)
            : ("", Palette.Muted);
    }

    public static long FolderSize(string path)
    {
        long total = 0;
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", opts))
            {
                try { total += f.Length; } catch { }
            }
        }
        catch { }
        return total;
    }

    public static List<LocalAccount> LocalAccounts()
    {
        var admins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var group = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Translate(typeof(NTAccount)).Value.Split('\\')[^1];
            foreach (var m in Wmi.Query($"SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent=\"Win32_Group.Domain='{Environment.MachineName}',Name='{group}'\""))
            {
                string part = Wmi.Str(m, "PartComponent");
                int i = part.IndexOf("Name=\"", StringComparison.Ordinal);
                if (i >= 0) admins.Add(part[(i + 6)..].TrimEnd('"'));
            }
        }
        catch { }
        return Wmi.Query("SELECT Name, Disabled FROM Win32_UserAccount WHERE LocalAccount=True")
            .Select(a => new LocalAccount(Wmi.Str(a, "Name"), Wmi.Get(a, "Disabled") is true, admins.Contains(Wmi.Str(a, "Name"))))
            .Where(a => a.Name is not ("DefaultAccount" or "WDAGUtilityAccount"))
            .OrderBy(a => a.Disabled).ThenBy(a => a.Name).ToList();
    }

    // ---------- Apagar ----------

    /// <summary>Apaga o perfil como o Windows faz em Sistema → Perfis de usuário: pasta e registro juntos.</summary>
    public static void Delete(UserProfileInfo p)
    {
        if (!p.CanDelete) throw new InvalidOperationException("Este perfil está em uso e não pode ser apagado.");
        var wmi = Wmi.Query($"SELECT * FROM Win32_UserProfile WHERE SID='{p.Sid.Replace("'", "")}'").FirstOrDefault()
                  ?? throw new InvalidOperationException("O perfil não foi encontrado (já foi apagado?).");
        wmi.Delete();
    }

    // ---------- Copiar ----------

    /// <summary>Pastas copiadas de um perfil para outro (nome na tela, subpasta do perfil).</summary>
    public static readonly IReadOnlyList<(string Label, string Folder)> CopyFolders = new[]
    {
        ("Área de Trabalho", "Desktop"), ("Documentos", "Documents"), ("Imagens", "Pictures"), ("Downloads", "Downloads"),
        ("Vídeos", "Videos"), ("Música", "Music"), ("Favoritos", "Favorites"),
    };

    /// <summary>Favoritos dos navegadores (arquivo dentro do perfil).</summary>
    public static readonly IReadOnlyList<(string Label, string File)> BrowserBookmarks = new[]
    {
        ("Favoritos do Chrome", @"AppData\Local\Google\Chrome\User Data\Default\Bookmarks"),
        ("Favoritos do Edge", @"AppData\Local\Microsoft\Edge\User Data\Default\Bookmarks"),
    };

    /// <summary>
    /// De onde copiar cada pasta: se o OneDrive do perfil tiver a pasta (Área de Trabalho/Documentos/Imagens
    /// sincronizadas), os arquivos estão lá. Só pastas que existem na origem.
    /// </summary>
    public static List<(string Label, string Source, string Target)> CopyPlan(string sourceProfile, string targetProfile)
    {
        var plan = new List<(string, string, string)>();
        var oneDrives = Directory.Exists(sourceProfile)
            ? Directory.GetDirectories(sourceProfile, "OneDrive*").ToList() : new List<string>();
        foreach (var (label, folder) in CopyFolders)
        {
            string src = System.IO.Path.Combine(sourceProfile, folder);
            string? od = oneDrives.Select(o => System.IO.Path.Combine(o, folder))
                .FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any());
            if (od != null && (!Directory.Exists(src) || !Directory.EnumerateFileSystemEntries(src).Any())) src = od;
            if (Directory.Exists(src)) plan.Add((label, src, System.IO.Path.Combine(targetProfile, folder)));
        }
        return plan;
    }

    /// <summary>robocopy: copia tudo (subpastas), sem apagar nada no destino, sem seguir atalhos de pasta, poucas tentativas.</summary>
    public static string RobocopyArgs(string source, string target) =>
        $"\"{source.TrimEnd('\\')}\" \"{target.TrimEnd('\\')}\" /E /XJ /COPY:DAT /DCOPY:T /R:1 /W:1 /NP /NFL /NDL /MT:8";

    /// <summary>robocopy: 0-7 = copiou (ou nada a copiar); 8 ou mais = falhou em algum arquivo.</summary>
    public static bool RobocopyOk(int code) => code is >= 0 and < 8;

    public static async Task<(int Copied, int Failed)> CopyAsync(string sourceProfile, string targetProfile, Action<string> log)
    {
        int ok = 0, failed = 0;
        foreach (var (label, src, dst) in CopyPlan(sourceProfile, targetProfile))
        {
            log($"--- {label}: {src} → {dst}");
            int code = await Repairs.Step(log, "robocopy", RobocopyArgs(src, dst), timeoutMinutes: 120);
            if (RobocopyOk(code)) ok++; else failed++;
        }
        foreach (var (label, file) in BrowserBookmarks)
        {
            string src = System.IO.Path.Combine(sourceProfile, file), dst = System.IO.Path.Combine(targetProfile, file);
            if (!File.Exists(src)) continue;
            if (File.Exists(dst)) { log($"--- {label}: o destino já tem favoritos, não foram substituídos (importe pelo navegador)"); continue; }
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
                File.Copy(src, dst);
                log($"--- {label}: copiados");
                ok++;
            }
            catch (Exception ex) { log($"--- {label}: {ex.Message}"); failed++; }
        }
        return (ok, failed);
    }
}
