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
        CheckBtn.IsEnabled = domain.Length > 0;
        var screen = DomainPlan.Describe(_current, domain, $@"{Environment.UserDomainName}\{Environment.UserName}");
        CurrentList.ItemsSource = screen.Items;
        Status.Text = screen.Status;
        if (screen.CredHint.Length > 0) CredHint.Text = screen.CredHint;
        ApplyBtn.Content = screen.ApplyText;
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var (hint, isError) = DomainPlan.NameHint(name);
        NameHint.Text = hint;
        NameHint.Foreground = Ui.Hex(isError ? Palette.Bad : Palette.Muted);
        // Mudou o nome: precisa verificar de novo
        if (!string.Equals(name, _verifiedFree, StringComparison.OrdinalIgnoreCase)) ResultBox.Visibility = Visibility.Collapsed;
        UpdateApplyState();
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim().ToUpperInvariant();
        if (DomainPlan.PreCheck(name, _current, DomainTools.DomainName) is { } pre)
        {
            ShowResult(pre, null);
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
                ShowResult(DomainPlan.Free(name, DomainTools.DomainName), null);
            }
            else
            {
                ShowResult(DomainPlan.InUse(name, DomainTools.DomainName), found.Items);
                MessageBox.Show($"O nome {name} já está em uso no AD {DomainTools.DomainName}.\n\n{found.DistinguishedName}\n\nEscolha outro nome.",
                    "UtiliTech — nome em uso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Consulta de nome no AD", ex);
            ShowResult(DomainPlan.QueryFailed(ex.Message, user != null, _current.PartOfDomain), null);
        }
        finally
        {
            CheckBtn.IsEnabled = true;
            UpdateApplyState();
        }
    }

    void ShowResult(DomainMessage m, List<InfoItem>? details) => ShowResult(m.Color, m.Title, m.Text, details);

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
        var (enabled, hint) = DomainPlan.ApplyState(_current.Name, NameBox.Text, _verifiedFree);
        ApplyBtn.IsEnabled = enabled;
        ApplyHint.Text = hint;
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
        var plan = DomainPlan.Apply(_current, name, DomainTools.DomainName, user);
        if (!await ConfirmWindow.ConfirmAsync(new ConfirmRequest
            {
                Title = plan.Title,
                Effects = plan.Effects,
                Warning = plan.Warning,
                ConfirmText = plan.ConfirmText,
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
            ApplyResult.Text = DomainPlan.Done(name, inDomain, DomainTools.DomainName);
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
