# Novidades do UtiliTech

Uma seção por versão, da mais nova para a mais antiga. O app mostra as seções novas na primeira vez que abre depois de atualizar, e a release do GitHub usa a seção da versão como texto.

## v0.6.26 — notas da versão ao abrir depois de atualizar
- Na primeira vez que o UtiliTech abre depois de atualizar, mostra o que mudou (inclusive das versões puladas)
- "Ver novidades" em Configurações e "novidades" na busca Ctrl+K
- As notas vão dentro do .exe: funcionam no pendrive mesmo sem internet
- A release do GitHub passa a mostrar estas notas, e o aviso de versão nova em Configurações também

## v0.6.25 — Telas pequenas e atalhos de teclado
- Janela cabe na área útil (notebook 1366×768 abre maximizado; mínimos nunca maiores que a tela)
- Menu lateral mais fino em janela estreita; cartões quebram colunas (Reparos, Instalar apps, Velocidade do disco, Estresse) e empilham na Visão geral (Responsive.MinItemWidth/StackBelow)
- Atalhos: Ctrl+1..9 e Ctrl+, abrem telas, F5 atualiza a tela aberta (IRefreshable em 14 telas), F1 mostra a lista (quadro novo em Configurações), atalho no tooltip do menu

## v0.6.24 — "Problemas encontrados" no topo da Visão geral
- Junta o que as ferramentas acham: disco com falha/atenção, pouco espaço no disco do Windows, drivers com problema, vídeo sem driver do fabricante, bateria gasta, antivírus/firewall/assinaturas/atualizações, eventos dos últimos 30 dias (telas azuis, erros de disco, desligamentos repetidos, vídeo, WHEA), Windows não ativado e reinício pendente
- Graves primeiro, cada um com o botão que leva à tela que resolve; "Verificar de novo"

## v0.6.23 — Busca rápida no menu (Ctrl+K)
- Ctrl+K em qualquer tela (ou o botão Buscar na barra de título) abre a busca
- Acha as 23 telas e as ferramentas dentro delas (reparos, chave do BitLocker, mapear unidade, instalar impressora pelo IP, Lexmark, tela azul...) pelo nome ou por sinônimo, sem acentos
- Setas escolhem, Enter abre a tela, Esc ou clicar fora fecha

## v0.6.22 — Teste de velocidade do disco (Diagnóstico → Velocidade do disco)
- Leitura e gravação sequencial (1 MiB) e aleatória 4K (QD1) sem o cache do Windows (FILE_FLAG_NO_BUFFERING + buffer alinhado), arquivo temporário com dados aleatórios
- Tamanho 256 MB / 1 GB / 4 GB, exige 1 GB de folga, botão Parar; o arquivo é apagado no fim mesmo se parar ou falhar
- Veredito: SSD NVMe, SSD SATA, HD mecânico (sugere trocar por SSD), SSD lento ou muito lento; resultado vai para o relatório e para os serviços da visita

## v0.6.21 — Eventos simplificados (Diagnóstico → Eventos)
- Lê o log Sistema (7/30/90 dias) filtrando por origem e número: telas azuis (WER 1001 e Kernel-Power 41 com código), desligamentos inesperados (41 sem código e EventLog 6008, com a hora em que o PC parou), erros de disco (disk 7/11/51/153) e NTFS (só quando é erro ou aviso), driver de vídeo (Display 4101) e WHEA
- Códigos de tela azul comuns explicados em português; minidumps do período
- Um mesmo travamento conta uma vez; resumo por tipo com o que fazer e atalho para a ferramenta

## v0.6.20 — Usuários e perfis (Manutenção → Usuários e perfis)
- Perfis com dono, último uso (registro ProfileList e NTUSER.DAT para quem não está logado), tamanho e marcas: você, em uso, conta apagada, parado há 90+ dias
- Apagar perfil pelo Windows (Win32_UserProfile.Delete: pasta + registro), com confirmação; bloqueado para o perfil em uso e o do técnico
- Copiar dados entre perfis: Área de Trabalho, Documentos, Imagens, Downloads, Vídeos, Música e Favoritos (do OneDrive quando estão lá) com robocopy sem apagar nada no destino, e os favoritos do Chrome/Edge quando o destino ainda não tem
- Contas locais: ativa/desativada e administrador

