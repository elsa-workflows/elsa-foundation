#!/usr/bin/env bash
set -euo pipefail
configuration=${1:-Release}
case "$configuration" in
  Debug|Release) ;;
  *) echo "Choose Debug or Release." >&2; exit 2 ;;
esac
root=$(git -c core.fsmonitor=false rev-parse --show-toplevel)
cd "$root"
app=apps/Elsa.Workbench
runner=essentials/tests/ActorFoundation/Native/Run
dotnet restore --locked-mode "$app"
dotnet restore --locked-mode "$runner"
dotnet build -c "$configuration" --no-restore "$app"
dotnet build -c "$configuration" --no-restore "$runner"
KERNEL_PREVIEW_WORKBENCH="$root/$app/bin/$configuration/net10.0/Elsa.Workbench.dll"
export KERNEL_PREVIEW_WORKBENCH
dll="$runner/bin/$configuration/net10.0/Kernel.Native.Run.dll"

scratch=$(mktemp -d "${TMPDIR:-/tmp}/elsa-core-preview.XXXXXXXX")
container=
cleanup() {
  if [[ -n "$container" ]]; then docker stop --time 3 "$container" >/dev/null || true; fi
  rm -rf -- "$scratch"
}
trap cleanup EXIT

export POSTGRES_PASSWORD
POSTGRES_PASSWORD=$(openssl rand -hex 24)
container=$(docker run -d --rm -e POSTGRES_PASSWORD -p 127.0.0.1::5432 "postgres:${PG_VERSION:-17}")
for ((attempt=0; attempt<60; attempt++)); do
  if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
  sleep 0.25
done
docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null
port=$(docker port "$container" 5432/tcp)
port=${port##*:}
[[ -n "$port" && "$port" != 5095 ]]
docker exec "$container" createdb -U postgres workbench_core_preview
export KERNEL_PREVIEW_POSTGRES="Host=127.0.0.1;Port=$port;Username=postgres;Password=$POSTGRES_PASSWORD;Database=workbench_core_preview;SSL Mode=Disable;GSS Encryption Mode=Disable"
for phase in disabled birth restart; do
  dotnet "$dll" pg-workbench-core-preview "$phase" "$scratch/waiting.json"
done
