#!/bin/sh
# Trava contra dados internos (domínio, IPs, nomes de PCs e usuários da empresa) no repositório público.
# Os termos ficam FORA do repositório, num arquivo indicado em:  git config utilitech.termos "C:/caminho/termos-proibidos.txt"
# (um termo por linha, sem diferenciar maiúsculas). Instalar: ferramentas/instalar-trava.ps1
#
# Uso pelo Git:  pre-commit  -> confere as linhas adicionadas no commit
#                commit-msg  -> confere a mensagem do commit (recebe o arquivo da mensagem)

termos=$(git config --get utilitech.termos)
if [ -z "$termos" ] || [ ! -f "$termos" ]; then
  echo "trava-dados-internos: lista de termos não configurada (git config utilitech.termos). Commit liberado SEM conferência." >&2
  exit 0
fi

modo="$1"
if [ "$modo" = "commit-msg" ]; then
  if grep -n -i -F -f "$termos" "$2" >/dev/null 2>&1; then
    echo "BLOQUEADO: a mensagem do commit tem um termo interno (lista em $termos). Reescreva a mensagem." >&2
    exit 1
  fi
  exit 0
fi

# Só as linhas adicionadas (+), sem o cabeçalho "+++ b/arquivo"
achou=0
for arquivo in $(git diff --cached --name-only --diff-filter=ACMR); do
  if git diff --cached -U0 -- "$arquivo" | grep '^+' | grep -v '^+++ ' | grep -i -F -f "$termos" >/dev/null 2>&1; then
    echo "BLOQUEADO: $arquivo tem um termo interno nas linhas adicionadas (lista em $termos)." >&2
    achou=1
  fi
done
exit $achou
