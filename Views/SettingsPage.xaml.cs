using System.IO;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class SettingsPage : UserControl
{
    UpdateInfo? _update;

    public SettingsPage()
    {
        InitializeComponent();
        var cfg = AppSettings.Current;
        VersionText.Text = $"UtiliTech {Updater.CurrentText}";
        RepoBox.Text = cfg.UpdateRepo;
        AutoCheckBox.IsChecked = cfg.CheckUpdatesOnStart;
        RestorePointBox.IsChecked = cfg.CreateRestorePoints;
        ShortcutList.ItemsSource = Shortcuts.All;
        UpdateThemeButtons();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            UpdateThemeButtons(); // o tema pode ter mudado pelo botão da barra de título
            RefreshLogInfo();
            RefreshAbout();
        };

        // Se a verificação ao abrir o app já achou versão nova, mostra direto
        if (AppState.AvailableUpdate is { } pending) ShowUpdate(pending);
    }

    // ---------- Aparência ----------

    void Dark_Click(object sender, RoutedEventArgs e) => SetTheme(false);
    void Light_Click(object sender, RoutedEventArgs e) => SetTheme(true);

    void SetTheme(bool light)
    {
        ThemeManager.Apply(light);
        AppSettings.Current.LightTheme = light;
        AppSettings.Current.Save();
        UpdateThemeButtons();
    }

    void UpdateThemeButtons()
    {
        if (DarkBtn == null) return;
        DarkBtn.Style = (Style)FindResource(ThemeManager.IsLight ? "BtnGhost" : "Btn");
        LightBtn.Style = (Style)FindResource(ThemeManager.IsLight ? "Btn" : "BtnGhost");
    }

    // ---------- Atualizações ----------

    void Repo_LostFocus(object sender, RoutedEventArgs e) => SaveRepo();

    void SaveRepo()
    {
        AppSettings.Current.UpdateRepo = RepoBox.Text.Trim();
        AppSettings.Current.Save();
    }

    void AutoCheck_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.CheckUpdatesOnStart = AutoCheckBox.IsChecked == true;
        AppSettings.Current.Save();
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        SaveRepo();
        if (string.IsNullOrWhiteSpace(AppSettings.Current.UpdateRepo))
        {
            SetStatus("Informe o repositório do GitHub (usuário/nome).", Palette.Warn);
            return;
        }
        CheckBtn.IsEnabled = false;
        InstallPanel.Visibility = UpdateNotes.Visibility = Visibility.Collapsed;
        SetStatus("Consultando o GitHub...", Palette.Muted);
        try
        {
            var update = await Updater.CheckAsync(AppSettings.Current.UpdateRepo);
            AppState.AvailableUpdate = update;
            if (update == null) SetStatus($"Você já está na versão mais recente ({Updater.CurrentText}).", Palette.Good);
            else ShowUpdate(update);
        }
        catch (Exception ex)
        {
            Logger.Error("Verificação de atualização", ex);
            SetStatus("Não foi possível verificar: " + ex.Message, Palette.Bad);
        }
        finally { CheckBtn.IsEnabled = true; }
    }

    void ShowUpdate(UpdateInfo update)
    {
        _update = update;
        SetStatus($"Versão nova disponível: {update.Tag} ({Fmt.Bytes(update.Size)}) · você está na {Updater.CurrentText}", Palette.Accent);
        if (!string.IsNullOrWhiteSpace(update.Notes))
        {
            UpdateNotes.Text = update.Notes.Length > 600 ? update.Notes[..600] + "…" : update.Notes;
            UpdateNotes.Visibility = Visibility.Visible;
        }
        InstallPanel.Visibility = Visibility.Visible;
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = $"Atualizar para o UtiliTech {_update.Tag}?",
                Effects = new[]
                {
                    $"Baixa o UtiliTech.exe novo do GitHub ({Fmt.Bytes(_update.Size)})",
                    "Confere a impressão digital (SHA-256) do arquivo com a publicada na release",
                    "Troca o .exe e abre o app de novo, já atualizado",
                },
                ConfirmText = "Baixar e instalar",
            }).Ok) return;

        InstallBtn.IsEnabled = CheckBtn.IsEnabled = false;
        DownloadBar.Visibility = Visibility.Visible;
        SetStatus("Baixando...", Palette.Accent);
        try
        {
            // Fecha os sensores (e o driver de passagem) antes: a versão nova vai carregar o dela
            SensorHub.Shutdown();
            await Updater.InstallAsync(_update, new Progress<double>(p =>
            {
                DownloadBar.Value = p;
                UpdateStatus.Text = $"Baixando... {p:0}%";
            }));
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error("Instalação de atualização", ex);
            SetStatus("A atualização falhou: " + ex.Message, Palette.Bad);
            InstallBtn.IsEnabled = CheckBtn.IsEnabled = true;
            DownloadBar.Visibility = Visibility.Collapsed;
        }
    }

    void SetStatus(string text, string color)
    {
        UpdateStatus.Text = text;
        UpdateStatus.Foreground = Ui.Hex(color);
    }

    void RefreshAbout() =>
        AboutText.Text =
            $"UtiliTech {Updater.CurrentText} · ferramentas do técnico\n" +
            "Sensores: LibreHardwareMonitorLib (MPL 2.0) e driver PawnIO 2.2.0 (GPL 2.0, licença em Licencas\\PawnIO).\n" +
            $"Driver de sensores: {SensorDriver.State}\n" +
            $"Configurações: {Path.Combine(AppContext.BaseDirectory, "settings.json")}";

    void Notes_Click(object sender, RoutedEventArgs e) => NotesWindow.ShowRecent(Window.GetWindow(this)!);

    /// <summary>F1: rola até a lista de atalhos.</summary>
    public void ShowShortcuts() => Dispatcher.InvokeAsync(() => ShortcutsCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

    // ---------- Segurança ----------

    void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.CreateRestorePoints = RestorePointBox.IsChecked == true;
        AppSettings.Current.Save();
    }

    // ---------- Registro de erros ----------

    void RefreshLogInfo()
    {
        int errors = Logger.ErrorsToday();
        LogInfo.Text = $"Pasta: {Logger.Folder}\nErros registrados hoje: {errors}";
        if (errors > 0) LogInfo.Foreground = Ui.Hex(Palette.Warn);
        else LogInfo.ClearValue(TextBlock.ForegroundProperty); // herda a cor do tema (muda junto com ele)
    }

    void OpenLogs_Click(object sender, RoutedEventArgs e) => Ui.Open(Logger.Folder);

    void OpenToday_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(Logger.TodayFile)) Logger.Info("Registro aberto pelo usuário");
        Ui.Open(Logger.TodayFile);
    }
}
