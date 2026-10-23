# UtiliTech

Ferramenta de bancada para técnico de informática (WPF .NET 8), inspirada no
[ResetExplode](https://github.com/GustavoReset/ResetExplode). Roda como administrador.

## Módulos

**Visão geral** (tela inicial): gráfico de temperatura/carga/consumo da CPU e da GPU (1, 5 ou 15 min), resumo do PC (modelo, nº de série, Windows), tabela de componentes (temperatura, carga, clock, consumo) com atalho para cada tela, memória e disco, eventos recentes e saúde do PC (discos, bateria, drivers).

**Atendimentos**: histórico guardado no pendrive. Ao exportar o relatório ou a planilha na tela Sistema, o "Salvar como" já abre na pasta do atendimento, dentro de `atendimentos` ao lado do `.exe` (se salvar em outro lugar, uma cópia vai para lá) (uma pasta por atendimento, com os dados em `atendimento.json`). A tela lista os atendimentos do mais novo para o mais antigo, com busca por cliente, OS, nº de série, nome do PC, modelo, técnico ou data (sem diferenciar acentos), e abre o relatório, a planilha ou a pasta com 1 clique. O nº de série identifica a máquina mesmo depois de formatada e renomeada. **Inventário**: a planilha `atendimentos\inventario.xlsx` é refeita sozinha a cada atendimento guardado, com uma linha por máquina (modelo, processador, memória, discos, Windows, MAC e IP, saúde, datas e clientes) e uma aba com todos os atendimentos; o botão "Inventário (planilha)" atualiza e abre.

**Diagnóstico**
- **Sistema**: fabricante, modelo, número de série, BIOS, placa-mãe, Windows (versão, ativação, chave OEM da BIOS, último boot), CPU, RAM por módulo, vídeo, discos, unidades e rede. Campos de atendimento (cliente, OS, técnico, observações) e **Exportar relatório** em HTML para entregar ao cliente (abre no navegador; imprima ou salve em PDF). **Exportar planilha** gera um Excel (.xlsx) com as abas Resumo, Sistema, Discos e Bateria, ou um CSV (separado por `;`), bom para controle de atendimentos e inventário de máquinas. O relatório e a planilha trazem também os **serviços feitos nesta visita** (limpeza, reparos, drivers, programas, nome e domínio, pontos de restauração, com hora e se deu certo) e o **antes e depois** do PC (espaço livre no disco do Windows e dispositivos com problema, de quando o app abriu até agora).
- **Monitor em tempo real**: cartões de CPU, GPU, RAM e discos (temperatura, uso, clock, consumo, VRAM) com gráfico dos últimos 60 s, e a lista completa de sensores de cada componente com atual/mínimo/máximo, atualizada a cada 1 s (estilo HWMonitor). Usa a [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0). Temperatura da CPU e da placa-mãe usam o driver **PawnIO**, que vai embutido no .exe e é carregado só enquanto o app está aberto (como o AIDA64): nada fica instalado no PC do cliente. Detalhes e licença em `Assets/Drivers/LEIA-ME.txt`.
- **Discos (S.M.A.R.T.)**: veredito Bom / Atenção / Ruim por disco físico com os motivos: status do Windows, temperatura, horas ligado, desgaste do SSD e erros não corrigidos. Previsão de falha do driver e espaço livre de cada unidade.
- **Bateria**: saúde (capacidade atual × de projeto), ciclos, tensão, estado e autonomia, com veredito. Botão para o relatório completo do `powercfg /batteryreport`.
- **Memória RAM**: módulos instalados; teste rápido dentro do Windows (6 padrões, várias passadas, multi-thread) e atalho para o Diagnóstico de Memória do Windows no boot, com leitura do último resultado no log.
- **Rede**: **Testar conexão** confere, nessa ordem, IP da placa, gateway, DNS e internet (pelo proxy da empresa, como o navegador) e diz em português onde está o problema e o que fazer (ex.: IP 169.254 = sem DHCP → Renovar IP; só o DNS falhando → Limpar cache DNS; internet bloqueada → proxy/firewall). Mostra as placas conectadas (IP, gateway, DNS, MAC, velocidade; avisa cabo abaixo de 1 Gbps), o Wi-Fi (rede, sinal, canal), ping, rastrear rota e resolver nome, e as unidades de rede mapeadas com o teste de acesso; mapeia unidade nova (`\\servidor\pasta`, aparece no Explorer depois de sair e entrar no Windows).
- **Segurança**: antivírus (Microsoft Defender ou outro registrado no Windows, proteção em tempo real e idade das assinaturas), firewall nos perfis Domínio/Particular/Público, BitLocker de cada unidade, Secure Boot e UEFI, TPM (versão e se está pronto), UAC e a última atualização do Windows instalada, cada um com OK / Atenção / Problema e o que fazer. Ações: atualizar assinaturas do Defender, verificação rápida, ligar o firewall de um perfil desligado e mostrar a **chave de recuperação do BitLocker** (só na tela, depois de confirmar; não vai para o registro nem para o relatório).
- **Eventos**: lê o log Sistema dos últimos 7, 30 ou 90 dias e explica em português: **telas azuis** (código traduzido, ex.: 0xD1 = driver com defeito, 0x124 = hardware, e os minidumps), **desligamentos inesperados** (queda de energia/travamento, com a hora em que o PC parou), **erros de disco** e do sistema de arquivos (NTFS), **driver de vídeo que travou** e **erros de hardware (WHEA)**. Um mesmo travamento (tela azul + Kernel-Power 41 + 6008) conta uma vez. Cada tipo diz o que fazer e tem atalho para Drivers, Discos, Memória RAM ou Monitor.

**Pós-formatação**
- **Checklist**: o que falta para entregar o PC, conferido sozinho toda vez que a tela abre: Windows ativado, drivers sem problema, Google Chrome, Mozilla Firefox e Microsoft 365 instalados (Office 2016/2019/2021/LTSC não conta como 365, e o checklist avisa qual Office encontrou), nome definitivo (não o `DESKTOP-...` da instalação), domínio, políticas de grupo aplicadas, reinício pendente, saúde dos discos e relatório guardado no histórico. Cada etapa que falta tem o botão que leva à ferramenta certa.
- **Instalar apps**: a lista de programas do técnico, instalados pelo winget em silêncio e um por vez. Detecta o que já está instalado. A lista é editável na própria tela: busca no winget e **Adicionar**, **×** para tirar, **Restaurar lista padrão**. Fica salva em `apps.json` ao lado do .exe (vai junto no pendrive) ou, se a pasta não aceitar escrita, em `%AppData%\UtiliTech`. Lista padrão: Chrome, Firefox, 7-Zip, WinRAR, Acrobat Reader, LibreOffice, VLC, AnyDesk, TeamViewer, Zoom, NAPS2, runtimes (Java, VC++, .NET), Notepad++ e CrystalDiskInfo, mais o instalador do PJe Office Pro do pendrive (pasta `Instaladores`). Os instaladores das impressoras Lexmark ficam em Manutenção → Impressoras.
- **Inicialização**: lista os programas que abrem com o Windows (registro do usuário, de todos os usuários, 32 bits e pastas Inicializar) e ativa/desativa igual ao Gerenciador de Tarefas.
- **Nome e domínio**: confere no AD do domínio informado na tela (catálogo global) se o nome novo já existe — se existir, alerta com a OU, criação e último logon do objeto e bloqueia; se estiver livre, renomeia e ingressa no domínio com uma única reinicialização (ou só renomeia, se o PC já estiver no domínio). Valida as regras do nome (até 15 caracteres) e lembra o usuário usado (a senha nunca é salva). Se o login for recusado, confere se o relógio do PC está diferente do servidor (causa comum em PC recém-formatado).

**Manutenção**
- **Reparos rápidos**: atualizar políticas de grupo (gpupdate /force), fila de impressão, DNS, IP, rede, Windows Update, relógio e Explorer, com 1 clique. Os que mexem no PC mostram antes o que vai acontecer; resetar rede e Windows Update oferecem criar um ponto de restauração. **Modo simulação**: registra os comandos e arquivos que seriam mexidos, sem executar nada.
- **Limpeza**: mostra quanto cada categoria ocupa e apaga só o que for marcado. **Modo simulação**: gera a lista dos arquivos que seriam apagados (pasta `logs`), sem apagar nada.
- **Usuários e perfis**: perfis do PC com dono, último uso, tamanho e marcas (você, em uso, conta apagada, parado há mais de 90 dias). **Apagar perfil** como o Windows faz (pasta e registro juntos, com confirmação; o perfil em uso não pode). **Copiar dados de um perfil para outro**: Área de Trabalho, Documentos, Imagens, Downloads, Vídeos, Música e Favoritos (pega do OneDrive se as pastas estiverem lá) e os favoritos do Chrome e do Edge, com robocopy, sem apagar nada no destino. Lista também as contas locais (ativa ou não, administrador).
- **Drivers**: lista os dispositivos com problema (sem driver, código 28, 43...), procura no Windows Update os drivers que faltam ou estão desatualizados e instala os marcados, e instala pacotes de driver de uma pasta (ex.: `InstaladoresDrivers` no pendrive), sem internet. Antes de instalar, oferece criar um ponto de restauração. **Placa de vídeo**: NVIDIA consulta o driver mais novo no site da NVIDIA, baixa, confere a assinatura digital e instala em silêncio; AMD abre a detecção automática do site da AMD; Intel instala o Intel Driver & Support Assistant.
- **Impressoras**: lista as impressoras com driver, IP (ou servidor) e se ele responde, documentos na fila, padrão, offline e as cópias que costumam ser fila fantasma ("Cópia 1"). Por impressora: página de teste, definir como padrão, limpar a fila, abrir a fila do Windows e remover. **Instalar pelo IP** (cria a porta TCP/IP e a impressora com um driver já instalado; "Microsoft IPP Class Driver" serve para a maioria das impressoras de rede) e **conectar impressora compartilhada** (`\\servidor\impressora`). **Lexmark do pendrive**: botões para o assistente da Lexmark do **MX620 (preto e branco, driver XL)** e do **CS725 (colorida, driver PS)**, a partir de `Instaladores\LM PB W10.exe` e `Instaladores\LM COLOR W10.exe`; depois da primeira vez, esses drivers aparecem no topo de Instalar pelo IP. **Testar IP e achar driver** pergunta o modelo à impressora (SNMP, só leitura) e já escolhe o driver: Brother DCP-L2540DW e Samsung SCX-483x/5x3x e SCX-5835/5935X usam o **Microsoft PCL6 Class Driver**, que vem guardado no Windows e é ativado na hora (não baixa nada); o PS Class Driver fica como alternativa. **Procurar impressoras na rede (exceto Lexmark)**: testa a porta 9100 em toda a rede da placa conectada (até um bloco /22), pergunta modelo, nome e nº de série a cada impressora (SNMP, só leitura) e lista as que não são Lexmark, com o driver indicado e se já estão instaladas no PC; "Usar para instalar" preenche o Instalar pelo IP.

## Requisitos

- Windows 10/11
- .NET 8 SDK para compilar: `winget install Microsoft.DotNet.SDK.8`

## Como compilar e rodar

```
cd UtiliTech
dotnet build
```

Depois abra `bin\Debug\net8.0-windows\UtiliTech.exe` (ele pede administrador).

## Testes

```
dotnet test UtiliTech.Tests
```

Os testes (xUnit) cobrem a lógica que não depende do PC: veredito dos discos e da bateria, regras do nome do computador, versão e SHA-256 das releases, planilha e limpeza (numa pasta temporária). Não precisam de administrador nem de internet. O GitHub roda os testes antes de publicar cada release.

## Usar no pendrive

1. Dê dois cliques em `publicar.bat`. Ele gera `..\UtiliTech-dist\UtiliTech.exe` (~70 MB), um arquivo único com o .NET embutido.
2. Copie o `UtiliTech.exe` para o pendrive (de preferência numa pasta, ex.: `E:\UtiliTech\`).
3. No PC do cliente, dê dois cliques no `.exe` e aceite o pedido de administrador. Não precisa instalar nada (Windows 10/11 64 bits).

A lista de programas de **Instalar apps** fica salva em `apps.json`, na mesma pasta do `.exe`, então ela anda junto com o pendrive. Sempre que alterar o código, rode o `publicar.bat` de novo e recopie o `.exe`.

## Atualização automática (GitHub Releases)

1. Crie um repositório no GitHub (ex.: `seu-usuario/UtiliTech`) e envie esta pasta para ele. O workflow `.github/workflows/publicar.yml` já vem pronto.
2. Para lançar uma versão, crie uma tag e envie: `git tag v0.2.0` e `git push --tags`. O GitHub compila o `UtiliTech.exe` e publica a release sozinho.
3. No app, em **Configurações → Atualizações**, informe `seu-usuario/UtiliTech`. Ao abrir, o UtiliTech avisa quando houver versão nova e instala com 1 clique (baixa, confere o SHA-256 com o publicado na release, troca o `.exe` e reabre). Se o hash não bater, nada é trocado.

O repositório precisa ser **público** para o app conseguir ler as releases sem senha.

## Configurações e registros

- `settings.json` (ao lado do `.exe`): tema, alertas e repositório de atualização.
- `logs\utilitech-AAAAMMDD.log`: erros do app, últimos 10 dias. Abra por **Configurações → Registro de erros**.

## Estrutura

- `Services/`: a lógica (WMI, sensores, winget, testes, reparos, limpeza, atualização, relatório), sem dependência da interface.
- `Views/`: uma tela por módulo.
- `Theme.xaml`: cores do Discord (fundos cinza-azulados e destaque blurple #5865F2), layout inspirado no ResetExplode com barra de título própria e ícones Segoe Fluent no menu; o tema claro troca os pincéis na hora (`Views/ThemeManager.cs`).
