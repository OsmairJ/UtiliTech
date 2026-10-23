using System.Globalization;
using System.Text;

namespace UtiliTech.Services;

/// <summary>Um item da busca rápida (Ctrl+K): uma tela ou algo que existe dentro dela.</summary>
public record SearchItem(string Title, string Where, string Page, string Glyph, string Keywords = "")
{
    /// <summary>Tela inteira (não uma ação dentro dela).</summary>
    public bool IsPage => Where.Length == 0;
    public string Subtitle => IsPage ? "Tela" : "em " + Where;
}

/// <summary>
/// Busca rápida do menu: acha telas e ferramentas pelo nome ou por sinônimo ("bsod", "gpupdate", "chave bitlocker"),
/// sem diferenciar acentos e maiúsculas. Todas as palavras digitadas precisam aparecer.
/// </summary>
public static class CommandSearch
{
    public static readonly IReadOnlyList<SearchItem> Items = new SearchItem[]
    {
        // Telas (o Glyph é o mesmo ícone do menu)
        new("Visão geral", "", "overview", "", "inicio home resumo painel dashboard temperatura cpu gpu"),
        new("Atendimentos", "", "attendance", "", "historico cliente os ordem de servico atendimento"),
        new("Sistema", "", "system", "", "informacoes hardware modelo fabricante bios placa mae windows ativacao chave oem"),
        new("Monitor", "", "monitor", "", "sensores temperatura clock consumo hwmonitor tempo real"),
        new("Discos (S.M.A.R.T.)", "", "disks", "", "smart saude hd ssd desgaste horas ligado"),
        new("Velocidade do disco", "", "diskspeed", "", "crystaldiskmark benchmark leitura gravacao 4k ssd lento"),
        new("Bateria", "", "battery", "", "saude ciclos notebook autonomia powercfg"),
        new("Memória RAM", "", "memory", "", "ram memoria teste modulos pente memtest"),
        new("Teste de estresse", "", "stress", "", "stress cpu 100% superaquecimento throttling"),
        new("Rede", "", "network", "", "internet conexao ip dns gateway wifi wi-fi cabo"),
        new("Segurança", "", "security", "", "antivirus defender firewall bitlocker tpm secure boot uac"),
        new("Eventos", "", "events", "", "log visualizador de eventos tela azul bsod desligamento travamento"),
        new("Alertas", "", "alerts", "", "aviso temperatura limite"),
        new("Checklist", "", "checklist", "", "pos formatacao pronto entregar o que falta"),
        new("Instalar apps", "", "install", "", "programas winget chrome firefox office pje instalar"),
        new("Inicialização", "", "startup", "", "programas que abrem com o windows startup lentidao"),
        new("Nome e domínio", "", "domain", "", "renomear computador nome do pc ingressar dominio active directory ad"),
        new("Reparos rápidos", "", "repairs", "", "consertar corrigir reparo"),
        new("Limpeza", "", "cleanup", "", "temporarios temp espaco liberar lixeira cache"),
        new("Usuários e perfis", "", "profiles", "", "usuario perfil conta local administrador"),
        new("Drivers", "", "drivers", "", "driver gerenciador de dispositivos windows update dispositivo desconhecido"),
        new("Impressoras", "", "printers", "", "impressora imprimir fila spooler"),
        new("Configurações", "", "settings", "", "opcoes ajustes preferencias"),

        // Ferramentas dentro das telas
        new("Exportar relatório para o cliente", "Sistema", "system", "", "relatorio html pdf imprimir"),
        new("Exportar planilha", "Sistema", "system", "", "excel xlsx csv inventario"),
        new("Número de série do PC", "Sistema", "system", "", "serial service tag numero de serie"),
        new("Inventário das máquinas (planilha)", "Atendimentos", "attendance", "", "inventario xlsx maquinas lote"),
        new("Testar conexão (IP, DNS, internet)", "Rede", "network", "", "sem internet nao conecta diagnostico"),
        new("Ping", "Rede", "network", "", "ping latencia"),
        new("Rastrear rota", "Rede", "network", "", "tracert traceroute"),
        new("Mapear unidade de rede", "Rede", "network", "", "net use pasta compartilhada unidade z"),
        new("Mostrar chave de recuperação do BitLocker", "Segurança", "security", "", "chave recuperacao bitlocker 48 digitos"),
        new("Atualizar assinaturas do Defender", "Segurança", "security", "", "antivirus atualizar vacina"),
        new("Verificação rápida de vírus", "Segurança", "security", "", "scan virus defender"),
        new("Telas azuis (BSOD)", "Eventos", "events", "", "bsod tela azul bugcheck minidump"),
        new("Desligamentos inesperados", "Eventos", "events", "", "desligou sozinho queda de energia kernel-power 41"),
        new("Erros de disco no log", "Eventos", "events", "", "bloco defeituoso ntfs chkdsk"),
        new("Destravar fila de impressão", "Reparos rápidos", "repairs", "", "spooler impressao travada documento preso"),
        new("Atualizar políticas de grupo (gpupdate)", "Reparos rápidos", "repairs", "", "gpupdate gpo politica dominio"),
        new("Limpar cache DNS", "Reparos rápidos", "repairs", "", "flushdns site nao abre"),
        new("Renovar IP", "Reparos rápidos", "repairs", "", "ipconfig release renew dhcp 169.254"),
        new("Resetar rede (Winsock/TCP-IP)", "Reparos rápidos", "repairs", "", "netsh winsock reset rede"),
        new("Resetar Windows Update", "Reparos rápidos", "repairs", "", "windows update travado softwaredistribution"),
        new("Sincronizar relógio", "Reparos rápidos", "repairs", "", "hora errada certificado w32tm data"),
        new("Reiniciar o Explorer", "Reparos rápidos", "repairs", "", "barra de tarefas travada icones sumiram"),
        new("Apagar perfil antigo", "Usuários e perfis", "profiles", "", "perfil antigo excluir usuario espaco"),
        new("Copiar dados de um perfil para outro", "Usuários e perfis", "profiles", "", "migrar perfil area de trabalho documentos favoritos backup"),
        new("Instalar impressora pelo IP", "Impressoras", "printers", "", "adicionar impressora ip porta tcp"),
        new("Lexmark do pendrive (MX620 e CS725)", "Impressoras", "printers", "", "lexmark mx620 mx622 cs725 driver xl ps"),
        new("Procurar impressoras na rede", "Impressoras", "printers", "", "scan buscar impressora brother samsung"),
        new("Conectar impressora compartilhada", "Impressoras", "printers", "", "\\\\servidor impressora compartilhada"),
        new("Procurar drivers no Windows Update", "Drivers", "drivers", "", "driver faltando atualizar"),
        new("Driver da placa de vídeo", "Drivers", "drivers", "", "nvidia amd intel video gpu"),
        new("Instalar drivers de uma pasta", "Drivers", "drivers", "", "pnputil inf offline pendrive"),
        new("Ponto de restauração", "Configurações", "settings", "", "restauracao sistema restore point voltar"),
        new("Atualizar o UtiliTech", "Configurações", "settings", "", "versao nova github atualizacao"),
        new("Tema claro / escuro", "Configurações", "settings", "", "tema cores escuro claro"),
        new("Registro de erros do app", "Configurações", "settings", "", "log erro"),
    };

