using System.DirectoryServices;
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
    /// Procura o computador no AD. Sem usuário/senha, usa a conta do Windows (funciona em PC já no domínio).
    /// Devolve null se o nome está livre.
    /// </summary>
    public static AdComputer? FindComputer(string name, string? user, string? password)
    {
        string filter = $"(&(objectCategory=computer)(sAMAccountName={EscapeLdap(name)}$))";
        string[] props = { "cn", "distinguishedName", "whenCreated", "lastLogonTimestamp", "operatingSystem", "description" };
        var auth = AuthenticationTypes.Secure | AuthenticationTypes.Sealing | AuthenticationTypes.Signing;

        // Catálogo global = a floresta inteira; se a porta do GC estiver fechada, tenta o LDAP do domínio
        Exception? last = null;
        foreach (var root in new[] { $"GC://{DomainName}", $"LDAP://{DomainName}" })
        {
            try
            {
                using var entry = user == null ? new DirectoryEntry(root) { AuthenticationType = auth } : new DirectoryEntry(root, user, password, auth);
                using var searcher = new DirectorySearcher(entry, filter, props) { SearchScope = SearchScope.Subtree, SizeLimit = 1 };
                var r = searcher.FindOne();
                if (r == null) return null;
                return new AdComputer(
                    Prop(r, "cn"), Prop(r, "distinguishedName"),
                    r.Properties["whenCreated"].Count > 0 ? ((DateTime)r.Properties["whenCreated"][0]).ToLocalTime() : null,
                    r.Properties["lastLogonTimestamp"].Count > 0 && (long)r.Properties["lastLogonTimestamp"][0] > 0
                        ? DateTime.FromFileTime((long)r.Properties["lastLogonTimestamp"][0]) : null,
                    Prop(r, "operatingSystem"), Prop(r, "description"));
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x8007052E))
            {
                throw new InvalidOperationException("usuário ou senha do domínio incorretos");
            }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("não foi possível consultar o AD: " + last?.Message, last);
    }

    static string Prop(System.DirectoryServices.SearchResult r, string name) =>
        r.Properties[name].Count > 0 ? r.Properties[name][0]?.ToString() ?? "" : "";

    static string EscapeLdap(string s) =>
        s.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");

    /// <summary>
    /// Renomeia e coloca no domínio numa única reinicialização. Se o PC já está no domínio, só renomeia no AD.
    /// Precisa de administrador; a mudança vale depois de reiniciar.
    /// </summary>
    public static void RenameAndJoin(string newName, string user, string password, string? ou)
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
            err = NetJoinDomain(null, DomainName, string.IsNullOrWhiteSpace(ou) ? null : ou.Trim(), user, password,
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
