namespace UtiliTech.Tests;

/// <summary>Leitura da versão das tags e do SHA-256 publicado na release.</summary>
public class AtualizacaoTests
{
    const string Hash = "62350c49c4d8202a7810843aaee8561e6a5061f4935d5f1fff1b386a9bd5acf8";

    [Theory]
    [InlineData("v0.6.4", "0.6.4")]
    [InlineData("V1.2", "1.2.0")]
    [InlineData("0.10.1", "0.10.1")]
    [InlineData(" v2.0.3.7 ", "2.0.3")]
    public void Versao_da_tag(string tag, string esperado)
    {
        Assert.Equal(Version.Parse(esperado), Updater.ParseTag(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    public void Tag_invalida_vira_null(string tag)
    {
        Assert.Null(Updater.ParseTag(tag));
    }

    [Fact]
    public void Comparacao_de_versoes_e_numerica()
    {
        Assert.True(Updater.ParseTag("v0.10.0") > Updater.ParseTag("v0.9.9"));
        Assert.True(Updater.ParseTag("v0.6.4") > Updater.ParseTag("v0.6.3"));
        Assert.Equal(Updater.ParseTag("v0.6.4"), Updater.ParseTag("0.6.4.0"));
    }

    [Fact]
    public void Hash_do_arquivo_sha256()
    {
        Assert.Equal(Hash, Updater.ParseHash($"{Hash}  UtiliTech.exe"));
        Assert.Equal(Hash, Updater.ParseHash(Hash.ToUpperInvariant() + "\r\n")!.ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é hash")]
    [InlineData("62350c49c4d8202a")]
    public void Arquivo_sem_hash_vira_null(string texto)
    {
        Assert.Null(Updater.ParseHash(texto));
    }

    [Fact]
    public void Digest_do_GitHub()
    {
        Assert.Equal(Hash, Updater.ParseDigest("sha256:" + Hash));
        Assert.Null(Updater.ParseDigest("sha512:" + Hash));
        Assert.Null(Updater.ParseDigest(null));
    }
}
