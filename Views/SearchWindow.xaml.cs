using System.Windows;
using System.Windows.Input;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Busca rápida (Ctrl+K): digita, escolhe com as setas e abre a ferramenta com Enter.</summary>
public partial class SearchWindow : Window
{
    static SearchWindow? _open;
    bool _closing;

    SearchWindow()
    {
        InitializeComponent();
        Refresh();
        Loaded += (_, _) => QueryBox.Focus();
        Closed += (_, _) => _open = null;
    }

    /// <summary>Abre centralizada no alto da janela principal (ou só foca, se já estiver aberta).</summary>
    public static void Show(Window owner)
    {
        if (_open != null) { _open.Activate(); _open.QueryBox.Focus(); return; }
        _open = new SearchWindow { Owner = owner };
        _open.Left = owner.Left + (owner.ActualWidth - _open.Width) / 2;
        _open.Top = owner.Top + (owner.WindowState == WindowState.Maximized ? 60 : 70);
        if (owner.WindowState == WindowState.Maximized)
        {
            var area = SystemParameters.WorkArea;
            _open.Left = area.Left + (area.Width - _open.Width) / 2;
        }
        _open.Show();
    }

    void Refresh()
    {
        var results = CommandSearch.Find(QueryBox.Text);
        ResultList.ItemsSource = results;
        ResultList.SelectedIndex = results.Count > 0 ? 0 : -1;
        Hint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Query_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int count = ResultList.Items.Count;
        switch (e.Key)
        {
            case Key.Escape:
                CloseOnce();
                e.Handled = true;
                break;
            case Key.Down when count > 0:
                ResultList.SelectedIndex = (ResultList.SelectedIndex + 1) % count;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                ResultList.SelectedIndex = (ResultList.SelectedIndex - 1 + count) % count;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Open(ResultList.SelectedItem as SearchItem);
                e.Handled = true;
                break;
        }
    }

    void Result_Click(object sender, MouseButtonEventArgs e) => Open(ResultList.SelectedItem as SearchItem);

    void Open(SearchItem? item)
    {
        if (item == null) return;
        CloseOnce();
        MainWindow.Navigate(item.Page);
    }

    // Clicar fora fecha, como as buscas do Windows e do VS Code
    void Window_Deactivated(object? sender, EventArgs e) => CloseOnce();

    void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
