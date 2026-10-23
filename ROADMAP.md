# UtiliTech — plano de melhoria

## Fase 1 — Monitoramento (concluída)
- [x] **Monitor**: temperatura, uso, clock e consumo de CPU, GPU, RAM e discos, com gráfico do último minuto, espaço livre das unidades e sensores principais com máximo. Driver PawnIO para CPU/placa-mãe.
- [x] **Teste de estresse com limite de temperatura**: CPU a 100% (e RAM opcional) por 1–30 min; para sozinho no limite, detecta queda de clock por calor e dá o veredito. O resultado vai para o relatório.
- [x] **Alertas**: tela própria em Diagnóstico; aviso no canto da tela quando CPU/GPU passam do limite ou um disco dá sinal de falha, com histórico da sessão. Temperatura da CPU/GPU sempre visível no rodapé do menu.

## Fase 2 — Manutenção (concluída)
- [x] **Reparos de 1 clique**: atualizar políticas de grupo, destravar fila de impressão, limpar cache DNS, renovar IP, resetar rede (Winsock/TCP-IP), resetar Windows Update, sincronizar relógio e reiniciar o Explorer, com registro de cada comando.
- [x] **Limpeza**: temporários (usuário e Windows), cache do Windows Update, lixeira, miniaturas, cache dos navegadores e relatórios de erro, mostrando quanto espaço libera antes de apagar.
- [x] **Drivers**: dispositivos com problema no Gerenciador (sem driver, com erro), drivers que faltam ou estão desatualizados no catálogo do Windows Update (instala os marcados) e instalação de pacotes de driver de uma pasta (pendrive/offline). Placa de vídeo direto do fabricante: NVIDIA confere a versão mais nova e instala (com verificação da assinatura); AMD e Intel pelas ferramentas oficiais de detecção.

## Fase 3 — Acabamento (concluída)
- [x] **Atualização automática** pelo GitHub Releases: avisa ao abrir quando há versão nova e instala com 1 clique em Configurações.
- [x] **Tema claro** opcional, trocando na hora.
- [x] **Registro de erros** do próprio app (pasta `logs`, últimos 10 dias).
- Assinatura digital do .exe: fora por enquanto (exige certificado pago).

