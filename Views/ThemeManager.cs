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
    // Cores do Discord: fundos cinza-azulados em camadas e destaque "blurple" (#5865F2)
    static readonly Dictionary<string, string> Dark = new()
    {
        ["TitleBrush"] = "#1E1F22", ["BgBrush"] = "#313338", ["PanelBrush"] = "#232428", ["CardBrush"] = "#2B2D31",
        ["InputBrush"] = "#1E1F22", ["LineBrush"] = "#3F4147", ["HoverBrush"] = "#35373C", ["SelectedBrush"] = "#404249",
        ["TextBrush"] = "#F2F3F5", ["MutedBrush"] = "#949BA4",
        ["AccentBrush"] = "#5865F2", ["AccentTextBrush"] = "#FFFFFF", ["DangerBrush"] = "#DA373C",
        ["ScrollThumbBrush"] = "#1A1B1E", ["ScrollThumbHoverBrush"] = "#4E5058", ["ScrollThumbDragBrush"] = "#6D6F78",
    };

    static readonly Dictionary<string, string> Light = new()
    {
        ["TitleBrush"] = "#E3E5E8", ["BgBrush"] = "#FFFFFF", ["PanelBrush"] = "#EBEDEF", ["CardBrush"] = "#F2F3F5",
        ["InputBrush"] = "#E3E5E8", ["LineBrush"] = "#E1E2E4", ["HoverBrush"] = "#E8E9EC", ["SelectedBrush"] = "#D7D9DC",
        ["TextBrush"] = "#313338", ["MutedBrush"] = "#5C5E66",
        ["AccentBrush"] = "#5865F2", ["AccentTextBrush"] = "#FFFFFF", ["DangerBrush"] = "#DA373C",
        ["ScrollThumbBrush"] = "#C4C9CE", ["ScrollThumbHoverBrush"] = "#A9AEB4", ["ScrollThumbDragBrush"] = "#8E9297",
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
        Palette.Accent = (light ? Light : Dark)["AccentBrush"]; // "em andamento", versão nova... em blurple
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
