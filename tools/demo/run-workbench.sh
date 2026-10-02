#!/usr/bin/env bash
# Runs a copy of the built Elsa.Workbench as the demo's designer host (Act 3): its own directory, its own Sqlite database, its own
# Nuplane state, its own port and its own folder feed, with the Notes module and its Add note activity enabled in its shell, and
# Elsa Studio (tools/demo/run-studio.sh) allowed to call it and to sign in through it.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/run-workbench.sh NAME --port PORT [--policy Validate|AutoMigrate] [--studio-port PORT]
                                        [--management-key-env VAR] [--feed DIR] [--closure DIR] [--prepare-only]

The host directory is artifacts/demo/hosts/NAME: the Workbench's build output, a shells.json (the Workbench's own, with the
Notes features and the Add note activity added) and an appsettings.Development.json naming the feeds, the engine to load, the
migrate policy and the Studio origin. `dotnet elsa persistence --host artifacts/demo/hosts/NAME --environment Development`
reads the packages the host installed and its configuration there, as for the Foundation.Host demo hosts.

  --port PORT              the port to listen on, as http://localhost:PORT (required)
  --policy                 the migrate policy; Validate (default) refuses a stale database rather than migrating under the operator
  --studio-port PORT       the port Elsa Studio runs on (default DEMO_PORT_STUDIO, else 5302): the host accepts calls from it
                           (CORS) and sends a signed-in user back to it
  --feed DIR               the host's package feed; default artifacts/demo/hosts/NAME/feed, where `publish.sh --host NAME` publishes
  --closure DIR            the resolve-only feed; default artifacts/demo/closure
  --management-key-env VAR the key of the host's management endpoints, read from the environment variable VAR: POST
                           /_admin/shells/reload/default, with the key in the X-Elsa-Module-Management-Key header, rebuilds the
                           shell from the packages installed. Without it the Workbench's own development key applies
  --prepare-only           write the host directory and stop

Every EF module of the Workbench, its diagnostics and the Notes module included, keeps its tables in one Sqlite file,
artifacts/demo/hosts/NAME/elsa.db. Under Validate a fresh database must be migrated before the host starts: the runbook's setup
does it with `dotnet elsa persistence apply --restore ... --from-host`, which applies every module the shell enables.

The host's process id is recorded in artifacts/demo/pids/NAME.pid, which tools/demo/reset.sh stops it by. A second host of the same
name, or a host on a port that is in use, is refused. Relative paths are relative to the repository root.
USAGE
}

case "${1:-}" in
  -h|--help) usage; exit 0 ;;
  ""|-*) demo_fail "Give the host a name: bash tools/demo/run-workbench.sh NAME --port PORT (see --help)." ;;
esac
name="$1"
shift

port=""
policy="Validate"
studio_port="${DEMO_PORT_STUDIO:-5302}"
feed=""
closure="artifacts/demo/closure"
management_key_env=""
prepare_only=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) demo_need_value "$@"; port="$2"; shift 2 ;;
    --policy) demo_need_value "$@"; policy="$2"; shift 2 ;;
    --studio-port) demo_need_value "$@"; studio_port="$2"; shift 2 ;;
    --feed) demo_need_value "$@"; feed="$2"; shift 2 ;;
    --closure) demo_need_value "$@"; closure="$2"; shift 2 ;;
    --management-key-env) demo_need_value "$@"; management_key_env="$2"; shift 2 ;;
    --prepare-only) prepare_only=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) demo_fail "unknown argument '$1' (see --help)." ;;
  esac
done
[[ -n "$port" ]] || demo_fail "--port is required (see --help)."
[[ "$port" =~ ^[0-9]+$ ]] || demo_fail "--port is a number, not '$port'."
[[ "$studio_port" =~ ^[0-9]+$ ]] || demo_fail "--studio-port is a number, not '$studio_port'."
[[ "$policy" == "Validate" || "$policy" == "AutoMigrate" ]] || demo_fail "--policy is Validate or AutoMigrate, not '$policy'."

demo_require rsync
demo_require_python
host="$(demo_dir "artifacts/demo/hosts/$name")"
feed="$(demo_dir "${feed:-artifacts/demo/hosts/$name/feed}")"
closure="$(demo_dir "$closure")"

management_key=""
if [[ -n "$management_key_env" ]]; then
  management_key="${!management_key_env:-}"
  [[ -n "$management_key" ]] || demo_fail "--management-key-env $management_key_env: that environment variable is not set."
fi

built="$demo_workbench_build"
[[ -f "$built/Elsa.Workbench.dll" ]] || demo_fail "Build the Workbench first: bash tools/demo/prepack.sh (or dotnet build -c Release src/apps/Elsa.Workbench)."

running="$(demo_host_pid "$name")"
[[ -z "$running" ]] || demo_fail "Host $name is already running (process $running). Stop it (kill $running) before starting it again."

rsync -a --exclude '.nuplane/' --exclude 'feed/' --exclude 'packages/' --exclude 'shells.json' --exclude 'appsettings.Development.json' "$built/" "$host/"
mkdir -p "$demo_logs"

