using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class InstallPage : UserControl
{
    readonly ObservableCollection<CatalogApp> _apps = new(AppInstaller.LoadCatalog());
    readonly HashSet<CatalogApp> _installed = new();
    List<string> _installedNames = new();
    bool _loaded;
    bool _installing;

    public InstallPage()
    {
        InitializeComponent();
        AppList.ItemsSource = _apps;
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            _installedNames = await Task.Run(() => AppInstaller.InstalledNames());
            foreach (var app in _apps) MarkIfInstalled(app);
            UpdateStatus();
        };
    }

    void MarkIfInstalled(CatalogApp app)
    {
        if (AppInstaller.IsInstalled(app, _installedNames))
        {
            _installed.Add(app);
            app.Status = "Já instalado";
            app.StatusColor = Palette.Good;
        }
        else if (app.IsLocal && AppInstaller.FindLocalInstaller(app) == null)
        {
            app.Status = "Falta o instalador";
            app.StatusColor = Palette.Warn;
        }
    }

    void OpenInstallers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppInstaller.InstallersFolder);
            Ui.Open(AppInstaller.InstallersFolder);
        }
        catch (Exception ex) { Ui.Error("Não foi possível abrir a pasta: " + ex.Message); }
    }

    void UpdateStatus() =>
        Status.Text = $"{_apps.Count} programa(s) na lista · {_apps.Count(_installed.Contains)} já instalado(s) neste PC";

    void SaveList()
    {
        try { ListPathText.Text = "Lista salva em " + AppInstaller.SaveCatalog(_apps); }
        catch (Exception ex) { Ui.Error(ex.Message); }
    }

    void SelectMissing_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _apps) app.Selected = !_installed.Contains(app);
    }

    void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _apps) app.Selected = false;
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_installing || sender is not Button { DataContext: CatalogApp app }) return;
        _apps.Remove(app);
        _installed.Remove(app);
        SaveList();
        UpdateStatus();
    }

    void ResetList_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        if (MessageBox.Show("Voltar para a lista padrão? Os programas que você adicionou serão retirados.", "UtiliTech",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _apps.Clear();
        _installed.Clear();
        foreach (var app in AppInstaller.DefaultCatalog())
        {
            _apps.Add(app);
            MarkIfInstalled(app);
        }
        SaveList();
        UpdateStatus();
    }

    void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Search_Click(sender, e);
    }

    async void Search_Click(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (query.Length < 2)
        {
            SearchStatus.Text = "Digite pelo menos 2 letras.";
            return;
        }
        SearchBtn.IsEnabled = false;
        SearchStatus.Text = $"Procurando \"{query}\" no winget...";
        ResultList.ItemsSource = null;
        try
        {
            var results = await AppInstaller.SearchAsync(query);
            if (results == null)
                SearchStatus.Text = "winget não encontrado. Instale o \"Instalador de Aplicativo\" pela Microsoft Store.";
            else if (results.Count == 0)
                SearchStatus.Text = "Nada encontrado. Tente outro nome.";
            else
            {
                SearchStatus.Text = $"{results.Count} resultado(s). Clique em Adicionar para colocar na sua lista.";
                ResultList.ItemsSource = results;
            }
        }
        finally { SearchBtn.IsEnabled = true; }
    }

    void AddResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SearchResult r }) return;
        if (_apps.Any(a => a.Id.Equals(r.Id, StringComparison.OrdinalIgnoreCase)))
        {
            SearchStatus.Text = $"{r.Name} já está na lista.";
            return;
        }
        var app = new CatalogApp { Name = r.Name, Id = r.Id, Category = "Adicionado por mim", Match = r.Name, Selected = true };
        _apps.Add(app);
        MarkIfInstalled(app);
        if (_installed.Contains(app)) app.Selected = false;
        SaveList();
        UpdateStatus();
        SearchStatus.Text = $"{r.Name} adicionado à lista.";
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = _apps.Where(a => a.Selected).ToList();
        if (selected.Count == 0)
        {
            Status.Text = "Marque pelo menos um programa";
            return;
        }

        _installing = true;
        InstallBtn.IsEnabled = false;
        int ok = 0;
        for (int i = 0; i < selected.Count; i++)
        {
            var app = selected[i];
            Status.Text = $"Instalando {i + 1} de {selected.Count}: {app.Name}...";
            app.Status = "Instalando...";
            app.StatusColor = Palette.Accent;
            Log($"Instalando {app.Name} ({(app.IsLocal ? app.File : app.Id)})...");

            var r = app.IsLocal ? await AppInstaller.InstallLocalAsync(app) : await AppInstaller.InstallAsync(app.Id);
            app.Status = r.Message;
            app.StatusColor = r.Success ? Palette.Good : Palette.Bad;
            Log($"{app.Name}: {r.Message}");
            if (app.IsLocal && !r.Success && AppInstaller.FindLocalInstaller(app) == null)
                Log($"   Coloque o arquivo \"{app.File}\" em {AppInstaller.InstallersFolder}" +
                    (app.Url != null ? $" (download oficial: {app.Url})" : ""));
            if (r.Success)
            {
                ok++;
                app.Selected = false;
                _installed.Add(app);
            }
            if (r.Message == "winget não encontrado")
            {
                Ui.Error("O winget não foi encontrado. Instale o \"Instalador de Aplicativo\" pela Microsoft Store e tente de novo.");
                break;
            }
        }
        Status.Text = $"Concluído: {ok} de {selected.Count} com sucesso";
        InstallBtn.IsEnabled = true;
        _installing = false;
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
