using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>
/// Troca o tema na hora: substitui os pincéis do Theme.xaml nos recursos do app
/// (as telas usam DynamicResource, então se atualizam sozinhas).
/// </summary>
public static class ThemeManager
{
    // Preto e branco: o destaque é a própria cor do texto (botão principal branco no escuro, preto no claro)
    static readonly Dictionary<string, string> Dark = new()
    {
        ["BgBrush"] = "#000000", ["PanelBrush"] = "#0B0B0C", ["CardBrush"] = "#111113", ["InputBrush"] = "#0B0B0C",
        ["LineBrush"] = "#26262A", ["HoverBrush"] = "#1C1C1F", ["SelectedBrush"] = "#222226",
        ["TextBrush"] = "#F2F2F2", ["MutedBrush"] = "#8C8C93",
        ["AccentBrush"] = "#F2F2F2", ["AccentTextBrush"] = "#0A0A0A", ["DangerBrush"] = "#DC2626",
        ["ScrollThumbBrush"] = "#2E2E33", ["ScrollThumbHoverBrush"] = "#46464D", ["ScrollThumbDragBrush"] = "#5E5E66",
    };

    static readonly Dictionary<string, string> Light = new()
    {
        ["BgBrush"] = "#F2F2F4", ["PanelBrush"] = "#FFFFFF", ["CardBrush"] = "#FFFFFF", ["InputBrush"] = "#F7F7F8",
        ["LineBrush"] = "#E3E3E7", ["HoverBrush"] = "#EEEEF1", ["SelectedBrush"] = "#E6E6EA",
        ["TextBrush"] = "#111113", ["MutedBrush"] = "#6B6B73",
        ["AccentBrush"] = "#111113", ["AccentTextBrush"] = "#FFFFFF", ["DangerBrush"] = "#DC2626",
        ["ScrollThumbBrush"] = "#CFCFD6", ["ScrollThumbHoverBrush"] = "#B1B1BA", ["ScrollThumbDragBrush"] = "#93939C",
    };

    public static bool IsLight { get; private set; }

    public static void Apply(bool light)
    {
        IsLight = light;
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in light ? Light : Dark)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }
        Palette.Text = (light ? Light : Dark)["TextBrush"];
        Palette.Accent = Palette.Text; // destaque monocromático: "em andamento" na cor do texto
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barra de título escura no tema escuro (Windows 10 20H1+ e 11).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsLight ? 0 : 1;
        const int DwmwaUseImmersiveDarkMode = 20;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }
}
