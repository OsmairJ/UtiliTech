using System.Windows;
using System.Windows.Input;
using UtiliTech.Services;

namespace UtiliTech.Views;

public enum MessageKind { Info, Warning, Error, Question }

/// <summary>Aviso ou pergunta simples no visual do app (no lugar da caixa cinza do Windows).</summary>
public partial class MessageWindow : Window
{
    bool _ok;

    MessageWindow(string title, string message, MessageKind kind, string okText, string? cancelText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        (IconText.Text, IconText.Foreground) = kind switch
        {
            MessageKind.Error => ("", Ui.Hex(Palette.Bad)),     // erro
            MessageKind.Warning => ("", Ui.Hex(Palette.Warn)),  // aviso
            MessageKind.Question => ("", Ui.Hex(Palette.Accent)),
            _ => ("", Ui.Hex(Palette.Accent)),                  // informação
        };
        OkBtn.Content = okText;
        if (cancelText != null)
        {
            CancelBtn.Content = cancelText;
            CancelBtn.Visibility = Visibility.Visible;
        }
        else OkBtn.IsCancel = true; // Esc fecha o aviso
    }

    /// <summary>Mostra a janela e devolve true se o técnico clicou no botão principal.</summary>
    public static bool Show(string title, string message, MessageKind kind = MessageKind.Info,
        string okText = "OK", string? cancelText = null)
    {
        try
        {
            var w = new MessageWindow(title, message, kind, okText, cancelText);
            if (Application.Current?.MainWindow is { IsVisible: true } main) w.Owner = main;
            else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowDialog();
            return w._ok;
        }
        catch (Exception ex)
        {
            // Se a janela do tema falhar (ex.: erro na própria tela), cai na caixa do Windows para o aviso não se perder
            Logger.Error("Janela de aviso", ex);
            var buttons = cancelText == null ? MessageBoxButton.OK : MessageBoxButton.OKCancel;
            return MessageBox.Show(message, "UtiliTech — " + title, buttons) == MessageBoxResult.OK;
        }
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        _ok = true;
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    void Body_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
