using System.IO;
using Microsoft.Win32;

namespace UtiliTech.Services;

public class StartupEntry : Observable
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";
    internal RegistryKey ApprovedRoot { get; init; } = Registry.CurrentUser;
    internal string ApprovedPath { get; init; } = "";
    internal string ValueName { get; init; } = "";

    bool _enabled;
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }
}

/// <summary>
/// Programas que abrem com o Windows. Ativar/desativar usa as mesmas chaves StartupApproved do
/// Gerenciador de Tarefas, então nada é apagado e a mudança aparece lá também.
/// </summary>
public static class StartupManager
{
    const string Run = @"Microsoft\Windows\CurrentVersion\Run";
    const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        AddRegistry(list, Registry.CurrentUser, @"Software\" + Run, Approved + "Run", "Registro · usuário atual");
        AddRegistry(list, Registry.LocalMachine, @"SOFTWARE\" + Run, Approved + "Run", "Registro · todos os usuários");
        AddRegistry(list, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + Run, Approved + "Run32", "Registro · 32 bits");
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "Pasta Inicializar · usuário atual");
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "Pasta Inicializar · todos os usuários");
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static void AddRegistry(List<StartupEntry> list, RegistryKey root, string runPath, string approvedPath, string source)
    {
        using var key = root.OpenSubKey(runPath);
        if (key == null) return;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new StartupEntry
            {
                Name = name,
                ValueName = name,
                Command = key.GetValue(name)?.ToString() ?? "",
                Source = source,
                ApprovedRoot = root,
                ApprovedPath = approvedPath,
                Enabled = IsEnabled(root, approvedPath, name),
            });
        }
    }

    static void AddFolder(List<StartupEntry> list, string folder, RegistryKey root, string source)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new StartupEntry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                ValueName = fileName,
                Command = ShortcutTarget(file),
                Source = source,
                ApprovedRoot = root,
                ApprovedPath = Approved + "StartupFolder",
                Enabled = IsEnabled(root, Approved + "StartupFolder", fileName),
            });
        }
    }

    // Byte 0 do valor: par (02/06) = ativado, ímpar (03/07) = desativado; sem valor = ativado
    static bool IsEnabled(RegistryKey root, string path, string valueName)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(valueName) is not byte[] { Length: > 0 } b || (b[0] & 1) == 0;
    }

    public static void SetEnabled(StartupEntry e, bool enabled)
    {
        using var k = e.ApprovedRoot.CreateSubKey(e.ApprovedPath, true);
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
        k.SetValue(e.ValueName, data, RegistryValueKind.Binary);
    }

    static string ShortcutTarget(string file)
    {
        if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return file;
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return file;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic lnk = shell.CreateShortcut(file);
            string target = lnk.TargetPath;
            string args = lnk.Arguments;
            return string.IsNullOrEmpty(target) ? file : $"\"{target}\" {args}".Trim();
        }
        catch { return file; }
    }

    /// <summary>Extrai o caminho do .exe de uma linha de comando, se o arquivo existir.</summary>
    public static string? ExecutablePath(string command)
    {
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        string path;
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            path = end > 0 ? c[1..end] : c.Trim('"');
        }
        else
        {
            int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exe > 0 ? c[..(exe + 4)] : c.Split(' ')[0];
        }
        return File.Exists(path) ? path : null;
    }
}
