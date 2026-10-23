using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Usuários e perfis: lista com tamanho e último uso, apagar perfil antigo, copiar dados entre perfis e contas locais.</summary>
public partial class ProfilesPage : UserControl, IRefreshable
{
    /// <summary>F5: atualiza a tela.</summary>
    public Task RefreshAsync() => LoadAsync();

    List<UserProfileInfo> _profiles = new();
    bool _busy, _loaded;

    public ProfilesPage()
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
        RefreshBtn.IsEnabled = false;
        Status.Text = "Lendo os perfis...";
        var now = DateTime.Now;
        _profiles = await Task.Run(ProfileTools.Profiles);
        foreach (var p in _profiles) ProfileTools.Describe(p, now);
        ProfileList.ItemsSource = _profiles;
        FromBox.ItemsSource = _profiles;
        ToBox.ItemsSource = _profiles;
        FromBox.SelectedItem = _profiles.FirstOrDefault(p => !p.IsCurrentUser && !p.Loaded) ?? _profiles.FirstOrDefault();
        ToBox.SelectedItem = _profiles.FirstOrDefault(p => p.IsCurrentUser) ?? _profiles.LastOrDefault();
        AccountList.ItemsSource = await Task.Run(ProfileTools.LocalAccounts);

        int old = _profiles.Count(p => !p.Loaded && (p.Orphan || p.DaysUnused(now) >= ProfileTools.OldDays));
        Status.Text = $"{_profiles.Count} perfil(is)" + (old > 0 ? $" · {old} antigo(s) ou de conta apagada" : "") + " · calculando tamanhos...";
        RefreshBtn.IsEnabled = true;

        // Tamanho de cada pasta em segundo plano (perfil grande demora)
        long total = 0;
        foreach (var p in _profiles)
        {
            p.Size = await Task.Run(() => ProfileTools.FolderSize(p.Path));
            p.SizeText = Fmt.Bytes(p.Size);
            total += p.Size;
        }
        Status.Text = $"{_profiles.Count} perfil(is) somando {Fmt.Bytes(total)}" + (old > 0 ? $" · {old} antigo(s) ou de conta apagada" : "") +
                      $" · lido às {DateTime.Now:HH:mm}";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { DataContext: UserProfileInfo p }) return;
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = $"Apagar o perfil de {p.Name}?",
                Effects = new[]
                {
                    $"Apaga a pasta {p.Path}" + (p.Size > 0 ? $" ({Fmt.Bytes(p.Size)})" : "") + " com tudo dentro: Área de Trabalho, Documentos, configurações",
                    "Tira o perfil do registro do Windows",
                    p.Orphan ? "A conta dele já não existe" : "Se a pessoa entrar de novo, o Windows cria um perfil novo e vazio",
                },
                Warning = "Não dá para desfazer. Copie antes o que a pessoa precisar (quadro Copiar dados abaixo).",
                ConfirmText = "Apagar perfil",
                Danger = true,
            }).Ok) return;

        _busy = true;
        Log($"=== Apagar perfil {p.Name} ({p.Path}) ===");
        try
        {
            await Task.Run(() => ProfileTools.Delete(p));
            Log($"✓ Perfil apagado" + (p.Size > 0 ? $": {Fmt.Bytes(p.Size)} liberados" : ""));
            SessionLog.Add("Perfis", $"perfil {p.Name} apagado" + (p.Size > 0 ? $" ({Fmt.Bytes(p.Size)})" : ""));
        }
        catch (Exception ex)
        {
            Logger.Error("Apagar perfil", ex);
            Log("Falhou: " + ex.Message + " (algum programa pode estar usando arquivos dele; reinicie e tente de novo)");
            SessionLog.Add("Perfis", $"perfil {p.Name}: {ex.Message}", false);
        }
        finally { _busy = false; }
        await LoadAsync();
    }

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (FromBox.SelectedItem is not UserProfileInfo from || ToBox.SelectedItem is not UserProfileInfo to) { Ui.Error("Escolha a origem e o destino."); return; }
        if (from.Sid == to.Sid) { Ui.Error("A origem e o destino são o mesmo perfil."); return; }
        var plan = ProfileTools.CopyPlan(from.Path, to.Path);
        if (plan.Count == 0) { Ui.Error($"Não há pastas para copiar em {from.Path}."); return; }

        var effects = plan.Select(x => $"{x.Label}: {x.Source} → {x.Target}").ToList();
        effects.Add("Favoritos do Chrome e do Edge, se o destino ainda não tiver");
        if (!ConfirmWindow.Ask(new ConfirmRequest
            {
                Title = $"Copiar de {from.Name} para {to.Name}?",
                Effects = effects,
                Warning = (to.Loaded && !to.IsCurrentUser ? $"{to.Name} está logado agora: feche os programas dele antes. " : "") +
                          "Nada é apagado no destino; arquivos com o mesmo nome são atualizados.",
                ConfirmText = "Copiar",
            }).Ok) return;

        _busy = true;
        CopyBtn.IsEnabled = false;
        Log($"=== Copiar dados de {from.Name} para {to.Name} ===");
        try
        {
            var (ok, failed) = await Task.Run(() => ProfileTools.CopyAsync(from.Path, to.Path, m => Dispatcher.InvokeAsync(() => Log(m))));
            Log(failed == 0 ? $"✓ Copiado ({ok} item(ns))" : $"Copiado com {failed} item(ns) com falha: veja acima");
            SessionLog.Add("Perfis", $"dados de {from.Name} copiados para {to.Name}", failed == 0);
        }
        catch (Exception ex)
        {
            Logger.Error("Copiar perfil", ex);
            Log("Falhou: " + ex.Message);
        }
        finally
        {
            _busy = false;
            CopyBtn.IsEnabled = true;
        }
        await LoadAsync();
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
