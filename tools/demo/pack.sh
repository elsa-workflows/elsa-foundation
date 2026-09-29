#!/usr/bin/env bash
# Packs one release of the schema-rollout demo module into the folder feed of each host named, and fills the resolve-only
# closure feed the hosts need for EF Core and the database engine. Everything comes from the checked-out commit, and so does
# the host, which is built first.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat >&2 <<'USAGE'
Usage: bash tools/demo/pack.sh <1|2> [--host NAME]... [--feed DIR]... [--closure DIR] [--no-host]

  1                release 1.0.0 of Elsa.Samples.Nuplane.Notes
  2                release 1.1.0
  --host NAME      pack into the feed of host NAME, artifacts/demo/hosts/NAME/feed, the feed tools/demo/run-host.sh gives it
                   (repeatable, so one command feeds every host). Default: --host a.
  --feed DIR       pack into DIR instead (repeatable); it is created when missing
  --closure DIR    the resolve-only closure feed; default artifacts/demo/closure
  --no-host        do not build Elsa.Foundation.Host first

Relative paths are relative to the repository root. Packing release 2 into the feed release 1 is already in adds a file and
removes none, which the host's folder watcher sees as an install. Pack into one host's feed only, and one host stays on
1.0.0 while the other is given 1.1.0.

The packing is `dotnet pack -o`, never `dotnet nuget push`: push writes a <id>/<version>/ folder layout, which the host's
directory feed cannot read. The feed is flat, `<id>.<version>.nupkg` files side by side.

Needs python3, and the packages the restore already put in the NuGet cache, so the closure needs no network.
USAGE
  exit 2
}

release="${1:-}"
[[ "$release" == "1" || "$release" == "2" ]] || usage
shift

feeds=()
closure="artifacts/demo/closure"
build_host=1
while [[ $# -gt 0 ]]; do
  # Every option but these two takes a value.
  [[ "$1" != --* || "$1" == --no-host || "$1" == --help || $# -ge 2 ]] || usage
  case "$1" in
    --host) feeds+=("artifacts/demo/hosts/$2/feed"); shift 2 ;;
    --feed) feeds+=("$2"); shift 2 ;;
    --closure) closure="$2"; shift 2 ;;
    --no-host) build_host=0; shift ;;
    -h|--help) usage ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done
[[ ${#feeds[@]} -gt 0 ]] || feeds=("artifacts/demo/hosts/a/feed")

demo_require_python
cd "$demo_root"
configuration="Release"
host="src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj"
host_deps="src/apps/Elsa.Foundation.Host/bin/$configuration/net10.0/Elsa.Foundation.Host.deps.json"
sample="samples/Elsa.Samples.Nuplane.Notes/Elsa.Samples.Nuplane.Notes.csproj"
persistence="src/essentials/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj"
tooling="tools/demo/Elsa.Samples.Nuplane.Notes.Tooling/Elsa.Samples.Nuplane.Notes.Tooling.csproj"

resolved=()
for feed in "${feeds[@]}"; do
  resolved+=("$(demo_dir "$feed")")
done
closure="$(demo_dir "$closure")"

if [[ "$build_host" -eq 1 ]]; then
  echo "== building Elsa.Foundation.Host ($configuration)"
  demo_quiet "Building Elsa.Foundation.Host" dotnet build "$host" -c "$configuration" -nologo -v q
fi

# Whether the built host carries Elsa.Persistence.EntityFramework, from its own deps.json.
[[ -f "$host_deps" ]] || demo_fail "There is no built host to read ($host_deps). Run without --no-host, or build the host first."
host_carries_persistence() {
  python3 - "$host_deps" <<'PY'
import json, sys

libraries = json.load(open(sys.argv[1])).get("libraries", {})
sys.exit(0 if any(key.split("/")[0] == "Elsa.Persistence.EntityFramework" for key in libraries) else 1)
PY
}

for feed in "${resolved[@]}"; do
  # DemoVersion selects the release; the package version (1.0.0 or 1.1.0) follows from it in the project file. It is not a
  # -p:Version because a global Version would also be the version the referenced Elsa projects are asked for.
  echo "== packing Elsa.Samples.Nuplane.Notes, release $release, into ${feed#"$demo_root"/}"
  demo_quiet "Packing the Notes sample" dotnet pack "$sample" -c "$configuration" -p:DemoVersion="$release" -o "$feed" -nologo -v q

  # A host that carries Elsa.Persistence.EntityFramework shares it with every package, and `dotnet elsa persistence` finds a
  # module only through that copy. A second copy in the feed would be acquired as a root of its own, and the tool would
  # silently not see Samples.Notes. So it is packed only for a host that does not carry it, and then once: repacking it into
  # a feed the host is watching would replace a file the host has already installed.
  if host_carries_persistence; then
    if compgen -G "$feed/Elsa.Persistence.EntityFramework.*.nupkg" >/dev/null; then
      demo_fail "$feed holds a copy of Elsa.Persistence.EntityFramework from an earlier pack, and this host carries its own. Delete it (rm $feed/Elsa.Persistence.EntityFramework.*.nupkg) and pack again."
    fi
    echo "   Elsa.Persistence.EntityFramework is not packed: the host carries and shares it"
  elif ! compgen -G "$feed/Elsa.Persistence.EntityFramework.*.nupkg" >/dev/null; then
    echo "== packing Elsa.Persistence.EntityFramework into ${feed#"$demo_root"/} (this host does not carry it)"
    demo_quiet "Packing Elsa.Persistence.EntityFramework" dotnet pack "$persistence" -c "$configuration" -p:IsPackable=true -o "$feed" -nologo -v q
  fi
done

# The closure feed: every package EF persistence resolved (EF Core and what it needs) and, from the tooling project's
# restore, the two database engines the module offers and their own closures. Copied from the NuGet cache.
echo "== filling the closure feed ${closure#"$demo_root"/}"
demo_quiet "Restoring the tooling project" dotnet restore "$tooling" -nologo -v q
python3 - "$closure" \
  "$demo_root/src/essentials/Persistence/EntityFramework/obj/project.assets.json" \
  "$demo_root/tools/demo/Elsa.Samples.Nuplane.Notes.Tooling/obj/project.assets.json" <<'PY'
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
print(f"   {len(copied)} packages in {os.path.relpath(closure)}")
PY

for feed in "${resolved[@]}"; do
  echo "== feed: ${feed#"$demo_root"/}"
  ls -1 "$feed" | sed 's/^/     /'
done
