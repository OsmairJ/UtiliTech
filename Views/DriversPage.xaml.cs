using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class DriversPage : UserControl
{
    List<DriverUpdate> _updates = new();
    bool _loaded;

    public DriversPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await ScanProblemsAsync();
        };
    }

    async Task ScanProblemsAsync()
    {
        Status.Text = "Verificando dispositivos...";
        var problems = await Task.Run(() => DriverManager.ScanProblems());
        ProblemList.ItemsSource = problems;
        int real = problems.Count(p => !p.IsDisabled);
        int disabled = problems.Count - real;
        ProblemsEmpty.Visibility = real == 0 ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = (real == 0 ? "Tudo certo no Gerenciador de Dispositivos" : $"{real} dispositivo(s) com problema") +
                      (disabled > 0 ? $" · {disabled} desativado(s)" : "") + $" · verificado às {DateTime.Now:HH:mm}";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await ScanProblemsAsync();

    async void ScanHardware_Click(object sender, RoutedEventArgs e)
    {
        ScanHwBtn.IsEnabled = false;
        Log("=== Procurar alterações de hardware ===");
        try
        {
            await DriverManager.ScanHardwareAsync(m => Dispatcher.InvokeAsync(() => Log(m)));
            await Task.Delay(2000); // dá tempo do Windows instalar o que encontrou
            await ScanProblemsAsync();
        }
        catch (Exception ex) { Log("Falhou: " + ex.Message); }
        finally { ScanHwBtn.IsEnabled = true; }
    }

    // ---------- Windows Update ----------

    async void Search_Click(object sender, RoutedEventArgs e)
    {
        SetWuBusy(true, "Procurando no Windows Update... (1 a 2 minutos)");
        InstallBtn.Visibility = Visibility.Collapsed;
        UpdateList.ItemsSource = null;
        Log("=== Busca de drivers no Windows Update ===");
        try
        {
            _updates = await DriverManager.SearchWindowsUpdateAsync();
            UpdateList.ItemsSource = _updates;
            if (_updates.Count == 0) SetWuStatus("✓ Nenhum driver novo no Windows Update para este PC.", Palette.Good);
            else
            {
                SetWuStatus($"{_updates.Count} driver(s) disponível(is). Desmarque o que não quiser e clique em Instalar selecionados.", Palette.Accent);
                InstallBtn.Visibility = Visibility.Visible;
            }
            Log($"Encontrados: {_updates.Count}");
            foreach (var u in _updates) Log("  " + u.Title);
        }
        catch (Exception ex)
        {
            Logger.Error("Busca de drivers no Windows Update", ex);
            SetWuStatus("Não foi possível consultar o Windows Update: " + ex.Message + " (sem internet ou bloqueado pela empresa?)", Palette.Bad);
            Log("Falhou: " + ex.Message);
        }
        finally { SetWuBusy(false, null); }
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _updates.Where(u => u.Selected).ToList();
        if (chosen.Count == 0)
        {
            SetWuStatus("Marque pelo menos um driver.", Palette.Warn);
            return;
        }
        if (MessageBox.Show($"Baixar e instalar {chosen.Count} driver(s) do Windows Update?\n\nA tela de algum dispositivo (vídeo, rede) pode piscar durante a instalação.",
                "UtiliTech", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        foreach (var u in chosen) { u.Status = "Na fila..."; u.StatusColor = Palette.Accent; }
        SetWuBusy(true, $"Baixando e instalando {chosen.Count} driver(s)...");
        Log("=== Instalação de drivers do Windows Update ===");
        try
        {
            var result = await DriverManager.InstallWindowsUpdateAsync(chosen.Select(u => u.UpdateId), m => Dispatcher.InvokeAsync(() => Log(m)));
            foreach (var u in chosen)
            {
                bool ok = result.PerUpdate.TryGetValue(u.UpdateId, out var s) && s;
                u.Status = ok ? "✓ Instalado" : "Falhou";
                u.StatusColor = ok ? Palette.Good : Palette.Bad;
                if (ok) u.Selected = false;
                Log($"  {u.Title}: {(ok ? "instalado" : "falhou")}");
            }
            SetWuStatus($"{result.Installed} instalado(s)" + (result.Failed > 0 ? $", {result.Failed} com falha" : "") +
                        (result.RebootRequired ? " · reinicie o PC para concluir" : ""),
                result.Failed > 0 ? Palette.Warn : Palette.Good);
            await ScanProblemsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Instalação de drivers do Windows Update", ex);
            foreach (var u in chosen.Where(u => u.Status == "Na fila...")) { u.Status = "Falhou"; u.StatusColor = Palette.Bad; }
            SetWuStatus("A instalação falhou: " + ex.Message, Palette.Bad);
            Log("Falhou: " + ex.Message);
        }
        finally { SetWuBusy(false, null); }
    }

    void SetWuBusy(bool busy, string? message)
    {
        SearchBtn.IsEnabled = InstallBtn.IsEnabled = FolderBtn.IsEnabled = !busy;
        WuBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) SetWuStatus(message, Palette.Accent);
    }

    void SetWuStatus(string text, string color)
    {
        WuStatus.Text = text;
        WuStatus.Foreground = Ui.Hex(color);
    }

    // ---------- Pasta de drivers ----------

    async void Folder_Click(object sender, RoutedEventArgs e)
    {
        var suggested = Path.Combine(AppInstaller.InstallersFolder, "Drivers");
        var dlg = new OpenFolderDialog
        {
            Title = "Escolha a pasta com os drivers (arquivos .inf)",
            InitialDirectory = Directory.Exists(suggested) ? suggested : AppContext.BaseDirectory,
        };
        if (dlg.ShowDialog() != true) return;

        FolderBtn.IsEnabled = SearchBtn.IsEnabled = false;
        FolderStatus.Text = "Instalando drivers da pasta...";
        FolderStatus.Foreground = Ui.Hex(Palette.Accent);
        Log($"=== Instalar drivers de {dlg.FolderName} ===");
        try
        {
            var msg = await DriverManager.InstallFromFolderAsync(dlg.FolderName, m => Dispatcher.InvokeAsync(() => Log(m)));
            FolderStatus.Text = msg;
            FolderStatus.Foreground = Ui.Hex(msg.StartsWith("O pnputil") ? Palette.Warn : Palette.Good);
            await ScanProblemsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Instalar drivers de pasta", ex);
            FolderStatus.Text = "Falhou: " + ex.Message;
            FolderStatus.Foreground = Ui.Hex(Palette.Bad);
        }
        finally { FolderBtn.IsEnabled = SearchBtn.IsEnabled = true; }
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
