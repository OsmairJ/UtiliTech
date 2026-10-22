using System.IO;

namespace UtiliTech.Tests;

/// <summary>Limpeza e modo simulação, sempre numa pasta temporária criada pelo próprio teste.</summary>
public sealed class LimpezaTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}");

    public LimpezaTests()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "sub", "funda"));
        Directory.CreateDirectory(Path.Combine(_pasta, "protegida"));
        File.WriteAllBytes(Path.Combine(_pasta, "a.tmp"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_pasta, "sub", "b.log"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_pasta, "sub", "funda", "c.dat"), new byte[300]);
        File.WriteAllBytes(Path.Combine(_pasta, "protegida", "fica.txt"), new byte[400]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); }
        catch { }
    }

    CleanCategory Categoria(string? filtro = null) => new()
    {
        Name = "Teste",
        Folders = () => new[] { _pasta },
        FilePattern = filtro,
        Exclude = new[] { @"\protegida\" },
    };

    [Fact]
    public void Simulacao_lista_tudo_e_nao_apaga_nada()
    {
        var lista = new List<string>();
        var r = Cleaner.Preview(Categoria(), lista);

        Assert.Equal(600, r.Freed);
        Assert.Equal(3, lista.Count);
        Assert.DoesNotContain(lista, l => l.Contains("fica.txt"));
        Assert.Equal(4, Directory.GetFiles(_pasta, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Simulacao_e_medida_batem()
    {
        var c = Categoria();
        Assert.Equal(Cleaner.Measure(c), Cleaner.Preview(c, new List<string>()).Freed);
    }

    [Fact]
    public void Limpeza_apaga_e_respeita_excecao()
    {
        var r = Cleaner.Clean(Categoria());

        Assert.Equal(600, r.Freed);
        Assert.Equal(0, r.Skipped);
        Assert.True(File.Exists(Path.Combine(_pasta, "protegida", "fica.txt")));
        Assert.False(Directory.Exists(Path.Combine(_pasta, "sub")));   // subpastas vazias somem
        Assert.True(Directory.Exists(_pasta));                          // a pasta em si fica
    }

    [Fact]
    public void Filtro_olha_so_o_primeiro_nivel()
    {
        var lista = new List<string>();
        var r = Cleaner.Preview(Categoria("*.tmp"), lista);
        Assert.Equal(100, r.Freed);
        Assert.Single(lista);
    }

    [Fact]
    public void Arquivo_em_uso_e_pulado()
    {
        using (File.Open(Path.Combine(_pasta, "a.tmp"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var r = Cleaner.Clean(Categoria());
            Assert.Equal(1, r.Skipped);
            Assert.Equal(500, r.Freed);
        }
        Assert.True(File.Exists(Path.Combine(_pasta, "a.tmp")));
    }
}
