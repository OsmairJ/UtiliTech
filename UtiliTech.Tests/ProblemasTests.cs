namespace UtiliTech.Tests;

/// <summary>"Problemas encontrados" da Visão geral.</summary>
public class ProblemasTests
{
    const long GB = 1L << 30;

    static IssueFacts Ok() => new()
    {
        Disks = new() { ("SSD 240G", "Bom") },
        SystemFree = 150 * GB,
        SystemTotal = 222 * GB,
        Security = new() { new("av", "Antivírus", SecState.Ok, "", ""), new("sb", "Secure Boot", SecState.Warning, "UEFI, mas com o Secure Boot desligado", "") },
        Events = new() { [EventKind.UnexpectedShutdown] = 1 },
        Activation = "Ativado",
    };

    [Fact]
    public void PC_em_ordem_nao_tem_problemas()
    {
        // 1 desligamento inesperado e o Secure Boot desligado não entram (pouco relevantes aqui)
        Assert.Empty(PcIssues.Build(Ok()));
    }

    [Fact]
    public void Disco_ruim_e_grave_e_leva_para_Discos()
    {
        var i = Assert.Single(PcIssues.Build(Ok() with { Disks = new() { ("ST1000DX002", "Ruim") } }));
        Assert.True(i.Serious);
        Assert.Equal("Disco com falha: ST1000DX002", i.Title);
        Assert.Equal("disks", i.Page);
        Assert.False(Assert.Single(PcIssues.Build(Ok() with { Disks = new() { ("SSD", "Atenção") } })).Serious);
    }

    [Theory]
    [InlineData(150, 222, 0, false)]
    [InlineData(12, 222, 1, false)]   // 5,4%: pouco espaço
    [InlineData(4, 222, 1, true)]     // < 5 GB: quase cheio
    [InlineData(20, 100, 0, false)]   // 20%
    [InlineData(14, 500, 1, true)]    // 2,8%: grave mesmo com 14 GB (abaixo de 5% ou de 5 GB)
    [InlineData(14, 100, 1, false)]   // 14%, mas só 14 GB → pouco espaço
    public void Espaco_livre(long livreGb, long totalGb, int problemas, bool grave)
    {
        var r = PcIssues.Build(Ok() with { SystemFree = livreGb * GB, SystemTotal = totalGb * GB }).Where(i => i.Page == "cleanup").ToList();
        Assert.Equal(problemas, r.Count);
        if (problemas > 0) Assert.Equal(grave, r[0].Serious);
    }

    [Fact]
    public void Drivers_video_e_bateria()
    {
        var r = PcIssues.Build(Ok() with { ProblemDevices = 2, GpuWithoutDriver = "NVIDIA GeForce GT 730", BatteryHealth = 52 });
        Assert.Equal(new[] { "2 dispositivo(s) sem driver ou com erro", "Placa de vídeo sem o driver do fabricante", "Bateria gasta" }, r.Select(i => i.Title));
        Assert.All(r, i => Assert.False(i.Serious));
        Assert.Empty(PcIssues.Build(Ok() with { BatteryHealth = 75 }));
    }

    [Fact]
    public void Seguranca_so_o_que_importa()
    {
        var f = Ok() with
        {
            Security = new()
            {
                new("fw", "Firewall", SecState.Bad, "Desligado no perfil Público", ""),
                new("av", "Antivírus", SecState.Warning, "assinaturas velhas", ""),
                new("sb", "Secure Boot", SecState.Warning, "desligado", ""),
                new("bl", "BitLocker", SecState.Warning, "C: sem BitLocker", ""),
            },
        };
        var r = PcIssues.Build(f);
        Assert.Equal(new[] { "Firewall: problema", "Antivírus: atenção" }, r.Select(i => i.Title));
        Assert.True(r[0].Serious);
    }

    [Fact]
    public void Eventos_dos_ultimos_30_dias()
    {
        var f = Ok() with { Events = new() { [EventKind.BlueScreen] = 3, [EventKind.UnexpectedShutdown] = 2, [EventKind.Video] = 2, [EventKind.Hardware] = 1 } };
        var r = PcIssues.Build(f);
        Assert.Equal("3 tela(s) azul(is) nos últimos 30 dias", r[0].Title);
        Assert.True(r[0].Serious);
        Assert.Contains(r, i => i.Title == "2 desligamentos inesperados");
        Assert.DoesNotContain(r, i => i.Title.Contains("vídeo"));   // 2 travamentos de vídeo ainda não é problema
        Assert.Contains(r, i => i.Title.Contains("WHEA"));
        Assert.All(r, i => Assert.Equal("events", i.Page));
    }

    [Fact]
    public void Ativacao_e_reinicio()
    {
        var r = PcIssues.Build(Ok() with { Activation = "Notificação (não ativado)", RebootPending = true });
        Assert.Equal(new[] { "Windows não ativado", "Reinício pendente" }, r.Select(i => i.Title));
        Assert.False(r[1].HasAction); // reiniciar não tem tela
        Assert.Empty(PcIssues.Build(Ok() with { Activation = "Não foi possível verificar" }));
    }

    [Fact]
    public void Graves_primeiro_mantendo_a_ordem()
    {
        var f = Ok() with
        {
            ProblemDevices = 1,
            Disks = new() { ("HD", "Ruim") },
            Events = new() { [EventKind.BlueScreen] = 1 },
            RebootPending = true,
        };
        var r = PcIssues.Build(f);
        Assert.Equal(new[] { true, true, false, false }, r.Select(i => i.Serious));
        Assert.StartsWith("Disco com falha", r[0].Title);
        Assert.Contains("tela(s) azul", r[1].Title);
        Assert.Equal(Palette.Bad, r[0].Color);
    }
}
