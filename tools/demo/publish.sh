#!/usr/bin/env bash
# The demo's "publish to the local NuGet feed" step: copies a release that tools/demo/prepack.sh staged into a host's folder
# feed. The host's folder watcher sees the new package and installs it at runtime.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat >&2 <<'USAGE'
Usage: bash tools/demo/publish.sh <1|2> --host NAME [--feed DIR]

  1               release 1.0.0 of Elsa.Samples.Nuplane.Notes
  2               release 1.1.0
  --host NAME     publish into the feed of host NAME, artifacts/demo/hosts/NAME/feed, the feed tools/demo/run-host.sh gives it
  --feed DIR      publish into DIR instead of a host's feed (relative paths are relative to the repository root)

The release must have been staged by tools/demo/prepack.sh. The package is copied under a name the watcher does not match and
then renamed into place, so the host never sees a half-written file.
USAGE
  exit 2
}

release="${1:-}"
[[ "$release" == "1" || "$release" == "2" ]] || usage
shift

feed=""
while [[ $# -gt 0 ]]; do
  [[ "$1" != --* || "$1" == --help || $# -ge 2 ]] || usage
  case "$1" in
    --host) feed="artifacts/demo/hosts/$2/feed"; shift 2 ;;
    --feed) feed="$2"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done
[[ -n "$feed" ]] || { echo "--host is required" >&2; usage; }

version="$(demo_release_version "$release")"
package="$demo_staging/$release/Elsa.Samples.Nuplane.Notes.$version.nupkg"
[[ -f "$package" ]] || demo_fail "Release $release is not staged (${package#"$demo_root"/} is missing). Run: bash tools/demo/prepack.sh"

feed="$(demo_dir "$feed")"
if compgen -G "$feed/Elsa.Persistence.EntityFramework.*.nupkg" >/dev/null; then
  demo_fail "$feed holds a copy of Elsa.Persistence.EntityFramework, and the host carries its own. Delete it (rm $feed/Elsa.Persistence.EntityFramework.*.nupkg) before publishing."
fi

# The temporary copy is beside the feed, not in it, so it is on the same volume (the rename is atomic) and never watched.
staging_copy="$(mktemp -d "$feed/../.publish.XXXXXX")"
trap 'rm -rf "$staging_copy"' EXIT
cp "$package" "$staging_copy/"
mv -f "$staging_copy/$(basename "$package")" "$feed/"
echo "published $(basename "$package") to ${feed#"$demo_root"/}"
