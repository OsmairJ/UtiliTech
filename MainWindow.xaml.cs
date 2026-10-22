using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;
using UtiliTech.Views;

namespace UtiliTech;

public partial class MainWindow : Window
{
    // Cada página é criada na primeira visita e reaproveitada depois (mantém o estado, ex.: teste de RAM rodando)
    readonly Dictionary<string, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        MachineText.Text = Environment.MachineName;
        AdminText.Text = IsAdmin() ? "Executando como administrador" : "Sem privilégio de administrador";
        NavSystem.IsChecked = true;

        VersionValue.Text = Updater.CurrentText;

        // Sensores em segundo plano (alertas) e situação da atualização no rodapé
        AlertWatcher.Raised += a => Dispatcher.InvokeAsync(() => ToastWindow.Show(a));
        Updater.Checked += (update, error) => Dispatcher.InvokeAsync(() => ShowUpdateState(update, error));
        Closed += (_, _) => SensorHub.Shutdown();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) =>
        {
            await SensorHub.StartAsync();
            AlertWatcher.Start();
            await CheckUpdatesAsync();
        };
    }

    // Verificação silenciosa ao abrir: o rodapé mostra o resultado e um aviso aparece se houver versão nova
    async Task CheckUpdatesAsync()
    {
        var cfg = AppSettings.Current;
        if (string.IsNullOrWhiteSpace(cfg.UpdateRepo)) { SetUpdateText("Não configurada", Palette.Muted); return; }
        if (!cfg.CheckUpdatesOnStart) { SetUpdateText("Verificação desligada", Palette.Muted); return; }
        try
        {
            var update = await Updater.CheckAsync(cfg.UpdateRepo);
            if (update == null) return;
            ToastWindow.Show(new AlertInfo($"UtiliTech {update.Tag} disponível",
                $"Você está na {Updater.CurrentText}. Clique na versão, no canto do menu, para atualizar.", Palette.Accent));
        }
        catch (Exception ex) { Logger.Error("Verificação de atualização ao abrir", ex); }
    }

    void ShowUpdateState(UpdateInfo? update, string? error)
    {
        AppState.AvailableUpdate = update;
        if (error != null) SetUpdateText("Sem conexão", Palette.Muted);
        else if (update != null) SetUpdateText($"{update.Tag} disponível", Palette.Accent);
        else SetUpdateText("Em dia", Palette.Good);
        VersionCard.Cursor = update != null ? System.Windows.Input.Cursors.Hand : null;
        VersionCard.ToolTip = update != null ? "Clique para atualizar em Configurações" : null;
    }

    void SetUpdateText(string text, string color)
    {
        UpdateValue.Text = text;
        UpdateValue.Foreground = Ui.Hex(color);
    }

    void VersionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AppState.AvailableUpdate != null) NavSettings.IsChecked = true;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string key }) return;
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "system" => new SystemPage(),
                "monitor" => new MonitorPage(),
                "disks" => new DisksPage(),
                "battery" => new BatteryPage(),
                "memory" => new MemoryPage(),
                "stress" => new StressPage(),
                "alerts" => new AlertsPage(),
                "install" => new InstallPage(),
                "startup" => new StartupPage(),
                "repairs" => new RepairsPage(),
                "cleanup" => new CleanupPage(),
                "settings" => new SettingsPage(),
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
            };
            _pages[key] = page;
        }
        Host.Content = page;

        // Marca o grupo da página aberta (barrinha azul), útil quando o grupo está fechado
        DiagGroup.Tag = DiagItems.Children.Contains((RadioButton)sender) ? "active" : null;
        PosGroup.Tag = PosItems.Children.Contains((RadioButton)sender) ? "active" : null;
        ManGroup.Tag = ManItems.Children.Contains((RadioButton)sender) ? "active" : null;
    }

    static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
