# Instala a trava contra dados internos neste clone do repositório (hooks pre-commit e commit-msg).
# Uso:  .\ferramentas\instalar-trava.ps1 -Termos "C:\caminho\fora\do\repositorio\termos-proibidos.txt"
param([Parameter(Mandatory = $true)][string]$Termos)

$ErrorActionPreference = 'Stop'
$repo = (git rev-parse --show-toplevel).Trim()
if (-not (Test-Path $Termos)) { throw "Arquivo de termos não encontrado: $Termos" }
$dentro = (Resolve-Path $repo).Path.TrimEnd('\') + '\'   # barra no fim: "UtiliTech-Contexto" não é "UtiliTech\"
if ((Resolve-Path $Termos).Path.StartsWith($dentro, [StringComparison]::OrdinalIgnoreCase)) {
    throw "A lista de termos não pode ficar dentro do repositório (ela iria para o GitHub)."
}

git config utilitech.termos ((Resolve-Path $Termos).Path -replace '\\', '/')
$hooks = Join-Path $repo '.git/hooks'
"#!/bin/sh`nexec sh ""`$(git rev-parse --show-toplevel)/ferramentas/trava-dados-internos.sh"" pre-commit`n" |
    Set-Content (Join-Path $hooks 'pre-commit') -NoNewline -Encoding ascii
"#!/bin/sh`nexec sh ""`$(git rev-parse --show-toplevel)/ferramentas/trava-dados-internos.sh"" commit-msg ""`$1""`n" |
    Set-Content (Join-Path $hooks 'commit-msg') -NoNewline -Encoding ascii
Write-Host "Trava instalada: pre-commit e commit-msg conferem os termos de $Termos"
