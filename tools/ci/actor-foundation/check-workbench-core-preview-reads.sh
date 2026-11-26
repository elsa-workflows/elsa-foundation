#!/usr/bin/env bash
set -euo pipefail
configuration=${1:-Release}
case "$configuration" in
  Debug|Release) ;;
  *) echo "Choose Debug or Release." >&2; exit 2 ;;
esac
root=$(git -c core.fsmonitor=false rev-parse --show-toplevel)
cd "$root"
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
app=server/apps/Elsa.Workbench.Host
runner=server/essentials/tests/ActorFoundation/Native/Run
dotnet build "$app" -c "$configuration" -p:RestoreLockedMode=true -p:WarningsNotAsErrors=NU1603 -p:ElsaDashboardQualification=true -m:3
dotnet build "$runner" -c "$configuration" -p:RestoreLockedMode=true -m:3

export POSTGRES_PASSWORD
POSTGRES_PASSWORD=$(openssl rand -hex 24)
container=$(docker run -d --rm -e POSTGRES_PASSWORD -p 127.0.0.1::5432 "postgres:${PG_VERSION:-17}")
trap 'docker stop --time 3 "$container" >/dev/null 2>&1 || true' EXIT
for ((attempt=0; attempt<60; attempt++)); do
  if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
  sleep .25
done
docker exec "$container" createdb -U postgres workbench_preview_reads
port=$(docker port "$container" 5432/tcp)
port=${port##*:}
[[ -n "$port" && "$port" != 5095 ]]
export KERNEL_PREVIEW_POSTGRES="Host=127.0.0.1;Port=$port;Username=postgres;Password=$POSTGRES_PASSWORD;Database=workbench_preview_reads;SSL Mode=Disable;GSS Encryption Mode=Disable"
python3 tools/ci/actor-foundation/workbench_core_preview_reads.py \
  "$root/$app/bin/$configuration/net10.0/Elsa.Workbench.Host" "$root/$runner/bin/$configuration/net10.0/Kernel.Native.Run"
