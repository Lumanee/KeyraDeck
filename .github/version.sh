#!/usr/bin/env bash
# Numéro de version affiché (0.0.1, 0.0.2… 0.0.9, 0.1.0…) : chaque publication sur main compte pour un.
# On compte les commits de premier niveau de main (= une fusion de PR, donc une publication) depuis BASE.
# Le numéro de build (github.run_number) reste ce que la mise à jour automatique compare.
set -euo pipefail
BASE=76c0145044cb193f0713fe941cc0f4f09b2bdef9
n=$(git rev-list --count --first-parent "$BASE..HEAD")
v="$((n / 100)).$((n / 10 % 10)).$((n % 10))"
# Les builds d'essai (PR, autres branches) ne sont pas publiés : on le signale dans le numéro.
if [ "${GITHUB_REF:-}" != "refs/heads/main" ]; then v="$v-essai"; fi
echo "$v"
