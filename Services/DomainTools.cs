using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace UtiliTech.Services;

/// <summary>Computador já cadastrado no AD com o nome procurado.</summary>
public record AdComputer(string Name, string DistinguishedName, DateTime? Created, DateTime? LastLogon, string OperatingSystem, string Description)
{
    public List<InfoItem> Items => new List<InfoItem>
    {
        new("Local no AD (OU)", DistinguishedName),
        new("Criado em", Created?.ToString("dd/MM/yyyy HH:mm") ?? ""),
        new("Último logon", LastLogon?.ToString("dd/MM/yyyy HH:mm") ?? "nunca"),
        new("Sistema", OperatingSystem),
        new("Descrição", Description),
    }.Where(i => i.Value.Length > 0).ToList();
}

public record MachineDomainInfo(string Name, string Domain, bool PartOfDomain);

/// <summary>
/// Nome do computador e ingresso no domínio: consulta o AD (catálogo global) para saber se o nome já
/// existe, e usa as mesmas APIs do Windows que o Add-Computer/Rename-Computer usam.
/// </summary>
public static class DomainTools
{
    public static string DomainName => AppSettings.Current.DomainName;

    public static MachineDomainInfo Current()
    {
        var cs = Wmi.Query("SELECT Name, Domain, PartOfDomain FROM Win32_ComputerSystem").FirstOrDefault();
        return cs == null
            ? new MachineDomainInfo(Environment.MachineName, "", false)
            : new MachineDomainInfo(Wmi.Str(cs, "Name"), Wmi.Str(cs, "Domain"), Wmi.Get(cs, "PartOfDomain") is true);
    }

    public static bool IsInDomain(MachineDomainInfo m) => m.PartOfDomain && m.Domain.Equals(DomainName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Regras do nome NetBIOS: até 15 caracteres, letras/números/hífen, não só números. Devolve o erro ou null.</summary>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Digite o novo nome.";
        if (name.Length > 15) return $"Máximo de 15 caracteres (este tem {name.Length}).";
        if (!Regex.IsMatch(name, "^[A-Za-z0-9-]+$")) return "Use só letras sem acento, números e hífen (sem espaços).";
        if (name.StartsWith('-') || name.EndsWith('-')) return "O nome não pode começar nem terminar com hífen.";
        if (Regex.IsMatch(name, "^[0-9]+$")) return "O nome não pode ser só números.";
        return null;
    }

    /// <summary>Usuário no formato que o AD aceita: "usuario" vira "usuario@dominio".</summary>
    public static string? NormalizeUser(string user)
    {
        user = user.Trim();
        if (user.Length == 0) return null;
        return user.Contains('\\') || user.Contains('@') ? user : $"{user}@{DomainName}";
    }

    /// <summary>
    /// Credencial com o domínio separado do usuário, como o AD espera de um PC fora do domínio:
    /// "DOMINIO\usuario" → (usuario, empresa.local); "usuario@empresa.local" fica como está; "usuario" → (usuario, empresa.local).
    /// </summary>
    static NetworkCredential Credential(string user, string password)
    {
        user = user.Trim();
        int slash = user.IndexOf('\\');
        if (slash > 0)
        {
            // "DOMINIO\usuario": de um PC fora do domínio o nome curto (NetBIOS) não serve para o Kerberos;
            // o nome DNS do domínio (ex.: empresa.local) funciona tanto para Kerberos quanto para NTLM
            string domain = user[..slash];
            return new NetworkCredential(user[(slash + 1)..], password, domain.Contains('.') ? domain : DomainName);
        }
        if (user.Contains('@')) return new NetworkCredential(user, password);
        return new NetworkCredential(user, password, DomainName);
    }

    /// <summary>
    /// Diferença entre o relógio deste PC e o do servidor do AD (lida sem login, no rootDSE).
    /// O Kerberos recusa o login quando passa de 5 minutos — comum em PC recém-formatado.
    /// </summary>
    public static TimeSpan? ClockSkew()
    {
        try
        {
            using var conn = new LdapConnection(new LdapDirectoryIdentifier(DomainName, 389)) { AuthType = AuthType.Anonymous, Timeout = TimeSpan.FromSeconds(10) };
            conn.SessionOptions.ProtocolVersion = 3;
            var response = (SearchResponse)conn.SendRequest(new SearchRequest("", "(objectClass=*)", System.DirectoryServices.Protocols.SearchScope.Base, "currentTime"));
            var value = response.Entries.Count > 0 && response.Entries[0].Attributes["currentTime"] is { Count: > 0 } a ? a[0]?.ToString() : null;
            var server = ParseGeneralizedTime(value ?? "");
            return server == null ? null : DateTime.Now - server.Value;
        }
        catch { return null; }
    }

