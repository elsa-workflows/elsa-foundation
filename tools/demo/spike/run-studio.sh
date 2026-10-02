#!/usr/bin/env bash
# Spike only: runs Elsa Studio (a checkout of elsa-foundation-studio, built with `pnpm install && pnpm build` and
# `dotnet build -c Release src/apps/Elsa.Studio.Web`) against a spike backend.
#
#   bash tools/demo/spike/run-studio.sh [--port 7221] [--backend http://localhost:7211] [--studio-dir DIR]
#
# DIR defaults to $SPIKE_STUDIO_DIR, then to the spike's own Studio worktree, artifacts/spike/studio. Sign in with the backend's
# development admin (the user and password its shells.json seeds).
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
port=7221
backend="http://localhost:7211"
studio="${SPIKE_STUDIO_DIR:-$root/artifacts/spike/studio}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) port="$2"; shift 2 ;;
    --backend) backend="$2"; shift 2 ;;
    --studio-dir) studio="$2"; shift 2 ;;
    *) echo "unknown argument $1" >&2; exit 1 ;;
  esac
done
web="$studio/src/apps/Elsa.Studio.Web"
[[ -f "$web/bin/Release/net10.0/Elsa.Studio.Web.dll" ]] || { echo "build Studio first: (cd $studio && pnpm install && pnpm build && dotnet build -c Release src/apps/Elsa.Studio.Web)" >&2; exit 1; }
mkdir -p "$root/artifacts/spike/pids" "$root/artifacts/spike/hosts/studio-$port"
echo "studio: http://localhost:$port  backend: $backend"
echo "$$" > "$root/artifacts/spike/pids/studio-$port.pid"
cd "$web"
# Studio keeps its Nuplane state under its content root; give each spike Studio its own.
export Nuplane__Setup__StateFilePath="$root/artifacts/spike/hosts/studio-$port/store-state.json"
export Studio__BackendBaseUrl="$backend"
export Studio__BackendServerBaseUrl="$backend"
export Studio__Auth__Enabled=true
ASPNETCORE_ENVIRONMENT=Development exec dotnet bin/Release/net10.0/Elsa.Studio.Web.dll --contentRoot "$web" --urls "http://localhost:$port"
