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
            await Task.WhenAll(ScanProblemsAsync(), LoadGpusAsync());
        };
    }

    // ---------- Placa de vídeo ----------

    async Task LoadGpusAsync()
    {
        var gpus = await Task.Run(() => GpuDrivers.Detect());
        var cards = gpus.Select(ToCard).ToList();
        GpuList.ItemsSource = cards;
        foreach (var c in cards.Where(c => c.Info.Vendor == GpuVendor.Nvidia)) _ = CheckNvidiaAsync(c);
    }

    static GpuCard ToCard(GpuInfo g)
    {
        string vendor = g.Vendor switch { GpuVendor.Nvidia => "NVIDIA", GpuVendor.Amd => "AMD", GpuVendor.Intel => "Intel", _ => "Outro fabricante" };
        string driver = g.HasVendorDriver
            ? $"driver {g.VendorVersion}" + (g.DriverDate is { } d ? $" de {d:dd/MM/yyyy}" : "")
            : "SEM DRIVER DO FABRICANTE";
        var card = new GpuCard
        {
            Info = g,
            Detail = $"{vendor} · {driver}",
            SitePage = g.Vendor switch
            {
                GpuVendor.Nvidia => GpuDrivers.NvidiaPage,
                GpuVendor.Amd => GpuDrivers.AmdPage,
                GpuVendor.Intel => GpuDrivers.IntelDsaPage,
                _ => "https://www.catalog.update.microsoft.com",
            },
        };
        switch (g.Vendor)
        {
            case GpuVendor.Nvidia:
                card.Status = "Consultando a versão mais nova na NVIDIA...";
                break;
            case GpuVendor.Amd:
                card.Status = "A AMD não informa a versão mais nova para outros programas. A detecção automática do site reconhece a placa e baixa o driver certo.";
                card.ActionText = "Detectar no site da AMD";
                card.ActionVisible = true;
                break;
            case GpuVendor.Intel:
                card.Status = (g.HasVendorDriver ? "" : "Sem driver: o Windows Update abaixo costuma ter um. ") +
                              "Para a versão mais nova, use o assistente oficial da Intel.";
                card.StatusColor = g.HasVendorDriver ? Palette.Muted : Palette.Warn;
                card.ActionText = "Instalar assistente da Intel";
                card.ActionVisible = true;
                break;
            default:
                card.Status = "Use a busca do Windows Update abaixo.";
                break;
        }
        return card;
    }

    async Task CheckNvidiaAsync(GpuCard card)
    {
        card.Status = "Consultando a versão mais nova na NVIDIA...";
        card.StatusColor = Palette.Muted;
        card.ActionVisible = false;
        try
        {
            var latest = await GpuDrivers.LatestNvidiaAsync(card.Info.Name);
            card.Latest = latest;
            string when = latest.ReleaseDate.Length > 0 ? $", lançado em {latest.ReleaseDate}" : "";
            if (!card.Info.HasVendorDriver || GpuDrivers.IsNewer(latest.Version, card.Info.VendorVersion))
            {
                card.Status = $"Versão nova disponível: {latest.Version}{when} · {Fmt.Bytes(latest.Size)}";
                card.StatusColor = Palette.Accent;
                card.ActionText = $"Baixar e instalar {latest.Version}";
                card.ActionVisible = true;
            }
            else
            {
                card.Status = $"✓ Driver em dia (o mais novo é o {latest.Version}{when})";
                card.StatusColor = Palette.Good;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Consulta do driver NVIDIA", ex);
            card.Status = "Não foi possível consultar a NVIDIA (" + ex.Message + "). A rede pode estar bloqueando o site: use o Site oficial.";
            card.StatusColor = Palette.Warn;
        }
    }

    void GpuSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GpuCard card }) Ui.Open(card.SitePage);
    }

    async void GpuAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GpuCard card }) return;
        switch (card.Info.Vendor)
        {
            case GpuVendor.Amd:
                Ui.Open(GpuDrivers.AmdPage);
                break;
            case GpuVendor.Intel:
                await InstallIntelAssistantAsync(card);
                break;
            case GpuVendor.Nvidia when card.Latest != null:
                await InstallNvidiaAsync(card, card.Latest);
                break;
        }
    }

    async Task InstallIntelAssistantAsync(GpuCard card)
    {
        card.ActionVisible = false;
        card.Status = "Instalando o Intel Driver & Support Assistant (winget)...";
        card.StatusColor = Palette.Accent;
        Log("=== Intel Driver & Support Assistant ===");
        var r = await GpuDrivers.InstallIntelAssistantAsync();
        Log("winget: " + r.Message);
        if (r.Success)
        {
            card.Status = "✓ Assistente instalado. Ele abriu no navegador: siga por lá para atualizar os drivers Intel.";
            card.StatusColor = Palette.Good;
            Ui.Open(GpuDrivers.IntelDsaPage);
        }
        else
        {
            card.Status = "Não foi possível instalar pelo winget (" + r.Message + "). Use o Site oficial.";
            card.StatusColor = Palette.Warn;
            card.ActionVisible = true;
        }
    }

    async Task InstallNvidiaAsync(GpuCard card, NvidiaDriver driver)
    {
        if (!await ConfirmWindow.ConfirmAsync(new ConfirmRequest
            {
                Title = $"Instalar o driver NVIDIA {driver.Version}?",
                Effects = new[]
                {
                    $"Baixa o {driver.Name} {driver.Version} ({Fmt.Bytes(driver.Size)}) do site da NVIDIA",
                    "Confere a assinatura digital do instalador",
                    "Instala em silêncio, por cima do driver atual: a tela vai piscar algumas vezes",
                },
                Warning = "Feche jogos e programas de vídeo antes.",
                ConfirmText = "Baixar e instalar",
                RestorePoint = $"driver NVIDIA {driver.Version}",
                RestorePointType = RestorePoint.DeviceDriverInstall,
            }, Log)) return;

        card.ActionVisible = false;
        card.Busy = true;
        card.Progress = 0;
        card.Status = "Baixando...";
        card.StatusColor = Palette.Accent;
        Log($"=== Driver NVIDIA {driver.Version} ===");
        try
        {
            int code = await GpuDrivers.InstallNvidiaAsync(driver,
                new Progress<double>(p => { card.Progress = p; card.Status = $"Baixando... {p:0}%"; }),
                m => Dispatcher.InvokeAsync(() =>
                {
                    Log(m);
                    if (m.StartsWith("Instalando")) { card.Status = "Instalando... (a tela pode piscar)"; card.Busy = false; }
                }));
            card.Busy = false;
            card.Status = code == 0 ? $"✓ Driver {driver.Version} instalado · reinicie o PC se a imagem ficar estranha" : $"O instalador da NVIDIA terminou com código {code}; veja o registro";
            card.StatusColor = code == 0 ? Palette.Good : Palette.Warn;
            if (code == 0) await LoadGpusAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Instalação do driver NVIDIA", ex);
            card.Busy = false;
            card.Status = "Falhou: " + ex.Message;
            card.StatusColor = Palette.Bad;
            card.ActionVisible = true;
        }
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
        if (!await ConfirmWindow.ConfirmAsync(new ConfirmRequest
            {
                Title = $"Instalar {chosen.Count} driver(s) do Windows Update?",
                Effects = chosen.Select(u => u.Title).Take(8)
                    .Concat(chosen.Count > 8 ? new[] { $"... e mais {chosen.Count - 8}" } : Array.Empty<string>()).ToList(),
                Warning = "A tela ou a rede podem piscar durante a instalação. Alguns drivers pedem reinício no final.",
                ConfirmText = "Baixar e instalar",
                RestorePoint = $"{chosen.Count} driver(s) do Windows Update",
                RestorePointType = RestorePoint.DeviceDriverInstall,
            }, Log)) return;

        foreach (var u in chosen) { u.Status = "Na fila"; u.StatusColor = Palette.Muted; }
        SetWuBusy(true, $"Preparando {chosen.Count} driver(s)...");
        WuBar.IsIndeterminate = false;
        WuBar.Value = 0;
        Log("=== Instalação de drivers do Windows Update ===");
        var byId = chosen.ToDictionary(u => u.UpdateId);
        var progress = new Progress<DriverInstallProgress>(p =>
        {
            WuBar.Value = p.Percent;
            SetWuStatus($"{p.Percent:0}% · {p.Index} de {p.Count} · {p.Phase} {p.Title}", Palette.Accent);
            if (!byId.TryGetValue(p.UpdateId, out var item)) return;
            item.Status = p.Success switch
            {
                true => "✓ Instalado",
                false => "Falhou",
                null => p.Phase + "...",
            };
            item.StatusColor = p.Success switch { true => Palette.Good, false => Palette.Bad, null => Palette.Accent };
            if (p.Success == true) item.Selected = false;
        });
        try
        {
            var result = await DriverManager.InstallWindowsUpdateAsync(chosen.Select(u => u.UpdateId), progress,
                m => Dispatcher.InvokeAsync(() => Log(m)));
            WuBar.Value = 100;
            SetWuStatus($"{result.Installed} instalado(s)" + (result.Failed > 0 ? $", {result.Failed} com falha" : "") +
                        (result.RebootRequired ? " · reinicie o PC para concluir" : ""),
                result.Failed > 0 ? Palette.Warn : Palette.Good);
            await ScanProblemsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Instalação de drivers do Windows Update", ex);
            foreach (var u in chosen.Where(u => !u.Status.StartsWith("✓"))) { u.Status = "Falhou"; u.StatusColor = Palette.Bad; }
            SetWuStatus("A instalação falhou: " + ex.Message, Palette.Bad);
            Log("Falhou: " + ex.Message);
        }
        finally { SetWuBusy(false, null); }
    }

    void SetWuBusy(bool busy, string? message)
    {
        SearchBtn.IsEnabled = InstallBtn.IsEnabled = FolderBtn.IsEnabled = !busy;
        WuBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WuBar.IsIndeterminate = true; // a busca não tem %; a instalação troca para a barra com %
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
        int infCount = Directory.EnumerateFiles(dlg.FolderName, "*.inf", SearchOption.AllDirectories).Count();
        if (!await ConfirmWindow.ConfirmAsync(new ConfirmRequest
            {
                Title = "Instalar drivers da pasta?",
                Effects = new[]
                {
                    $"Adiciona ao Windows os {infCount} pacote(s) de driver (.inf) de {dlg.FolderName} e subpastas",
                    "Os dispositivos compatíveis passam a usar esses drivers (pnputil /add-driver /install)",
                },
                Warning = infCount == 0 ? "Nenhum arquivo .inf nesta pasta: nada será instalado." : null,
                ConfirmText = "Instalar",
                RestorePoint = infCount > 0 ? "drivers de pasta" : null,
                RestorePointType = RestorePoint.DeviceDriverInstall,
            }, Log)) return;

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
