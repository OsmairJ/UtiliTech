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

## Fase 5 — Atendimento e histórico
- [ ] **Histórico de atendimentos no pendrive**: relatórios e planilhas salvos sozinhos, com busca por cliente, OS ou nº de série.
- [ ] **Relatório "antes e depois"**: limpeza, drivers instalados, reparos feitos e teste de estresse no mesmo HTML.
- [ ] **Inventário em lote**: cada máquina atendida entra numa planilha única do pendrive.
- [ ] **Checklist de pós-formatação**: apps → drivers → nome/domínio → políticas → relatório, mostrando o que falta.

## Fase 6 — Novas ferramentas
- [ ] **Rede**: ping/traceroute, teste de DNS, IP/gateway, velocidade da placa, Wi-Fi e mapeamento de unidades e impressoras de rede.
- [ ] **Impressoras**: listar, remover filas fantasmas, página de teste e instalar por IP.
- [ ] **Usuários e perfis**: listar, apagar perfis antigos e copiar dados de um perfil para outro.
- [ ] **Segurança do Windows**: Defender, BitLocker (com chave de recuperação), firewall, Secure Boot, TPM e atualizações pendentes.
- [ ] **Eventos simplificados**: telas azuis (minidump), desligamentos inesperados e erros de disco explicados em português.
- [ ] **Velocidade do disco**: leitura e escrita sequencial e aleatória.

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
