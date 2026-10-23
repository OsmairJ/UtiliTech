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

        // Tela pequena (notebook 1366×768, escala 125%): a janela cabe na área útil e abre maximizada
        var area = SystemParameters.WorkArea;
        var fit = Shortcuts.FitWindow(area.Width, area.Height, Width, Height, MinWidth, MinHeight);
        (Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);
        if (fit.Maximize) { WindowState = WindowState.Maximized; UpdateMaximized(); }
        SizeChanged += (_, e) => SideColumn.Width = new GridLength(Shortcuts.SidebarWidth(e.NewSize.Width));

        // Atalhos: Ctrl+K busca, Ctrl+1..9 e Ctrl+, telas, F5 atualiza, F1 lista de atalhos
        // (Preview: funcionam mesmo com o cursor numa caixa de texto)
        PreviewKeyDown += OnShortcut;
        foreach (var rb in new[] { DiagItems, PosItems, ManItems }.SelectMany(p => p.Children.OfType<RadioButton>())
                     .Concat(new[] { NavOverview, NavAttendance, NavSettings }))
            if (rb.Tag is string key && Shortcuts.KeysFor(key) is { } keys) rb.ToolTip = keys;

        // Sensores em segundo plano (alertas) e situação da atualização no rodapé
        AlertWatcher.Raised += a => Dispatcher.InvokeAsync(() => ToastWindow.Show(a));
        Updater.Checked += (update, error) => Dispatcher.InvokeAsync(() => ShowUpdateState(update, error));
        Closed += (_, _) => SensorHub.Shutdown();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) =>
        {
            _ = Task.Run(SessionLog.CaptureBaseline); // "antes" do relatório: o PC como estava ao abrir o app
            // Novidades da versão, se o app acabou de ser atualizado (depois de a janela aparecer)
            _ = Dispatcher.InvokeAsync(() => NotesWindow.ShowIfUpdated(this), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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

    void Search_Click(object sender, RoutedEventArgs e) => SearchWindow.Show(this);

    async void OnShortcut(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var mods = System.Windows.Input.Keyboard.Modifiers;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.K && mods == System.Windows.Input.ModifierKeys.Control)
        {
            SearchWindow.Show(this);
            e.Handled = true;
        }
        else if (Shortcuts.PageFor(key, mods) is { } page)
        {
            Navigate(page);
            e.Handled = true;
        }
        else if (key == System.Windows.Input.Key.F1 && mods == System.Windows.Input.ModifierKeys.None)
        {
            Navigate("settings");
            if (_pages.TryGetValue("settings", out var s) && s is SettingsPage settings) settings.ShowShortcuts();
            e.Handled = true;
        }
        else if (key == System.Windows.Input.Key.F5 && mods == System.Windows.Input.ModifierKeys.None && Host.Content is IRefreshable r)
        {
            e.Handled = true;
            try { await r.RefreshAsync(); }
            catch (Exception ex) { Logger.Error("F5", ex); }
        }
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
                "diskspeed" => new DiskSpeedPage(),
                "battery" => new BatteryPage(),
                "memory" => new MemoryPage(),
                "stress" => new StressPage(),
                "network" => new NetworkPage(),
                "security" => new SecurityPage(),
                "events" => new EventsPage(),
                "alerts" => new AlertsPage(),
                "checklist" => new ChecklistPage(),
                "install" => new InstallPage(),
                "startup" => new StartupPage(),
                "domain" => new DomainPage(),
                "repairs" => new RepairsPage(),
                "cleanup" => new CleanupPage(),
                "profiles" => new ProfilesPage(),
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
