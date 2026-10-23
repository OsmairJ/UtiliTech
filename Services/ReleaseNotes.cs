using System.IO;
using System.Text.RegularExpressions;

namespace UtiliTech.Services;

/// <summary>As novidades de uma versão (seção "## vX.Y.Z — título" do NOVIDADES.md).</summary>
public record ReleaseSection(Version Version, string Title, List<string> Items)
{
    public string Header => $"v{Version.ToString(3)} · {Title}";
}

/// <summary>
/// Notas da versão: o NOVIDADES.md vai embutido no .exe (funciona sem internet). Na primeira vez que o app abre
/// depois de atualizar, mostra as seções entre a última versão vista e a atual (inclusive as puladas).
/// </summary>
public static class ReleaseNotes
{
    const string ResourceName = "UtiliTech.NOVIDADES.md";

    public static string Embedded()
    {
        using var s = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(ResourceName);
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Lê as seções "## vX.Y.Z — título" com os itens "- ..." embaixo, da mais nova para a mais antiga.</summary>
    public static List<ReleaseSection> Parse(string markdown)
    {
        var list = new List<ReleaseSection>();
        ReleaseSection? cur = null;
        foreach (var raw in markdown.Split('\n'))
        {
            string line = raw.TrimEnd('\r').TrimEnd();
            var h = Regex.Match(line, @"^##\s+v(\d+\.\d+\.\d+)\s*(?:[—–-]\s*(.*))?$");
            if (h.Success)
            {
                cur = new ReleaseSection(Version.Parse(h.Groups[1].Value), h.Groups[2].Value.Trim(), new List<string>());
                list.Add(cur);
            }
            else if (cur != null && line.StartsWith("- "))
                cur.Items.Add(line[2..].Trim());
            else if (line.StartsWith("#")) cur = null;
        }
        return list.OrderByDescending(s => s.Version).ToList();
    }

    /// <summary>Seções novas para quem estava na <paramref name="lastSeen"/> e agora está na <paramref name="current"/>.</summary>
    public static List<ReleaseSection> Since(IEnumerable<ReleaseSection> all, Version? lastSeen, Version current) =>
        all.Where(s => (lastSeen == null || s.Version > lastSeen) && s.Version <= current).OrderByDescending(s => s.Version).ToList();

    /// <summary>
    /// O que mostrar ao abrir: nada no primeiro uso (sem versão vista) nem quando a versão não mudou;
    /// depois de atualizar, as seções novas. Devolve também a versão a gravar como vista.
    /// </summary>
    public static (List<ReleaseSection> Show, string Save) OnStartup(string lastSeenText, Version current, IEnumerable<ReleaseSection> all)
    {
        string save = current.ToString(3);
        if (!Version.TryParse(lastSeenText, out var lastSeen)) return (new(), save);
        if (lastSeen >= current) return (new(), save);
        return (Since(all, lastSeen, current), save);
    }

    /// <summary>Texto da seção para a release do GitHub (o workflow faz o mesmo em PowerShell).</summary>
    public static string SectionText(string markdown, string tag)
    {
        var m = Regex.Match(markdown, $@"(?ms)^##\s+{Regex.Escape(tag)}\b.*?(?=^##\s|\z)");
        return m.Success ? m.Value.Trim() : "";
    }
}
