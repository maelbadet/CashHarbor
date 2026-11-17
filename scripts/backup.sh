#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
if [[ $EUID -ne 0 ]]; then echo 'Exécuter avec sudo.' >&2; exit 1; fi
if [[ $# -ne 1 || $1 != age1* ]]; then echo 'Usage : sudo bash scripts/backup.sh age1<CLE_PUBLIQUE_AGE>' >&2; exit 1; fi
command -v age >/dev/null
command -v docker >/dev/null
docker compose config --quiet
# Pull before stopping the application, so a network error does not extend the downtime.
docker pull alpine:3.23 >/dev/null
umask 077
mkdir -p backups
output="backups/cashharbor-$(date -u +%Y%m%dT%H%M%SZ).tar.age"
temporary="${output}.partial"
restart() { docker compose start app >/dev/null; }
trap restart EXIT
docker compose stop app >/dev/null
if docker run --rm --user 1654:1654 --network none --read-only --cap-drop ALL --security-opt no-new-privileges \
  -v cashharbor_data:/snapshot/data:ro \
  -v cashharbor_keys:/snapshot/keys:ro \
  -v "$PWD/.secrets/keys.pfx:/snapshot/secrets/keys.pfx:ro" \
  -v "$PWD/.secrets/keys-password:/snapshot/secrets/keys-password:ro" \
  alpine:3.23 tar -C /snapshot -cf - data keys secrets | age -r "$1" -o "$temporary"; then
  mv -- "$temporary" "$output"
  echo "Sauvegarde chiffrée : $output"
  echo 'Copier cette archive hors du VPS. La clé privée age doit rester sur une autre machine.'
else
  echo "Sauvegarde échouée, archive partielle inutilisable : $temporary" >&2
  exit 1
fi
