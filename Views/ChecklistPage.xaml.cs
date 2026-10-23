using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Checklist de pós-formatação: mostra o que falta antes de entregar o PC, com atalho para cada ferramenta.</summary>
public partial class ChecklistPage : UserControl, IRefreshable
{
    /// <summary>F5: atualiza a tela.</summary>
    public Task RefreshAsync() => CheckAsync();

    bool _busy;

    public ChecklistPage()
    {
        InitializeComponent();
        // Verifica de novo sempre que a tela aparece: o técnico volta para cá depois de cada ferramenta
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible) await CheckAsync();
        };
    }

    async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshBtn.IsEnabled = false;
        Status.Text = "Verificando o PC (drivers, programas, domínio, discos)...";
        try
        {
            var facts = await Task.Run(PostFormatChecklist.Gather);
            var steps = PostFormatChecklist.Evaluate(facts, DateTime.Now);
            StepList.ItemsSource = steps;

            var counted = steps.Where(s => s.State != StepState.NotApplicable).ToList();
            int done = counted.Count(s => s.State == StepState.Done);
            int pending = counted.Count(s => s.State == StepState.Pending);
            int warn = counted.Count(s => s.State == StepState.Warning);
            CountText.Text = $"{done} de {counted.Count}";
            Progress.Maximum = Math.Max(1, counted.Count);
            Progress.Value = done;
            SummaryTitle.Text = pending == 0 && warn == 0 ? "Pronto para entregar" : "Pronto para entregar?";
            SummaryText.Text = pending == 0 && warn == 0
                ? "Tudo feito neste PC."
                : string.Join(" · ", new[] { pending > 0 ? $"{pending} etapa(s) faltando" : "", warn > 0 ? $"{warn} com atenção" : "" }
                    .Where(s => s.Length > 0));
            Status.Text = $"{facts.ComputerName} · verificado às {DateTime.Now:HH:mm}. Tudo é conferido sozinho: volte aqui depois de cada etapa.";
        }
        catch (Exception ex)
        {
            Logger.Error("Checklist de pós-formatação", ex);
            Status.Text = "Não foi possível verificar: " + ex.Message;
        }
        finally
        {
            _busy = false;
            RefreshBtn.IsEnabled = true;
        }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    void Go_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page } && page.Length > 0) MainWindow.Navigate(page);
    }
}
