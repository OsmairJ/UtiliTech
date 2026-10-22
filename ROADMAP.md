# UtiliTech — plano de melhoria

## Fase 1 — Monitoramento (concluída)
- [x] **Monitor**: temperatura, uso, clock e consumo de CPU, GPU, RAM e discos, com gráfico do último minuto, espaço livre das unidades e sensores principais com máximo. Driver PawnIO para CPU/placa-mãe.
- [x] **Teste de estresse com limite de temperatura**: CPU a 100% (e RAM opcional) por 1–30 min; para sozinho no limite, detecta queda de clock por calor e dá o veredito. O resultado vai para o relatório.
- [x] **Alertas**: tela própria em Diagnóstico; aviso no canto da tela quando CPU/GPU passam do limite ou um disco dá sinal de falha, com histórico da sessão. Temperatura da CPU/GPU sempre visível no rodapé do menu.

## Fase 2 — Manutenção (concluída)
- [x] **Reparos de 1 clique**: destravar fila de impressão, limpar cache DNS, renovar IP, resetar rede (Winsock/TCP-IP), resetar Windows Update, sincronizar relógio e reiniciar o Explorer, com registro de cada comando.
- [x] **Limpeza**: temporários (usuário e Windows), cache do Windows Update, lixeira, miniaturas, cache dos navegadores e relatórios de erro, mostrando quanto espaço libera antes de apagar.
- [x] **Drivers**: dispositivos com problema no Gerenciador (sem driver, com erro), drivers que faltam ou estão desatualizados no catálogo do Windows Update (instala os marcados) e instalação de pacotes de driver de uma pasta (pendrive/offline). Placa de vídeo direto do fabricante: NVIDIA confere a versão mais nova e instala (com verificação da assinatura); AMD e Intel pelas ferramentas oficiais de detecção.

## Fase 3 — Acabamento (concluída)
- [x] **Atualização automática** pelo GitHub Releases: avisa ao abrir quando há versão nova e instala com 1 clique em Configurações.
- [x] **Tema claro** opcional, trocando na hora.
- [x] **Registro de erros** do próprio app (pasta `logs`, últimos 10 dias).
- Assinatura digital do .exe: fora por enquanto (exige certificado pago).
