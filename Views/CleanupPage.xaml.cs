using System.IO;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class CleanupPage : UserControl
{
    readonly List<CleanCategory> _categories = Cleaner.Categories();
    bool _loaded, _busy;

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

    async Task AnalyzeAsync()
    {
        SetBusy(true);
        Status.Text = "Analisando quanto espaço dá para liberar...";
        ResultText.Visibility = Visibility.Collapsed;
        foreach (var c in _categories) c.SizeText = "...";
        foreach (var c in _categories)
            c.Size = await Task.Run(() => Cleaner.Measure(c));
        UpdateTotals();
        Status.Text = "Marque o que quer apagar e clique em Limpar selecionados.";
        SetBusy(false);
    }

    void UpdateTotals()
    {
        long total = _categories.Where(c => c.Selected && c.Size > 0).Sum(c => c.Size);
        TotalText.Text = Fmt.Bytes(total);
        var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        DriveText.Text = $"Unidade {sys.Name.TrimEnd('\\')} · {Fmt.Bytes(sys.AvailableFreeSpace)} livres de {Fmt.Bytes(sys.TotalSize)}";
    }

    void Selection_Click(object sender, RoutedEventArgs e) => UpdateTotals();

    async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _categories.Where(c => c.Selected).ToList();
        if (selected.Count == 0)
        {
            Status.Text = "Marque pelo menos uma categoria.";
            return;
        }
        long planned = selected.Where(c => c.Size > 0).Sum(c => c.Size);
        string names = string.Join("\n", selected.Select(c => $"• {c.Name} ({c.SizeText})"));
        if (MessageBox.Show($"Apagar {Fmt.Bytes(planned)}?\n\n{names}\n\nIsso não pode ser desfeito.", "UtiliTech",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SetBusy(true);
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

        ResultText.Text = $"Liberados {Fmt.Bytes(Math.Max(freed, gained))}" +
                          (skipped > 0 ? $" · {skipped:N0} arquivo(s) em uso ficaram para a próxima" : "");
        ResultText.Visibility = Visibility.Visible;
        UpdateTotals();
        Status.Text = "Limpeza concluída.";
        SetBusy(false);
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        AnalyzeBtn.IsEnabled = CleanBtn.IsEnabled = !busy;
        CategoryList.IsEnabled = !busy;
    }
}
