using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class DisksPage : UserControl, IRefreshable
{
    /// <summary>F5: atualiza a tela.</summary>
    public Task RefreshAsync() => LoadAsync();

    bool _loaded;

    public DisksPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadAsync();
        };
    }

    async Task LoadAsync()
    {
        Status.Text = "Lendo S.M.A.R.T. dos discos...";
        var (disks, volumes, predicted) = await Task.Run(() =>
            (DiskHealth.Collect(), DiskHealth.Volumes(), DiskHealth.PredictedFailures()));
        DiskList.ItemsSource = disks;
        VolumeList.ItemsSource = volumes;

        PredictText.Text = predicted == 0
            ? "Previsão de falha do driver (SMART): nenhum alerta"
            : $"ATENÇÃO: o driver prevê falha em {predicted} disco(s). Faça backup dos dados imediatamente.";
        PredictText.Foreground = Ui.Hex(predicted == 0 ? Palette.Muted : Palette.Bad);

        Status.Text = disks.Count == 0
            ? "Nenhum disco físico encontrado (o app precisa rodar como administrador)"
            : $"{disks.Count} disco(s) · atualizado às {DateTime.Now:HH:mm:ss}";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
}
