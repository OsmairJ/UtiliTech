using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UtiliTech.Services;

public enum SecState { Ok, Warning, Bad, Unknown }

/// <summary>Um item da tela Segurança, já avaliado.</summary>
public record SecurityItem(string Key, string Title, SecState State, string Detail, string Advice)
{
    public string StateText => State switch
    {
        SecState.Ok => "OK",
        SecState.Warning => "Atenção",
        SecState.Bad => "Problema",
        _ => "Não verificado",
    };
    public string Color => State switch
    {
        SecState.Ok => Palette.Good,
        SecState.Warning => Palette.Warn,
        SecState.Bad => Palette.Bad,
        _ => Palette.Muted,
    };
    public string Glyph => State switch
    {
        SecState.Ok => "",
        SecState.Warning => "",
        SecState.Bad => "",
        _ => "",
    };
    public bool HasAdvice => Advice.Length > 0;
}

/// <summary>Antivírus registrado na Central de Segurança do Windows.</summary>
public record AntivirusInfo(string Name, bool Enabled, bool UpToDate);

/// <summary>Unidade com BitLocker (Win32_EncryptableVolume).</summary>
public record BitLockerVolume(string Drive, int Protection, int Conversion, int Percent)
{
    /// <summary>ProtectionStatus: 0 desligada, 1 ligada, 2 desconhecida (bloqueada). ConversionStatus: 0 descriptografada, 1 criptografada, 2 criptografando, 3 descriptografando, 4/5 pausada.</summary>
    public string Text => Conversion switch
    {
        2 => $"{Drive} criptografando ({Percent}%)",
        3 => $"{Drive} descriptografando ({Percent}%)",
        4 or 5 => $"{Drive} criptografia pausada ({Percent}%)",
        _ => Protection == 1 ? $"{Drive} protegida" : Conversion == 1 ? $"{Drive} criptografada, proteção suspensa" : $"{Drive} sem BitLocker",
    };
}

/// <summary>O que foi lido do PC (separado da avaliação para poder testar).</summary>
public record SecurityFacts
{
    public bool? DefenderEnabled { get; init; }
    public bool? RealTime { get; init; }
    public DateTime? SignaturesUpdated { get; init; }
    public DateTime? LastQuickScan { get; init; }
    public List<AntivirusInfo> Antivirus { get; init; } = new();
    /// <summary>Perfil do firewall → ligado (Domain, Private, Public).</summary>
    public Dictionary<string, bool> Firewall { get; init; } = new();
    public List<BitLockerVolume> BitLocker { get; init; } = new();
    public string SystemDrive { get; init; } = "C:";
    public bool? Uefi { get; init; }
    public bool? SecureBoot { get; init; }
    public bool? TpmPresent { get; init; }
    public bool? TpmReady { get; init; }
    public string TpmVersion { get; init; } = "";
    public bool? Uac { get; init; }
    public DateTime? LastUpdateInstalled { get; init; }
}

/// <summary>
/// Segurança do Windows: antivírus (Defender ou outro), firewall, BitLocker (com a chave de recuperação sob pedido),
/// Secure Boot/UEFI, TPM, UAC e a última atualização instalada. Só leitura, menos as ações da tela.
/// </summary>
public static class SecurityCheck
{
    // ---------- Avaliação ----------

