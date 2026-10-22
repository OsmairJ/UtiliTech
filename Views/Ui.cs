using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace UtiliTech.Views;

static class Ui
{
    public static Brush Hex(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public static void Open(string pathOrUrl) =>
        Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });

    public static void Error(string message)
    {
        Services.Logger.Error("Aviso ao usuário: " + message);
        MessageBox.Show(message, "UtiliTech", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
