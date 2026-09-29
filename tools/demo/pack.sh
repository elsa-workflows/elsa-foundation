#!/usr/bin/env bash
# Packs one release of the schema-rollout demo module into a flat folder feed, beside the EF persistence package it
# carries a private copy of, and fills the resolve-only closure feed the host needs for EF Core and the database engine.
# Everything comes from the checked-out commit, and so does the host, which is built first.
#
# Usage:
#   bash tools/demo/pack.sh 1 [--feed DIR] [--closure DIR] [--no-host]   # release 1.0.0
#   bash tools/demo/pack.sh 2 [--feed DIR] [--closure DIR] [--no-host]   # release 1.1.0
#
# Defaults: --feed artifacts/demo/feed, --closure artifacts/demo/closure (artifacts/ is git-ignored).
#
# The packing is `dotnet pack -o`, never `dotnet nuget push`: push writes a <id>/<version>/ folder layout, which the
# host's directory feed cannot read. The feed is flat, `<id>.<version>.nupkg` files side by side, so packing release 2
# into the folder release 1 is already in adds a file and removes none, which the host's folder watcher sees as an install.
#
# Needs python3 (it walks a restore's project.assets.json, as tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostFeed.cs
# does in C#) and the packages the restore already put in the NuGet cache, so the closure needs no network.
set -euo pipefail

release="${1:-}"
[[ "$release" == "1" || "$release" == "2" ]] || { sed -n '2,15p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 2; }
shift

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
feed="artifacts/demo/feed"
closure="artifacts/demo/closure"
build_host=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --feed) feed="$2"; shift 2 ;;
    --closure) closure="$2"; shift 2 ;;
    --no-host) build_host=0; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

configuration="Release"
host="src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj"
sample="samples/Elsa.Samples.Nuplane.Notes/Elsa.Samples.Nuplane.Notes.csproj"
persistence="src/essentials/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj"
tooling="tools/demo/Elsa.Samples.Nuplane.Notes.Tooling/Elsa.Samples.Nuplane.Notes.Tooling.csproj"
mkdir -p "$feed" "$closure"

if [[ "$build_host" -eq 1 ]]; then
  echo "== building Elsa.Foundation.Host ($configuration)"
  dotnet build "$host" -c "$configuration" -nologo -v q
fi

# DemoVersion selects the release; the package version (1.0.0 or 1.1.0) follows from it in the project file. It is not a
# -p:Version because a global Version would also be the version the referenced Elsa projects are asked for.
echo "== packing Elsa.Samples.Nuplane.Notes, release $release, into $feed"
dotnet pack "$sample" -c "$configuration" -p:DemoVersion="$release" -o "$feed" -nologo -v q

# The host carries no EF Core and no Elsa.Persistence.EntityFramework, so the feed supplies the copy the module loads.
# It is packed once: repacking it into a feed the host is watching would replace a file the host has already installed.
if ! compgen -G "$feed/Elsa.Persistence.EntityFramework.*.nupkg" >/dev/null; then
  echo "== packing Elsa.Persistence.EntityFramework into $feed"
  dotnet pack "$persistence" -c "$configuration" -p:IsPackable=true -o "$feed" -nologo -v q
fi

# The closure feed: every package EF persistence resolved (EF Core and what it needs) and, from the tooling project's
# restore, the two database engines the module offers and their own closures. Copied from the NuGet cache.
echo "== filling the closure feed $closure"
dotnet restore "$tooling" -nologo -v q >/dev/null
python3 - "$closure" \
  "$root/src/essentials/Persistence/EntityFramework/obj/project.assets.json" \
  "$root/tools/demo/Elsa.Samples.Nuplane.Notes.Tooling/obj/project.assets.json" <<'PY'
import json, os, shutil, sys

closure, persistence_assets, tooling_assets = sys.argv[1:4]
engines = ["Microsoft.EntityFrameworkCore.Sqlite", "Npgsql.EntityFrameworkCore.PostgreSQL"]

def packages(assets_file, roots):
    """The .nupkg of each root and of everything it depends on, as the restore of assets_file resolved them; every
    package it resolved when no root is named. Each is looked up in every package folder the restore names."""
    assets = json.load(open(assets_file))
    folders = list(assets["packageFolders"])
    libraries = assets["libraries"]
    target = next(iter(assets["targets"].values()))
    by_id = {key.split("/")[0].lower(): key for key in target}
    pending = list(roots) if roots else list(by_id)
    seen = set()
    while pending:
        name = pending.pop().lower()
        key = by_id.get(name)
        if key is None or name in seen or libraries[key]["type"] != "package":
            continue
        seen.add(name)
        library = libraries[key]
        sha = next(f for f in library["files"] if f.endswith(".nupkg.sha512"))
        relative = os.path.join(library["path"], sha[: -len(".sha512")])
        found = next((os.path.join(folder, relative) for folder in folders if os.path.exists(os.path.join(folder, relative))), None)
        if found is None:
            sys.exit(f"Package {key} (from the restore in {assets_file}) has no {os.path.basename(relative)} in {folders}. Restore the project again.")
        yield found
        pending.extend(target[key].get("dependencies", {}))

copied = set()
for path in [*packages(persistence_assets, []), *packages(tooling_assets, engines)]:
    if path not in copied:
        shutil.copyfile(path, os.path.join(closure, os.path.basename(path)))
        copied.add(path)
print(f"   {len(copied)} packages in {closure}")
PY

echo "== feed:    $feed"
ls -1 "$feed" | sed 's/^/     /'
