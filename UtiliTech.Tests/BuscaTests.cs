namespace UtiliTech.Tests;

/// <summary>Busca rápida (Ctrl+K).</summary>
public class BuscaTests
{
    static string Primeiro(string busca) => CommandSearch.Find(busca)[0].Title;

    [Theory]
    [InlineData("rede", "Rede")]
    [InlineData("REDE", "Rede")]
    [InlineData("seguranca", "Segurança")]          // sem acento acha com acento
    [InlineData("impressoras", "Impressoras")]
    [InlineData("checklist", "Checklist")]
    [InlineData("velocidade", "Velocidade do disco")]
    public void Acha_a_tela_pelo_nome(string busca, string tela)
    {
        Assert.Equal(tela, Primeiro(busca));
        Assert.True(CommandSearch.Find(busca)[0].IsPage);
    }

    [Theory]
    [InlineData("bsod", "events")]
    [InlineData("tela azul", "events")]
    [InlineData("gpupdate", "repairs")]
    [InlineData("chave bitlocker", "security")]
    [InlineData("spooler", "repairs")]
    [InlineData("lexmark", "printers")]
    [InlineData("crystaldiskmark", "diskspeed")]
    [InlineData("perfil antigo", "profiles")]
    [InlineData("mapear", "network")]
    [InlineData("service tag", "system")]
    [InlineData("renomear", "domain")]
    [InlineData("winget", "install")]
    [InlineData("hora errada", "repairs")]
    public void Acha_pelo_sinonimo(string busca, string pagina)
    {
        var r = CommandSearch.Find(busca);
        Assert.NotEmpty(r);
        Assert.Equal(pagina, r[0].Page);
    }

    [Fact]
    public void Todas_as_palavras_precisam_aparecer()
    {
        Assert.All(CommandSearch.Find("impressora ip"), i => Assert.Equal("printers", i.Page));
        Assert.Empty(CommandSearch.Find("bitlocker impressora"));
        Assert.Empty(CommandSearch.Find("xyzabc"));
    }

    [Fact]
    public void Busca_vazia_mostra_as_telas()
    {
        var r = CommandSearch.Find("   ");
        Assert.NotEmpty(r);
        Assert.All(r, i => Assert.True(i.IsPage));
    }

    [Fact]
    public void Tela_vem_antes_da_acao_no_empate()
    {
        var r = CommandSearch.Find("impressoras");
        Assert.Equal("Impressoras", r[0].Title);
        Assert.Contains(r, i => i.Title == "Instalar impressora pelo IP");
    }

    [Fact]
    public void Toda_entrada_aponta_para_uma_tela_do_menu()
    {
        var telas = CommandSearch.Items.Where(i => i.IsPage).Select(i => i.Page).ToHashSet();
        Assert.Equal(23, telas.Count);
        Assert.All(CommandSearch.Items, i => Assert.Contains(i.Page, telas));
        Assert.Equal(CommandSearch.Items.Count, CommandSearch.Items.Select(i => i.Title).Distinct().Count());
    }

    [Fact]
    public void Texto_da_acao_diz_onde_fica()
    {
        var a = CommandSearch.Find("gpupdate")[0];
        Assert.Equal("Atualizar políticas de grupo (gpupdate)", a.Title);
        Assert.Equal("em Reparos rápidos", a.Subtitle);
        Assert.Equal("Tela", CommandSearch.Find("rede")[0].Subtitle);
    }

    [Fact]
    public void Normaliza_acentos()
    {
        Assert.Equal("seguranca atencao configuracoes", CommandSearch.Normalize("Segurança Atenção Configurações"));
    }
}
