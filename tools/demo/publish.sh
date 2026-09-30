#!/usr/bin/env bash
# The demo's "publish to the local NuGet feed" step: copies a release that tools/demo/prepack.sh staged into a host's folder
# feed. The host's folder watcher sees the new package and installs it at runtime.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/publish.sh <1|2> --host NAME [--feed DIR]

  1               release 1.0.0 of Elsa.Samples.Nuplane.Notes
  2               release 1.1.0
  --host NAME     publish into the feed of host NAME, artifacts/demo/hosts/NAME/feed, the feed tools/demo/run-host.sh gives it
  --feed DIR      publish into DIR instead of a host's feed (relative paths are relative to the repository root)

The release must have been staged by tools/demo/prepack.sh. The package is copied beside the feed and then renamed into it, so
the host's folder watcher never sees a half-written file.
USAGE
}

case "${1:-}" in
  -h|--help) usage; exit 0 ;;
  ""|-*) demo_fail "Give the release to publish, 1 or 2, first: bash tools/demo/publish.sh <1|2> --host NAME (see --help)." ;;
esac
release="$1"
package="$(demo_staged_package "$release")" || exit 1
shift

feed=""
while [[ $# -gt 0 ]]; do
  [[ "$1" != --* || "$1" == --help || $# -ge 2 ]] || demo_fail "$1 needs a value (see --help)."
  case "$1" in
    --host) feed="artifacts/demo/hosts/$2/feed"; shift 2 ;;
    --feed) feed="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) demo_fail "unknown argument '$1' (see --help)." ;;
  esac
done
[[ -n "$feed" ]] || demo_fail "--host or --feed is required (see --help)."

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
