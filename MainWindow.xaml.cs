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

        // Sensores em segundo plano: rodapé ao vivo e alertas
        SensorHub.Updated += s => Dispatcher.InvokeAsync(() => UpdateLive(s));
        AlertWatcher.Raised += a => Dispatcher.InvokeAsync(() => ToastWindow.Show(a));
        Closed += (_, _) => SensorHub.Shutdown();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) =>
        {
            await SensorHub.StartAsync();
            if (SensorHub.Last is { } last) UpdateLive(last);
            AlertWatcher.Start();
            await CheckUpdatesAsync();
        };
    }

    // Verificação silenciosa ao abrir: só avisa se houver versão nova
    async Task CheckUpdatesAsync()
    {
        var cfg = AppSettings.Current;
        if (!cfg.CheckUpdatesOnStart || string.IsNullOrWhiteSpace(cfg.UpdateRepo)) return;
        try
        {
            var update = await Updater.CheckAsync(cfg.UpdateRepo);
            if (update == null) return;
            AppState.AvailableUpdate = update;
            ToastWindow.Show(new AlertInfo($"UtiliTech {update.Tag} disponível",
                $"Você está na {Updater.CurrentText}. Abra Configurações para atualizar.", Palette.Accent));
        }
        catch (Exception ex) { Logger.Error("Verificação de atualização ao abrir", ex); }
    }

    void UpdateLive(MonitorSnapshot s)
    {
        SetLive(LiveCpu, s.CpuTemp, s.CpuLoad);
        SetLive(LiveGpu, s.GpuTemp, s.GpuLoad);
    }

    static void SetLive(TextBlock tb, float? temp, float? load)
    {
        tb.Text = temp is { } t ? $"{t:0} °C" : load is { } l ? $"{l:0}%" : "—";
        tb.Foreground = Ui.Hex(temp.HasValue ? HardwareMonitor.TempColor(temp) : Palette.Muted);
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
