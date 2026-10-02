#!/usr/bin/env bash
# Runs Elsa Studio, the workflow designer of Act 3, from a built checkout of its own repository, pointed at the demo's Workbench
# (tools/demo/run-workbench.sh): the browser calls the Workbench directly and signs in through it.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/run-studio.sh [--port PORT] [--backend-port PORT]

  --port PORT          the port Studio listens on, as http://localhost:PORT; default DEMO_PORT_STUDIO, else 5302
  --backend-port PORT  the Workbench's port; default DEMO_PORT_WB, else 5301. Start the Workbench with the same Studio port
                       (run-workbench.sh --studio-port, which defaults to DEMO_PORT_STUDIO too), or it refuses Studio's calls

The Studio checkout is DEMO_STUDIO_DIR, relative to the repository root unless absolute; default ../elsa-foundation-studio-demo.
It must be built:

  cd DEMO_STUDIO_DIR && pnpm install && pnpm build && dotnet build -c Release src/apps/Elsa.Studio.Web

Studio runs from the checkout's src/apps/Elsa.Studio.Web, with its Nuplane state in artifacts/demo/hosts/studio, so it writes
nothing into the checkout (unless its own feature or module management pages are used to change a setting). Sign in with the
Workbench's development admin: SeedAdminUserName and SeedAdminPassword in artifacts/demo/hosts/wb/shells.json.

The process id is recorded in artifacts/demo/pids/studio.pid, which tools/demo/reset.sh stops it by.
USAGE
}

port="${DEMO_PORT_STUDIO:-5302}"
backend_port="${DEMO_PORT_WB:-5301}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) demo_need_value "$@"; port="$2"; shift 2 ;;
    --backend-port) demo_need_value "$@"; backend_port="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) demo_fail "unknown argument '$1' (see --help)." ;;
  esac
done
[[ "$port" =~ ^[0-9]+$ ]] || demo_fail "--port is a number, not '$port'."
[[ "$backend_port" =~ ^[0-9]+$ ]] || demo_fail "--backend-port is a number, not '$backend_port'."

build_commands="cd ${DEMO_STUDIO_DIR:-../elsa-foundation-studio-demo} && pnpm install && pnpm build && dotnet build -c Release src/apps/Elsa.Studio.Web"
[[ -d "$demo_studio_dir" ]] ||
  demo_fail "There is no Studio checkout at ${DEMO_STUDIO_DIR:-../elsa-foundation-studio-demo} (DEMO_STUDIO_DIR). Clone elsa-workflows/elsa-foundation-studio there, or set DEMO_STUDIO_DIR to one, then build it: $build_commands"
web="$demo_studio_dir/src/apps/Elsa.Studio.Web"
[[ -f "$web/bin/Release/net10.0/Elsa.Studio.Web.dll" && -f "$web/wwwroot/studio/index.html" ]] ||
  demo_fail "The Studio checkout at ${DEMO_STUDIO_DIR:-../elsa-foundation-studio-demo} is not built (no bin/Release/net10.0/Elsa.Studio.Web.dll or wwwroot/studio/index.html under src/apps/Elsa.Studio.Web). Build it: $build_commands"

running="$(demo_host_pid studio)"
[[ -z "$running" ]] || demo_fail "Studio is already running (process $running). Stop it (kill $running) before starting it again."
if demo_port_in_use "$port"; then
  demo_fail "Port $port is already in use on 127.0.0.1. Give Studio another --port, or stop what is listening: lsof -nP -iTCP:$port -sTCP:LISTEN"
fi

state="$(demo_dir artifacts/demo/hosts/studio)"
mkdir -p "$demo_logs" "$demo_pids"
echo "studio:         http://localhost:$port"
echo "workbench:      http://localhost:$backend_port (sign in with its development admin)"
echo "state:          ${state#"$demo_root"/}"

# Studio serves its pages and its modules' scripts from the folders its build recorded, by absolute path, in its static web assets
# manifest. A checkout that was built in one folder and then moved (git worktree move) still points at the old one, and Studio then
# fails at start. The same folders are in the checkout, so a copy of the manifest re-based onto it is used instead; the checkout's
# own file is left as it is. A folder that is in neither place means the build is incomplete.
demo_require_python
assets="$(python3 - "$web/bin/Release/net10.0/Elsa.Studio.Web.staticwebassets.runtime.json" "$(cd "$demo_studio_dir" && pwd -P)" "$state" <<'PY'
import json, os, sys

manifest_file, checkout, state = sys.argv[1:4]
if not os.path.isfile(manifest_file):
    sys.exit(0)
manifest = json.load(open(manifest_file))
roots = manifest.get("ContentRoots", [])
if all(os.path.isdir(root) for root in roots):
    sys.exit(0)
marker = "/src/apps/Elsa.Studio.Web/"
built_in = next((root[: root.index(marker)] for root in roots if marker in root), None)
rebased = [checkout + root[len(built_in):] if built_in and root.startswith(built_in + "/") else root for root in roots]
missing = [root for root in rebased if not os.path.isdir(root)]
if built_in is None or missing:
    sys.exit("missing")
manifest["ContentRoots"] = rebased
target = os.path.join(state, "Elsa.Studio.Web.staticwebassets.runtime.json")
with open(target, "w") as out:
    json.dump(manifest, out)
print(target)
PY
)" || demo_fail "The Studio build at ${DEMO_STUDIO_DIR:-../elsa-foundation-studio-demo} names asset folders that are not there. Build it again: $build_commands"
asset_arguments=()
if [[ -n "$assets" ]]; then
  asset_arguments=("--staticWebAssets=$assets")
  echo "assets:         the checkout was built in another folder; a copy of its asset manifest re-based onto it is used (${assets#"$demo_root"/})"
fi

# The browser calls the Workbench directly (its CORS allows this origin), and so does Studio's own server.
export Studio__BackendBaseUrl="http://localhost:$backend_port"
export Studio__BackendServerBaseUrl="http://localhost:$backend_port"
export Studio__Auth__Enabled=true

echo "$$" >"$demo_pids/studio.pid"
cd "$web"
ASPNETCORE_ENVIRONMENT=Development exec dotnet bin/Release/net10.0/Elsa.Studio.Web.dll --contentRoot "$web" --urls "http://localhost:$port" \
  "--Nuplane:Setup:StateFilePath=$state/store-state.json" "--Nuplane:FeedResolution:PackageInstallRoot=$state/packages" \
  ${asset_arguments[@]+"${asset_arguments[@]}"}
