using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class DomainPage : UserControl
{
    MachineDomainInfo _current = DomainTools.Current();
    /// <summary>Último nome confirmado como livre no AD; o botão só libera para este nome exato.</summary>
    string? _verifiedFree;

    public DomainPage()
    {
        InitializeComponent();
        UserBox.Text = AppSettings.Current.LastDomainUser;
        // Primeiro uso: sugere o domínio em que este PC já está
        if (string.IsNullOrWhiteSpace(AppSettings.Current.DomainName) && DomainTools.Current() is { PartOfDomain: true } m)
            SaveDomain(m.Domain);
        DomainBox.Text = AppSettings.Current.DomainName;
        RefreshCurrent();
        UpdateApplyState();
    }

    void DomainBox_LostFocus(object sender, RoutedEventArgs e) => SaveDomain(DomainBox.Text);

    void DomainBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) SaveDomain(DomainBox.Text);
    }

    void SaveDomain(string domain)
    {
        domain = domain.Trim().ToLowerInvariant();
        if (domain == AppSettings.Current.DomainName) return;
        AppSettings.Current.DomainName = domain;
        AppSettings.Current.Save();
        _verifiedFree = null; // outro domínio: o nome precisa ser conferido de novo
        if (IsLoaded) { ResultBox.Visibility = Visibility.Collapsed; RefreshCurrent(); UpdateApplyState(); }
    }

    void RefreshCurrent()
    {
        _current = DomainTools.Current();
        string domain = DomainTools.DomainName;
        bool inDomain = DomainTools.IsInDomain(_current);
        CheckBtn.IsEnabled = domain.Length > 0;
        if (domain.Length == 0)
        {
            CurrentList.ItemsSource = new List<InfoItem>
            {
                new("Nome do computador", _current.Name),
                new(_current.PartOfDomain ? "Domínio" : "Grupo de trabalho", _current.Domain),
            };
            Status.Text = "Informe abaixo o domínio da empresa para conferir nomes e ingressar.";
            ApplyBtn.Content = "Renomear e adicionar ao domínio";
            return;
        }
        CurrentList.ItemsSource = new List<InfoItem>
        {
            new("Nome do computador", _current.Name),
            new(_current.PartOfDomain ? "Domínio" : "Grupo de trabalho", _current.Domain),
            new("Usuário do Windows", $@"{Environment.UserDomainName}\{Environment.UserName}"),
        };
        Status.Text = inDomain
            ? $"Este PC já está no domínio {domain}: dá para renomear (o nome muda também no AD)."
            : $"Este PC não está no {domain}: renomeia e ingressa no domínio com uma única reinicialização.";
        CredHint.Text = inDomain
            ? "Conta com permissão para renomear computadores no AD. Também é usada para consultar o AD (se ficar vazia, a consulta usa a conta do Windows)."
            : "Conta com permissão para ingressar máquinas no domínio. Também é usada para consultar o AD, já que este PC ainda não está no domínio.";
        ApplyBtn.Content = inDomain ? "Renomear no domínio" : $"Renomear e adicionar ao {domain}";
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var error = name.Length == 0 ? null : DomainTools.Validate(name);
        NameHint.Text = error ?? (name.Length > 0 ? $"{name.Length} de 15 caracteres" : "");
        NameHint.Foreground = Ui.Hex(error != null ? Palette.Bad : Palette.Muted);
        // Mudou o nome: precisa verificar de novo
        if (!string.Equals(name, _verifiedFree, StringComparison.OrdinalIgnoreCase)) ResultBox.Visibility = Visibility.Collapsed;
        UpdateApplyState();
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim().ToUpperInvariant();
        var error = DomainTools.Validate(name);
        if (error != null)
        {
            ShowResult(Palette.Bad, error, "", null);
            return;
        }
        if (name.Equals(_current.Name, StringComparison.OrdinalIgnoreCase) && DomainTools.IsInDomain(_current))
        {
            ShowResult(Palette.Warn, "Este já é o nome deste computador.", "Nada a mudar.", null);
            return;
        }

        string? user = DomainTools.NormalizeUser(UserBox.Text);
        string? pass = user == null ? null : PassBox.Password;
        CheckBtn.IsEnabled = false;
        ShowResult(Palette.Accent, $"Consultando o AD {DomainTools.DomainName}...", "", null);
        _verifiedFree = null;
        try
        {
            var found = await Task.Run(() => DomainTools.FindComputer(name, user, pass));
            if (found == null)
            {
                _verifiedFree = name;
                ShowResult(Palette.Good, $"✓ {name} está livre no AD {DomainTools.DomainName}.", "Pode usar este nome.", null);
            }
            else
            {
                ShowResult(Palette.Bad, $"⚠ {name} JÁ ESTÁ EM USO no AD {DomainTools.DomainName}.",
                    "Escolha outro nome. Se for este mesmo computador sendo reformatado, peça a exclusão do objeto antigo no AD antes.",
                    found.Items);
                MessageBox.Show($"O nome {name} já está em uso no AD {DomainTools.DomainName}.\n\n{found.DistinguishedName}\n\nEscolha outro nome.",
                    "UtiliTech — nome em uso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Consulta de nome no AD", ex);
            ShowResult(Palette.Bad, "Não foi possível consultar o AD.",
                ex.Message + (user == null && !_current.PartOfDomain ? " · Este PC não está no domínio: informe usuário e senha no passo 2." : ""), null);
        }
        finally
        {
            CheckBtn.IsEnabled = true;
            UpdateApplyState();
        }
    }

    void ShowResult(string color, string title, string text, List<InfoItem>? details)
    {
        ResultBox.Visibility = Visibility.Visible;
        ResultBox.BorderBrush = Ui.Hex(color);
        ResultTitle.Text = title;
        ResultTitle.Foreground = Ui.Hex(color);
        ResultText.Text = text;
        ResultText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultDetails.ItemsSource = details;
    }

    void UpdateApplyState()
    {
        var name = NameBox.Text.Trim();
        bool verified = name.Length > 0 && string.Equals(name, _verifiedFree, StringComparison.OrdinalIgnoreCase);
        ApplyBtn.IsEnabled = verified;
        ApplyHint.Text = verified
            ? $"Pronto: {_current.Name} → {name}. A mudança vale depois de reiniciar."
            : "Primeiro verifique no AD se o novo nome está livre. O botão só libera para um nome confirmado como livre.";
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim().ToUpperInvariant();
        if (!string.Equals(name, _verifiedFree, StringComparison.OrdinalIgnoreCase)) return;
        string? user = DomainTools.NormalizeUser(UserBox.Text);
        if (user == null || PassBox.Password.Length == 0)
        {
            ApplyResult.Text = "Informe usuário e senha do domínio no passo 2.";
            ApplyResult.Foreground = Ui.Hex(Palette.Warn);
            return;
        }
        bool inDomain = DomainTools.IsInDomain(_current);
        string action = inDomain ? $"Renomear {_current.Name} para {name} no domínio" : $"Renomear {_current.Name} para {name} e adicionar ao domínio {DomainTools.DomainName}";
        var effects = new List<string> { $"O nome do computador muda de {_current.Name} para {name}" };
        if (!inDomain) effects.Add($"O PC entra no domínio {DomainTools.DomainName} (conta criada no AD com a conta {user})");
        else effects.Add($"A conta do computador é renomeada no AD (com a conta {user})");
        effects.Add("O PC precisa reiniciar para concluir");
        if (!await ConfirmWindow.ConfirmAsync(new ConfirmRequest
            {
                Title = action + "?",
                Effects = effects,
                Warning = inDomain ? null : "Depois de reiniciar, o login passa a ser com usuário do domínio. Confira se há uma conta local de administrador com senha conhecida.",
                ConfirmText = inDomain ? "Renomear" : "Renomear e ingressar",
                // Sem ponto de restauração: voltar um ponto depois de ingressar desfaz a senha da conta do
                // computador e quebra a relação de confiança com o domínio
            }, null)) return;

        ApplyBtn.IsEnabled = CheckBtn.IsEnabled = false;
        ApplyResult.Text = inDomain ? "Renomeando no domínio..." : "Ingressando no domínio...";
        ApplyResult.Foreground = Ui.Hex(Palette.Accent);
        string password = PassBox.Password;
        try
        {
            await Task.Run(() => DomainTools.RenameAndJoin(name, user, password));
            AppSettings.Current.LastDomainUser = UserBox.Text.Trim();
            AppSettings.Current.Save();
            SessionLog.Add("Nome e domínio", $"{_current.Name} renomeado para {name}" + (inDomain ? "" : $" e ingressado no domínio {DomainTools.DomainName}"));
            ApplyResult.Text = $"✓ Feito: o computador vai se chamar {name}" + (inDomain ? "" : $" e já está no domínio {DomainTools.DomainName}") +
                               ". Reinicie o PC para concluir.";
            ApplyResult.Foreground = Ui.Hex(Palette.Good);
            RestartBtn.Visibility = Visibility.Visible;
            _verifiedFree = null;
        }
        catch (Exception ex)
        {
            Logger.Error("Renomear/ingressar no domínio", ex);
            ApplyResult.Text = "Não foi possível: " + ex.Message;
            ApplyResult.Foreground = Ui.Hex(Palette.Bad);
            ApplyBtn.IsEnabled = true;
        }
        finally
        {
            PassBox.Clear(); // a senha não fica na tela depois de usada
            CheckBtn.IsEnabled = true;
        }
    }

    void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = "Reiniciar o computador agora?",
                Effects = new[] { "Fecha todos os programas e reinicia o Windows", "O novo nome (e o domínio) passam a valer na volta" },
                Warning = "O que não estiver salvo nos programas abertos será perdido.",
                ConfirmText = "Reiniciar",
                Danger = true,
            }).Ok)
            DomainTools.Restart();
    }
}
