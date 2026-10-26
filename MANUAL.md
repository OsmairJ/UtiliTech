# Manual do UtiliTech

Guia de uso para o técnico. Explica como levar o UtiliTech no pendrive, como fazer um atendimento do começo ao fim e
o que cada tela faz. Para compilar ou mexer no código, veja o `README.md`.

## 1. Primeiros passos

### O que vai no pendrive

| Arquivo ou pasta | Para que serve |
|---|---|
| `UtiliTech.exe` | O aplicativo inteiro (não precisa instalar nada) |
| `settings.json` | Suas configurações: tema, domínio, último usuário, alertas, atualização. Criado sozinho |
| `apps.json` | Sua lista de programas de **Instalar apps**. Criado quando você muda a lista |
| `atendimentos\` | Histórico: uma pasta por atendimento e a planilha `inventario.xlsx` |
| `Instaladores\` | Instaladores que o app usa: assistentes da Lexmark, PJe Office Pro e a pasta `Drivers` |
| `logs\` | Registro de erros do app e listas do modo simulação |
| `Manual do UtiliTech.html` | Este manual, para ler sem internet ou imprimir |

### Abrir

1. Dê dois cliques no `UtiliTech.exe` e aceite o pedido de administrador (UAC).
2. Na primeira vez num PC, o Windows pode mostrar o aviso do SmartScreen: clique em **Mais informações → Executar assim mesmo**.
3. O app abre na **Visão geral**. Enquanto ele está aberto, um driver de sensores fica carregado para ler a temperatura;
   ao fechar, ele é removido e nada fica instalado no PC do cliente.

### Andar pelo app

- O menu da esquerda tem quatro grupos que abrem e fecham: **Diagnóstico**, **Pós-formatação**, **Manutenção** e, no fim, **Configurações**.
- **Ctrl+K** (ou o botão Buscar na barra de título) abre a busca: digite o problema com suas palavras, como "tela azul",
  "impressora", "gpupdate" ou "perfil antigo". Setas escolhem, Enter abre, Esc fecha.
- Atalhos:

| Tecla | O que faz |
|---|---|
| Ctrl+1 a Ctrl+9 | Visão geral, Sistema, Monitor, Rede, Checklist, Reparos, Drivers, Impressoras, Atendimentos |
| Ctrl+vírgula | Configurações |
| F5 | Atualiza a tela aberta |
| F1 | Mostra a lista de atalhos |

- Em notebook de tela pequena, a janela abre maximizada e as telas se arrumam em menos colunas.

## 2. Um atendimento do começo ao fim

Roteiro sugerido para um PC que chegou com problema ou que vai ser formatado.

1. **Visão geral.** Olhe primeiro o quadro **Problemas encontrados**: ele junta o que as outras telas acharam (disco com
   falha, pouco espaço, drivers, bateria gasta, antivírus, telas azuis, Windows não ativado, reinício pendente), os graves
   primeiro. Cada problema tem um botão que leva à tela que resolve.
2. **Sistema.** Preencha cliente, OS, técnico e observações. Esses dados identificam o atendimento no histórico.
3. **Diagnóstico** do que o cliente reclamou: Discos, Velocidade do disco, Memória RAM, Eventos, Rede, Segurança, Monitor
   ou Teste de estresse (veja a seção 3).
4. **Conserto:** Reparos rápidos, Limpeza, Drivers, Impressoras, Usuários e perfis (seção 5).
5. **Se formatou:** siga o **Checklist** (seção 4) até ficar tudo verde.
6. **Fechar:** volte em **Sistema** e clique em **Exportar relatório** (e, se quiser, **Exportar planilha**). O "Salvar
   como" já abre na pasta do atendimento no pendrive. O relatório mostra o que foi feito na visita e o **antes e depois**
   (espaço livre e dispositivos com problema).
7. Entregue o relatório ao cliente (abre no navegador: imprima ou salve em PDF).

Dica: tudo o que você executa no app fica anotado e vai para o relatório, com a hora e se deu certo.

## 3. Diagnóstico

### Visão geral
Problemas encontrados, gráfico de temperatura, carga e consumo da CPU e da GPU (1, 5 ou 15 minutos), resumo do PC
(modelo, número de série, Windows), componentes, memória, disco, eventos recentes e saúde do PC.

### Sistema
Fabricante, modelo, número de série, BIOS, placa-mãe, Windows (versão, ativação, chave OEM), processador, memória por
módulo, vídeo, discos e rede. Aqui ficam os campos do atendimento e os botões **Exportar relatório** (HTML) e
**Exportar planilha** (Excel ou CSV).

### Monitor
Temperatura, uso, clock e consumo de CPU, GPU, memória e discos, atualizados a cada segundo, com mínimo e máximo.

### Discos
Veredito **Bom / Atenção / Ruim** de cada disco, com o motivo: status do Windows, temperatura, horas ligado, desgaste do
SSD e erros. Disco **Ruim**: faça backup e troque.

### Velocidade do disco
Mede leitura e gravação como o CrystalDiskMark, sem o cache do Windows. Escolha o tamanho do teste (256 MB, 1 GB ou 4 GB);
o arquivo temporário é apagado no fim. O resultado diz se é típico de SSD NVMe, SSD SATA, HD mecânico (com a dica de
trocar por SSD) ou SSD lento.

### Bateria
Saúde (capacidade atual comparada com a de fábrica), ciclos e autonomia, com veredito. O botão gera o relatório completo
do Windows.

### Memória RAM
Módulos instalados, teste rápido dentro do Windows e atalho para o Diagnóstico de Memória do Windows (roda ao reiniciar;
depois o app mostra o resultado).

### Teste de estresse
Coloca a CPU a 100% em todas as threads para ver se o resfriamento dá conta. O teste para sozinho se a temperatura
chegar no limite, quando o tempo acabar ou se a memória apresentar erro.

### Rede
- **Testar conexão** confere, nesta ordem, IP da placa, gateway, DNS e internet, e diz onde está o problema e o que fazer.
  Exemplos: IP começando com 169.254 = não pegou IP (use Renovar IP); só o DNS falhando = Limpar cache DNS.
- Placas conectadas (IP, gateway, DNS, MAC, velocidade; avisa cabo abaixo de 1 Gbps) e Wi-Fi (rede, sinal, canal).
- Ping, rastrear rota e resolver nome.
- Unidades de rede mapeadas, com teste de acesso, e **Mapear unidade** (`\\servidor\pasta`).

### Segurança
Antivírus, firewall, BitLocker, Secure Boot, TPM, UAC e última atualização do Windows, cada um com OK, Atenção ou
Problema e o que fazer. Ações: atualizar e verificar com o Defender, ligar o firewall e mostrar a **chave de
recuperação do BitLocker** (só aparece na tela, depois de confirmar; não vai para o relatório).

### Eventos
Lê o registro do Windows dos últimos 7, 30 ou 90 dias e explica em português: telas azuis (com o código traduzido),
desligamentos inesperados (queda de energia ou travamento), erros de disco, driver de vídeo que travou e erros de
hardware. Cada tipo diz o que fazer e tem atalho para a tela certa.

### Alertas
Aviso no canto da tela, por cima das outras janelas, quando a temperatura passar do limite ou um disco der sinal de
falha. Funciona com o app aberto, em qualquer tela; cada aviso se repete no máximo a cada 5 minutos.

## 4. Pós-formatação

### Checklist
O que falta para entregar o PC, conferido sozinho toda vez que a tela abre:

- Windows ativado
- Drivers sem problema
- Google Chrome, Mozilla Firefox e Microsoft 365 instalados (outro Office não conta como 365; o checklist diz qual achou)
- Nome definitivo (não o `DESKTOP-...` da instalação)
- No domínio e com as políticas de grupo aplicadas
- Sem reinício pendente
- Discos saudáveis
- Relatório guardado no histórico

Cada item que falta tem o botão que leva à ferramenta certa.

### Instalar apps
Instala em silêncio, um por vez, os programas marcados (pelo winget). O que já está instalado aparece marcado. A lista é
sua: busque e clique em **Adicionar**, use **×** para tirar ou **Restaurar lista padrão**. Ela fica no `apps.json` do
pendrive. O PJe Office Pro é instalado a partir da pasta `Instaladores`.

### Inicialização
Programas que abrem com o Windows. Ative ou desative como no Gerenciador de Tarefas.

### Nome e domínio
1. Digite o domínio da empresa (fica salvo só no `settings.json` do pendrive), o nome novo do PC e um usuário com
   permissão. A senha nunca é salva.
2. Clique para conferir: o app pergunta ao servidor se o nome já existe. Se existir, mostra onde está e quando foi usado,
   e não deixa continuar.
3. Se estiver livre, ele renomeia e coloca no domínio com uma reinicialização só (ou só renomeia, se o PC já estiver no domínio).
4. Se o login for recusado, o app confere se o relógio do PC está diferente do servidor, causa comum em PC recém-formatado.

## 5. Manutenção

### Reparos rápidos
Um clique para: atualizar políticas de grupo (gpupdate), destravar a fila de impressão, limpar DNS, renovar IP,
resetar a rede, consertar o Windows Update, acertar o relógio e reiniciar o Explorer. Os que mexem no PC mostram antes
o que vai acontecer; resetar rede e Windows Update oferecem criar um ponto de restauração.

### Limpeza
Mostra quanto cada categoria ocupa (temporários, cache do Windows Update, lixeira etc.) e apaga só o que você marcar.

### Usuários e perfis
- Lista os perfis do PC com dono, último uso, tamanho e marcas (você, em uso, conta apagada, parado há mais de 90 dias).
- **Apagar perfil**: apaga a pasta e o registro juntos, como o Windows faz. O perfil em uso não pode ser apagado.
- **Copiar dados de um perfil para outro**: Área de Trabalho, Documentos, Imagens, Downloads, Vídeos, Música, Favoritos
  e os favoritos do Chrome e do Edge (também do OneDrive). Nada é apagado no destino.

### Drivers
- Lista os dispositivos com problema, procura no Windows Update o que falta e instala os marcados.
- **Instalar de uma pasta**: usa os pacotes de driver da pasta `Instaladores\Drivers` do pendrive, sem internet.
- **Placa de vídeo**: NVIDIA baixa e instala o driver mais novo; AMD e Intel abrem a ferramenta do fabricante.
- Antes de instalar, oferece criar um ponto de restauração.

### Impressoras
- Lista as impressoras com driver, IP, se ele responde, documentos na fila e as cópias fantasma ("Cópia 1").
  Por impressora: página de teste, definir como padrão, limpar a fila e remover.
- **Lexmark**: botões para o assistente da Lexmark do pendrive (preto e branco e colorida). Depois da primeira vez, o
  driver aparece no topo de Instalar pelo IP.
- **Testar IP e achar driver**: pergunta o modelo à impressora e escolhe o driver. Brother e Samsung usam o
  **Microsoft PCL6 Class Driver**, que já vem no Windows (não baixa nada).
- **Instalar pelo IP**: cria a porta e a impressora com o driver escolhido.
- **Conectar impressora compartilhada**: `\\servidor\impressora`.
- **Procurar impressoras na rede (exceto Lexmark)**: varre a rede da placa conectada e lista as impressoras com modelo,
  nome, número de série, driver indicado e se já estão instaladas. "Usar para instalar" preenche o Instalar pelo IP.

## 6. Histórico e inventário

- **Atendimentos** lista todos os atendimentos guardados no pendrive, do mais novo para o mais antigo. Busque por cliente,
  OS, número de série, nome do PC, modelo, técnico ou data. Um clique abre o relatório, a planilha ou a pasta.
- O **número de série** identifica a máquina mesmo depois de formatada e renomeada.
- **Inventário (planilha)** atualiza e abre `atendimentos\inventario.xlsx`: uma linha por máquina (modelo, processador,
  memória, discos, Windows, MAC e IP, saúde, datas e clientes) e uma aba com todos os atendimentos.

## 7. Segurança e modo simulação

- **Confirmações**: toda ação que apaga ou muda algo importante mostra antes o que vai acontecer.
- **Ponto de restauração**: oferecido antes de instalar drivers e de reparos grandes. Pode ser ligado ou desligado em
  Configurações → Segurança.
- **Modo simulação** (em Reparos rápidos e Limpeza): não executa nada; grava na pasta `logs` a lista do que seria feito
  ou apagado. Bom para conferir antes num PC delicado.

## 8. Configurações e atualização

- **Tema** claro ou escuro.
- **Atualizações**: ao abrir, o app avisa quando há versão nova no GitHub e instala com um clique. Ele só troca o `.exe`
  se o arquivo baixado for idêntico ao publicado (conferência SHA-256). Depois de atualizar, mostra as novidades; elas
  também ficam em **Ver novidades**.
- **Registro de erros**: abre o log do dia, útil para relatar um problema do app.

## 9. Problemas comuns

| Situação | O que fazer |
|---|---|
| O app não abre | Confira se aceitou o UAC. Antivírus de terceiros pode segurar o `.exe` na primeira vez |
| Temperatura da CPU não aparece | Alguns PCs bloqueiam o driver de sensores (Integridade de Memória ou antivírus). O resto funciona |
| "Salvar como" não abriu no pendrive | O pendrive pode estar protegido contra escrita; o app guarda uma cópia na pasta do atendimento quando consegue |
| Login recusado no domínio | Confira usuário e senha e o relógio do PC (o app avisa se estiver diferente do servidor) |
| Instalar apps falha | O winget precisa de internet; em rede com proxy, teste a conexão em Rede. Se aparecer "winget não encontrado", atualize o "Instalador de Aplicativo" pela Microsoft Store ou rode o Windows Update |
| Impressora não responde | Use **Testar IP**; confira cabo e se a impressora está na mesma rede |
