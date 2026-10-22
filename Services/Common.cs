using System.ComponentModel;
using System.Management;
using System.Runtime.CompilerServices;

namespace UtiliTech.Services;

public record InfoItem(string Label, string Value);
public record InfoSection(string Title, List<InfoItem> Items);

public static class Palette
{
    public const string Good = "#22C55E";
    public const string Warn = "#F59E0B";
    public const string Bad = "#EF4444";
    public const string Muted = "#8B93A1";
    public const string Accent = "#3B82F6";
    /// <summary>Cor do texto principal do tema atual (muda com o tema claro/escuro).</summary>
    public static string Text { get; set; } = "#E6E8EC";
}

/// <summary>Estado compartilhado entre páginas (ex.: resultado dos testes vai no relatório).</summary>
public static class AppState
{
    public static string? LastMemoryTest { get; set; }
    public static string? LastStressTest { get; set; }
    /// <summary>Versão nova encontrada ao abrir o app (null = nenhuma ou não verificado).</summary>
    public static UpdateInfo? AvailableUpdate { get; set; }

    /// <summary>Testes feitos nesta sessão, um por linha (null se nenhum).</summary>
    public static string? TestsSummary()
    {
        var lines = new List<string>();
        if (LastMemoryTest != null) lines.Add("Memória RAM: " + LastMemoryTest);
        if (LastStressTest != null) lines.Add("Estresse da CPU: " + LastStressTest);
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public static class Fmt
{
    /// <summary>Tamanho em base 1024 (RAM, espaço livre).</summary>
    public static string Bytes(long b)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = b;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

    /// <summary>Tamanho em base 1000, como o fabricante anuncia o disco.</summary>
    public static string DiskSize(long b) => b >= 1_000_000_000_000 ? $"{b / 1e12:0.##} TB" : $"{b / 1e9:0} GB";
}

/// <summary>Consultas WMI que nunca lançam exceção: propriedade ausente vira vazio/null.</summary>
public static class Wmi
{
    public static List<ManagementObject> Query(string wql, string scope = @"root\cimv2")
    {
        var list = new List<ManagementObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            foreach (ManagementObject mo in searcher.Get()) list.Add(mo);
        }
        catch { }
        return list;
    }

    public static object? Get(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop]; }
        catch { return null; }
    }

    public static string Str(ManagementBaseObject mo, string prop) => Get(mo, prop)?.ToString()?.Trim() ?? "";

    public static long? LongN(ManagementBaseObject mo, string prop)
    {
        var v = Get(mo, prop);
        if (v == null) return null;
        try { return Convert.ToInt64(v); }
        catch { return null; }
    }

    public static long Long(ManagementBaseObject mo, string prop) => LongN(mo, prop) ?? 0;

    public static string[] Arr(ManagementBaseObject mo, string prop) =>
        Get(mo, prop) is Array a ? a.Cast<object>().Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToArray() : Array.Empty<string>();

    public static DateTime? Date(ManagementBaseObject mo, string prop)
    {
        var s = Str(mo, prop);
        if (s.Length == 0) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch { return null; }
    }
}
