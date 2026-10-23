namespace UtiliTech.Services;

/// <summary>Resultado de uma verificação mostrado na tela Nome e domínio (cor, título, texto).</summary>
public record DomainMessage(string Color, string Title, string Text);

/// <summary>O que a tela mostra sobre o PC e o domínio informado.</summary>
public record DomainScreen(List<InfoItem> Items, string Status, string CredHint, string ApplyText);

/// <summary>O que pedir na confirmação antes de renomear/ingressar.</summary>
public record DomainApply(string Title, List<string> Effects, string? Warning, string ConfirmText);

/// <summary>
/// Decisões da tela Nome e domínio, sem interface (para testar): textos conforme o PC está ou não no domínio,
/// conferências antes de consultar o AD, quando liberar o botão e o que vai acontecer ao aplicar.
/// </summary>
public static class DomainPlan
{
    public static DomainScreen Describe(MachineDomainInfo current, string domain, string windowsUser)
    {
        var items = new List<InfoItem>
        {
            new("Nome do computador", current.Name),
            new(current.PartOfDomain ? "Domínio" : "Grupo de trabalho", current.Domain),
        };
        if (domain.Length == 0)
            return new(items, "Informe abaixo o domínio da empresa para conferir nomes e ingressar.", "", "Renomear e adicionar ao domínio");

        items.Add(new("Usuário do Windows", windowsUser));
        bool inDomain = IsIn(current, domain);
        return new(items,
            inDomain
                ? $"Este PC já está no domínio {domain}: dá para renomear (o nome muda também no AD)."
                : $"Este PC não está no {domain}: renomeia e ingressa no domínio com uma única reinicialização.",
            inDomain
                ? "Conta com permissão para renomear computadores no AD. Também é usada para consultar o AD (se ficar vazia, a consulta usa a conta do Windows)."
                : "Conta com permissão para ingressar máquinas no domínio. Também é usada para consultar o AD, já que este PC ainda não está no domínio.",
            inDomain ? "Renomear no domínio" : $"Renomear e adicionar ao {domain}");
    }

    public static bool IsIn(MachineDomainInfo m, string domain) =>
        m.PartOfDomain && domain.Length > 0 && m.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase);

    /// <summary>Conferências antes de ir ao AD; null = pode consultar.</summary>
    public static DomainMessage? PreCheck(string name, MachineDomainInfo current, string domain)
    {
        if (DomainTools.Validate(name) is { } error) return new(Palette.Bad, error, "");
        if (name.Equals(current.Name, StringComparison.OrdinalIgnoreCase) && IsIn(current, domain))
            return new(Palette.Warn, "Este já é o nome deste computador.", "Nada a mudar.");
        return null;
    }

    /// <summary>Texto embaixo do campo do nome: o erro das regras ou "N de 15 caracteres".</summary>
    public static (string Text, bool IsError) NameHint(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return ("", false);
        return DomainTools.Validate(name) is { } error ? (error, true) : ($"{name.Length} de 15 caracteres", false);
    }

    /// <summary>O botão só libera para o nome que o AD confirmou como livre.</summary>
    public static (bool Enabled, string Hint) ApplyState(string currentName, string typedName, string? verifiedFree)
    {
        string name = typedName.Trim();
        bool verified = name.Length > 0 && string.Equals(name, verifiedFree, StringComparison.OrdinalIgnoreCase);
        return (verified, verified
            ? $"Pronto: {currentName} → {name}. A mudança vale depois de reiniciar."
            : "Primeiro verifique no AD se o novo nome está livre. O botão só libera para um nome confirmado como livre.");
    }

    public static DomainMessage Free(string name, string domain) =>
        new(Palette.Good, $"✓ {name} está livre no AD {domain}.", "Pode usar este nome.");

    public static DomainMessage InUse(string name, string domain) =>
        new(Palette.Bad, $"⚠ {name} JÁ ESTÁ EM USO no AD {domain}.",
            "Escolha outro nome. Se for este mesmo computador sendo reformatado, peça a exclusão do objeto antigo no AD antes.");

    public static DomainMessage QueryFailed(string error, bool hasUser, bool pcInDomain) =>
        new(Palette.Bad, "Não foi possível consultar o AD.",
            error + (!hasUser && !pcInDomain ? " · Este PC não está no domínio: informe usuário e senha no passo 2." : ""));

    /// <summary>Confirmação de renomear (e ingressar, se ainda não estiver no domínio).</summary>
    public static DomainApply Apply(MachineDomainInfo current, string newName, string domain, string user)
    {
        bool inDomain = IsIn(current, domain);
        var effects = new List<string> { $"O nome do computador muda de {current.Name} para {newName}" };
        effects.Add(inDomain
            ? $"A conta do computador é renomeada no AD (com a conta {user})"
            : $"O PC entra no domínio {domain} (conta criada no AD com a conta {user})");
        effects.Add("O PC precisa reiniciar para concluir");
        return new(
            (inDomain ? $"Renomear {current.Name} para {newName} no domínio" : $"Renomear {current.Name} para {newName} e adicionar ao domínio {domain}") + "?",
            effects,
            inDomain ? null : "Depois de reiniciar, o login passa a ser com usuário do domínio. Confira se há uma conta local de administrador com senha conhecida.",
            inDomain ? "Renomear" : "Renomear e ingressar");
    }

    public static string Done(string newName, bool wasInDomain, string domain) =>
        $"✓ Feito: o computador vai se chamar {newName}" + (wasInDomain ? "" : $" e já está no domínio {domain}") + ". Reinicie o PC para concluir.";
}
