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

    [Fact]
    public void Lexmark_nao_fica_mais_em_Instalar_apps()
    {
        Assert.DoesNotContain(AppInstaller.DefaultCatalog(), a => a.Name.Contains("Lexmark") || (a.File ?? "").StartsWith("LM "));
        Assert.Contains(AppInstaller.DefaultCatalog(), a => a.Name == "PJe Office Pro");
    }

    [Fact]
    public void Pacotes_Lexmark_do_pendrive()
    {
        Assert.Equal(new[] { ("MX620", "XL", "LM PB W10.exe"), ("CS725", "PS", "LM COLOR W10.exe") },
            PrinterTools.LexmarkPackages.Select(p => (p.Model, p.DriverType, p.File)));
    }

    [Fact]
    public void Drivers_Lexmark_dos_modelos_vem_primeiro()
    {
        var ordem = PrinterTools.SortDrivers(new[]
        {
            "Microsoft IPP Class Driver", "Lexmark Universal v2", "Lexmark CS725 Series PS3", "HP Universal Printing PCL 6", "Lexmark MX620 Series XL",
        });
        Assert.Equal(new[]
        {
            "Lexmark CS725 Series PS3", "Lexmark MX620 Series XL", "Lexmark Universal v2", "HP Universal Printing PCL 6", "Microsoft IPP Class Driver",
        }, ordem);
    }

    // ---------- Drivers e identificação do modelo ----------

    static readonly string[] Instalados = { "Microsoft IPP Class Driver", "Microsoft Print To PDF", "Lexmark MX620 Series XL" };

    [Fact]
    public void Lista_tem_os_drivers_do_Windows_para_Brother_e_Samsung()
    {
        var opcoes = PrinterTools.DriverOptions(Instalados);
        var pcl6 = opcoes.Single(o => o.Name == PrinterTools.Pcl6Driver);
        Assert.True(pcl6.BuiltIn);
        Assert.Contains("Brother DCP-L2540DW", pcl6.Label);
        Assert.Contains("Samsung SCX-483x", pcl6.Label);
        Assert.EndsWith("(vem no Windows)", pcl6.Label);
        Assert.Contains(opcoes, o => o.Name == PrinterTools.PsDriver);
        // Lexmark do modelo primeiro, depois os de classe do Windows, depois o resto
        Assert.Equal(new[] { "Lexmark MX620 Series XL", PrinterTools.Pcl6Driver, PrinterTools.PsDriver }, opcoes.Take(3).Select(o => o.Name));
        Assert.Equal("Lexmark MX620 Series XL · Lexmark MX620 · preto e branco", opcoes[0].Label);
        Assert.Equal(opcoes.Count, opcoes.Select(o => o.Name).Distinct().Count());
    }

    [Fact]
    public void Driver_do_Windows_ja_ativado_nao_repete()
    {
        var opcoes = PrinterTools.DriverOptions(Instalados.Append(PrinterTools.Pcl6Driver));
        var pcl6 = Assert.Single(opcoes, o => o.Name == PrinterTools.Pcl6Driver);
        Assert.DoesNotContain("(vem no Windows)", pcl6.Label);
    }

    [Theory]
    [InlineData("Lexmark MX622adhe 7018840002TB8 MXTGM.052.026", "Lexmark MX622adhe")]
    [InlineData("Lexmark CS725 50288380F354Y CSTAT.081.225", "Lexmark CS725")]
    [InlineData("Lexmark CS725 version CSTAT.052.024 kernel 4.11.12-yocto", "Lexmark CS725")]
    [InlineData("Brother DCP-L2540DW series", "Brother DCP-L2540DW series")]
    [InlineData("Samsung SCX-483x 5x3x Series; V2.00.01.18 NOV-28-2011;Engine 1.00.27", "Samsung SCX-483x 5x3x Series")]
    [InlineData("Samsung SCX-5835_5935X Series", "Samsung SCX-5835_5935X Series")]
    public void Modelo_sem_serie_e_firmware(string descricao, string modelo)
    {
        Assert.Equal(modelo, PrinterTools.ShortModel(descricao));
    }

    [Theory]
    [InlineData("Lexmark MX622adhe", "Lexmark MX620 Series XL")]
    [InlineData("Brother DCP-L2540DW series", PrinterTools.Pcl6Driver)]
    [InlineData("Samsung SCX-483x 5x3x Series", PrinterTools.Pcl6Driver)]
    [InlineData("Samsung SCX-5835_5935X Series", PrinterTools.Pcl6Driver)]
    [InlineData("HP LaserJet M404", PrinterTools.IppDriver)]
    public void Driver_indicado_pelo_modelo(string modelo, string driver)
    {
        var opcoes = PrinterTools.DriverOptions(Instalados).Select(o => o.Name);
        Assert.Equal(driver, PrinterTools.RecommendDriver(modelo, opcoes));
    }

    [Fact]
    public void Lexmark_sem_driver_instalado_aponta_o_assistente_do_pendrive()
    {
        var opcoes = PrinterTools.DriverOptions(new[] { "Microsoft IPP Class Driver" }).Select(o => o.Name);
        Assert.Null(PrinterTools.RecommendDriver("Lexmark CS725", opcoes));
        Assert.Equal("CS725", PrinterTools.LexmarkFor("Lexmark CS725")!.Model);
        Assert.Equal("MX620", PrinterTools.LexmarkFor("Lexmark MX622adhe")!.Model);
        Assert.Null(PrinterTools.LexmarkFor("Brother DCP-L2540DW series"));
    }

    [Fact]
    public void Script_ativa_o_driver_se_precisar()
    {
        string s = PrinterTools.InstallScript("10.0.0.50", "Brother", PrinterTools.Pcl6Driver);
        Assert.Contains("if (-not (Get-PrinterDriver -Name 'Microsoft PCL6 Class Driver' -ErrorAction SilentlyContinue)) { Add-PrinterDriver -Name 'Microsoft PCL6 Class Driver' }", s);
        Assert.True(s.IndexOf("Add-PrinterDriver", StringComparison.Ordinal) < s.IndexOf("Add-Printer -Name", StringComparison.Ordinal));
    }

    [Fact]
    public void SNMP_monta_o_pedido_e_le_a_resposta()
    {
        var req = Snmp.GetRequest("1.3.6.1.2.1.1.1.0");
        Assert.Equal(new byte[] { 0x2B, 6, 1, 2, 1, 1, 1, 0 }, Snmp.EncodeOid("1.3.6.1.2.1.1.1.0"));
        Assert.Equal(0x30, req[0]);
        Assert.Equal(req.Length - 2, req[1]);
        // Resposta montada à mão: GetResponse com 1 varbind texto
        byte[] texto = System.Text.Encoding.ASCII.GetBytes("Lexmark CS725");
        byte[] oid = Snmp.EncodeOid("1.3.6.1.2.1.1.1.0");
        byte[] vb = new byte[] { 0x30, (byte)(2 + oid.Length + 2 + texto.Length), 0x06, (byte)oid.Length }.Concat(oid)
            .Concat(new byte[] { 0x04, (byte)texto.Length }).Concat(texto).ToArray();
        byte[] vbl = new byte[] { 0x30, (byte)vb.Length }.Concat(vb).ToArray();
        byte[] pdu = new byte[] { 0xA2, (byte)(9 + vbl.Length), 0x02, 1, 1, 0x02, 1, 0, 0x02, 1, 0 }.Concat(vbl).ToArray();
        byte[] corpo = new byte[] { 0x02, 1, 0, 0x04, 6 }.Concat(System.Text.Encoding.ASCII.GetBytes("public")).Concat(pdu).ToArray();
        byte[] msg = new byte[] { 0x30, (byte)corpo.Length }.Concat(corpo).ToArray();
        Assert.Equal(new[] { "Lexmark CS725" }, Snmp.ParseStrings(msg));
        Assert.Empty(Snmp.ParseStrings(msg[..20])); // resposta cortada não derruba
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
