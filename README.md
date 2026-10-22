# UtiliTech

Ferramenta de bancada para técnico de informática (WPF .NET 8), inspirada no
[ResetExplode](https://github.com/GustavoReset/ResetExplode). Roda como administrador.

## Módulos

**Visão geral** (tela inicial): gráfico de temperatura/carga/consumo da CPU e da GPU (1, 5 ou 15 min), resumo do PC (modelo, nº de série, Windows), tabela de componentes (temperatura, carga, clock, consumo) com atalho para cada tela, memória e disco, eventos recentes e saúde do PC (discos, bateria, drivers).

**Diagnóstico**
- **Sistema**: fabricante, modelo, número de série, BIOS, placa-mãe, Windows (versão, ativação, chave OEM da BIOS, último boot), CPU, RAM por módulo, vídeo, discos, unidades e rede. Campos de atendimento (cliente, OS, técnico, observações) e **Exportar relatório** em HTML para entregar ao cliente (abre no navegador; imprima ou salve em PDF). **Exportar planilha** gera um Excel (.xlsx) com as abas Resumo, Sistema, Discos e Bateria, ou um CSV (separado por `;`), bom para controle de atendimentos e inventário de máquinas.
- **Monitor em tempo real**: cartões de CPU, GPU, RAM e discos (temperatura, uso, clock, consumo, VRAM) com gráfico dos últimos 60 s, e a lista completa de sensores de cada componente com atual/mínimo/máximo, atualizada a cada 1 s (estilo HWMonitor). Usa a [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0). Temperatura da CPU e da placa-mãe usam o driver **PawnIO**, que vai embutido no .exe e é carregado só enquanto o app está aberto (como o AIDA64): nada fica instalado no PC do cliente. Detalhes e licença em `Assets/Drivers/LEIA-ME.txt`.
- **Discos (S.M.A.R.T.)**: veredito Bom / Atenção / Ruim por disco físico com os motivos: status do Windows, temperatura, horas ligado, desgaste do SSD e erros não corrigidos. Previsão de falha do driver e espaço livre de cada unidade.
- **Bateria**: saúde (capacidade atual × de projeto), ciclos, tensão, estado e autonomia, com veredito. Botão para o relatório completo do `powercfg /batteryreport`.
- **Memória RAM**: módulos instalados; teste rápido dentro do Windows (6 padrões, várias passadas, multi-thread) e atalho para o Diagnóstico de Memória do Windows no boot, com leitura do último resultado no log.

**Pós-formatação**
- **Instalar apps**: a lista de programas do técnico, instalados pelo winget em silêncio e um por vez. Detecta o que já está instalado. A lista é editável na própria tela: busca no winget e **Adicionar**, **×** para tirar, **Restaurar lista padrão**. Fica salva em `apps.json` ao lado do .exe (vai junto no pendrive) ou, se a pasta não aceitar escrita, em `%AppData%\UtiliTech`. Lista padrão: Chrome, Firefox, 7-Zip, WinRAR, Acrobat Reader, LibreOffice, VLC, AnyDesk, TeamViewer, Zoom, NAPS2, runtimes (Java, VC++, .NET), Notepad++ e CrystalDiskInfo, mais os instaladores do pendrive (pasta `Instaladores`): PJe Office Pro e os drivers das impressoras Lexmark.
- **Inicialização**: lista os programas que abrem com o Windows (registro do usuário, de todos os usuários, 32 bits e pastas Inicializar) e ativa/desativa igual ao Gerenciador de Tarefas.
- **Nome e domínio**: confere no AD (empresa.local, catálogo global) se o nome novo já existe — se existir, alerta com a OU, criação e último logon do objeto e bloqueia; se estiver livre, renomeia e ingressa no domínio com uma única reinicialização (ou só renomeia, se o PC já estiver no domínio). Valida as regras do nome (até 15 caracteres) e lembra o usuário usado (a senha nunca é salva). Se o login for recusado, confere se o relógio do PC está diferente do servidor (causa comum em PC recém-formatado).

**Manutenção**
- **Reparos rápidos**: fila de impressão, DNS, IP, rede, Windows Update, relógio e Explorer, com 1 clique.
- **Limpeza**: mostra quanto cada categoria ocupa e apaga só o que for marcado.
- **Drivers**: lista os dispositivos com problema (sem driver, código 28, 43...), procura no Windows Update os drivers que faltam ou estão desatualizados e instala os marcados, e instala pacotes de driver de uma pasta (ex.: `InstaladoresDrivers` no pendrive), sem internet. **Placa de vídeo**: NVIDIA consulta o driver mais novo no site da NVIDIA, baixa, confere a assinatura digital e instala em silêncio; AMD abre a detecção automática do site da AMD; Intel instala o Intel Driver & Support Assistant.

## Requisitos

- Windows 10/11
- .NET 8 SDK para compilar: `winget install Microsoft.DotNet.SDK.8`

## Como compilar e rodar

```
cd UtiliTech
dotnet build
```

Depois abra `bin\Debug\net8.0-windows\UtiliTech.exe` (ele pede administrador).

## Usar no pendrive

1. Dê dois cliques em `publicar.bat`. Ele gera `..\UtiliTech-dist\UtiliTech.exe` (~70 MB), um arquivo único com o .NET embutido.
2. Copie o `UtiliTech.exe` para o pendrive (de preferência numa pasta, ex.: `E:\UtiliTech\`).
3. No PC do cliente, dê dois cliques no `.exe` e aceite o pedido de administrador. Não precisa instalar nada (Windows 10/11 64 bits).

A lista de programas de **Instalar apps** fica salva em `apps.json`, na mesma pasta do `.exe`, então ela anda junto com o pendrive. Sempre que alterar o código, rode o `publicar.bat` de novo e recopie o `.exe`.

## Atualização automática (GitHub Releases)

1. Crie um repositório no GitHub (ex.: `seu-usuario/UtiliTech`) e envie esta pasta para ele. O workflow `.github/workflows/publicar.yml` já vem pronto.
2. Para lançar uma versão, crie uma tag e envie: `git tag v0.2.0` e `git push --tags`. O GitHub compila o `UtiliTech.exe` e publica a release sozinho.
3. No app, em **Configurações → Atualizações**, informe `seu-usuario/UtiliTech`. Ao abrir, o UtiliTech avisa quando houver versão nova e instala com 1 clique (baixa, troca o `.exe` e reabre).

O repositório precisa ser **público** para o app conseguir ler as releases sem senha.

## Configurações e registros

- `settings.json` (ao lado do `.exe`): tema, alertas e repositório de atualização.
- `logs\utilitech-AAAAMMDD.log`: erros do app, últimos 10 dias. Abra por **Configurações → Registro de erros**.

## Estrutura

- `Services/`: a lógica (WMI, sensores, winget, testes, reparos, limpeza, atualização, relatório), sem dependência da interface.
- `Views/`: uma tela por módulo.
- `Theme.xaml`: visual preto e branco (inspirado no ResetExplode), com barra de título própria e ícones Segoe Fluent no menu; o tema claro troca os pincéis na hora (`Views/ThemeManager.cs`).
