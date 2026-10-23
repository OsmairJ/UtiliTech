using System.IO;

namespace UtiliTech.Tests;

/// <summary>Perfis de usuário: classificação, plano de cópia (em pastas temporárias) e robocopy.</summary>
public sealed class PerfisTests : IDisposable
{
    static readonly DateTime Agora = new(2026, 10, 23, 10, 0, 0);
    readonly string _pasta = Path.Combine(Path.GetTempPath(), $"utilitech-teste-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); }
        catch { }
    }

    string Criar(params string[] arquivos)
    {
        foreach (var a in arquivos)
        {
            string p = Path.Combine(_pasta, a);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "x");
        }
        return _pasta;
    }

    [Theory]
    [InlineData("S-1-5-18", true)]
    [InlineData("S-1-5-19", true)]
    [InlineData("S-1-5-20", true)]
    [InlineData("S-1-5-80-123", true)]
    [InlineData("S-1-5-21-612967213-3348040195-189864473-1001", false)]
    public void Contas_do_sistema_ficam_de_fora(string sid, bool sistema)
    {
        Assert.Equal(sistema, ProfileTools.IsSystemSid(sid));
    }

    static UserProfileInfo Perfil(int? diasParado, bool emUso = false, bool eu = false, string conta = @"EMPRESA\fulano") => new()
    {
        Account = conta, Path = @"C:\Users\fulano", Loaded = emUso, IsCurrentUser = eu,
        LastUse = diasParado is { } d ? Agora.AddDays(-d) : null,
    };

    [Fact]
    public void Perfil_parado_ha_mais_de_90_dias()
    {
        var p = Perfil(120);
        ProfileTools.Describe(p, Agora);
        Assert.Equal("PARADO HÁ 120 DIAS", p.Flag);
        Assert.Equal(Palette.Warn, p.FlagColor);
        Assert.Contains("último uso 25/06/2026 (há 120 dias)", p.Detail);
        Assert.True(p.CanDelete);
    }

    [Fact]
    public void Perfil_recente_nao_tem_marca()
    {
        var p = Perfil(10);
        ProfileTools.Describe(p, Agora);
        Assert.Equal("", p.Flag);
        var ontem = Perfil(1);
        ProfileTools.Describe(ontem, Agora);
        Assert.EndsWith("(ontem)", ontem.Detail);
    }

    [Fact]
    public void Perfil_em_uso_e_o_seu_nao_podem_ser_apagados()
    {
        var emUso = Perfil(0, emUso: true);
        ProfileTools.Describe(emUso, Agora);
        Assert.Equal("EM USO", emUso.Flag);
        Assert.False(emUso.CanDelete);

        var eu = Perfil(0, emUso: true, eu: true);
        ProfileTools.Describe(eu, Agora);
        Assert.Equal("VOCÊ", eu.Flag);
        Assert.False(eu.CanDelete);
        Assert.Throws<InvalidOperationException>(() => ProfileTools.Delete(eu));
    }

    [Fact]
    public void Perfil_de_conta_apagada()
    {
        var p = Perfil(null, conta: "");
        ProfileTools.Describe(p, Agora);
        Assert.True(p.Orphan);
        Assert.Equal("fulano (conta não existe mais)", p.Name);
        Assert.Equal("CONTA APAGADA", p.Flag);
        Assert.Contains("último uso desconhecido", p.Detail);
    }

    [Fact]
    public void Data_do_registro_ProfileList()
    {
        var d = new DateTime(2026, 6, 1, 8, 30, 0);
        long ft = d.ToFileTime();
        Assert.Equal(d, ProfileTools.FileTime((int)(ft >> 32), unchecked((int)(uint)ft)));
        Assert.Null(ProfileTools.FileTime(0, 0));
        Assert.Null(ProfileTools.FileTime(null, 5));
    }

    [Fact]
    public void Plano_de_copia_so_com_as_pastas_que_existem()
    {
        Criar(@"origem\Desktop\a.txt", @"origem\Documents\b.txt", @"origem\Favorites\c.url");
        var plano = ProfileTools.CopyPlan(Path.Combine(_pasta, "origem"), Path.Combine(_pasta, "destino"));
        Assert.Equal(new[] { "Área de Trabalho", "Documentos", "Favoritos" }, plano.Select(p => p.Label));
        Assert.Equal(Path.Combine(_pasta, "destino", "Documents"), plano[1].Target);
    }

    [Fact]
    public void Pasta_no_OneDrive_vem_do_OneDrive()
    {
        Criar(@"origem\Desktop\desktop.ini", @"origem\OneDrive - Empresa\Desktop\relatorio.docx", @"origem\Documents\b.txt");
        Directory.Delete(Path.Combine(_pasta, "origem", "Desktop"), true);
        Directory.CreateDirectory(Path.Combine(_pasta, "origem", "Desktop")); // pasta local vazia: arquivos estão no OneDrive
        var plano = ProfileTools.CopyPlan(Path.Combine(_pasta, "origem"), Path.Combine(_pasta, "destino"));
        Assert.Equal(Path.Combine(_pasta, "origem", "OneDrive - Empresa", "Desktop"), plano.Single(p => p.Label == "Área de Trabalho").Source);
        Assert.Equal(Path.Combine(_pasta, "origem", "Documents"), plano.Single(p => p.Label == "Documentos").Source);
    }

    [Fact]
    public void Robocopy_nao_apaga_nada_no_destino()
    {
        string args = ProfileTools.RobocopyArgs(@"C:\Users\a\Desktop\", @"C:\Users\b\Desktop");
        Assert.Equal("\"C:\\Users\\a\\Desktop\" \"C:\\Users\\b\\Desktop\" /E /XJ /COPY:DAT /DCOPY:T /R:1 /W:1 /NP /NFL /NDL /MT:8", args);
        Assert.DoesNotContain("/MIR", args);
        Assert.DoesNotContain("/PURGE", args);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(16, false)]
    [InlineData(-1, false)]
    public void Codigo_do_robocopy(int codigo, bool ok)
    {
        Assert.Equal(ok, ProfileTools.RobocopyOk(codigo));
    }

    [Fact]
    public async Task Copia_de_verdade_entre_pastas_temporarias()
    {
        Criar(@"origem\Desktop\a.txt", @"origem\Documents\sub\b.txt",
              @"origem\AppData\Local\Google\Chrome\User Data\Default\Bookmarks",
              @"destino\Documents\ja_existia.txt",
              @"destino\AppData\Local\Microsoft\Edge\User Data\Default\Bookmarks");
        Criar(@"origem\AppData\Local\Microsoft\Edge\User Data\Default\Bookmarks");
        var log = new List<string>();
        var (ok, falhas) = await ProfileTools.CopyAsync(Path.Combine(_pasta, "origem"), Path.Combine(_pasta, "destino"), log.Add);

        Assert.Equal(0, falhas);
        Assert.True(File.Exists(Path.Combine(_pasta, @"destino\Desktop\a.txt")));
        Assert.True(File.Exists(Path.Combine(_pasta, @"destino\Documents\sub\b.txt")));
        Assert.True(File.Exists(Path.Combine(_pasta, @"destino\Documents\ja_existia.txt"))); // nada apagado no destino
        Assert.True(File.Exists(Path.Combine(_pasta, @"destino\AppData\Local\Google\Chrome\User Data\Default\Bookmarks")));
        Assert.Contains(log, l => l.Contains("Favoritos do Edge: o destino já tem favoritos"));
        Assert.Equal(3, ok); // Desktop, Documents e favoritos do Chrome
    }
}