## v0.6.19 — Segurança do Windows (Diagnóstico → Segurança)
- Antivírus (Defender ou outro registrado na Central de Segurança, tempo real, idade das assinaturas), firewall por perfil, BitLocker por unidade, Secure Boot/UEFI, TPM, UAC e última atualização instalada, cada um com veredito (OK / Atenção / Problema) e o que fazer
- Ações: atualizar assinaturas e verificação rápida do Defender (MpCmdRun), ligar firewall (netsh, com confirmação), abrir a Segurança do Windows
- Chave de recuperação do BitLocker só depois de confirmar, mostrada na tela e nunca registrada

## v0.6.18 — Procurar impressoras na rede (exceto Lexmark)
- Impressoras → "Procurar impressoras na rede (exceto Lexmark)": porta 9100 em toda a rede da placa conectada (rede maior que /22 é limitada ao bloco /22 do PC), em lotes e com tempo curto
- Modelo, nome e nº de série por SNMP (só leitura); lista as que não são Lexmark com o driver indicado e se já estão instaladas no PC; botão Parar
- "Usar para instalar" preenche IP, nome e driver no Instalar pelo IP

## v0.6.17 — Lexmark sai de Instalar apps
- Os instaladores da Lexmark (LM COLOR W10.exe e LM PB W10.exe) saem da lista padrão de Instalar apps: ficam só em Manutenção → Impressoras → Lexmark do pendrive
- README atualizado; teste garante que não voltam para a lista

## v0.6.16 — Drivers para Brother e Samsung e identificação do modelo pelo IP
- Instalar pelo IP lista o Microsoft PCL6 Class Driver (Brother DCP-L2540DW, Samsung SCX-483x/5x3x e SCX-5835/5935X) e o PS Class Driver: vêm guardados no Windows e são ativados na hora
- "Testar IP e achar driver" pergunta o modelo à impressora (SNMP v1, só leitura) e escolhe o driver; Lexmark MX620/CS725 sem driver instalado aponta o assistente certo do pendrive
- Lista mostra os modelos que cada driver atende

## v0.6.15 — Lexmark MX620 (XL) e CS725 (PS) na tela de Impressoras
- Quadro "Lexmark do pendrive": abre o assistente da Lexmark do MX620 (preto e branco, driver XL) e do CS725 (colorida, driver PS) a partir da pasta Instaladores, e registra a impressora criada
- Instalar pelo IP lista primeiro os drivers desses modelos (e os da Lexmark), para as próximas instalações do mesmo modelo saírem direto pelo IP
- Botões "Padrão" e "Limpar fila" só aparecem quando fazem sentido

## v0.6.14 — Ferramenta de Impressoras e mapear unidade de rede
- Manutenção → Impressoras: lista com driver, IP/servidor (e se responde), fila, padrão, offline e cópias fantasmas ("Cópia 1"); página de teste, padrão, limpar fila, abrir fila e remover, com confirmação nas ações sem volta
- Instalar impressora pelo IP (porta TCP/IP + impressora, driver escolhido da lista) e conectar impressora compartilhada (\servidor\impressora); entradas validadas antes do PowerShell
- Tela Rede: mapear unidade de rede (net use persistente) com as letras livres
- Ações de impressora e rede entram nos serviços feitos da visita

## v0.6.13 — Ferramenta de Rede (Diagnóstico → Rede)
- Testar conexão: IP → gateway → DNS → internet (pelo proxy, como o navegador), com o diagnóstico em português e o que fazer (DHCP, DNS, proxy/firewall, roteador sem ping)
- Placas conectadas: IP, gateway, DNS, MAC e velocidade do link (avisa cabo abaixo de 1 Gbps)
- Wi-Fi: rede, sinal, banda e canal (netsh, em português ou inglês)
- Ping, rastrear rota e resolver nome; endereço digitado é validado antes de ir para o tracert
- Unidades de rede mapeadas do usuário e se estão acessíveis

