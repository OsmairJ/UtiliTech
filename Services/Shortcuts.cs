using System.Windows.Input;

namespace UtiliTech.Services;

/// <summary>Um atalho de teclado mostrado em Configurações (e no tooltip do menu).</summary>
public record Shortcut(string Keys, string Description, string Page = "");

/// <summary>Atalhos de teclado do app e o tamanho da janela para telas pequenas.</summary>
public static class Shortcuts
{
    /// <summary>Ctrl+número → tela (as mais usadas no atendimento).</summary>
    static readonly (int Number, string Page, string Name)[] Pages =
    {
        (1, "overview", "Visão geral"), (2, "system", "Sistema"), (3, "monitor", "Monitor"), (4, "network", "Rede"),
        (5, "checklist", "Checklist"), (6, "repairs", "Reparos rápidos"), (7, "drivers", "Drivers"),
        (8, "printers", "Impressoras"), (9, "attendance", "Atendimentos"),
    };

    public static IReadOnlyList<Shortcut> All { get; } =
        new[] { new Shortcut("Ctrl+K", "Busca rápida: acha qualquer tela ou ferramenta") }
        .Concat(Pages.Select(p => new Shortcut($"Ctrl+{p.Number}", p.Name, p.Page)))
        .Concat(new[]
        {
            new Shortcut("Ctrl+,", "Configurações", "settings"),
            new Shortcut("F5", "Atualizar a tela aberta"),
            new Shortcut("F1", "Esta lista de atalhos"),
            new Shortcut("Esc", "Fechar a busca ou a confirmação"),
        })
        .ToList();

    /// <summary>Tela de um atalho Ctrl+tecla (1-9 do teclado ou do numérico, vírgula); null se não for atalho de tela.</summary>
    public static string? PageFor(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control) return null;
        if (key == Key.OemComma) return "settings";
        int n = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => 0,
        };
        return Pages.FirstOrDefault(p => p.Number == n).Page;
    }

    /// <summary>Atalho de uma tela, para o tooltip do menu ("Ctrl+4"); null se não tiver.</summary>
    public static string? KeysFor(string page) => All.FirstOrDefault(s => s.Page == page)?.Keys;

    // ---------- Tamanho da janela ----------

    /// <summary>
    /// Ajusta a janela à área útil da tela (em DIPs, já com a escala do Windows): notebook 1366×768 a 125% tem
    /// ~1093×580 úteis, menos que a janela padrão. Tela pequena abre maximizada; os mínimos nunca passam da tela.
    /// </summary>
    public static (double Width, double Height, double MinWidth, double MinHeight, bool Maximize) FitWindow(
        double areaWidth, double areaHeight, double width = 1280, double height = 820, double minWidth = 980, double minHeight = 620)
    {
        double minW = Math.Min(minWidth, areaWidth), minH = Math.Min(minHeight, areaHeight);
        bool small = areaWidth < width + 40 || areaHeight < height + 40;
        return (Math.Min(width, areaWidth), Math.Min(height, areaHeight), minW, minH, small);
    }

    /// <summary>Largura do menu lateral: mais fino em janela estreita.</summary>
    public static double SidebarWidth(double windowWidth) => windowWidth < 1150 ? 212 : 248;

    /// <summary>Colunas de uma grade de cartões que cabem na largura (pelo menos 1, no máximo as do layout).</summary>
    public static int ColumnsFor(double width, double minItemWidth, int max) =>
        width <= 0 || minItemWidth <= 0 ? max : Math.Clamp((int)(width / minItemWidth), 1, max);
}
