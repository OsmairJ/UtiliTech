using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class RepairsPage : UserControl, INotifyPropertyChanged
{
    bool _canRun = true;

    /// <summary>Um reparo por vez: os botões ficam desligados enquanto um roda.</summary>
    public bool CanRun
    {
        get => _canRun;
        set { _canRun = value; Changed(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public RepairsPage()
    {
        InitializeComponent();
        DataContext = this;
        RepairList.ItemsSource = Repairs.All();
    }

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RepairAction repair }) return;
        if (repair.Confirm != null &&
            MessageBox.Show(repair.Confirm, "UtiliTech", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        CanRun = false;
        repair.Status = "Executando...";
        repair.StatusColor = Palette.Accent;
        Status.Text = $"Executando: {repair.Title}";
        Log($"=== {repair.Title} ===");
        try
        {
            var result = await Task.Run(() => repair.Run(msg => Dispatcher.InvokeAsync(() => Log(msg))));
            repair.Status = "✓ " + result;
            repair.StatusColor = Palette.Good;
            Log("Concluído: " + result);
        }
        catch (Exception ex)
        {
            repair.Status = "Falhou: " + ex.Message;
            repair.StatusColor = Palette.Bad;
            Log("Falhou: " + ex.Message);
        }
        finally
        {
            CanRun = true;
            Status.Text = "Correções comuns do dia a dia, com 1 clique. Tudo fica no registro abaixo.";
        }
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