    public static List<SecurityItem> Evaluate(SecurityFacts f, DateTime now)
    {
        var items = new List<SecurityItem>();

        // Antivírus: Defender ou outro produto registrado e ligado
        var others = f.Antivirus.Where(a => !a.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase)).ToList();
        var otherOn = others.FirstOrDefault(a => a.Enabled);
        if (f.DefenderEnabled == true && f.RealTime == true)
        {
            int days = f.SignaturesUpdated is { } s ? (int)(now - s).TotalDays : -1;
            string detail = "Microsoft Defender ativo, com proteção em tempo real" +
                            (f.SignaturesUpdated is { } su ? $" · assinaturas de {su:dd/MM/yyyy HH:mm}" : "") +
                            (f.LastQuickScan is { } q ? $" · última verificação rápida em {q:dd/MM/yyyy}" : " · nenhuma verificação registrada");
            items.Add(days > 7
                ? new("av", "Antivírus", SecState.Warning, detail, $"Assinaturas com {days} dias: clique em Atualizar assinaturas (precisa de internet).")
                : new("av", "Antivírus", SecState.Ok, detail, ""));
        }
        else if (otherOn != null)
            items.Add(new("av", "Antivírus", otherOn.UpToDate ? SecState.Ok : SecState.Warning,
                $"{otherOn.Name} ativo (o Defender fica em modo passivo)" + (otherOn.UpToDate ? "" : " · desatualizado"),
                otherOn.UpToDate ? "" : $"Atualize o {otherOn.Name} pelo próprio programa."));
        else if (f.DefenderEnabled == true)
            items.Add(new("av", "Antivírus", SecState.Bad, "Defender instalado, mas a proteção em tempo real está desligada",
                "Abra a Segurança do Windows → Proteção contra vírus e ameaças e ligue a proteção em tempo real."));
        else if (f.DefenderEnabled == false || f.Antivirus.Count > 0)
            items.Add(new("av", "Antivírus", SecState.Bad,
                others.Count > 0 ? $"{others[0].Name} instalado, mas desligado" : "Nenhum antivírus ativo",
                "O PC está sem proteção: ligue o Defender (Segurança do Windows) ou o antivírus da empresa."));
        else
            items.Add(new("av", "Antivírus", SecState.Unknown, "Não foi possível ler o estado do antivírus", ""));

        // Firewall
        string Pt(string p) => p switch { "Domain" => "Domínio", "Private" => "Particular", "Public" => "Público", _ => p };
        if (f.Firewall.Count == 0)
            items.Add(new("fw", "Firewall", SecState.Unknown, "Não foi possível ler o firewall", ""));
        else
        {
            var off = f.Firewall.Where(p => !p.Value).Select(p => Pt(p.Key)).ToList();
            items.Add(off.Count == 0
                ? new("fw", "Firewall", SecState.Ok, "Ligado nos perfis " + string.Join(", ", f.Firewall.Keys.Select(Pt)), "")
                : new("fw", "Firewall", SecState.Bad, "Desligado no perfil " + string.Join(", ", off),
                    "Use Ligar firewall (ou confira se a empresa desliga de propósito por política)."));
        }

        // BitLocker (unidade do Windows é a que importa)
        var sys = f.BitLocker.FirstOrDefault(v => v.Drive.Equals(f.SystemDrive, StringComparison.OrdinalIgnoreCase));
        string all = string.Join(" · ", f.BitLocker.Select(v => v.Text));
        if (f.BitLocker.Count == 0)
            items.Add(new("bl", "BitLocker", SecState.Unknown, "Não foi possível ler o BitLocker (edição Home não tem)", ""));
        else if (sys == null)
            items.Add(new("bl", "BitLocker", SecState.Unknown, all, ""));
        else if (sys.Protection == 1)
            items.Add(new("bl", "BitLocker", SecState.Ok, all, "Antes de trocar placa-mãe, atualizar BIOS ou mexer no TPM, anote a chave de recuperação (Mostrar chave)."));
        else if (sys.Conversion is 2 or 3 or 4 or 5)
            items.Add(new("bl", "BitLocker", SecState.Warning, all, "Aguarde a criptografia terminar antes de desligar o PC."));
        else if (sys.Conversion == 1)
            items.Add(new("bl", "BitLocker", SecState.Warning, all, "A proteção está suspensa (comum depois de atualizar BIOS): retome em Gerenciar o BitLocker."));
        else
            items.Add(new("bl", "BitLocker", SecState.Warning, all, "Disco sem criptografia: se o notebook for perdido, os dados ficam expostos. Siga a política da empresa."));

        // Secure Boot
        if (f.Uefi == false)
            items.Add(new("sb", "Secure Boot", SecState.Warning, "PC iniciando em modo Legacy (BIOS antiga): sem Secure Boot",
                "Windows 11 exige UEFI + Secure Boot. Mudar para UEFI exige converter o disco (mbr2gpt): faça só com backup."));
        else if (f.SecureBoot == true)
            items.Add(new("sb", "Secure Boot", SecState.Ok, "UEFI com Secure Boot ligado", ""));
        else if (f.SecureBoot == false)
            items.Add(new("sb", "Secure Boot", SecState.Warning, "UEFI, mas com o Secure Boot desligado",
                "Ligue no setup da BIOS/UEFI (Boot → Secure Boot). Se o BitLocker estiver ativo, anote a chave antes."));
        else
            items.Add(new("sb", "Secure Boot", SecState.Unknown, "Não foi possível ler o Secure Boot", ""));

