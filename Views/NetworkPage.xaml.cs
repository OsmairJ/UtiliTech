using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Ferramenta de rede: teste de conexão com diagnóstico, placas, Wi-Fi, ping, rota e unidades mapeadas.</summary>
public partial class NetworkPage : UserControl, IRefreshable
{
    /// <summary>F5: atualiza a tela.</summary>
    public Task RefreshAsync() => LoadAsync();

    List<AdapterInfo> _adapters = new();
    bool _loaded, _busy;

    /// <summary>Linha da lista de unidades mapeadas, com o resultado do teste de acesso.</summary>
    record DriveRow(string Title, string State, string Color);

    public NetworkPage()
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
        Status.Text = "Lendo as placas de rede...";
        _adapters = await Task.Run(NetworkTools.Adapters);
        var wifi = await Task.Run(NetworkTools.Wifi);
        AdapterList.ItemsSource = _adapters;
        NoAdapters.Visibility = _adapters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        WifiBox.Visibility = wifi != null ? Visibility.Visible : Visibility.Collapsed;
        if (wifi != null)
        {
            string quality = wifi.Signal switch { >= 70 => "bom", >= 40 => "razoável", null => "", _ => "fraco: aproxime do roteador" };
            WifiText.Text = $"Wi-Fi conectado em \"{wifi.Ssid}\"" +
                            (wifi.Signal is { } s ? $" · sinal {s}% ({quality})" : "") +
                            (wifi.Band.Length > 0 ? $" · {wifi.Band}" : "") +
                            (wifi.Channel.Length > 0 ? $" · canal {wifi.Channel}" : "") +
                            (wifi.Rate.Length > 0 ? $" · {wifi.Rate} Mbps" : "");
            WifiText.Foreground = Ui.Hex(wifi.Signal is < 40 ? Palette.Warn : Palette.Text);
        }

        if (HostBox.Text.Length == 0)
            HostBox.Text = _adapters.SelectMany(a => a.Gateways).FirstOrDefault() ?? NetworkTools.DnsTestHost;

        await LoadDrivesAsync();
        Status.Text = $"{_adapters.Count} placa(s) conectada(s) · lido às {DateTime.Now:HH:mm}";
    }

    async Task LoadDrivesAsync()
    {
        var drives = NetworkTools.MappedDrives();
        NoDrives.Visibility = drives.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var free = NetworkTools.FreeLetters(drives);
        LetterBox.ItemsSource = free;
        LetterBox.SelectedItem = free.LastOrDefault(); // do Z para trás, como o Windows sugere
        var rows = drives.Select(d => new DriveRow(d.Title, "verificando...", Palette.Muted)).ToList();
        DriveList.ItemsSource = rows;
        for (int i = 0; i < drives.Count; i++)
        {
            bool ok = await NetworkTools.ReachableAsync(drives[i].RemotePath);
            rows[i] = rows[i] with { State = ok ? "acessível" : "sem acesso", Color = ok ? Palette.Good : Palette.Bad };
            DriveList.ItemsSource = null;
            DriveList.ItemsSource = rows;
        }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        DiagnosisText.Text = "Testando...";
        DiagnosisText.Foreground = Ui.Hex(Palette.Accent);
        try
        {
            _adapters = await Task.Run(NetworkTools.Adapters);
            AdapterList.ItemsSource = _adapters;
            var steps = await NetworkTools.QuickTestAsync(_adapters);
            StepList.ItemsSource = steps;
            DiagnosisText.Text = NetworkTools.Diagnose(steps, _adapters);
            bool allOk = steps.All(s => s.State == NetStepState.Ok);
            DiagnosisText.Foreground = Ui.Hex(allOk ? Palette.Good : steps.Any(s => s.State == NetStepState.Fail && s.Name != "Gateway (roteador)") ? Palette.Bad : Palette.Warn);
            Log("Teste de conexão: " + DiagnosisText.Text);
        }
        catch (Exception ex)
        {
            Logger.Error("Teste de conexão", ex);
            DiagnosisText.Text = "Não foi possível testar: " + ex.Message;
            DiagnosisText.Foreground = Ui.Hex(Palette.Bad);
        }
        finally { SetBusy(false); }
    }

    string? Host()
    {
        string host = HostBox.Text.Trim();
        if (NetworkTools.IsValidHost(host)) return host;
        Ui.Error("Digite um endereço IP ou nome válido (ex.: 192.168.0.1 ou www.exemplo.com.br).");
        return null;
    }

    async void Ping_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Host() is not { } host) return;
        SetBusy(true);
        Log($"=== Ping {host} ===");
        var r = await NetworkTools.PingAsync(host, 4, m => Dispatcher.InvokeAsync(() => Log("  " + m)));
        Log("Resultado: " + r.Summary);
        SetBusy(false);
    }

    async void Trace_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Host() is not { } host) return;
        SetBusy(true);
        Log($"=== Rota até {host} (até 20 saltos; pode levar 1 minuto) ===");
        try { await Task.Run(() => NetworkTools.TraceAsync(host, m => Dispatcher.InvokeAsync(() => Log(m)))); }
        catch (Exception ex) { Log("Falhou: " + ex.Message); }
        SetBusy(false);
    }

    async void Resolve_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Host() is not { } host) return;
        SetBusy(true);
        var (ok, detail) = await NetworkTools.ResolveAsync(host);
        string dns = string.Join(", ", _adapters.SelectMany(a => a.Dns).Distinct());
        Log($"=== Resolver {host} ===");
        Log((ok ? "  " : "  Falhou: ") + detail + (dns.Length > 0 ? $" · DNS usado: {dns}" : ""));
        SetBusy(false);
    }

    async void Map_Click(object sender, RoutedEventArgs e)
    {
        string path = SharePathBox.Text.Trim().TrimEnd('\\');
        if (_busy) return;
        if (LetterBox.SelectedItem is not string letter) { Ui.Error("Escolha a letra da unidade."); return; }
        if (!NetworkTools.IsValidUncPath(path)) { Ui.Error("Use o formato \\\\servidor\\pasta."); return; }
        SetBusy(true);
        Log($"=== Mapear {letter}: → {path} ===");
        int code = await Task.Run(() => NetworkTools.MapDriveAsync(letter, path, m => Dispatcher.InvokeAsync(() => Log(m))));
        Log(code == 0
            ? $"✓ {letter}: mapeada. Aparece no Explorer depois de sair e entrar de novo no Windows."
            : $"Falhou (código {code}): o caminho existe? O usuário tem acesso? Se pedir outra senha, mapeie pelo Explorer.");
        SessionLog.Add("Rede", $"unidade {letter}: mapeada para {path}", code == 0);
        SetBusy(false);
        await LoadDrivesAsync();
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        TestBtn.IsEnabled = PingBtn.IsEnabled = TraceBtn.IsEnabled = ResolveBtn.IsEnabled = MapBtn.IsEnabled = !busy;
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
