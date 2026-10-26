using UtiliTech.Services;

namespace UtiliTech.Tests;

/// <summary>Instalar apps: escolha do winget.exe dentro das pastas do Instalador de Aplicativo.</summary>
public class WingetTests
{
    const string Apps = @"C:\Program Files\WindowsApps\";

    [Theory]
    [InlineData("Microsoft.DesktopAppInstaller_1.24.25200.0_x64__8wekyb3d8bbwe", "1.24.25200.0")]
    [InlineData("Microsoft.DesktopAppInstaller_1.9.1792.0_neutral_~_8wekyb3d8bbwe", "1.9.1792.0")]
    [InlineData("pasta-estranha", "0.0")]
    public void Le_a_versao_no_nome_da_pasta(string pasta, string esperado) =>
        Assert.Equal(Version.Parse(esperado), AppInstaller.PackageVersion(pasta));

    [Fact]
    public void Escolhe_a_versao_mais_nova_que_tem_winget()
    {
        var pastas = new[]
        {
            Apps + "Microsoft.DesktopAppInstaller_1.9.1792.0_x64__8wekyb3d8bbwe",
            Apps + "Microsoft.DesktopAppInstaller_1.24.25200.0_x64__8wekyb3d8bbwe",
            Apps + "Microsoft.DesktopAppInstaller_1.25.1.0_neutral_~_8wekyb3d8bbwe", // só recursos, sem winget.exe
        };
        var existe = (string p) => !p.Contains("neutral");

        Assert.Equal(Apps + @"Microsoft.DesktopAppInstaller_1.24.25200.0_x64__8wekyb3d8bbwe\winget.exe",
            AppInstaller.PickWinget(pastas, existe));
    }

    [Fact]
    public void Sem_winget_em_nenhuma_pasta_devolve_nulo() =>
        Assert.Null(AppInstaller.PickWinget(new[] { Apps + "Microsoft.DesktopAppInstaller_1.0.0.0_x64__8wekyb3d8bbwe" }, _ => false));
}
