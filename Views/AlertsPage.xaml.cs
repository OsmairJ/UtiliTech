using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class AlertsPage : UserControl
{
    static readonly int[] Limits = { 75, 80, 85, 90, 95, 100 };
    bool _loading;

    public AlertsPage()
    {
        InitializeComponent();
        LoadSettings();
        AlertWatcher.Raised += _ => Dispatcher.InvokeAsync(RefreshHistory);
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshHistory(); };
        RefreshHistory();
    }

    void LoadSettings()
    {
        _loading = true;
        var cfg = AppSettings.Current;
        AlertsBox.IsChecked = cfg.AlertsEnabled;
        foreach (var box in new[] { CpuLimitBox, GpuLimitBox })
            foreach (var l in Limits) box.Items.Add($"{l} °C");
        CpuLimitBox.SelectedIndex = Math.Max(0, Array.IndexOf(Limits, cfg.CpuAlertLimit));
        GpuLimitBox.SelectedIndex = Math.Max(0, Array.IndexOf(Limits, cfg.GpuAlertLimit));
        _loading = false;
        UpdateStatus();
    }

    void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = AppSettings.Current;
        cfg.AlertsEnabled = AlertsBox.IsChecked == true;
        cfg.CpuAlertLimit = Limits[Math.Max(0, CpuLimitBox.SelectedIndex)];
        cfg.GpuAlertLimit = Limits[Math.Max(0, GpuLimitBox.SelectedIndex)];
        cfg.Save();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        var cfg = AppSettings.Current;
        Status.Text = cfg.AlertsEnabled
            ? $"Ligados · CPU acima de {cfg.CpuAlertLimit} °C · GPU acima de {cfg.GpuAlertLimit} °C · disco com falha"
            : "Desligados";
    }

    void RefreshHistory()
    {
        var history = AlertWatcher.History;
        HistoryList.ItemsSource = history;
        EmptyText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Test_Click(object sender, RoutedEventArgs e) => AlertWatcher.Test();
}
