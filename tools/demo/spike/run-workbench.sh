#!/usr/bin/env bash
# Spike only: runs a copy of the built Elsa.Workbench (Release) as the "designer" backend on its own port, with its own
# Sqlite files, its own Nuplane state and its own package feed (artifacts/spike/hosts/wb/packages), and a shells.json that
# enables the package-loaded features the spike drops into that feed.
#
#   bash tools/demo/spike/run-workbench.sh [--port 7211] [--studio-origin http://localhost:7221] [--prepare-only]
#
# Build first: dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
port=7211
studio_origin="http://localhost:7221"
prepare_only=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) port="$2"; shift 2 ;;
    --studio-origin) studio_origin="$2"; shift 2 ;;
    --prepare-only) prepare_only=1; shift ;;
    *) echo "unknown argument $1" >&2; exit 1 ;;
  esac
done

built="$root/src/apps/Elsa.Workbench/bin/Release/net10.0"
[[ -f "$built/Elsa.Workbench.dll" ]] || { echo "build the Workbench first (Release)" >&2; exit 1; }
host="$root/artifacts/spike/hosts/wb"
mkdir -p "$host/packages" "$root/artifacts/spike/logs" "$root/artifacts/spike/pids"
rsync -a --exclude '.nuplane/' --exclude 'packages/' --exclude 'shells.json' --exclude 'appsettings.Development.json' "$built/" "$host/"

python3 - "$root/src/apps/Elsa.Workbench/shells.json" "$host" "$studio_origin" <<'PY'
import json, os, sys
source, host, studio_origin = sys.argv[1:4]
shells = json.load(open(source))
features = shells["CShells"]["Shells"]["default"]["Features"]
# The package-loaded features the spike drops into the feed. A name no installed package supplies is skipped with a warning.
features["NotesEntityFrameworkCore"] = {"Provider": "Sqlite", "ConnectionString": "Data Source=" + os.path.join(host, "notes.db")}
features["Notes"] = {}
features["NotesWithTags"] = {}
features["NotesActivities"] = {}
# The Studio this spike runs is on its own origin; a post-login returnUrl may go back to it.
identity = features["FoundationIdentityAspNetCoreIdentity"]
identity["AllowedReturnUrlOrigins"] = sorted(set(identity.get("AllowedReturnUrlOrigins", []) + [studio_origin]))
# Not needed for the spike, and it would look for a GitHub token.
features["GitHubCopilotAgent"] = {"Enabled": False}
with open(os.path.join(host, "shells.json"), "w") as out:
    json.dump(shells, out, indent=2)
appsettings = {
    "Logging": {"LogLevel": {"Default": "Information", "Microsoft.AspNetCore": "Warning", "Nuplane": "Information",
                             "Nuplane.Observability.ReconciliationLogger": "Warning",
                             "Microsoft.EntityFrameworkCore": "Warning", "Polly": "Warning"}},
    "Cors": {"AllowedOrigins": [studio_origin]},
    "ConnectionStrings": {"Elsa": "Data Source=" + os.path.join(host, "elsa.db")},
    # Feeds[0] is the Workbench's own drop folder (packages/); the closure feed only resolves what the Notes module needs
    # (its EF engine, which its ef-provider capability names), as in the demo.
    "Nuplane": {"Setup": {"Feeds": [{}, {"Name": "closure", "DirectoryPath": os.path.join(os.path.dirname(os.path.dirname(host)), "closure")}]},
                "Capabilities": {"ef-provider": "Sqlite"}},
    "Elsa": {"Diagnostics": {"ConsoleLogStreaming": {"Enabled": True}},
             "Persistence": {"EntityFramework": {
                 "Migrate": {"Policy": os.environ.get("SPIKE_MIGRATE_POLICY", "AutoMigrate")},
                 "Finalization": {"EvaluationInterval": "00:00:02", "RefreshInterval": "00:00:02"},
                 "Backfill": {"CheckInterval": "00:00:05"}}}},
}
with open(os.path.join(host, "appsettings.Development.json"), "w") as out:
    json.dump(appsettings, out, indent=2)
PY

echo "workbench host: ${host#"$root"/}  url: http://localhost:$port  feed: ${host#"$root"/}/packages"
[[ "$prepare_only" -eq 0 ]] || exit 0
echo "$$" > "$root/artifacts/spike/pids/wb.pid"
cd "$host"
ASPNETCORE_ENVIRONMENT=Development exec dotnet Elsa.Workbench.dll --contentRoot "$host" --urls "http://localhost:$port"
