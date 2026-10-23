namespace UtiliTech.Tests;

/// <summary>Impressoras (sem mexer nas impressoras do PC): validações, comandos montados e classificação.</summary>
public class ImpressorasTests
{
    [Theory]
    [InlineData("HP LaserJet (Cópia 1)", true)]
    [InlineData("Lexmark MS610 (Copy 2)", true)]
    [InlineData("Brother (copia 3)", true)]
    [InlineData("Lexmark MS610", false)]
    [InlineData("Cópia e scanner", false)]
    public void Fila_fantasma(string nome, bool fantasma)
    {
        Assert.Equal(fantasma, PrinterTools.IsGhost(nome));
    }

    [Theory]
    [InlineData("PORTPROMPT:", "Microsoft Print To PDF", true)]
    [InlineData("nul:", "Send to Microsoft OneNote 16 Driver", true)]
    [InlineData("IP_10.0.0.50", "Microsoft XPS Document Writer v4", true)]
    [InlineData("IP_10.0.0.50", "Lexmark Universal v2 XL", false)]
    [InlineData("USB001", "HP LaserJet Pro M404", false)]
    public void Impressora_virtual(string porta, string driver, bool virtual_)
    {
        Assert.Equal(virtual_, PrinterTools.IsVirtual(porta, driver));
    }

    [Theory]
    [InlineData("Lexmark MS610, 12", "Lexmark MS610")]
    [InlineData("Impressora, andar 2, 7", "Impressora, andar 2")]
    [InlineData("SemNumero", "SemNumero")]
    public void Trabalho_da_fila(string job, string impressora)
    {
        Assert.Equal(impressora, PrinterTools.JobPrinter(job));
    }

    [Fact]
    public void Nome_do_driver_sem_versao()
    {
        Assert.Equal("Microsoft IPP Class Driver", PrinterTools.DriverName("Microsoft IPP Class Driver,4,Windows x64"));
        Assert.Equal("Sem versão", PrinterTools.DriverName("Sem versão"));
    }

    [Theory]
    [InlineData("192.168.0.50", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("256.1.1.1", false)]
    [InlineData("192.168.0", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("192.168.0.50; rm", false)]
    [InlineData("impressora.local", false)]
    public void IP_da_impressora(string ip, bool valido)
    {
        Assert.Equal(valido, PrinterTools.IsValidIp(ip));
    }

    [Theory]
    [InlineData("Lexmark Recepção", true)]
    [InlineData("HP do 2º andar (sala 3)", true)]
    [InlineData("", false)]
    [InlineData("aspas \" no nome", false)]
    [InlineData("com $variavel", false)]
    [InlineData("crase `", false)]
    [InlineData("barra \\ invertida", false)]
    public void Nome_da_impressora(string nome, bool valido)
    {
        Assert.Equal(valido, PrinterTools.IsValidPrinterName(nome));
    }

    [Theory]
    [InlineData(@"\\servidor\impressora-andar1", true)]
    [InlineData(@"\\srv.empresa.local\Lexmark Recepção", true)]
    [InlineData(@"\\servidor", false)]
    [InlineData(@"servidor\impressora", false)]
    [InlineData(@"\\servidor\imp""ressora", false)]
    [InlineData(@"\\servidor\a\b", false)]
    public void Impressora_compartilhada(string caminho, bool valido)
    {
        Assert.Equal(valido, PrinterTools.IsValidSharePath(caminho));
    }

    [Fact]
    public void Script_de_instalacao_escapa_aspas_simples()
    {
        string s = PrinterTools.InstallScript("10.0.0.50", "Sala d'água", "Lexmark Universal v2 XL");
        Assert.Contains("Add-PrinterPort -Name 'IP_10.0.0.50' -PrinterHostAddress '10.0.0.50'", s);
        Assert.Contains("Add-Printer -Name 'Sala d''água' -DriverName 'Lexmark Universal v2 XL' -PortName 'IP_10.0.0.50'", s);
        Assert.Contains("if (-not (Get-PrinterPort -Name 'IP_10.0.0.50'", s); // porta já existente não dá erro
        Assert.DoesNotContain("\"", s); // vai entre aspas duplas na linha de comando
    }

    [Fact]
    public void Script_de_conexao()
    {
        Assert.Equal("$ErrorActionPreference='Stop'; Add-Printer -ConnectionName '\\\\srv\\Lexmark'; Write-Output 'Impressora conectada'",
            PrinterTools.ConnectScript(@"\\srv\Lexmark"));
    }

    [Fact]
    public void Escape_do_WMI()
    {
        Assert.Equal(@"\\\\srv\\Lexmark d\'água", PrinterTools.WmiEscape(@"\\srv\Lexmark d'água"));
    }

    [Fact]
    public void Texto_da_impressora()
    {
        var p = new PrinterInfo { Name = "Lexmark (Cópia 1)", Driver = "Lexmark Universal", Port = "IP_10.0.0.50", Host = "10.0.0.50", IsDefault = true, Jobs = 3 };
        Assert.Equal("Lexmark Universal · IP 10.0.0.50 · 3 documento(s) na fila", p.Detail);
        Assert.Equal("PADRÃO  CÓPIA (fila fantasma?)", p.Badges);
        Assert.Equal(Palette.Warn, p.BadgeColor);
        var compartilhada = new PrinterInfo { Name = @"\\srv\Lexmark", Driver = "Lexmark", Port = "", Host = "srv", IsShared = true };
        Assert.Equal("Lexmark · servidor srv", compartilhada.Detail);
    }

    // ---------- Mapear unidade (tela Rede) ----------

    [Fact]
    public void Letras_livres_para_mapear()
    {
        var livres = NetworkTools.FreeLetters(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C", "d", "E", "Z" });
        Assert.Equal("F", livres[0]);
        Assert.Equal("Y", livres[^1]);
        Assert.DoesNotContain("Z", livres);
        Assert.Equal(20, livres.Count); // D..Z = 23, menos D, E e Z
    }

    [Theory]
    [InlineData(@"\\servidor\arquivos", true)]
    [InlineData(@"\\servidor\arquivos\Financeiro 2026", true)]
    [InlineData(@"\\10.0.0.5\publico", true)]
    [InlineData(@"\\servidor", false)]
    [InlineData(@"C:\pasta", false)]
    [InlineData(@"\\servidor\a"" /user:x", false)]
    [InlineData(@"\\servidor\a & del", false)]
    public void Caminho_de_rede(string caminho, bool valido)
    {
        Assert.Equal(valido, NetworkTools.IsValidUncPath(caminho));
    }
}
