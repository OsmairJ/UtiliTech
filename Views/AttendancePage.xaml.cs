using System.IO;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Histórico de atendimentos guardados no pendrive, com busca.</summary>
public partial class AttendancePage : UserControl
{
    List<AttendanceRecord> _all = new();

    public AttendancePage()
    {
        InitializeComponent();
        // Recarrega sempre que a tela aparece: um relatório pode ter sido exportado na tela Sistema
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible) await LoadAsync();
        };
    }

    async Task LoadAsync()
    {
        _all = await Task.Run(AttendanceHistory.LoadAll);
        Apply();
    }

    void Apply()
    {
        var shown = AttendanceHistory.Search(_all, SearchBox.Text);
        List.ItemsSource = shown;
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        int machines = _all.Select(r => r.NumeroSerie.Length > 0 ? r.NumeroSerie : r.Computador).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Status.Text = _all.Count == 0
            ? "Cada relatório ou planilha exportado na tela Sistema fica guardado aqui, na pasta atendimentos do pendrive."
            : $"{_all.Count} atendimento(s) de {machines} computador(es) · inventário em inventario.xlsx" +
              (SearchBox.Text.Trim().Length > 0 ? $" · {shown.Count} encontrado(s)" : "") + $" · {AttendanceHistory.Folder}";

        EmptyBox.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _all.Count == 0
            ? "Nenhum atendimento ainda. Em Diagnóstico → Sistema, preencha cliente e OS e clique em Exportar relatório ou Exportar planilha: uma cópia vem para cá."
            : "Nada encontrado com essa busca.";
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e) => Apply();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AttendanceHistory.Folder);
        Ui.Open(AttendanceHistory.Folder);
    }

    /// <summary>Refaz o inventario.xlsx (ele também é refeito sozinho a cada atendimento guardado) e abre.</summary>
    async void Inventory_Click(object sender, RoutedEventArgs e)
    {
        if (_all.Count == 0)
        {
            Ui.Error("Ainda não há atendimentos no histórico para montar o inventário.");
            return;
        }
        InventoryBtn.IsEnabled = false;
        try
        {
            string? error = await Task.Run(AttendanceHistory.UpdateInventory);
            if (error != null)
            {
                Ui.Error("Não foi possível atualizar o inventário: " + error);
                return;
            }
            try { Ui.Open(AttendanceHistory.InventoryPath); }
            catch { Ui.Open(AttendanceHistory.Folder); } // PC sem Excel: mostra a pasta com a planilha
        }
        finally { InventoryBtn.IsEnabled = true; }
    }

    void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Ui.Error("O arquivo não existe mais: " + path);
            return;
        }
        try { Ui.Open(path); }
        catch (Exception ex) { Ui.Error("Não foi possível abrir: " + ex.Message); }
    }
}
