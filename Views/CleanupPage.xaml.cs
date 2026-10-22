using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class CleanupPage : UserControl
{
    readonly List<CleanCategory> _categories = Cleaner.Categories();
    bool _loaded, _busy;
    string? _simulationFile;

    public CleanupPage()
    {
        InitializeComponent();
        CategoryList.ItemsSource = _categories;
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await AnalyzeAsync();
        };
    }

    bool Simulating => SimulationBox.IsChecked == true;

    async Task AnalyzeAsync()
    {
        SetBusy(true);
        Status.Text = "Analisando quanto espaço dá para liberar...";
        ResultText.Visibility = OpenListBtn.Visibility = Visibility.Collapsed;
        foreach (var c in _categories) c.SizeText = "...";
        foreach (var c in _categories)
            c.Size = await Task.Run(() => Cleaner.Measure(c));
        UpdateTotals();
        Status.Text = IdleText();
        SetBusy(false);
    }

    string IdleText() => Simulating
        ? "Modo simulação: marque as categorias e clique em Simular para ver a lista do que seria apagado."
        : "Marque o que quer apagar e clique em Limpar selecionados.";

    void UpdateTotals()
    {
        long total = _categories.Where(c => c.Selected && c.Size > 0).Sum(c => c.Size);
        TotalText.Text = Fmt.Bytes(total);
        var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        DriveText.Text = $"Unidade {sys.Name.TrimEnd('\\')} · {Fmt.Bytes(sys.AvailableFreeSpace)} livres de {Fmt.Bytes(sys.TotalSize)}";
    }

    void Selection_Click(object sender, RoutedEventArgs e) => UpdateTotals();

    void Simulation_Click(object sender, RoutedEventArgs e)
    {
        CleanBtn.Content = Simulating ? "Simular limpeza" : "Limpar selecionados";
        if (!_busy) Status.Text = IdleText();
    }

    async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _categories.Where(c => c.Selected).ToList();
        if (selected.Count == 0)
        {
            Status.Text = "Marque pelo menos uma categoria.";
            return;
        }
        if (Simulating)
        {
            await SimulateAsync(selected);
            return;
        }

        long planned = selected.Where(c => c.Size > 0).Sum(c => c.Size);
        var effects = selected.Select(c => $"{c.Name} ({c.SizeText}) · {c.Description}").ToList();
        if (selected.Any(c => c.StopServices.Length > 0))
            effects.Add("O Windows Update fica parado durante a limpeza do cache e volta sozinho no final");
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = $"Apagar {Fmt.Bytes(planned)}?",
                Effects = effects,
                Warning = "Os arquivos são apagados de vez (não vão para a lixeira) e não dá para desfazer." +
                          (selected.Any(c => c.Kind == CleanKind.RecycleBin) ? " A lixeira está marcada: confirme com o cliente." : ""),
                ConfirmText = "Apagar",
                Danger = true,
            }).Ok) return;

        SetBusy(true);
        ResultText.Visibility = OpenListBtn.Visibility = Visibility.Collapsed;
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        long freeBefore = drive.AvailableFreeSpace;
        long freed = 0;
        int skipped = 0;
        foreach (var c in selected)
        {
            Status.Text = $"Limpando: {c.Name}...";
            c.SizeText = "limpando...";
            var r = await Task.Run(() => Cleaner.Clean(c));
            freed += r.Freed;
            skipped += r.Skipped;
            c.Size = await Task.Run(() => Cleaner.Measure(c));
        }
        long gained = drive.AvailableFreeSpace - freeBefore;
        SessionLog.Add("Limpeza", $"{Fmt.Bytes(Math.Max(freed, gained))} liberados ({string.Join(", ", selected.Select(c => c.Name))})");

        ResultText.Text = $"Liberados {Fmt.Bytes(Math.Max(freed, gained))}" +
                          (skipped > 0 ? $" · {skipped:N0} arquivo(s) em uso ficaram para a próxima" : "");
        ResultText.Foreground = Ui.Hex(Palette.Good);
        ResultText.Visibility = Visibility.Visible;
        UpdateTotals();
        Status.Text = "Limpeza concluída.";
        SetBusy(false);
    }

    /// <summary>Lista o que seria apagado num arquivo de texto (pasta de registros), sem apagar nada.</summary>
    async Task SimulateAsync(List<CleanCategory> selected)
    {
        SetBusy(true);
        ResultText.Visibility = OpenListBtn.Visibility = Visibility.Collapsed;
        var report = new StringBuilder().AppendLine($"UtiliTech {Updater.CurrentText} · simulação da limpeza · {DateTime.Now:dd/MM/yyyy HH:mm}");
        report.AppendLine("Nada foi apagado. Arquivos em uso por algum programa seriam pulados na limpeza de verdade.");
        long total = 0;
        int files = 0;
        foreach (var c in selected)
        {
            Status.Text = $"Simulando: {c.Name}...";
            var list = new List<string>();
            var r = await Task.Run(() => Cleaner.Preview(c, list));
            total += r.Freed;
            if (c.Kind != CleanKind.RecycleBin) files += list.Count;
            report.AppendLine().AppendLine($"=== {c.Name} · {Fmt.Bytes(r.Freed)} · {list.Count:N0} linha(s) ===");
            foreach (var line in list) report.AppendLine(line);
        }

        _simulationFile = Path.Combine(Logger.Folder, $"simulacao-limpeza-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        try { await File.WriteAllTextAsync(_simulationFile, report.ToString(), Encoding.UTF8); }
        catch (Exception ex)
        {
            Logger.Error("Salvar a lista da simulação", ex);
            _simulationFile = null;
        }

        ResultText.Text = $"Simulação: apagaria {files:N0} arquivo(s), cerca de {Fmt.Bytes(total)}. Nada foi apagado.";
        ResultText.Foreground = Ui.Hex(Palette.Accent);
        ResultText.Visibility = Visibility.Visible;
        OpenListBtn.Visibility = _simulationFile != null ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = "Simulação concluída.";
        SetBusy(false);
    }

    void OpenList_Click(object sender, RoutedEventArgs e)
    {
        if (_simulationFile != null && File.Exists(_simulationFile)) Ui.Open(_simulationFile);
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        AnalyzeBtn.IsEnabled = CleanBtn.IsEnabled = SimulationBox.IsEnabled = !busy;
        CategoryList.IsEnabled = !busy;
    }
}
