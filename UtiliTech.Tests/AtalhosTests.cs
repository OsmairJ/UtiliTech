using System.Windows.Input;

namespace UtiliTech.Tests;

/// <summary>Atalhos de teclado e ajustes para telas pequenas.</summary>
public class AtalhosTests
{
    [Theory]
    [InlineData(Key.D1, "overview")]
    [InlineData(Key.D4, "network")]
    [InlineData(Key.NumPad6, "repairs")]
    [InlineData(Key.D9, "attendance")]
    [InlineData(Key.OemComma, "settings")]
    public void Ctrl_numero_abre_a_tela(Key tecla, string tela)
    {
        Assert.Equal(tela, Shortcuts.PageFor(tecla, ModifierKeys.Control));
    }

    [Fact]
    public void So_com_Ctrl_sozinho()
    {
        Assert.Null(Shortcuts.PageFor(Key.D1, ModifierKeys.None));          // digitar "1" numa caixa de texto
        Assert.Null(Shortcuts.PageFor(Key.D1, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Null(Shortcuts.PageFor(Key.D0, ModifierKeys.Control));
        Assert.Null(Shortcuts.PageFor(Key.A, ModifierKeys.Control));         // Ctrl+A continua selecionando tudo
        Assert.Null(Shortcuts.PageFor(Key.C, ModifierKeys.Control));
    }

    [Fact]
    public void Lista_de_atalhos_e_tooltip_do_menu()
    {
        Assert.Contains(Shortcuts.All, s => s.Keys == "Ctrl+K");
        Assert.Contains(Shortcuts.All, s => s.Keys == "F5");
        Assert.Contains(Shortcuts.All, s => s.Keys == "F1");
        Assert.Equal("Ctrl+4", Shortcuts.KeysFor("network"));
        Assert.Equal("Ctrl+,", Shortcuts.KeysFor("settings"));
        Assert.Null(Shortcuts.KeysFor("battery"));
        // Toda tela com atalho existe na busca (mesma lista de telas do menu)
        var telas = CommandSearch.Items.Where(i => i.IsPage).Select(i => i.Page).ToHashSet();
        Assert.All(Shortcuts.All.Where(s => s.Page.Length > 0), s => Assert.Contains(s.Page, telas));
        Assert.Equal(Shortcuts.All.Count, Shortcuts.All.Select(s => s.Keys).Distinct().Count());
    }

    [Fact]
    public void Notebook_1366x768_a_125_por_cento_abre_maximizado()
    {
        // 1366×768 a 125% = 1093×614 DIPs; menos a barra de tarefas ≈ 1093×576
        var f = Shortcuts.FitWindow(1093, 576);
        Assert.True(f.Maximize);
        Assert.Equal(576, f.Height);
        Assert.Equal(576, f.MinHeight);  // mínimo nunca maior que a tela
        Assert.Equal(980, f.MinWidth);
        Assert.Equal(1093, f.Width);
    }

    [Fact]
    public void Monitor_grande_abre_no_tamanho_normal()
    {
        var f = Shortcuts.FitWindow(1920, 1040);
        Assert.False(f.Maximize);
        Assert.Equal((1280, 820, 980, 620), (f.Width, f.Height, f.MinWidth, f.MinHeight));
    }

    [Theory]
    [InlineData(1366, 248)]
    [InlineData(1149, 212)]
    [InlineData(980, 212)]
    public void Menu_mais_fino_em_janela_estreita(double largura, double menu)
    {
        Assert.Equal(menu, Shortcuts.SidebarWidth(largura));
    }

    [Theory]
    [InlineData(900, 380, 2, 2)]
    [InlineData(700, 380, 2, 1)]
    [InlineData(800, 190, 4, 4)]
    [InlineData(600, 190, 4, 3)]
    [InlineData(150, 190, 4, 1)]
    [InlineData(0, 190, 4, 4)]   // antes de medir, fica como no layout
    public void Colunas_que_cabem(double largura, double minimo, int maximo, int esperado)
    {
        Assert.Equal(esperado, Shortcuts.ColumnsFor(largura, minimo, maximo));
    }
}