    /// <summary>
    /// Procura o computador no AD. Sem usuário/senha, usa a conta do Windows (funciona em PC já no domínio).
    /// Devolve null se o nome está livre.
    /// </summary>
    public static AdComputer? FindComputer(string name, string? user, string? password)
    {
        string filter = $"(&(objectCategory=computer)(sAMAccountName={EscapeLdap(name)}$))";
        string[] attrs = { "cn", "distinguishedName", "whenCreated", "lastLogonTimestamp", "operatingSystem", "description" };
        var cred = user == null ? null : Credential(user, password ?? "");

        // Catálogo global (porta 3268) = a floresta inteira; se estiver fechado, o LDAP do domínio (389).
        // Login recusado não tenta de novo: repetir senha errada pode bloquear a conta.
        Exception? last = null;
        foreach (var (port, baseDn) in new[] { (3268, ""), (389, DomainDn) })
        {
            try
            {
                using var conn = new LdapConnection(new LdapDirectoryIdentifier(DomainName, port), cred, AuthType.Negotiate);
                conn.SessionOptions.ProtocolVersion = 3;
                conn.SessionOptions.Signing = true;
                conn.SessionOptions.Sealing = true;
                conn.Timeout = TimeSpan.FromSeconds(20);
                conn.Bind();

                var request = new SearchRequest(baseDn, filter, System.DirectoryServices.Protocols.SearchScope.Subtree, attrs) { SizeLimit = 1 };
                var response = (SearchResponse)conn.SendRequest(request);
                if (response.Entries.Count == 0) return null;
                var e = response.Entries[0];
                string A(string n) => e.Attributes[n] is { Count: > 0 } v ? v[0]?.ToString() ?? "" : "";
                return new AdComputer(A("cn"), e.DistinguishedName, ParseGeneralizedTime(A("whenCreated")),
                    long.TryParse(A("lastLogonTimestamp"), out var ft) && ft > 0 ? DateTime.FromFileTime(ft) : null,
                    A("operatingSystem"), A("description"));
            }
            catch (LdapException ex) when (ex.ErrorCode == 49) // credencial recusada
            {
                throw new InvalidOperationException(LoginFailure(ex.ServerErrorMessage, cred));
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.SizeLimitExceeded)
            {
                return null; // não acontece com SizeLimit=1 e resultado vazio, mas não é erro
            }
            catch (Exception ex)
            {
                last = ex;
                Logger.Error($"Consulta ao AD na porta {port}", ex);
            }
        }
        throw new InvalidOperationException($"não foi possível falar com o AD {DomainName} ({last?.Message}). O PC está na rede da empresa?", last);
    }

    // "DC=pf,DC=gov,DC=br"
    static string DomainDn => string.Join(",", DomainName.Split('.').Select(p => "DC=" + p));