        // TPM
        if (f.TpmPresent == false)
            items.Add(new("tpm", "TPM", SecState.Warning, "Nenhum TPM encontrado",
                "Pode estar desligado no setup da BIOS (Intel PTT / AMD fTPM). Windows 11 e BitLocker precisam dele."));
        else if (f.TpmPresent == true)
        {
            string ver = f.TpmVersion.Length > 0 ? $"versão {f.TpmVersion}" : "versão não informada";
            bool v2 = f.TpmVersion.StartsWith("2");
            items.Add(f.TpmReady == true
                ? new("tpm", "TPM", v2 ? SecState.Ok : SecState.Warning, $"TPM ativo, {ver}", v2 ? "" : "TPM 1.2 não atende o Windows 11.")
                : new("tpm", "TPM", SecState.Warning, $"TPM presente, mas não pronto ({ver})", "Ative no setup da BIOS ou em tpm.msc."));
        }
        else
            items.Add(new("tpm", "TPM", SecState.Unknown, "Não foi possível ler o TPM", ""));

        // UAC
        items.Add(f.Uac switch
        {
            true => new("uac", "Controle de Conta de Usuário (UAC)", SecState.Ok, "Ligado", ""),
            false => new("uac", "Controle de Conta de Usuário (UAC)", SecState.Bad, "Desligado: qualquer programa roda como administrador",
                "Ligue em Painel de Controle → Contas de Usuário → Alterar configurações de Controle de Conta de Usuário."),
            _ => new("uac", "Controle de Conta de Usuário (UAC)", SecState.Unknown, "Não foi possível ler", ""),
        });

        // Atualizações
        if (f.LastUpdateInstalled is { } last)
        {
            int days = (int)(now.Date - last.Date).TotalDays;
            items.Add(days > 45
                ? new("wu", "Atualizações do Windows", SecState.Warning, $"Última atualização instalada em {last:dd/MM/yyyy} (há {days} dias)",
                    "Rode o Windows Update (ou Reparos → Resetar Windows Update se estiver travado).")
                : new("wu", "Atualizações do Windows", SecState.Ok, $"Última atualização instalada em {last:dd/MM/yyyy}", ""));
        }
        else
            items.Add(new("wu", "Atualizações do Windows", SecState.Unknown, "Nenhuma atualização registrada", ""));

