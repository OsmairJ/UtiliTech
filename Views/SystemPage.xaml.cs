using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class SystemPage : UserControl
{
    List<InfoSection> _sections = new();
    bool _loaded;

    public SystemPage()
    {
        InitializeComponent();
        TecnicoBox.Text = Environment.UserName;
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadAsync();
        };
    }

    async Task LoadAsync()
    {
        Status.Text = "Lendo informações do hardware...";
        _sections = await Task.Run(() => SystemInfo.Collect());
        Sections.ItemsSource = _sections;
        Status.Text = $"Atualizado às {DateTime.Now:HH:mm:ss}";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SystemInfo.ToText(_sections));
            Status.Text = "Resumo copiado para a área de transferência";
        }
        catch (Exception ex) { Ui.Error("Não foi possível copiar: " + ex.Message); }
    }

    async void Export_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("Página HTML (*.html)|*.html", ".html", (path, at, sections, disks, battery, memory) =>
            File.WriteAllText(path, ReportBuilder.Html(at, sections, disks, battery, memory), Encoding.UTF8));

    async void ExportSheet_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("Planilha do Excel (*.xlsx)|*.xlsx|Planilha CSV (*.csv)|*.csv", ".xlsx", SpreadsheetExporter.Save);

    delegate void ReportWriter(string path, Atendimento at, List<InfoSection> sections, List<DiskReport> disks,
        BatteryReport battery, string? memoryTest);

    async Task ExportAsync(string filter, string extension, ReportWriter write)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            FileName = $"Relatorio_{Environment.MachineName}_{DateTime.Now:yyyy-MM-dd}{extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != true) return;

        ExportBtn.IsEnabled = ExportSheetBtn.IsEnabled = false;
        Status.Text = "Gerando relatório (lendo discos e bateria)...";
        try
        {
            var at = new Atendimento(ClienteBox.Text, OsBox.Text, TecnicoBox.Text, ObsBox.Text);
            var sections = _sections;
            var path = dlg.FileName;
            string? history = await Task.Run(() =>
            {
                var disks = DiskHealth.Collect();
                var battery = BatteryInfo.Collect();
                string? tests = AppState.TestsSummary();
                write(path, at, sections, disks, battery, tests);
                // Cópia no histórico de atendimentos (pasta atendimentos no pendrive); se falhar, o arquivo exportado já está salvo
                try { return AttendanceHistory.Save(AttendanceHistory.Build(at, sections, disks, battery, tests, DateTime.Now), path); }
                catch (Exception ex)
                {
                    Logger.Error("Guardar no histórico de atendimentos", ex);
                    return null;
                }
            });
            Status.Text = "Relatório salvo em " + path + (history != null ? " · cópia no histórico de atendimentos" : "");
            try { Ui.Open(path); }
            catch { /* sem programa associado (ex.: PC sem Excel): o arquivo já está salvo */ }
        }
        catch (IOException ex) { Ui.Error("Não foi possível salvar (o arquivo está aberto em outro programa?): " + ex.Message); }
        catch (Exception ex) { Ui.Error("Falha ao gerar o relatório: " + ex.Message); }
        finally { ExportBtn.IsEnabled = ExportSheetBtn.IsEnabled = true; }
    }
}