## v0.6.12 — Inventário em lote (atendimentos\inventario.xlsx)
- Planilha única no pendrive, refeita sozinha a cada atendimento guardado ou pelo botão "Inventário (planilha)" na tela Atendimentos
- Aba Máquinas: uma linha por máquina (nº de série ou nome), com os dados mais novos de cada campo; aba Atendimentos: um por linha
- Atendimento passa a guardar processador, memória, discos físicos e MAC/IP
- Planilha aberta no Excel não derruba nada: avisa e mantém a versão anterior

## v0.6.11 — Relatório "antes e depois" com os serviços feitos na visita e formato brasileiro fixo
- Testes fixam pt-BR: no GitHub (Windows em inglês) "22,1 GB" saía "22.1 GB" e a release v0.6.10 foi barrada pelos testes, como deveria
- O app também fixa pt-BR ao abrir: números e datas no formato brasileiro mesmo num Windows em inglês
- Inclui tudo da v0.6.10 (serviços feitos na visita e antes e depois no relatório, planilha e histórico)

## v0.6.9 — Checklist confere só Chrome, Firefox e Microsoft 365
- Etapa Programas do checklist deixa de usar a lista de Instalar apps e exige Google Chrome, Mozilla Firefox e Microsoft 365
- Office 2016/2019/2021/LTSC não conta como 365; o checklist mostra qual Office encontrou
- Testes com os nomes reais de Aplicativos instalados (116 no total)

## v0.6.8 — Checklist de pós-formatação
- Tela Pós-formatação → Checklist: Windows ativado, drivers, programas da lista, nome definitivo (não o DESKTOP-... da instalação), domínio, políticas de grupo, reinício pendente, discos e relatório guardado no histórico, tudo conferido sozinho ao abrir a tela
- Cada etapa que falta tem o botão para a ferramenta certa; contador "X de Y" e barra de progresso
- Avaliação separada da leitura do PC (PostFormatChecklist.Evaluate) com

## v0.6.7 — "Salvar como" do relatório e da planilha abre na pasta do atendimento
- O diálogo já abre em atendimentos\AAAA-MM-DD_HHmm_NOMEDOPC (a do atendimento do dia, se já existir)
- Salvo direto ali, o arquivo não é duplicado; salvo em outro lugar, vai uma cópia para o histórico
- Cancelar não deixa pasta vazia para trás

## v0.6.6 — Histórico de atendimentos no pendrive, com busca
- Ao exportar relatório ou planilha na tela Sistema, uma cópia vai para a pasta atendimentos ao lado do .exe (uma pasta por atendimento, com atendimento.json)
- Relatório e planilha do mesmo PC, cliente e OS no mesmo dia ficam juntos; o nº de série identifica a máquina mesmo depois de renomeada
- Tela Atendimentos no menu: lista do mais novo ao mais antigo, busca sem diferenciar acentos (cliente, OS, nº de série, nome do PC, modelo, técnico, data) e botões para relatório, planilha e pasta

## v0.6.5 — Testes automáticos (UtiliTech.Tests) rodando antes de cada release
- Projeto xUnit com 67 testes: veredito S.M.A.R.T. e bateria, nome do PC e usuário do domínio, versão das tags e SHA-256, planilha .xlsx/.csv e limpeza/simulação em pasta temporária
- Veredito do disco e da bateria separado da leitura WMI (DiskHealth.Judge, BatteryInfo.Judge)
- Leitura da tag e do digest do GitHub em funções próprias (Updater.ParseTag, ParseDigest)
- Workflow roda os testes antes de compilar; se falharem, a release não sai

## v0.6.4 — Ponto de restauração, atualização conferida por SHA-256, confirmações claras e modo simulação
- Ponto de restauração antes de resetar rede/Windows Update e instalar drivers (oferece ligar a Proteção do Sistema)
- Atualização só troca o .exe se o SHA-256 bater com o da release; workflow publica UtiliTech.exe.sha256
- Janela de confirmação própria: o que vai acontecer, aviso do que não tem volta e opção de ponto de restauração
- Modo simulação em Reparos rápidos e na Limpeza (lista do que seria apagado, sem apagar)
- ROADMAP com as fases 4 a 8 do plano de melhoria
