#!/usr/bin/env bash
# Spike only: signs in to a backend with the development admin seeded by its shells.json and prints a bearer token for it.
#
#   bash tools/demo/spike/token.sh [BASE_URL] [HOST_DIR]
#
# BASE_URL defaults to http://localhost:7211 and HOST_DIR to artifacts/spike/hosts/wb (the shells.json the user and password
# are read from, so nothing secret is written here). The cookie jar lives in the host directory.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
base="${1:-http://localhost:7211}"
host="${2:-$root/artifacts/spike/hosts/wb}"
read -r user password < <(python3 - "$host/shells.json" <<'PY'
import json, sys
features = json.load(open(sys.argv[1]))["CShells"]["Shells"]["default"]["Features"]
seed = next(v for k, v in features.items() if k.startswith("FoundationIdentityAspNetCoreIdentity") and isinstance(v, dict) and "SeedAdminUserName" in v)
print(seed["SeedAdminUserName"], seed["SeedAdminPassword"])
PY
)
jar="$host/.spike-cookies"
body="$(python3 -c 'import json,sys; print(json.dumps({"username": sys.argv[1], "password": sys.argv[2]}))' "$user" "$password")"
code="$(curl -s -o /dev/null -w '%{http_code}' -c "$jar" -b "$jar" -H 'Content-Type: application/json' -d "$body" "$base/_elsa/identity/login")"
[[ "$code" == 200 ]] || { echo "login failed: HTTP $code" >&2; exit 1; }
curl -s -b "$jar" -c "$jar" "$base/_elsa/identity/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])'
