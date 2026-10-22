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
    static readonly Dictionary<string, string> Dark = new()
    {
        ["BgBrush"] = "#0F1115", ["PanelBrush"] = "#161920", ["CardBrush"] = "#1D2129", ["InputBrush"] = "#12151B",
        ["LineBrush"] = "#2A2F3A", ["HoverBrush"] = "#262B35", ["TextBrush"] = "#E6E8EC", ["MutedBrush"] = "#8B93A1",
        ["AccentBrush"] = "#3B82F6", ["DangerBrush"] = "#DC2626",
        ["ScrollThumbBrush"] = "#3A404C", ["ScrollThumbHoverBrush"] = "#4F5768", ["ScrollThumbDragBrush"] = "#646E82",
    };

    static readonly Dictionary<string, string> Light = new()
    {
        ["BgBrush"] = "#F3F5F8", ["PanelBrush"] = "#FFFFFF", ["CardBrush"] = "#FFFFFF", ["InputBrush"] = "#F7F9FB",
        ["LineBrush"] = "#DFE3E8", ["HoverBrush"] = "#EDF0F4", ["TextBrush"] = "#1E2330", ["MutedBrush"] = "#667085",
        ["AccentBrush"] = "#2563EB", ["DangerBrush"] = "#DC2626",
        ["ScrollThumbBrush"] = "#C5CBD3", ["ScrollThumbHoverBrush"] = "#A9B0BA", ["ScrollThumbDragBrush"] = "#8C94A0",
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
