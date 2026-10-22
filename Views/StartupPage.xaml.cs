using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class StartupPage : UserControl
{
    List<StartupEntry> _entries = new();
    bool _loaded;

    public StartupPage()
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
        _entries = await Task.Run(() => StartupManager.List());
        EntryList.ItemsSource = _entries;
        UpdateStatus();
    }

    void UpdateStatus() =>
        Status.Text = $"{_entries.Count} programa(s) · {_entries.Count(e => e.Enabled)} ativado(s)";

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupEntry entry }) return;
        try
        {
            StartupManager.SetEnabled(entry, entry.Enabled);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            entry.Enabled = !entry.Enabled;
            Ui.Error($"Não foi possível alterar \"{entry.Name}\": {ex.Message}");
        }
    }

    void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: StartupEntry entry }) return;
        var exe = StartupManager.ExecutablePath(entry.Command);
        if (exe == null)
        {
            Ui.Error("O arquivo deste programa não foi encontrado (pode ter sido desinstalado).");
            return;
        }
        Process.Start("explorer.exe", $"/select,\"{exe}\"");
    }
}
