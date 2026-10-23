using System.IO;

namespace UtiliTech.Tests;

/// <summary>Notas da versão (NOVIDADES.md embutido no .exe).</summary>
public class NovidadesTests
{
    const string Md = """
        # Novidades do UtiliTech

        Texto de introdução.

        ## v0.6.26 — notas da versão
        - Mostra o que mudou
        - Botão em Configurações

        ## v0.6.25 — atalhos
        - Ctrl+1..9

        ## v0.6.23 — busca
        - Ctrl+K
        """;

    static readonly Version V26 = new(0, 6, 26), V25 = new(0, 6, 25), V23 = new(0, 6, 23);

    [Fact]
    public void Le_as_secoes()
    {
        var s = ReleaseNotes.Parse(Md);
        Assert.Equal(new[] { V26, V25, V23 }, s.Select(x => x.Version));
        Assert.Equal("notas da versão", s[0].Title);
        Assert.Equal(new[] { "Mostra o que mudou", "Botão em Configurações" }, s[0].Items);
        Assert.Equal("v0.6.26 · notas da versão", s[0].Header);
    }

    [Fact]
    public void Primeiro_uso_nao_mostra_nada_e_grava_a_versao()
    {
        var (show, save) = ReleaseNotes.OnStartup("", V26, ReleaseNotes.Parse(Md));
        Assert.Empty(show);
        Assert.Equal("0.6.26", save);
    }

    [Fact]
    public void Mesma_versao_nao_mostra()
    {
        Assert.Empty(ReleaseNotes.OnStartup("0.6.26", V26, ReleaseNotes.Parse(Md)).Show);
    }

    [Fact]
    public void Depois_de_atualizar_mostra_as_versoes_puladas()
    {
        var (show, save) = ReleaseNotes.OnStartup("0.6.22", V26, ReleaseNotes.Parse(Md));
        Assert.Equal(new[] { V26, V25, V23 }, show.Select(s => s.Version));
        Assert.Equal("0.6.26", save);
        Assert.Equal(new[] { V26 }, ReleaseNotes.OnStartup("0.6.25", V26, ReleaseNotes.Parse(Md)).Show.Select(s => s.Version));
    }

    [Fact]
    public void Nao_mostra_secao_de_versao_futura()
    {
        // .exe 0.6.25 com NOVIDADES já tendo a 0.6.26 (ex.: compilado no meio do trabalho)
        Assert.Equal(new[] { V25 }, ReleaseNotes.OnStartup("0.6.24", V25, ReleaseNotes.Parse(Md)).Show.Select(s => s.Version));
    }

    [Fact]
    public void Texto_da_release_do_GitHub()
    {
        string t = ReleaseNotes.SectionText(Md, "v0.6.25");
        Assert.StartsWith("## v0.6.25 — atalhos", t);
        Assert.Contains("- Ctrl+1..9", t);
        Assert.DoesNotContain("v0.6.23", t);
        Assert.Equal("", ReleaseNotes.SectionText(Md, "v9.9.9"));
    }

    [Fact]
    public void Arquivo_do_projeto_esta_embutido_e_tem_a_versao_atual()
    {
        var all = ReleaseNotes.Parse(ReleaseNotes.Embedded());
        Assert.NotEmpty(all);
        Assert.Contains(all, s => s.Version == Updater.Current); // toda versão lançada precisa ter a seção
        Assert.All(all, s => Assert.NotEmpty(s.Items));
        Assert.Equal(all.Count, all.Select(s => s.Version).Distinct().Count());
    }
}
