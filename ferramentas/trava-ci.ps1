# Trava contra dados internos no GitHub Actions. Os termos vêm do segredo TERMOS_PROIBIDOS (variável TERMOS).
# Mostra só o arquivo onde achou, nunca o termo nem a linha: os logs de repositório público são públicos.
$ErrorActionPreference = 'Stop'
if (-not $env:TERMOS) { throw "Segredo TERMOS_PROIBIDOS não configurado no repositório." }

$lista = Join-Path $env:RUNNER_TEMP 'termos.txt'
[IO.File]::WriteAllText($lista, ($env:TERMOS -replace "`r", ''), (New-Object Text.UTF8Encoding($false)))
$problemas = @()

# Arquivos do commit
$arquivos = git grep -l -i -I -F -f $lista HEAD -- .
if ($LASTEXITCODE -gt 1) { throw "git grep falhou" }
$problemas += $arquivos | ForEach-Object { "arquivo " + ($_ -replace '^HEAD:', '') }

# Mensagem do commit
$mensagem = (git log -1 --format=%B) -join "`n"
$termos = $env:TERMOS -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
if ($termos | Where-Object { $mensagem.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 }) { $problemas += "mensagem do commit" }

Remove-Item $lista -Force
if ($problemas.Count -gt 0) {
    $problemas | ForEach-Object { Write-Host "::error::Dado interno encontrado: $_" }
    throw "Trava: $($problemas.Count) lugar(es) com dado interno. Nada foi publicado."
}
Write-Host "Trava: nenhum dado interno nos arquivos nem na mensagem do commit."
