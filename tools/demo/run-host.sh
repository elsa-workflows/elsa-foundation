#!/usr/bin/env bash
# Runs a copy of the built Elsa.Foundation.Host as one demo host: its own directory, its own Nuplane state, its own port,
# its own folder feed, and the Notes module's shell composition.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat >&2 <<'USAGE'
Usage: bash tools/demo/run-host.sh NAME --port PORT [--provider Sqlite|PostgreSql] [--policy Validate|AutoMigrate]
                                   [--cluster HOSTID [--fast-membership]] [--management-key-env VAR]
                                   [--feed DIR] [--closure DIR] [--prepare-only]

The host directory is artifacts/demo/hosts/NAME: the host's build output, an appsettings.Development.json naming the feeds,
the engine to load and the migrate policy, and a shells.json enabling the Notes features. `dotnet elsa persistence
--host artifacts/demo/hosts/NAME --environment Development` reads the packages the running host installed, recorded in
.nuplane/store-state.json there, and the engine choice and feeds in the overlay.

  --port PORT              the port to listen on, on 127.0.0.1 (required)
  --provider               Sqlite (default) or PostgreSql
  --policy                 the migrate policy; Validate (default) refuses a stale database rather than migrating under the operator
  --feed DIR               the host's package feed; default artifacts/demo/hosts/NAME/feed, where `pack.sh --host NAME` packs
  --closure DIR            the resolve-only feed; default artifacts/demo/closure
  --cluster HOSTID         join the durable EF cluster membership under this host id, so hosts that share the database
                           count each other before a new schema version is finalized. Every host of a database gives the
                           same connection and its own id. Needs a host build that carries EF cluster membership.
  --fast-membership        with --cluster: a 2 s heartbeat, a 10 s expiry and a 2 s skew allowance (the defaults are 10 s, 30 s
                           and 5 s). A host stopped cleanly (Ctrl-C) leaves at once and needs no wait; a crashed or stalled host
                           is dropped only after the expiry, so one restarted after a crash rejoins in about 12 s instead of
                           35 s. For the demo only; every host of a database can use its own timings
  --management-key-env VAR enable the module-management endpoints, with the key read from the environment variable VAR:
                           POST /_module-management/reload, with the key in the X-Elsa-Module-Management-Key header,
                           re-composes the shells
  --prepare-only           write the host directory and stop

The database connection is taken from ELSA_EF_CONNECTION, the variable `dotnet elsa persistence` reads, and handed to the
host as an environment variable: it is never written to disk or printed. When it is unset, Sqlite uses the default file,
artifacts/demo/notes.db. Two hosts that share a database run with the same ELSA_EF_CONNECTION.
Relative paths are relative to the repository root.
USAGE
  exit 2
}

name="${1:-}"
[[ -n "$name" && "$name" != -* ]] || usage
shift

port=""
provider="Sqlite"
policy="Validate"
feed=""
closure="artifacts/demo/closure"
cluster_host_id=""
fast_membership=0
management_key_env=""
prepare_only=0
while [[ $# -gt 0 ]]; do
  # Every option but these three takes a value.
  [[ "$1" != --* || "$1" == --prepare-only || "$1" == --fast-membership || "$1" == --help || $# -ge 2 ]] || usage
  case "$1" in
    --port) port="$2"; shift 2 ;;
    --provider) provider="$2"; shift 2 ;;
    --policy) policy="$2"; shift 2 ;;
    --feed) feed="$2"; shift 2 ;;
    --closure) closure="$2"; shift 2 ;;
    --cluster) cluster_host_id="$2"; shift 2 ;;
    --management-key-env) management_key_env="$2"; shift 2 ;;
    --fast-membership) fast_membership=1; shift ;;
    --prepare-only) prepare_only=1; shift ;;
    -h|--help) usage ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done
[[ -n "$port" ]] || { echo "--port is required" >&2; usage; }
[[ "$provider" == "Sqlite" || "$provider" == "PostgreSql" ]] || demo_fail "--provider is Sqlite or PostgreSql, not '$provider'."
[[ "$fast_membership" -eq 0 || -n "$cluster_host_id" ]] || demo_fail "--fast-membership needs --cluster."

demo_require_python
host="$(demo_dir "artifacts/demo/hosts/$name")"
feed="$(demo_dir "${feed:-artifacts/demo/hosts/$name/feed}")"
closure="$(demo_dir "$closure")"

if [[ -n "${ELSA_EF_CONNECTION:-}" ]]; then
  connection="$ELSA_EF_CONNECTION"
  database="$provider, from ELSA_EF_CONNECTION"
elif [[ "$provider" == "Sqlite" ]]; then
  connection="$demo_sqlite_connection"
  database="Sqlite, the default file ${demo_sqlite_file#"$demo_root"/}"
else
  demo_fail "$provider needs its connection string in ELSA_EF_CONNECTION."
fi

management_key=""
if [[ -n "$management_key_env" ]]; then
  management_key="${!management_key_env:-}"
  [[ -n "$management_key" ]] || demo_fail "--management-key-env $management_key_env: that environment variable is not set."
fi

