using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Eventos simplificados: telas azuis, desligamentos inesperados, erros de disco, vídeo e hardware.</summary>
public partial class EventsPage : UserControl, IRefreshable
{
    /// <summary>F5: atualiza a tela.</summary>
    public Task RefreshAsync() => LoadAsync();

    int _days = 30;
    bool _busy;

    public EventsPage()
    {
        InitializeComponent();
        Days30.IsChecked = true; // dispara a primeira leitura
    }

    async void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string t } && int.TryParse(t, out int d)) _days = d;
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        int days = _days;
        Status.Text = $"Lendo o log do sistema ({days} dias)...";
        try
        {
            var (events, summary, dumps) = await Task.Run(() => EventTools.Analyze(days));
            SummaryList.ItemsSource = summary;
            EventList.ItemsSource = events.Take(200).ToList();
            EmptyText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DumpText.Visibility = dumps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DumpText.Text = $"Minidumps no período: {string.Join(", ", dumps.Take(5).Select(f => $"{f.Name} ({f.LastWriteTime:dd/MM HH:mm})"))}" +
                            (dumps.Count > 5 ? $" e mais {dumps.Count - 5}" : "") + " · pasta C:\\Windows\\Minidump";
            int serious = summary.Where(s => s.Kind is EventKind.BlueScreen or EventKind.Disk).Sum(s => s.Count);
            Status.Text = (events.Count == 0 ? "Nada de errado registrado" : serious > 0 ? $"{serious} evento(s) grave(s)" : $"{events.Count} evento(s) para conferir") +
                          $" nos últimos {days} dias · lido às {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            Logger.Error("Eventos", ex);
            Status.Text = "Não foi possível ler o log do sistema: " + ex.Message;
        }
        finally
        {
            _busy = false;
            if (_days != days) await LoadAsync(); // trocou o período durante a leitura
        }
    }

    void Go_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page } && page.Length > 0) MainWindow.Navigate(page);
    }

    void OpenViewer_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true }); }
        catch (Exception ex) { Ui.Error("Não foi possível abrir o Visualizador de Eventos: " + ex.Message); }
    }
}