# The two files are written by a JSON encoder. The shell is the Workbench's own default shell, plus the package-loaded features
# the demo publishes into the feed: release 1.0.0 has no NotesWithTags, so the host says it is not available and runs the rest,
# and the feature appears with release 1.1.0. Nothing secret is added: the seeded development admin is the Workbench's own.
python3 - "$built/shells.json" "$host" "$policy" "$feed" "$closure" "$studio_port" <<'PY'
import json, os, sys

source, host, policy, feed, closure, studio_port = sys.argv[1:7]
studio = [f"http://localhost:{studio_port}", f"http://127.0.0.1:{studio_port}"]

shells = json.load(open(source))
features = shells["CShells"]["Shells"]["default"]["Features"]
features["NotesEntityFrameworkCore"] = {"Provider": "Sqlite"}
features["Notes"] = {}
features["NotesWithTags"] = {}
features["NotesActivities"] = {}
# The diagnostics keep their tables in the one database too (the shared connection, which telemetry does not fall back to by
# itself), so a single `persistence apply --from-host` migrates all of it.
for diagnostics in ("DiagnosticsOpenTelemetryEntityFrameworkCore", "DiagnosticsStructuredLogsEntityFrameworkCore"):
    features[diagnostics].pop("ConnectionString", None)
    features[diagnostics]["ConnectionName"] = "Elsa"
identity = features["FoundationIdentityAspNetCoreIdentity"]
identity["AllowedReturnUrlOrigins"] = sorted(set(identity.get("AllowedReturnUrlOrigins", []) + studio))
# It looks for a GitHub token in the environment, which the demo has no use for.
features["GitHubCopilotAgent"] = {"Enabled": False}

appsettings = {
    # The reconciliation logger writes a few lines per catalog read, as on the Foundation.Host hosts. The token pruning fails on the
    # in-memory token store of a development Workbench (it cannot ExecuteDelete) and logs a warning of some 17,000 lines at each
    # start, which buried the host tab and the rehearsal's failure output; it is retried hourly and costs nothing else.
    "Logging": {"LogLevel": {"Default": "Information", "Microsoft.AspNetCore": "Warning", "Nuplane": "Information",
                             "Nuplane.Observability.ReconciliationLogger": "Warning",
                             "Elsa.Workbench.WorkbenchOpenIddictPruningService": "Error",
                             "Microsoft.EntityFrameworkCore": "Warning", "Polly": "Warning"}},
    "Cors": {"AllowedOrigins": studio},
    "Nuplane": {
        "Setup": {"Feeds": [
            {"Name": "local-packages", "DirectoryPath": feed, "IncludePatterns": ["*"],
             "Directory": {"Watch": True, "DebounceWindow": "00:00:01"}},
            {"Name": "closure", "DirectoryPath": closure},
        ]},
        "Capabilities": {"ef-provider": "Sqlite"},
    },
    "Elsa": {
        "Diagnostics": {"ConsoleLogStreaming": {"Enabled": True}},
        "Persistence": {"EntityFramework": {
            "Migrate": {"Policy": policy},
            "Finalization": {"EvaluationInterval": "00:00:02", "RefreshInterval": "00:00:02"},
            "Backfill": {"CheckInterval": "00:00:05"},
        }},
    },
}

for file, document in (("shells.json", shells), ("appsettings.Development.json", appsettings)):
    with open(os.path.join(host, file), "w") as out:
        json.dump(document, out, indent=2)
        out.write("\n")
PY

# The connection and the management key reach the host as environment variables of its process, as on the Foundation.Host hosts.
ConnectionStrings__Elsa="$(demo_workbench_connection "$host")"
export ConnectionStrings__Elsa
[[ -z "$management_key" ]] || export Elsa__ModuleManagement__ApiKey="$management_key"

echo "host directory: ${host#"$demo_root"/}"
echo "database:       Sqlite, ${host#"$demo_root"/}/elsa.db"
echo "feed:           ${feed#"$demo_root"/}"
echo "studio:         http://localhost:$studio_port may call it and sign in through it"
[[ -z "$management_key" ]] || echo "management:     POST /_admin/shells/reload/default, key from \$$management_key_env"
echo "persistence:    bash tools/demo/elsa.sh persistence apply --host ${host#"$demo_root"/} --environment Development --provider Sqlite --modules Samples.Notes"
[[ "$prepare_only" -eq 0 ]] || exit 0

if demo_port_in_use "$port"; then
  demo_fail "Port $port is already in use on 127.0.0.1. Give this host another --port, or stop what is listening: lsof -nP -iTCP:$port -sTCP:LISTEN"
fi

mkdir -p "$demo_pids"
echo "$$" >"$demo_pids/$name.pid"

cd "$host"
ASPNETCORE_ENVIRONMENT=Development exec dotnet Elsa.Workbench.dll --contentRoot "$host" --urls "http://localhost:$port"
