using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Aviso no canto inferior direito da tela, por cima de tudo; some sozinho em 10 s.</summary>
public partial class ToastWindow : Window
{
    static readonly List<ToastWindow> Open = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };

    ToastWindow(AlertInfo alert)
    {
        InitializeComponent();
        TitleText.Text = alert.Title;
        MessageText.Text = alert.Message;
        Bar.Background = Ui.Hex(alert.Color);
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Open.Remove(this);
            Arrange();
        };
    }

    public static void Show(AlertInfo alert)
    {
        var t = new ToastWindow(alert);
        Open.Add(t);
        t.Show();
        Arrange();
        t._timer.Start();
    }

    // Empilha de baixo para cima, colado na área de trabalho (acima da barra de tarefas)
    static void Arrange()
    {
        var area = SystemParameters.WorkArea;
        double bottom = area.Bottom - 6;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            t.UpdateLayout();
            t.Left = area.Right - t.ActualWidth - 6;
            t.Top = bottom - t.ActualHeight;
            bottom = t.Top;
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Body_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { TemplatedParent: System.Windows.Controls.Button }) return;
        var main = Application.Current.MainWindow;
        if (main != null)
        {
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }
        Close();
    }
}
