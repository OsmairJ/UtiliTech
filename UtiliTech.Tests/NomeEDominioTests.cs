namespace UtiliTech.Tests;

/// <summary>Regras do nome do computador e formato do usuário do domínio.</summary>
public class NomeEDominioTests
{
    [Theory]
    [InlineData("PC-01")]
    [InlineData("ESTACAO2024")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJKLMNO")] // 15 caracteres
    public void Nomes_validos(string nome)
    {
        Assert.Null(DomainTools.Validate(nome));
    }

    [Theory]
    [InlineData("", "Digite")]
    [InlineData("   ", "Digite")]
    [InlineData("ABCDEFGHIJKLMNOP", "15 caracteres")]
    [InlineData("PC 01", "sem espaços")]
    [InlineData("ESTAÇÃO", "sem acento")]
    [InlineData("PC_01", "hífen")]
    [InlineData("-PC01", "começar nem terminar")]
    [InlineData("PC01-", "começar nem terminar")]
    [InlineData("12345", "só números")]
    public void Nomes_invalidos(string nome, string trechoDoErro)
    {
        Assert.Contains(trechoDoErro, DomainTools.Validate(nome));
    }

    [Fact]
    public void Usuario_recebe_o_dominio_configurado()
    {
        string anterior = AppSettings.Current.DomainName;
        try
        {
            AppSettings.Current.DomainName = "empresa.local";
            Assert.Equal("tecnico@empresa.local", DomainTools.NormalizeUser(" tecnico "));
            Assert.Equal(@"DOMINIO\tecnico", DomainTools.NormalizeUser(@"DOMINIO\tecnico"));
            Assert.Equal("tecnico@outra.local", DomainTools.NormalizeUser("tecnico@outra.local"));
            Assert.Null(DomainTools.NormalizeUser("  "));
        }
        finally { AppSettings.Current.DomainName = anterior; }
    }
}
