using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Impressoras: lista com ações, instalar por IP e conectar impressora compartilhada.</summary>
public partial class PrintersPage : UserControl
{
    bool _busy;

    public PrintersPage()
    {
        InitializeComponent();
        // Recarrega ao aparecer: um driver pode ter sido instalado em outra tela
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible) await LoadAsync();
        };
    }

    async Task LoadAsync()
    {
        Status.Text = "Lendo as impressoras...";
        var printers = await Task.Run(PrinterTools.List);
        PrinterList.ItemsSource = printers;

        string? chosen = DriverBox.SelectedItem as string;
        // Driver com aspas quebraria o comando de instalação: fica de fora da lista
        var drivers = await Task.Run(() => PrinterTools.SortDrivers(PrinterTools.Drivers().Where(d => !d.Contains('"'))));
        DriverBox.ItemsSource = drivers;
        DriverBox.SelectedItem = chosen != null && drivers.Contains(chosen) ? chosen
            : drivers.FirstOrDefault(d => d.Contains("IPP Class", StringComparison.OrdinalIgnoreCase)) ?? drivers.FirstOrDefault();
        LexmarkList.ItemsSource = PrinterTools.LexmarkPackages.ToList(); // refaz: o arquivo pode ter sido copiado para o pendrive

        int real = printers.Count(p => !p.IsVirtual);
        int ghosts = printers.Count(p => p.IsGhost);
        Status.Text = $"{real} impressora(s) de papel · {printers.Count - real} virtual(is)" +
                      (ghosts > 0 ? $" · {ghosts} cópia(s) que podem ser fila fantasma" : "") + $" · lido às {DateTime.Now:HH:mm}";

        // Testa em segundo plano se o IP/servidor de cada impressora responde
        foreach (var p in printers.Where(p => p.Host.Length > 0))
        {
            p.Reach = "testando conexão...";
            var r = await NetworkTools.PingAsync(p.Host, 2);
            p.Reach = r.Received > 0 ? $"{p.Host} responde ({r.AverageMs} ms)" : $"{p.Host} não responde: impressora desligada, fora da rede ou IP mudou";
            p.ReachColor = r.Received > 0 ? Palette.Good : Palette.Bad;
        }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: string action, DataContext: PrinterInfo p }) return;
        try
        {
            switch (action)
            {
                case "test":
                    string t = await Task.Run(() => PrinterTools.PrintTestPage(p.Name));
                    Log($"{p.Name}: {t}");
                    SessionLog.Add("Impressora", $"página de teste em {p.Name}", t.StartsWith("Página"));
                    break;

                case "default":
                    string d = await Task.Run(() => PrinterTools.SetDefault(p.Name));
                    Log($"{p.Name}: {d}");
                    SessionLog.Add("Impressora", $"{p.Name} definida como padrão", d.StartsWith("Definida"));
                    await LoadAsync();
                    break;

                case "clear":
                    if (p.Jobs == 0) { Log($"{p.Name}: a fila já está vazia"); break; }
                    if (!ConfirmWindow.Ask(new ConfirmRequest
                        {
                            Title = $"Limpar a fila de {p.Name}?",
                            Effects = new[] { $"Cancela os {p.Jobs} documento(s) que estão esperando nesta impressora" },
                            Warning = "Os documentos cancelados precisam ser mandados de novo.",
                            ConfirmText = "Limpar fila",
                            Danger = true,
                        }).Ok) return;
                    int n = await Task.Run(() => PrinterTools.ClearQueue(p.Name));
                    Log($"{p.Name}: {n} documento(s) cancelado(s)");
                    SessionLog.Add("Impressora", $"fila de {p.Name} limpa ({n} documento(s))");
                    await LoadAsync();
                    break;

                case "queue":
                    PrinterTools.OpenQueue(p.Name);
                    break;

                case "remove":
                    if (!ConfirmWindow.Ask(new ConfirmRequest
                        {
                            Title = $"Remover {p.Name}?",
                            Effects = new[]
                            {
                                $"Apaga a impressora \"{p.Name}\" do Windows (para todos os usuários do PC)",
                                p.Jobs > 0 ? $"Os {p.Jobs} documento(s) na fila dela são perdidos" : "O driver continua instalado, para usar de novo",
                            },
                            Warning = p.IsDefault ? "É a impressora padrão: o Windows vai escolher outra." : null,
                            ConfirmText = "Remover",
                            Danger = true,
                        }).Ok) return;
                    await Task.Run(() => PrinterTools.Remove(p.Name));
                    Log($"{p.Name}: removida");
                    SessionLog.Add("Impressora", $"{p.Name} removida");
                    await LoadAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Impressora {p.Name}: {action}", ex);
            Log($"{p.Name}: falhou: {ex.Message}");
        }
    }

    async void Lexmark_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { DataContext: PrinterTools.LexmarkPackage pkg } btn) return;
        SetBusy(true);
        btn.IsEnabled = false;
        var before = PrinterTools.List().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Log($"=== {pkg.Title} (driver {pkg.DriverType}): abrindo o assistente da Lexmark ===");
        Log("Na lista, escolha a impressora pelo IP ou nome de host, confira o driver " + pkg.DriverType + " e clique em Avançar.");
        try
        {
            int code = await PrinterTools.RunLexmarkAsync(pkg);
            var added = (await Task.Run(PrinterTools.List)).Where(p => !before.Contains(p.Name)).Select(p => p.Name).ToList();
            if (added.Count > 0)
            {
                Log($"✓ Instalada(s): {string.Join(", ", added)}");
                SessionLog.Add("Impressora", $"{string.Join(", ", added)} instalada(s) pelo assistente da Lexmark ({pkg.Model}, driver {pkg.DriverType})");
            }
            else Log($"O assistente fechou (código {code}) sem impressora nova.");
        }
        catch (Exception ex)
        {
            Logger.Error("Instalador da Lexmark", ex);
            Log("Falhou: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            btn.IsEnabled = pkg.Available;
        }
        await LoadAsync();
    }

    async void PingIp_Click(object sender, RoutedEventArgs e)
    {
        string ip = IpBox.Text.Trim();
        if (!PrinterTools.IsValidIp(ip)) { Ui.Error("Digite um IP válido (ex.: 192.168.0.50)."); return; }
        SetBusy(true);
        var r = await NetworkTools.PingAsync(ip, 3);
        Log($"Ping {ip}: {r.Summary}");
        SetBusy(false);
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        string ip = IpBox.Text.Trim(), name = NameBox.Text.Trim();
        string? driver = DriverBox.SelectedItem as string;
        if (!PrinterTools.IsValidIp(ip)) { Ui.Error("Digite um IP válido (ex.: 192.168.0.50)."); return; }
        if (!PrinterTools.IsValidPrinterName(name)) { Ui.Error("Digite um nome para a impressora (sem aspas, barras, vírgula, $ ou `)."); return; }
        if (driver == null) { Ui.Error("Escolha o driver."); return; }
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = $"Instalar {name}?",
                Effects = new[]
                {
                    $"Cria a porta TCP/IP IP_{ip} (se ainda não existir)",
                    $"Cria a impressora \"{name}\" com o driver {driver}",
                },
                ConfirmText = "Instalar",
            }).Ok) return;

        SetBusy(true);
        Log($"=== Instalar {name} ({ip}) ===");
        var ping = await NetworkTools.PingAsync(ip, 2);
        if (ping.Received == 0) Log($"Aviso: {ip} não respondeu ao ping (a impressora pode estar desligada); instalando mesmo assim");
        int code = await Task.Run(() => PrinterTools.RunPowerShell(PrinterTools.InstallScript(ip, name, driver), m => Dispatcher.InvokeAsync(() => Log(m))));
        Log(code == 0 ? $"✓ {name} instalada" : $"Falhou (código {code}): veja as mensagens acima");
        SessionLog.Add("Impressora", $"{name} instalada pelo IP {ip} ({driver})", code == 0);
        SetBusy(false);
        await LoadAsync();
    }

    async void Connect_Click(object sender, RoutedEventArgs e)
    {
        string path = ShareBox.Text.Trim();
        if (!PrinterTools.IsValidSharePath(path)) { Ui.Error("Use o formato \\\\servidor\\impressora."); return; }
        SetBusy(true);
        Log($"=== Conectar {path} ===");
        int code = await Task.Run(() => PrinterTools.RunPowerShell(PrinterTools.ConnectScript(path), m => Dispatcher.InvokeAsync(() => Log(m))));
        Log(code == 0 ? $"✓ {path} conectada" : $"Falhou (código {code}): o servidor está acessível? O usuário tem permissão?");
        SessionLog.Add("Impressora", $"{path} conectada", code == 0);
        SetBusy(false);
        await LoadAsync();
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        InstallBtn.IsEnabled = ConnectBtn.IsEnabled = PingBtn.IsEnabled = !busy;
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
