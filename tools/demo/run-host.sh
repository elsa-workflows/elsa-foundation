#!/usr/bin/env bash
# Runs a copy of the built Elsa.Foundation.Host as one demo host: its own directory, its own Nuplane state, its own port,
# the folder feed tools/demo/pack.sh fills, and the Notes module's shell composition.
#
# Usage:
#   bash tools/demo/run-host.sh NAME --port PORT [--provider Sqlite|PostgreSql] [--connection STRING]
#                                    [--policy Validate|AutoMigrate] [--feed DIR] [--closure DIR] [--prepare-only]
#
# The host directory is artifacts/demo/hosts/NAME: the host's build output, an appsettings.Development.json naming the feeds,
# the engine to load and the migrate policy, and a shells.json enabling the Notes features. A host run this way is what
# `dotnet elsa persistence --host artifacts/demo/hosts/NAME --environment Development` reads: the packages the running host
# installed are recorded in artifacts/demo/hosts/NAME/.nuplane/store-state.json, and the engine choice and feeds are in the overlay.
#
# Defaults: Sqlite in artifacts/demo/notes.db, the Validate policy (a stale database refuses the module rather than migrating
# under the operator), --feed artifacts/demo/feed, --closure artifacts/demo/closure. Two hosts that share a database are two
# names given the same --connection.
set -euo pipefail

name="${1:-}"
[[ -n "$name" && "$name" != --* ]] || { sed -n '2,17p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 2; }
shift

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
demo="$root/artifacts/demo"
port=""
provider="Sqlite"
connection=""
policy="Validate"
feed="$demo/feed"
closure="$demo/closure"
prepare_only=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) port="$2"; shift 2 ;;
    --provider) provider="$2"; shift 2 ;;
    --connection) connection="$2"; shift 2 ;;
    --policy) policy="$2"; shift 2 ;;
    --feed) feed="$(cd "$2" && pwd -P)"; shift 2 ;;
    --closure) closure="$(cd "$2" && pwd -P)"; shift 2 ;;
    --prepare-only) prepare_only=1; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$port" ]] || { echo "--port is required" >&2; exit 2; }
[[ -d "$feed" ]] || feed="$(mkdir -p "$feed" && cd "$feed" && pwd -P)"
[[ -d "$closure" ]] || closure="$(mkdir -p "$closure" && cd "$closure" && pwd -P)"
if [[ -z "$connection" ]]; then
  [[ "$provider" == "Sqlite" ]] || { echo "--connection is required for $provider" >&2; exit 2; }
  connection="Data Source=$demo/notes.db;Pooling=False"
fi

source_dir="$root/src/apps/Elsa.Foundation.Host"
built="$source_dir/bin/Release/net10.0"
[[ -f "$built/Elsa.Foundation.Host.dll" ]] || { echo "Build the host first: bash tools/demo/pack.sh 1 (or dotnet build -c Release $source_dir)" >&2; exit 1; }
host="$demo/hosts/$name"
mkdir -p "$host"
rsync -a --exclude '.nuplane/' --exclude 'shells.json' --exclude 'appsettings.Development.json' "$built/" "$host/"

cat > "$host/shells.json" <<EOF
{
  "CShells": {
    "Shells": {
      "default": {
        "Name": "default",
        "Features": {
          "NotesEntityFrameworkCore": { "Provider": "$provider", "ConnectionString": "$connection" },
          "Notes": {},
          "NotesWithTags": {}
        },
        "Configuration": { "WebRouting": { "Path": "" } }
      }
    }
  }
}
EOF

# NotesWithTags is listed from the start: release 1.0.0 has no such feature, so the host says it is not available and
# runs the rest, and the feature appears when release 1.1.0 is installed.
cat > "$host/appsettings.Development.json" <<EOF
{
  "Logging": { "LogLevel": { "Default": "Information", "CShells": "Information", "Nuplane": "Information", "Microsoft.AspNetCore": "Warning", "Microsoft.EntityFrameworkCore.Database.Command": "Warning" } },
  "Nuplane": {
    "Setup": {
      "Feeds": [
        { "Name": "local-packages", "DirectoryPath": "$feed", "IncludePatterns": [ "*" ], "Directory": { "Watch": true, "DebounceWindow": "00:00:01" } },
        { "Name": "closure", "DirectoryPath": "$closure" }
      ]
    },
    "Capabilities": { "ef-provider": "$provider" }
  },
  "Elsa": {
    "Persistence": {
      "EntityFramework": {
        "Migrate": { "Policy": "$policy" },
        "Finalization": { "EvaluationInterval": "00:00:05", "RefreshInterval": "00:00:02" }
      }
    }
  }
}
EOF

echo "host directory: $host"
echo "database:       $provider, $connection"
echo "feed:           $feed"
echo "for the persistence tool:  export ELSA_EF_CONNECTION='$connection'  --host '$host' --provider $provider --environment Development"
[[ "$prepare_only" -eq 0 ]] || exit 0

cd "$host"
ASPNETCORE_ENVIRONMENT=Development exec dotnet Elsa.Foundation.Host.dll --contentRoot "$host" --urls "http://127.0.0.1:$port"
