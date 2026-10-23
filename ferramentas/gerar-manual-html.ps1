# Gera o "Manual do UtiliTech.html" a partir do MANUAL.md, para ler no pendrive sem internet e imprimir.
# Uso: .\ferramentas\gerar-manual-html.ps1 [-Saida ..\UtiliTech-dist]
# Entende só o Markdown que o manual usa: títulos, parágrafos, listas, tabelas, **negrito** e `código`.
param([string]$Saida = (Join-Path $PSScriptRoot '..\..\UtiliTech-dist'))
$ErrorActionPreference = 'Stop'

$raiz = Split-Path $PSScriptRoot -Parent
$md = [IO.File]::ReadAllLines((Join-Path $raiz 'MANUAL.md'), [Text.Encoding]::UTF8)
$versao = ([xml](Get-Content (Join-Path $raiz 'UtiliTech.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

function Inline([string]$s) {
    $s = [Net.WebUtility]::HtmlEncode($s)
    $s = [regex]::Replace($s, '`([^`]+)`', '<code>$1</code>')
    $s = [regex]::Replace($s, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
    return $s
}

$html = New-Object Text.StringBuilder
$para = New-Object Collections.Generic.List[string]
$lista = $null      # 'ul' ou 'ol' aberta
$tabela = $false
$toc = New-Object Collections.Generic.List[string]

function FecharTudo {
    if ($para.Count) { [void]$html.AppendLine("<p>$(Inline ($para -join ' '))</p>"); $para.Clear() }
    if ($script:lista) { [void]$html.AppendLine("</$($script:lista)>"); $script:lista = $null }
    if ($script:tabela) { [void]$html.AppendLine('</tbody></table>'); $script:tabela = $false }
}

$n = 0
foreach ($linha in $md) {
    $n++
    if ($linha -match '^(#{1,3}) (.+)$') {
        FecharTudo
        $nivel = $Matches[1].Length; $texto = $Matches[2]
        if ($nivel -eq 1) { continue }   # o título vai no cabeçalho
        $id = "s$n"
        if ($nivel -eq 2) { $toc.Add("<li><a href=""#$id"">$(Inline $texto)</a></li>") }
        [void]$html.AppendLine("<h$nivel id=""$id"">$(Inline $texto)</h$nivel>")
    }
    elseif ($linha -match '^\|') {
        if ($para.Count -or $lista) { FecharTudo }
        $celulas = $linha.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        if ($celulas[0] -match '^-+$') { continue }
        if (-not $tabela) {
            [void]$html.AppendLine('<table><thead><tr>' + (($celulas | ForEach-Object { "<th>$(Inline $_)</th>" }) -join '') + '</tr></thead><tbody>')
            $tabela = $true
        } else {
            [void]$html.AppendLine('<tr>' + (($celulas | ForEach-Object { "<td>$(Inline $_)</td>" }) -join '') + '</tr>')
        }
    }
    elseif ($linha -match '^(- |\d+\. )(.+)$') {
        $tipo = if ($Matches[1] -eq '- ') { 'ul' } else { 'ol' }
        if ($para.Count -or $tabela -or ($lista -and $lista -ne $tipo)) { FecharTudo }
        if (-not $lista) { [void]$html.AppendLine("<$tipo>"); $lista = $tipo }
        [void]$html.Append("<li>$(Inline $Matches[2])")
        [void]$html.AppendLine('</li>')
    }
    elseif ($linha -match '^\s{2,}\S' -and $lista) {
        # continuação do item anterior: reabre o </li>
        $html.Length -= ("</li>" + [Environment]::NewLine).Length
        [void]$html.AppendLine(" $(Inline $linha.Trim())</li>")
    }
    elseif ($linha.Trim() -eq '') { FecharTudo }
    else {
        if ($lista -or $tabela) { FecharTudo }
        $para.Add($linha.Trim())
    }
}
FecharTudo

$pagina = @"
<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Manual do UtiliTech</title>
<style>
:root { --fundo:#ffffff; --texto:#1e1f22; --suave:#5c5e66; --borda:#d9dadf; --caixa:#f2f3f5; --destaque:#5865f2; }
@media (prefers-color-scheme: dark) { :root { --fundo:#313338; --texto:#dbdee1; --suave:#a5a9b1; --borda:#45474f; --caixa:#2b2d31; --destaque:#8891f2; } }
* { box-sizing: border-box; }
body { margin:0; background:var(--fundo); color:var(--texto); font:16px/1.6 "Segoe UI", system-ui, sans-serif; }
main { max-width: 860px; margin: 0 auto; padding: 32px 16px 64px; }
header { border-bottom: 3px solid var(--destaque); margin-bottom: 24px; padding-bottom: 12px; }
header h1 { margin: 0; font-size: 2rem; }
header p { margin: 4px 0 0; color: var(--suave); }
nav { background: var(--caixa); border-radius: 8px; padding: 12px 20px; margin-bottom: 24px; }
nav ol { margin: 0; padding-left: 20px; columns: 2; }
h2 { margin-top: 40px; padding-bottom: 4px; border-bottom: 1px solid var(--borda); }
h3 { margin-top: 28px; color: var(--destaque); }
a { color: var(--destaque); }
code { background: var(--caixa); border-radius: 4px; padding: 1px 5px; font-family: Consolas, monospace; font-size: .9em; }
table { border-collapse: collapse; width: 100%; margin: 12px 0; display: block; overflow-x: auto; }
th, td { border: 1px solid var(--borda); padding: 6px 10px; text-align: left; vertical-align: top; }
th { background: var(--caixa); }
li { margin: 4px 0; }
@media (max-width: 600px) { nav ol { columns: 1; } }
@media print {
  :root { --fundo:#fff; --texto:#000; --suave:#444; --borda:#999; --caixa:#eee; --destaque:#333; }
  nav { display: none; } main { max-width: none; padding: 0; } h2 { page-break-after: avoid; } table, li { page-break-inside: avoid; }
}
</style>
</head>
<body>
<main>
<header><h1>Manual do UtiliTech</h1><p>Versão $versao · para ler sem internet ou imprimir (Ctrl+P)</p></header>
<nav><strong>Conteúdo</strong><ol>
$($toc -join "`n")
</ol></nav>
$($html.ToString())
</main>
</body>
</html>
"@

$destino = Join-Path (Resolve-Path $Saida) 'Manual do UtiliTech.html'
[IO.File]::WriteAllText($destino, $pagina, (New-Object Text.UTF8Encoding $false))
"Gerado: $destino"
