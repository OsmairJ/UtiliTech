using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace UtiliTech.Views;

static class Ui
{
    public static Brush Hex(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public static void Open(string pathOrUrl) =>
        Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });

    public static void Error(string message, string title = "Não deu certo")
    {
        Services.Logger.Error("Aviso ao usuário: " + message);
        MessageWindow.Show(title, message, MessageKind.Warning);
    }

    /// <summary>Pergunta sim/não no visual do app; true = botão principal.</summary>
    public static bool Ask(string title, string message, string yesText = "Sim", string noText = "Cancelar") =>
        MessageWindow.Show(title, message, MessageKind.Question, yesText, noText);
}