    // O AD explica a recusa no texto do erro: "... data 52e, ..." (códigos do Windows)
    static string LoginFailure(string? serverMessage, NetworkCredential? cred)
    {
        string who = cred == null ? "a conta do Windows" : string.IsNullOrEmpty(cred.Domain) ? cred.UserName : $@"{cred.Domain}\{cred.UserName}";
        var code = Regex.Match(serverMessage ?? "", @"data ([0-9a-f]+)", RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
        return code switch
        {
            "525" => $"o usuário {who} não existe no domínio (confira o nome da conta)",
            "52e" => $"senha incorreta para {who}",
            "530" => $"{who} não pode entrar neste horário",
            "531" => $"{who} não pode entrar a partir deste computador",
            "532" or "773" => $"a senha de {who} expirou ou precisa ser trocada; troque num PC do domínio e tente de novo",
            "533" => $"a conta {who} está desativada",
            "701" => $"a conta {who} expirou",
            "775" => $"a conta {who} está bloqueada (muitas tentativas); peça o desbloqueio",
            _ => $"o AD recusou o login de {who}" + (code.Length > 0 ? $" (código {code})" : "") + ". " + LoginHints(),
        };
    }

    // O AD não diz o motivo nesse tipo de login: aponta as causas comuns, começando pelo relógio
    static string LoginHints()
    {
        var skew = ClockSkew();
        if (skew is { } s && Math.Abs(s.TotalMinutes) >= 5)
            return $"O RELÓGIO deste PC está {Math.Abs(s.TotalMinutes):0} minutos {(s.Ticks > 0 ? "adiantado" : "atrasado")} em relação ao servidor, " +
                   "e o domínio recusa o login por isso: acerte data/hora (Manutenção → Reparos rápidos → Sincronizar relógio, ou manualmente) e tente de novo.";
        return "Confira o nome da conta (o mesmo do login de rede, ex.: DOMINIO\\nome.sobrenome) e a senha; a conta também pode estar bloqueada ou com a senha expirada.";
    }

    static DateTime? ParseGeneralizedTime(string s) =>
        DateTime.TryParseExact(s, "yyyyMMddHHmmss.0'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) ? utc.ToLocalTime() : null;

    static string EscapeLdap(string s) =>
        s.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");

    /// <summary>
    /// Renomeia e coloca no domínio numa única reinicialização. Se o PC já está no domínio, só renomeia no AD.
    /// Precisa de administrador; a mudança vale depois de reiniciar.
    /// </summary>
    public static void RenameAndJoin(string newName, string user, string password)
    {
        var current = Current();
        int err;
        if (IsInDomain(current))
        {
            // Já no domínio: renomeia a conta do computador no AD e no Windows
            err = NetRenameMachineInDomain(null, newName, user, password, NetsetupAcctCreate);
        }
        else
        {
            // Fora do domínio: grava o nome novo (pendente) e ingressa já com ele, como o Add-Computer -NewName
            if (!newName.Equals(current.Name, StringComparison.OrdinalIgnoreCase) &&
                !SetComputerNameEx(ComputerNamePhysicalDnsHostname, newName))
                throw new InvalidOperationException("o Windows recusou o nome novo (" + new System.ComponentModel.Win32Exception().Message + ")");
            err = NetJoinDomain(null, DomainName, null, user, password, // conta criada no contêiner padrão do domínio
                NetsetupJoinDomain | NetsetupAcctCreate | NetsetupJoinWithNewName);
        }
        if (err != 0) throw new InvalidOperationException(Describe(err));
        Logger.Info($"Computador {current.Name} → {newName} no domínio {DomainName} (reinício pendente)");
    }

    static string Describe(int code) => code switch
    {
        5 => "acesso negado (abra o UtiliTech como administrador e use uma conta com permissão de ingressar máquinas)",
        53 or 1355 => $"domínio {DomainName} não encontrado (o PC está na rede da empresa? confira o DNS)",
        1326 => "usuário ou senha do domínio incorretos",
        1909 => "a conta do domínio está bloqueada",
        2224 or 2691 => "já existe um computador com este nome no domínio",
        2692 => "este computador não está no domínio",
        8557 => "esta conta atingiu o limite de máquinas que pode ingressar no domínio",
        1210 or 2351 => "nome de computador inválido",
        _ => $"o Windows devolveu o erro {code} ({new System.ComponentModel.Win32Exception(code).Message})",
    };

    /// <summary>Reinicia o PC em 10 segundos.</summary>
    public static void Restart() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 10 /c \"UtiliTech: reiniciando para aplicar o nome e o domínio\"")
        { UseShellExecute = false, CreateNoWindow = true });

    const int NetsetupJoinDomain = 0x1;
    const int NetsetupAcctCreate = 0x2;
    const int NetsetupJoinWithNewName = 0x400;
    const int ComputerNamePhysicalDnsHostname = 5;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern int NetJoinDomain(string? server, string domain, string? accountOu, string? account, string? password, int options);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern int NetRenameMachineInDomain(string? server, string newName, string? account, string? password, int options);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetComputerNameEx(int type, string name);
}
