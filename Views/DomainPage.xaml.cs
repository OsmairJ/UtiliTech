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
        RefreshCurrent();
        UpdateApplyState();
    }

    void RefreshCurrent()
    {
        _current = DomainTools.Current();
        string domain = DomainTools.DomainName;
        bool inDomain = DomainTools.IsInDomain(_current);
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
        if (MessageBox.Show($"{action}?\n\nConta: {user}\nO PC precisa reiniciar no final.", "UtiliTech",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        ApplyBtn.IsEnabled = CheckBtn.IsEnabled = false;
        ApplyResult.Text = inDomain ? "Renomeando no domínio..." : "Ingressando no domínio...";
        ApplyResult.Foreground = Ui.Hex(Palette.Accent);
        string password = PassBox.Password;
        try
        {
            await Task.Run(() => DomainTools.RenameAndJoin(name, user, password));
            AppSettings.Current.LastDomainUser = UserBox.Text.Trim();
            AppSettings.Current.Save();
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
        if (MessageBox.Show("Reiniciar o computador agora? Salve o que estiver aberto.", "UtiliTech",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            DomainTools.Restart();
    }
}
