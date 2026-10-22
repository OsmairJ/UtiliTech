using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class BatteryPage : UserControl
{
    bool _loaded;

    public BatteryPage()
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
        Status.Text = "Lendo dados da bateria...";
        var r = await Task.Run(() => BatteryInfo.Collect());

        NoBattery.Visibility = r.Present ? Visibility.Collapsed : Visibility.Visible;
        BatteryCard.Visibility = r.Present ? Visibility.Visible : Visibility.Collapsed;
        PowercfgBtn.IsEnabled = r.Present;
        if (r.Present)
        {
            VerdictText.Text = r.Verdict;
            VerdictBadge.Background = Ui.Hex(r.VerdictColor);
            HealthText.Text = r.HealthPercent is { } h ? $"{h:0}%" : "—";
            HealthBar.Value = r.HealthPercent ?? 0;
            HealthBar.Foreground = Ui.Hex(r.VerdictColor);
            BatteryItems.ItemsSource = r.Items;
            ReasonsText.Text = string.Join("\n", r.Reasons.Select(x => "• " + x));
        }
        Status.Text = $"Atualizado às {DateTime.Now:HH:mm:ss}";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    async void Powercfg_Click(object sender, RoutedEventArgs e)
    {
        PowercfgBtn.IsEnabled = false;
        Status.Text = "Gerando relatório do Windows...";
        try
        {
            var path = await BatteryInfo.GeneratePowercfgReportAsync();
            Status.Text = "Relatório aberto no navegador";
            Ui.Open(path);
        }
        catch (Exception ex) { Ui.Error(ex.Message); }
        finally { PowercfgBtn.IsEnabled = true; }
    }
}
