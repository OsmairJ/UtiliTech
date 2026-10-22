# UtiliTech

Ferramenta de bancada para técnico de informática (WPF .NET 8), inspirada no
[ResetExplode](https://github.com/GustavoReset/ResetExplode). Roda como administrador.

## Módulos

**Diagnóstico**
- **Sistema**: fabricante, modelo, número de série, BIOS, placa-mãe, Windows (versão, ativação, chave OEM da BIOS, último boot), CPU, RAM por módulo, vídeo, discos, unidades e rede. Campos de atendimento (cliente, OS, técnico, observações) e **Exportar relatório** em HTML para entregar ao cliente (abre no navegador; imprima ou salve em PDF). **Exportar planilha** gera um Excel (.xlsx) com as abas Resumo, Sistema, Discos e Bateria, ou um CSV (separado por `;`), bom para controle de atendimentos e inventário de máquinas.
- **Monitor em tempo real**: cartões de CPU, GPU, RAM e discos (temperatura, uso, clock, consumo, VRAM) com gráfico dos últimos 60 s, e a lista completa de sensores de cada componente com atual/mínimo/máximo, atualizada a cada 1 s (estilo HWMonitor). Usa a [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0). Temperatura da CPU e da placa-mãe usam o driver **PawnIO**, que vai embutido no .exe e é carregado só enquanto o app está aberto (como o AIDA64): nada fica instalado no PC do cliente. Detalhes e licença em `Assets/Drivers/LEIA-ME.txt`.
- **Discos (S.M.A.R.T.)**: veredito Bom / Atenção / Ruim por disco físico com os motivos: status do Windows, temperatura, horas ligado, desgaste do SSD e erros não corrigidos. Previsão de falha do driver e espaço livre de cada unidade.
- **Bateria**: saúde (capacidade atual × de projeto), ciclos, tensão, estado e autonomia, com veredito. Botão para o relatório completo do `powercfg /batteryreport`.
- **Memória RAM**: módulos instalados; teste rápido dentro do Windows (6 padrões, várias passadas, multi-thread) e atalho para o Diagnóstico de Memória do Windows no boot, com leitura do último resultado no log.

**Pós-formatação**
- **Instalar apps**: a lista de programas do técnico, instalados pelo winget em silêncio e um por vez. Detecta o que já está instalado. A lista é editável na própria tela: busca no winget e **Adicionar**, **×** para tirar, **Restaurar lista padrão**. Fica salva em `apps.json` ao lado do .exe (vai junto no pendrive) ou, se a pasta não aceitar escrita, em `%AppData%\UtiliTech`. Lista padrão: Chrome, Firefox, 7-Zip, WinRAR, Acrobat Reader, LibreOffice, VLC, AnyDesk, TeamViewer, RustDesk, Zoom, Discord, runtimes (Java, VC++, .NET), Notepad++, Lightshot, PowerToys, CrystalDiskInfo, HWiNFO e Steam.
- **Inicialização**: lista os programas que abrem com o Windows (registro do usuário, de todos os usuários, 32 bits e pastas Inicializar) e ativa/desativa igual ao Gerenciador de Tarefas.

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
- `Theme.xaml`: cores e estilos; o tema claro troca os pincéis na hora (`Views/ThemeManager.cs`).
