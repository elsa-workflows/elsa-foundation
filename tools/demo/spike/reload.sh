#!/usr/bin/env bash
# Spike only: rebuilds the Workbench spike host's default shell (POST /_admin/shells/reload/default, with the host's
# development management key read from its appsettings.json) and prints the answer.
#
#   bash tools/demo/spike/reload.sh [BASE_URL]
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
base="${1:-http://localhost:7211}"
key_file="$root/artifacts/spike/hosts/wb/.mgmt-key"
python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["Elsa"]["ModuleManagement"]["ApiKey"])' \
  "$root/artifacts/spike/hosts/wb/appsettings.json" >"$key_file"
curl -s -w '\nHTTP %{http_code}\n' -X POST -H "X-Elsa-Module-Management-Key: $(cat "$key_file")" "$base/_admin/shells/reload/default"
