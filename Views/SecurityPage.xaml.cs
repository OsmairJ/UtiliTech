using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Segurança do Windows: antivírus, firewall, BitLocker, Secure Boot, TPM, UAC e atualizações, com as ações básicas.</summary>
public partial class SecurityPage : UserControl, IRefreshable
{
    /// <summary>F5: atualiza a tela.</summary>
    public Task RefreshAsync() => LoadAsync();

    SecurityFacts? _facts;
    bool _busy;

    public SecurityPage()
    {
        InitializeComponent();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible && !_busy) await LoadAsync();
        };
    }

    async Task LoadAsync()
    {
        RefreshBtn.IsEnabled = false;
        Status.Text = "Verificando antivírus, firewall, BitLocker, Secure Boot e TPM...";
        try
        {
            _facts = await Task.Run(SecurityCheck.Gather);
            var items = SecurityCheck.Evaluate(_facts, DateTime.Now);
            ItemList.ItemsSource = items;
            int bad = items.Count(i => i.State == SecState.Bad), warn = items.Count(i => i.State == SecState.Warning);
            Status.Text = (bad == 0 && warn == 0 ? "Tudo em ordem" : string.Join(" · ", new[]
                { bad > 0 ? $"{bad} problema(s)" : "", warn > 0 ? $"{warn} ponto(s) de atenção" : "" }.Where(s => s.Length > 0))) +
                $" · verificado às {DateTime.Now:HH:mm}";
            FirewallBtn.IsEnabled = _facts.Firewall.Any(p => !p.Value);
            KeyBtn.IsEnabled = _facts.BitLocker.Any(v => v.Protection == 1 || v.Conversion != 0);
            SignaturesBtn.IsEnabled = ScanBtn.IsEnabled = _facts.DefenderEnabled == true;
        }
        catch (Exception ex)
        {
            Logger.Error("Segurança do Windows", ex);
            Status.Text = "Não foi possível verificar: " + ex.Message;
        }
        finally { RefreshBtn.IsEnabled = true; }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    void OpenWindowsSecurity_Click(object sender, RoutedEventArgs e)
    {
        try { Ui.Open("windowsdefender:"); }
        catch (Exception ex) { Ui.Error("Não foi possível abrir a Segurança do Windows: " + ex.Message); }
    }

    async Task RunAsync(string title, Func<Action<string>, Task<int>> action, string sessionText)
    {
        if (_busy) return;
        _busy = true;
        SignaturesBtn.IsEnabled = ScanBtn.IsEnabled = FirewallBtn.IsEnabled = false;
        Log($"=== {title} ===");
        try
        {
            int code = await Task.Run(() => action(m => Dispatcher.InvokeAsync(() => Log(m))));
            Log(code == 0 ? "✓ Concluído" : $"Terminou com código {code}: veja as mensagens acima");
            SessionLog.Add("Segurança", sessionText, code == 0);
        }
        catch (Exception ex)
        {
            Logger.Error(title, ex);
            Log("Falhou: " + ex.Message);
        }
        finally { _busy = false; }
        await LoadAsync();
    }

    async void Signatures_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Atualizar assinaturas do Defender", SecurityCheck.UpdateSignaturesAsync, "assinaturas do Defender atualizadas");

    async void QuickScan_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = "Fazer uma verificação rápida?",
                Effects = new[] { "O Defender verifica as áreas onde vírus costumam ficar (memória, inicialização, pastas do sistema)", "Leva de 2 a 15 minutos; o PC continua utilizável" },
                ConfirmText = "Verificar",
            }).Ok) return;
        await RunAsync("Verificação rápida do Defender", SecurityCheck.QuickScanAsync, "verificação rápida do Defender");
    }

    async void Firewall_Click(object sender, RoutedEventArgs e)
    {
        var off = _facts?.Firewall.Where(p => !p.Value).Select(p => p.Key).ToList() ?? new();
        if (off.Count == 0) return;
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = "Ligar o firewall?",
                Effects = off.Select(p => $"Liga o firewall do Windows no perfil {p}").ToList(),
                Warning = "Se a empresa desliga o firewall por política, ele pode ser desligado de novo na próxima atualização de políticas.",
                ConfirmText = "Ligar",
            }).Ok) return;
        await RunAsync("Ligar firewall", log => SecurityCheck.EnableFirewallAsync(off, log), "firewall ligado nos perfis " + string.Join(", ", off));
    }

    async void Key_Click(object sender, RoutedEventArgs e)
    {
        if (_facts == null) return;
        string drive = _facts.SystemDrive;
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = $"Mostrar a chave de recuperação do BitLocker ({drive})?",
                Effects = new[] { "Mostra a chave de 48 dígitos na tela, só aqui (não vai para o registro nem para o relatório)" },
                Warning = "Quem tiver essa chave consegue ler o disco. Guarde em lugar seguro e esconda depois de anotar.",
                ConfirmText = "Mostrar",
            }).Ok) return;
        try
        {
            var keys = await Task.Run(() => SecurityCheck.RecoveryKeys(drive));
            if (keys.Count == 0)
            {
                Log($"{drive}: nenhuma chave de recuperação numérica encontrada (BitLocker desligado ou só com TPM).");
                return;
            }
            KeyTitle.Text = $"Chave de recuperação de {drive} · ID {keys[0].Id.Trim('{', '}')[..8]}" + (keys.Count > 1 ? $" (mais {keys.Count - 1})" : "");
            KeyText.Text = string.Join("\n", keys.Select(k => k.Password));
            KeyBox.Visibility = Visibility.Visible;
            Log($"{drive}: chave de recuperação exibida na tela.");
        }
        catch (Exception ex)
        {
            Logger.Error("Chave do BitLocker", ex);
            Log("Não foi possível ler a chave: " + ex.Message);
        }
    }

    void CopyKey_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(KeyText.Text); Log("Chave copiada para a área de transferência."); }
        catch (Exception ex) { Ui.Error("Não foi possível copiar: " + ex.Message); }
    }

    void HideKey_Click(object sender, RoutedEventArgs e)
    {
        KeyText.Text = "";
        KeyBox.Visibility = Visibility.Collapsed;
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
