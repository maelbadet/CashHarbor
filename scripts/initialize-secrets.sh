#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
if [[ $EUID -ne 0 ]]; then echo "Exécuter avec sudo pour attribuer les secrets à l’UID 1654 du conteneur." >&2; exit 1; fi
if [[ -e .secrets/keys.pfx || -e .secrets/keys-password ]]; then echo "Secrets existants : aucune modification. Ne pas les régénérer." >&2; exit 1; fi
umask 077
mkdir -p .secrets
openssl rand -base64 48 > .secrets/keys-password
openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 3650 -subj '/CN=CashHarbor Key Encryption' -keyout .secrets/temporary.key -out .secrets/temporary.crt
openssl pkcs12 -export -out .secrets/keys.pfx -inkey .secrets/temporary.key -in .secrets/temporary.crt -passout file:.secrets/keys-password
rm -- .secrets/temporary.key .secrets/temporary.crt
chown 1654:1654 .secrets/keys.pfx .secrets/keys-password
chmod 400 .secrets/keys.pfx .secrets/keys-password
echo 'Certificat de protection créé. Sauvegarder les secrets avec les volumes data et keys, dans une archive chiffrée hors du VPS.'
