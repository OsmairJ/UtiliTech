using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace UtiliTech.Services;

public enum CleanKind { Folders, RecycleBin }

/// <summary>Uma categoria de limpeza: pastas cujo conteúdo é apagado (a pasta em si fica).</summary>
public class CleanCategory : Observable
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public CleanKind Kind { get; init; } = CleanKind.Folders;
    /// <summary>Pastas a esvaziar (calculadas na hora, ex.: um cache por perfil de navegador).</summary>
    public Func<IEnumerable<string>> Folders { get; init; } = Enumerable.Empty<string>;
    /// <summary>Filtro de arquivos (ex.: thumbcache_*.db); null = tudo.</summary>
    public string? FilePattern { get; init; }
    /// <summary>Serviços parados durante a limpeza e religados depois (ex.: Windows Update).</summary>
    public string[] StopServices { get; init; } = Array.Empty<string>();
    /// <summary>Trechos de caminho que nunca são apagados.</summary>
    public string[] Exclude { get; init; } = Array.Empty<string>();

    internal bool IsExcluded(string path) => Exclude.Any(x => path.Contains(x, StringComparison.OrdinalIgnoreCase));

    bool _selected = true;
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }

    long _size = -1;
    public long Size
    {
        get => _size;
        set
        {
            SetField(ref _size, value);
            SizeText = value < 0 ? "" : value == 0 ? "vazio" : Fmt.Bytes(value);
        }
    }

    string _sizeText = "";
    public string SizeText { get => _sizeText; set => SetField(ref _sizeText, value); }
}

public record CleanResult(long Freed, int Skipped);

public static class Cleaner
{
    static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // não segue atalhos de pasta (junctions)
    };

    public static List<CleanCategory> Categories()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return new()
        {
            new()
            {
                Name = "Temporários do usuário", Description = @"%TEMP% — sobras de instaladores e programas",
                Folders = () => new[] { Path.GetTempPath() },
                Exclude = new[] { @"\.net\" }, // arquivos que o próprio UtiliTech (.exe único) extrai para rodar
            },
            new() { Name = "Temporários do Windows", Description = @"C:\Windows\Temp", Folders = () => new[] { Path.Combine(windows, "Temp") } },
            new()
            {
                Name = "Cache do Windows Update", Description = "Atualizações já baixadas (o Windows baixa de novo se precisar)",
                Folders = () => new[] { Path.Combine(windows, "SoftwareDistribution", "Download") },
                StopServices = new[] { "wuauserv", "bits" },
            },
            // Desmarcada por padrão: o cliente pode ter algo na lixeira que ainda quer
            new() { Name = "Lixeira", Description = "Arquivos apagados de todas as unidades · confirme com o cliente", Kind = CleanKind.RecycleBin, Selected = false },
            new()
            {
                Name = "Miniaturas", Description = "Cache das miniaturas de imagens e vídeos do Explorer",
                Folders = () => new[] { Path.Combine(local, "Microsoft", "Windows", "Explorer") }, FilePattern = "thumbcache_*.db",
            },
            new()
            {
                Name = "Cache dos navegadores", Description = "Chrome, Edge e Firefox · só o cache: senhas, histórico e logins ficam",
                Folders = () => BrowserCaches(local),
            },
            new()
            {
                Name = "Relatórios de erro", Description = "Relatórios de travamento já enviados ou na fila (WER)",
                Folders = () => new[]
                {
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(local, "CrashDumps"),
                },
            },
        };
    }

    static IEnumerable<string> BrowserCaches(string local)
    {
        foreach (var userData in new[] { Path.Combine(local, "Google", "Chrome", "User Data"), Path.Combine(local, "Microsoft", "Edge", "User Data") })
        {
            if (!Directory.Exists(userData)) continue;
            foreach (var profile in Directory.GetDirectories(userData))
                foreach (var cache in new[] { Path.Combine(profile, "Cache"), Path.Combine(profile, "Code Cache"), Path.Combine(profile, "GPUCache") })
                    if (Directory.Exists(cache)) yield return cache;
        }
        var firefox = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox))
            foreach (var profile in Directory.GetDirectories(firefox))
            {
                var cache = Path.Combine(profile, "cache2");
                if (Directory.Exists(cache)) yield return cache;
            }
    }

    public static long Measure(CleanCategory c)
    {
        if (c.Kind == CleanKind.RecycleBin) return RecycleBinSize();
        long total = 0;
        foreach (var folder in c.Folders().Where(Directory.Exists))
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles(c.FilePattern ?? "*", c.FilePattern == null ? AllFiles : TopOnly))
            {
                if (c.IsExcluded(f.FullName)) continue;
                try { total += f.Length; }
                catch { }
            }
        return total;
    }

    static readonly EnumerationOptions TopOnly = new() { IgnoreInaccessible = true };

    public static CleanResult Clean(CleanCategory c)
    {
        if (c.Kind == CleanKind.RecycleBin)
        {
            long size = RecycleBinSize();
            SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4); // sem confirmação, sem progresso, sem som
            return new CleanResult(size - RecycleBinSize(), 0);
        }

        foreach (var s in c.StopServices) RunQuiet("net", $"stop {s} /y");
        long freed = 0;
        int skipped = 0;
        try
        {
            foreach (var folder in c.Folders().Where(Directory.Exists))
            {
                var root = new DirectoryInfo(folder);
                foreach (var f in root.EnumerateFiles(c.FilePattern ?? "*", c.FilePattern == null ? AllFiles : TopOnly).ToList())
                {
                    if (c.IsExcluded(f.FullName)) continue;
                    try
                    {
                        long len = f.Length;
                        if (f.IsReadOnly) f.IsReadOnly = false;
                        f.Delete();
                        freed += len;
                    }
                    catch { skipped++; } // em uso por algum programa: fica para a próxima
                }
                if (c.FilePattern != null) continue;
                // Subpastas que ficaram vazias, da mais funda para a mais rasa
                foreach (var d in root.EnumerateDirectories("*", AllFiles).OrderByDescending(d => d.FullName.Length).ToList())
                {
                    if (c.IsExcluded(d.FullName + "\\")) continue;
                    try { if (!d.EnumerateFileSystemInfos().Any()) d.Delete(); }
                    catch { }
                }
            }
        }
        finally
        {
            foreach (var s in c.StopServices.Reverse()) RunQuiet("net", $"start {s}");
        }
        return new CleanResult(freed, skipped);
    }

    static void RunQuiet(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(60_000);
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    static long RecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }
}