## Fase 4 — Segurança e confiabilidade (concluída)
- [x] **Ponto de restauração automático** antes de resetar rede, resetar Windows Update e instalar drivers (Windows Update, pasta e NVIDIA). Vem marcado na confirmação (Configurações → Segurança); se a Proteção do Sistema estiver desligada, oferece ligar. Nome e domínio fica de fora de propósito: voltar um ponto depois de ingressar quebra a relação de confiança com o domínio.
- [x] **Atualização conferida por SHA-256**: o app só troca o .exe se o hash do arquivo baixado bater com o informado pelo GitHub (ou com o `UtiliTech.exe.sha256` que o workflow publica junto).
- [x] **Confirmações claras**: janela própria que lista o que vai acontecer, avisa o que não tem volta (botão vermelho) e oferece o ponto de restauração.
- [x] **Modo simulação** em Reparos rápidos (mostra no registro os comandos e arquivos, sem executar) e na Limpeza (gera a lista dos arquivos que seriam apagados, em `logs\`).
- [x] **Testes automáticos** (`UtiliTech.Tests`, xUnit): veredito de S.M.A.R.T. e bateria, regras do nome do PC e do usuário do domínio, versão das tags e SHA-256, planilha .xlsx/.csv e limpeza/simulação numa pasta temporária. Rodam no GitHub antes de cada release (se falharem, a versão não sai).

## Fase 5 — Atendimento e histórico (concluída)
- [x] **Histórico de atendimentos no pendrive**: ao exportar relatório ou planilha na tela Sistema, o "Salvar como" já abre na pasta do atendimento em `atendimentos` ao lado do .exe (se salvar em outro lugar, vai uma cópia) (uma pasta por atendimento, com `atendimento.json`). Tela **Atendimentos** no menu com busca por cliente, OS, nº de série, nome do PC, modelo, técnico ou data, sem diferenciar acentos. Relatório e planilha do mesmo PC, cliente e OS no mesmo dia ficam juntos; o nº de série identifica a máquina mesmo depois de renomeada.
- [x] **Relatório "antes e depois"**: o app anota o que faz na visita (limpeza, reparos, drivers, programas, nome e domínio, pontos de restauração) e compara o PC de quando abriu com o de agora (espaço livre e dispositivos com problema). Entra no relatório HTML ("Serviços feitos nesta visita" e "Antes e depois"), na planilha (aba Serviços) e no histórico de atendimentos.
- [x] **Inventário em lote**: `atendimentos\inventario.xlsx` no pendrive, refeito sozinho a cada atendimento guardado (ou pelo botão "Inventário" na tela Atendimentos). Aba **Máquinas** (uma linha por máquina, pelo nº de série: modelo, processador, memória, discos, Windows, MAC/IP, saúde, primeiro e último atendimento, clientes) e aba **Atendimentos** (um por linha). Se estiver aberta no Excel, avisa e mantém a anterior.
- [x] **Checklist de pós-formatação** (Pós-formatação → Checklist): Windows ativado, drivers, programas (Google Chrome, Mozilla Firefox e Microsoft 365; outro Office não conta), nome do computador (não o padrão DESKTOP-...), domínio, políticas de grupo, reinício pendente, discos e relatório guardado no histórico. Tudo conferido sozinho a cada vez que a tela abre, com atalho para a ferramenta de cada etapa que falta.

## Fase 6 — Novas ferramentas (concluída)
- [x] **Rede** (Diagnóstico → Rede): teste de conexão em etapas (IP → gateway → DNS → internet) com o diagnóstico em português e o que fazer; placas conectadas (IP, gateway, DNS, MAC, velocidade do link, aviso de cabo abaixo de 1 Gbps); Wi-Fi (rede, sinal, banda, canal); ping, rastrear rota e resolver nome; unidades de rede mapeadas e se estão acessíveis. Mapear unidades e impressoras de rede fica para a ferramenta de Impressoras.
- [x] **Impressoras** (Manutenção → Impressoras): lista com driver, IP/servidor e se responde, fila, padrão, offline e cópias fantasmas ("Cópia 1"); página de teste, definir padrão, limpar fila, abrir fila e remover (com confirmação); instalar pelo IP escolhendo o driver instalado (porta TCP/IP + impressora) e conectar impressora compartilhada (\\servidor\impressora). Lexmark do pendrive: assistente do MX620 (P&B, driver XL) e do CS725 (colorida, driver PS), com esses drivers no topo de Instalar pelo IP. Testar IP identifica o modelo (SNMP) e escolhe o driver; Brother e Samsung usam o Microsoft PCL6 Class Driver do próprio Windows. Procurar impressoras na rede (exceto Lexmark): porta 9100 + SNMP na rede da placa, com driver indicado e "Usar para instalar". Na tela Rede: mapear unidade de rede (net use, persistente).
- [x] **Usuários e perfis** (Manutenção → Usuários e perfis): perfis com dono, último uso (também de quem não está logado), tamanho e marcas (você, em uso, conta apagada, parado há 90+ dias); apagar perfil pelo Windows (pasta + registro, com confirmação; bloqueado para o perfil em uso); copiar Área de Trabalho, Documentos, Imagens, Downloads, Vídeos, Música e Favoritos (do OneDrive, se for lá que estão) e os favoritos do Chrome/Edge com robocopy, sem apagar nada no destino; contas locais (ativa, administrador).
- [x] **Segurança do Windows** (Diagnóstico → Segurança): antivírus (Defender ou outro registrado, tempo real, idade das assinaturas), firewall por perfil, BitLocker por unidade (chave de recuperação só sob pedido, fora de log e relatório), Secure Boot/UEFI, TPM (versão e se está pronto), UAC e última atualização instalada, cada um com veredito e o que fazer. Ações: atualizar assinaturas, verificação rápida, ligar firewall e abrir a Segurança do Windows.
- [x] **Eventos simplificados** (Diagnóstico → Eventos, 7/30/90 dias): telas azuis com o código explicado (ex.: 0xD1 = driver), desligamentos inesperados (com a hora em que o PC parou), erros de disco e do NTFS, driver de vídeo que travou e erros de hardware (WHEA). Um travamento que gera vários eventos conta uma vez; cada tipo tem o que fazer e atalho para a ferramenta (Drivers, Discos, Memória, Monitor).
- [x] **Velocidade do disco** (Diagnóstico → Velocidade do disco): leitura e gravação sequencial (1 MiB) e aleatória 4K (QD1) sem o cache do Windows, com arquivo temporário de 256 MB/1 GB/4 GB apagado no fim (também ao parar); veredito do tipo de disco (NVMe, SSD SATA, HD mecânico, SSD lento) e o resultado entra no relatório.

## Fase 7 — Interface e usabilidade
- [ ] **Busca no menu (Ctrl+K)** para achar qualquer ferramenta ou reparo.
- [ ] **"Problemas encontrados"** na Visão geral, com o botão que resolve.
- [ ] **Telas pequenas e DPI** (notebooks 1366×768) e atalhos de teclado.
- [ ] **Notas da versão** ao abrir depois de atualizar.

## Fase 8 — Código e publicação
- [ ] **Separar lógica das telas grandes** (Drivers, Visão geral, Nome e domínio) para facilitar os testes.
- [ ] **Trava contra dados internos** no workflow ou num hook de pré-commit.
- [ ] **Tamanho do .exe**: avaliar *trimming* para reduzir os ~70 MB.
- [ ] **Assinatura digital**: avaliar opção gratuita para código aberto (ex.: SignPath).
