#!/usr/bin/env bash
# Numéro de version affiché : 0.0.1, 0.0.2… 0.0.19, puis 0.1.0, 0.1.1… 0.1.19, puis 0.2.0…
# Chaque publication sur main compte pour un : on compte les commits de premier niveau de main
# (= une fusion de PR, donc une publication) depuis BASE. 20 publications = +1 au chiffre du milieu.
# Le numéro de build (github.run_number) reste ce que la mise à jour automatique compare.
# Usage : version.sh [commit]   (par défaut HEAD ; un commit donné sert à numéroter l'historique)
set -euo pipefail
BASE=76c0145044cb193f0713fe941cc0f4f09b2bdef9
REV="${1:-HEAD}"
n=$(git rev-list --count --first-parent "$BASE..$REV")
v="$((n / 200)).$((n / 20 % 10)).$((n % 20))"
# Les builds d'essai (PR, autres branches) ne sont pas publiés : on le signale dans le numéro.
if [ -z "${1:-}" ] && [ "${GITHUB_REF:-}" != "refs/heads/main" ]; then v="$v-essai"; fi
echo "$v"
