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
        NavOverview.IsChecked = true;

        VersionValue.Text = Updater.CurrentText;
        StateChanged += (_, _) => UpdateMaximized();

        // Sensores em segundo plano (alertas) e situação da atualização no rodapé
        AlertWatcher.Raised += a => Dispatcher.InvokeAsync(() => ToastWindow.Show(a));
        Updater.Checked += (update, error) => Dispatcher.InvokeAsync(() => ShowUpdateState(update, error));
        Closed += (_, _) => SensorHub.Shutdown();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) =>
        {
            _ = Task.Run(SessionLog.CaptureBaseline); // "antes" do relatório: o PC como estava ao abrir o app
            SensorHistory.Start();
            await SensorHub.StartAsync();
            AlertWatcher.Start();
            await CheckUpdatesAsync();
        };
    }

    // Verificação silenciosa ao abrir: o rodapé mostra o resultado e um aviso aparece se houver versão nova
    async Task CheckUpdatesAsync()
    {
        var cfg = AppSettings.Current;
        if (string.IsNullOrWhiteSpace(cfg.UpdateRepo)) { SetUpdateText("—", Palette.Muted); return; }
        if (!cfg.CheckUpdatesOnStart) { SetUpdateText("desligada", Palette.Muted); return; }
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
        if (error != null) SetUpdateText("sem conexão", Palette.Muted);
        else if (update != null) SetUpdateText($"{update.Tag} nova", Palette.Warn);
        else SetUpdateText("em dia", Palette.Good);
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

    // ---------- Barra de título ----------

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        bool light = !ThemeManager.IsLight;
        ThemeManager.Apply(light);
        AppSettings.Current.LightTheme = light;
        AppSettings.Current.Save();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Maximizada, a janela sem borda passa um pouco das bordas da tela: compensa com margem
    void UpdateMaximized()
    {
        bool max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxBtn.Content = max ? "" : "";
        MaxBtn.ToolTip = max ? "Restaurar" : "Maximizar";
    }

    // ---------- Navegação ----------

    /// <summary>Abre uma tela pelo nome (Tag do item do menu), abrindo o grupo dela. Usado pela Visão geral.</summary>
    public static void Navigate(string key)
    {
        if (Application.Current.MainWindow is not MainWindow w) return;
        foreach (var (group, items) in new[] { (w.DiagGroup, w.DiagItems), (w.PosGroup, w.PosItems), (w.ManGroup, w.ManItems) })
            foreach (var rb in items.Children.OfType<RadioButton>())
                if ((string)rb.Tag == key)
                {
                    group.IsChecked = true;
                    rb.IsChecked = true;
                    return;
                }
        if (key == "settings") w.NavSettings.IsChecked = true;
        if (key == "overview") w.NavOverview.IsChecked = true;
        if (key == "attendance") w.NavAttendance.IsChecked = true;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string key } nav) return;
        PageTitle.Text = nav.Content as string;
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "overview" => new OverviewPage(),
                "attendance" => new AttendancePage(),
                "system" => new SystemPage(),
                "monitor" => new MonitorPage(),
                "disks" => new DisksPage(),
                "battery" => new BatteryPage(),
                "memory" => new MemoryPage(),
                "stress" => new StressPage(),
                "network" => new NetworkPage(),
                "alerts" => new AlertsPage(),
                "checklist" => new ChecklistPage(),
                "install" => new InstallPage(),
                "startup" => new StartupPage(),
                "domain" => new DomainPage(),
                "repairs" => new RepairsPage(),
                "cleanup" => new CleanupPage(),
                "drivers" => new DriversPage(),
                "printers" => new PrintersPage(),
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