    /// <summary>Sem acento e em minúsculas, para comparar ("Segurança" → "seguranca").</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Resultados em ordem de relevância: título que começa com o texto, depois palavra do título que começa com ele,
    /// depois título que contém, depois só sinônimo. Vazio = todas as telas.
    /// </summary>
    public static List<SearchItem> Find(string query, int max = 12)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Items.Where(i => i.IsPage).Take(max).ToList();

        string q = string.Join(' ', words);
        return Items
            .Select(i =>
            {
                string title = Normalize(i.Title), all = title + " " + Normalize(i.Where) + " " + Normalize(i.Keywords);
                if (!words.All(w => all.Contains(w))) return (Item: i, Score: -1);
                int score = title.StartsWith(q) ? 400
                    : title.Split(' ', '(', ')', '/', '-').Any(t => t.StartsWith(words[0])) ? 300
                    : title.Contains(q) ? 200
                    : 100;
                // Pelo nome, a tela vem antes; só pelo sinônimo, a ação específica vem antes da tela genérica
                return (Item: i, Score: score + (score > 100 ? (i.IsPage ? 10 : 0) : (i.IsPage ? 0 : 5)));
            })
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Item.Title.Length)
            .Take(max)
            .Select(x => x.Item)
            .ToList();
    }
}