        return items;
    }

    /// <summary>productState da Central de Segurança: bits 12-15 = ligado (1), bits 4-7 = atualizado (0).</summary>
    public static AntivirusInfo DecodeProductState(string name, uint state) =>
        new(name, ((state >> 12) & 0xF) == 1, ((state >> 4) & 0xF) == 0);

    /// <summary>Win32_QuickFixEngineering.InstalledOn vem em vários formatos ("10/22/2026", "20261022" ou FILETIME em hexa).</summary>
    public static DateTime? ParseInstalledOn(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        if (DateTime.TryParseExact(s, new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (s.Length is >= 14 and <= 16 && long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long ft))
            try { return DateTime.FromFileTime(ft); } catch { }
        return null;
    }

    // ---------- Leitura ----------

    public static SecurityFacts Gather()
    {
        var def = Wmi.Query("SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureLastUpdated, QuickScanEndTime FROM MSFT_MpComputerStatus",
            @"root\Microsoft\Windows\Defender").FirstOrDefault();
        var tpm = Wmi.Query("SELECT * FROM Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm").FirstOrDefault();
        bool tpmRead = Wmi.Query("SELECT * FROM __Namespace WHERE Name='MicrosoftTpm'", @"root\CIMV2\Security").Count > 0;

        return new SecurityFacts
        {
            DefenderEnabled = def == null ? null : Wmi.Get(def, "AntivirusEnabled") as bool?,
            RealTime = def == null ? null : Wmi.Get(def, "RealTimeProtectionEnabled") as bool?,
            SignaturesUpdated = def == null ? null : Wmi.Date(def, "AntivirusSignatureLastUpdated"),
            LastQuickScan = def == null ? null : Wmi.Date(def, "QuickScanEndTime"),
            Antivirus = Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2")
                .Select(a => DecodeProductState(Wmi.Str(a, "displayName"), (uint)Wmi.Long(a, "productState"))).ToList(),
            Firewall = Wmi.Query("SELECT Name, Enabled FROM MSFT_NetFirewallProfile", @"root\StandardCimv2")
                .ToDictionary(p => Wmi.Str(p, "Name"), p => Wmi.Long(p, "Enabled") == 1),
            BitLocker = BitLockerVolumes(),
            SystemDrive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'),
            Uefi = FirmwareIsUefi(),
            SecureBoot = RegistryDword(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled") is { } sb ? sb == 1 : null,
            TpmPresent = tpm != null ? true : tpmRead ? false : null,
            TpmReady = tpm == null ? null : Wmi.Get(tpm, "IsEnabled_InitialValue") is true && Wmi.Get(tpm, "IsActivated_InitialValue") is true,
            TpmVersion = tpm == null ? "" : Wmi.Str(tpm, "SpecVersion").Split(',')[0].Trim(),
            Uac = RegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") is { } lua ? lua != 0 : null,
            LastUpdateInstalled = Wmi.Query("SELECT InstalledOn FROM Win32_QuickFixEngineering")
                .Select(q => ParseInstalledOn(Wmi.Str(q, "InstalledOn"))).Where(d => d != null && d <= DateTime.Now.AddDays(1)).Max(),
        };
    }

    const string BitLockerScope = @"root\CIMV2\Security\MicrosoftVolumeEncryption";

    static List<BitLockerVolume> BitLockerVolumes()
    {
        var list = new List<BitLockerVolume>();
        foreach (var v in Wmi.Query("SELECT * FROM Win32_EncryptableVolume", BitLockerScope))
        {
            string drive = Wmi.Str(v, "DriveLetter");
            if (drive.Length == 0) continue;
            int conversion = 0, percent = 0;
            try
            {
                var r = v.InvokeMethod("GetConversionStatus", null, null);
                conversion = Convert.ToInt32(r?["ConversionStatus"] ?? 0);
                percent = (int)Math.Round(Convert.ToDouble(r?["EncryptionPercentage"] ?? 0));
            }
            catch { }
            list.Add(new BitLockerVolume(drive, (int)Wmi.Long(v, "ProtectionStatus"), conversion, percent));
        }
        return list.OrderBy(v => v.Drive).ToList();
    }

    /// <summary>Chaves de recuperação (senha numérica de 48 dígitos) da unidade. Não registra em log.</summary>
    public static List<(string Id, string Password)> RecoveryKeys(string drive)
    {
        var keys = new List<(string, string)>();
        var v = Wmi.Query($"SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter='{drive.Replace("'", "")}'", BitLockerScope).FirstOrDefault();
        if (v == null) return keys;
        var inp = v.GetMethodParameters("GetKeyProtectors");
        inp["KeyProtectorType"] = 3; // senha numérica
        var ids = v.InvokeMethod("GetKeyProtectors", inp, null)?["VolumeKeyProtectorID"] as string[] ?? Array.Empty<string>();
        foreach (var id in ids)
        {
            var p = v.GetMethodParameters("GetKeyProtectorNumericalPassword");
            p["VolumeKeyProtectorID"] = id;
            if (v.InvokeMethod("GetKeyProtectorNumericalPassword", p, null)?["NumericalPassword"] is string pw && pw.Length > 0)
                keys.Add((id, pw));
        }
        return keys;
    }

    static int? RegistryDword(string path, string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(path);
            return k?.GetValue(name) is int v ? v : null;
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll")]
    static extern bool GetFirmwareType(out int type);

    /// <summary>1 = BIOS (Legacy), 2 = UEFI.</summary>
    static bool? FirmwareIsUefi()
    {
        try { return GetFirmwareType(out int t) ? t == 2 : null; }
        catch { return null; }
    }

    // ---------- Ações ----------

    static string MpCmdRun => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");

    public static Task<int> UpdateSignaturesAsync(Action<string> log) => Repairs.Step(log, MpCmdRun, "-SignatureUpdate", timeoutMinutes: 10);

    public static Task<int> QuickScanAsync(Action<string> log) => Repairs.Step(log, MpCmdRun, "-Scan -ScanType 1", timeoutMinutes: 60);

    /// <summary>Liga o firewall nos perfis desligados (netsh, um perfil por vez).</summary>
    public static async Task<int> EnableFirewallAsync(IEnumerable<string> profiles, Action<string> log)
    {
        int last = 0;
        foreach (var p in profiles)
        {
            string arg = p switch { "Domain" => "domainprofile", "Private" => "privateprofile", "Public" => "publicprofile", _ => "" };
            if (arg.Length > 0) last = await Repairs.Step(log, "netsh", $"advfirewall set {arg} state on");
        }
        return last;
    }
}