source_dir="$demo_root/src/apps/Elsa.Foundation.Host"
built="$source_dir/bin/Release/net10.0"
[[ -f "$built/Elsa.Foundation.Host.dll" ]] || demo_fail "Build the host first: bash tools/demo/pack.sh 1 (or dotnet build -c Release $source_dir)."
if [[ -n "$cluster_host_id" && ! -f "$built/Elsa.Cluster.EntityFrameworkCore.dll" ]]; then
  demo_fail "--cluster needs a host build that carries the EF cluster membership provider (Elsa.Cluster.EntityFrameworkCore.dll), and this build does not."
fi

# One host per name: the copy below rewrites the directory a running host is using.
running="$(demo_host_pid "$name")"
[[ -z "$running" ]] || demo_fail "Host $name is already running (process $running). Stop it (kill $running) before starting it again."

rsync -a --exclude '.nuplane/' --exclude 'feed/' --exclude 'shells.json' --exclude 'appsettings.Development.json' "$built/" "$host/"

# The two files are written by a JSON encoder from the values above; the connection is in neither. NotesWithTags is listed from
# the start: release 1.0.0 has no such feature, so the host says it is not available and runs the rest, and the feature
# appears when release 1.1.0 is installed. The feature's connection is ConnectionStrings:Elsa, which the host reads from
# its environment (below), where an explicit shells.json value would outrank it.
python3 - "$host" "$provider" "$policy" "$feed" "$closure" <<'PY'
import json, os, sys

host, provider, policy, feed, closure = sys.argv[1:6]

shells = {"CShells": {"Shells": {"default": {
    "Name": "default",
    "Features": {"NotesEntityFrameworkCore": {"Provider": provider}, "Notes": {}, "NotesWithTags": {}},
    "Configuration": {"WebRouting": {"Path": ""}},
}}}}

appsettings = {
    "Logging": {"LogLevel": {"Default": "Information", "CShells": "Information", "Nuplane": "Information",
                             "Microsoft.AspNetCore": "Warning", "Microsoft.EntityFrameworkCore.Database.Command": "Warning"}},
    "Nuplane": {
        "Setup": {"Feeds": [
            {"Name": "local-packages", "DirectoryPath": feed, "IncludePatterns": ["*"],
             "Directory": {"Watch": True, "DebounceWindow": "00:00:01"}},
            {"Name": "closure", "DirectoryPath": closure},
        ]},
        "Capabilities": {"ef-provider": provider},
    },
    "Elsa": {"Persistence": {"EntityFramework": {
        "Migrate": {"Policy": policy},
        "Finalization": {"EvaluationInterval": "00:00:05", "RefreshInterval": "00:00:02"},
        "Backfill": {"CheckInterval": "00:00:05"},
    }}},
}

for file, document in (("shells.json", shells), ("appsettings.Development.json", appsettings)):
    with open(os.path.join(host, file), "w") as out:
        json.dump(document, out, indent=2)
        out.write("\n")
PY

# Everything secret reaches the host as environment variables of its process, and nowhere else.
export ConnectionStrings__Elsa="$connection"
if [[ -n "$cluster_host_id" ]]; then
  export Elsa__Cluster__Membership__HostId="$cluster_host_id"
  export Elsa__Cluster__Membership__EntityFrameworkCore__Enabled=true
  export Elsa__Cluster__Membership__EntityFrameworkCore__Provider="$provider"
  export Elsa__Cluster__Membership__EntityFrameworkCore__ConnectionString="$connection"
  if [[ "$fast_membership" -eq 1 ]]; then
    export Elsa__Cluster__Membership__HeartbeatInterval=00:00:02
    export Elsa__Cluster__Membership__ExpiryPeriod=00:00:10
    export Elsa__Cluster__Membership__SkewAllowance=00:00:02
  fi
fi
if [[ -n "$management_key" ]]; then
  export Elsa__ModuleManagement__Enabled=true
  export Elsa__ModuleManagement__ApiKey="$management_key"
fi

modules="Samples.Notes"
[[ -z "$cluster_host_id" ]] || modules="$modules,Cluster.Membership"
echo "host directory: ${host#"$demo_root"/}"
echo "database:       $database"
echo "feed:           ${feed#"$demo_root"/}"
if [[ -n "$cluster_host_id" ]]; then
  timing="default timings (a crashed host's entry lingers up to 35 s)"
  [[ "$fast_membership" -eq 0 ]] || timing="fast timings (2 s heartbeat, 10 s expiry, 2 s skew)"
  echo "cluster:        EF membership, host id $cluster_host_id, $timing"
fi
[[ -z "$management_key" ]] || echo "management:     POST /_module-management/reload, key from \$$management_key_env"
echo "persistence:    bash tools/demo/elsa.sh persistence apply --host ${host#"$demo_root"/} --environment Development --provider $provider --modules $modules"
[[ "$prepare_only" -eq 0 ]] || exit 0

# One host per port. A second start would otherwise fail deep in the host's own log.
if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
  demo_fail "Port $port is already in use on 127.0.0.1. Give this host another --port, or stop what is listening: lsof -nP -iTCP:$port -sTCP:LISTEN"
fi

# The pid file is what tools/demo/reset.sh stops the host by; the shell becomes the host below, so $$ is the host's process.
mkdir -p "$demo_pids"
echo "$$" >"$demo_pids/$name.pid"

cd "$host"
ASPNETCORE_ENVIRONMENT=Development exec dotnet Elsa.Foundation.Host.dll --contentRoot "$host" --urls "http://127.0.0.1:$port"
