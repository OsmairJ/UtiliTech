using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class RepairsPage : UserControl, INotifyPropertyChanged
{
    const string DefaultStatus = "Correções comuns do dia a dia, com 1 clique. Tudo fica no registro abaixo.";
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

    bool Simulating => SimulationBox.IsChecked == true;

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RepairAction repair }) return;
        bool simulate = Simulating;

        // Na simulação nada muda no PC: não precisa confirmar nem criar ponto de restauração
        if (!simulate && repair.Effects.Length > 0 &&
            !await ConfirmWindow.ConfirmAsync(new ConfirmRequest
            {
                Title = repair.Title,
                Effects = repair.Effects,
                Warning = repair.Warning,
                ConfirmText = "Executar",
                Danger = repair.Risky,
                RestorePoint = repair.Risky ? repair.Title : null,
            }, Log)) return;

        CanRun = false;
        SimulationBox.IsEnabled = false;
        repair.Status = simulate ? "Simulando..." : "Executando...";
        repair.StatusColor = Palette.Accent;
        Status.Text = (simulate ? "Simulando: " : "Executando: ") + repair.Title;
        Log($"=== {repair.Title}{(simulate ? " (simulação)" : "")} ===");
        // A simulação vale só durante este reparo: Repairs.Step também é usado pela tela de drivers
        Repairs.Simulation = simulate;
        try
        {
            var result = await Task.Run(() => repair.Run(msg => Dispatcher.InvokeAsync(() => Log(msg))));
            repair.Status = (simulate ? "○ " : "✓ ") + result;
            repair.StatusColor = simulate ? Palette.Accent : Palette.Good;
            Log("Concluído: " + result);
            if (!simulate) SessionLog.Add("Reparo", $"{repair.Title}: {result}");
        }
        catch (Exception ex)
        {
            repair.Status = "Falhou: " + ex.Message;
            repair.StatusColor = Palette.Bad;
            Log("Falhou: " + ex.Message);
            if (!simulate) SessionLog.Add("Reparo", $"{repair.Title}: {ex.Message}", false);
        }
        finally
        {
            Repairs.Simulation = false;
            CanRun = true;
            SimulationBox.IsEnabled = true;
            Status.Text = DefaultStatus;
        }
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
