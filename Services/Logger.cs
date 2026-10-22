using System.IO;
using System.Text;

namespace UtiliTech.Services;

/// <summary>
/// Registro de erros do próprio app: logs\utilitech-AAAAMMDD.log ao lado do .exe (ou no AppData).
/// Guarda os últimos 10 dias.
/// </summary>
public static class Logger
{
    static readonly object Gate = new();
    static string? _folder;

    public static string Folder => _folder ??= ResolveFolder();

    static string ResolveFolder()
    {
        foreach (var dir in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "logs"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UtiliTech", "logs"),
                 })
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".teste");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch { }
        }
        return Path.GetTempPath();
    }

    public static string TodayFile => Path.Combine(Folder, $"utilitech-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string context, Exception? ex = null) => Write("ERRO", context, ex);

    static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}");
            if (ex != null) sb.Append($"\n    {ex.GetType().Name}: {ex.Message}\n    {ex.StackTrace?.Replace("\n", "\n    ")}");
            lock (Gate)
            {
                File.AppendAllText(TodayFile, sb + Environment.NewLine, Encoding.UTF8);
                Prune();
            }
        }
        catch { } // registrar erro nunca pode derrubar o app
    }

    static void Prune()
    {
        var old = new DirectoryInfo(Folder).GetFiles("utilitech-*.log").OrderByDescending(f => f.Name).Skip(10);
        foreach (var f in old)
        {
            try { f.Delete(); }
            catch { }
        }
    }

    /// <summary>Quantidade de erros registrados hoje.</summary>
    public static int ErrorsToday()
    {
        try
        {
            return File.Exists(TodayFile) ? File.ReadLines(TodayFile).Count(l => l.Contains("] ERRO ")) : 0;
        }
        catch { return 0; }
    }
}
