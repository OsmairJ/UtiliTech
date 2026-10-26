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

    // Trecho da resposta do GitHub para a release do microsoft/winget-cli
    const string Release = """
        { "tag_name": "v1.29.380", "assets": [
          { "name": "DesktopAppInstallerPolicies.zip", "size": 41003, "browser_download_url": "https://x/policies.zip" },
          { "name": "DesktopAppInstaller_Dependencies.zip", "size": 97760717, "browser_download_url": "https://x/deps.zip",
            "digest": "sha256:ba875afe9d190f61218985ac0292a99d1db710bf93e13c68944ca9d89f0d82d1" },
          { "name": "e53e159d00e04f729cc2180cffd1c02e_License1.xml", "size": 2686, "browser_download_url": "https://x/lic.xml" },
          { "name": "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle", "size": 217276577, "browser_download_url": "https://x/b.msixbundle",
            "digest": "sha256:65dea9c01ce08ee7b763366b27c0e651f97db857c11ca9b9c301826c10092f2e" }
        ] }
        """;

    [Fact]
    public void Pega_pacote_dependencias_e_licenca_da_release()
    {
        var assets = WingetSetup.PickAssets(Release)!;
        Assert.Equal(new[] { WingetSetup.BundleName, WingetSetup.DependenciesName, "e53e159d00e04f729cc2180cffd1c02e_License1.xml" },
            assets.Select(a => a.Name));
        Assert.Equal("65DEA9C01CE08EE7B763366B27C0E651F97DB857C11CA9B9C301826C10092F2E", assets[0].Sha256, ignoreCase: true);
        Assert.Null(assets[2].Sha256);
        Assert.Equal(217276577 + 97760717 + 2686, assets.Sum(a => a.Size));
    }

    [Fact]
    public void Release_sem_licenca_nao_serve() =>
        Assert.Null(WingetSetup.PickAssets(Release.Replace("_License1.xml", ".txt")));

    [Theory]
    [InlineData(System.Runtime.InteropServices.Architecture.X64, "x64")]
    [InlineData(System.Runtime.InteropServices.Architecture.Arm64, "arm64")]
    [InlineData(System.Runtime.InteropServices.Architecture.X86, "x86")]
    public void Escolhe_a_pasta_de_dependencias_do_processador(System.Runtime.InteropServices.Architecture arch, string pasta) =>
        Assert.Equal(pasta, WingetSetup.ArchFolder(arch));

    [Fact]
    public void Script_de_instalacao_protege_aspas_nos_caminhos()
    {
        var script = WingetSetup.InstallScript(@"D:\Pen d'Ana\winget\pacote.msixbundle",
            new[] { @"C:\t\x64\VCLibs.appx", @"C:\t\x64\Runtime.appx" }, @"D:\lic.xml");
        Assert.Contains(@"$deps = @('C:\t\x64\VCLibs.appx','C:\t\x64\Runtime.appx')", script);
        Assert.Contains(@"-PackagePath 'D:\Pen d''Ana\winget\pacote.msixbundle'", script);
        Assert.Contains("Add-AppxProvisionedPackage -Online", script);
        Assert.Contains("Add-AppxPackage -Path", script);
    }
}
