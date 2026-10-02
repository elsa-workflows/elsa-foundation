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

Host wb, the Workbench of Act 3, gets both packages of the release: the Notes module and its Add note activity,
Elsa.Samples.Nuplane.Notes.Activities, which is released in step with it (1.0.0 and 1.1.0) and needs a workflow host. Every
other host, and --feed, gets the Notes module only.

The release must have been staged by tools/demo/prepack.sh. Each package is copied beside the feed and then renamed into it, so
the host's folder watcher never sees a half-written file; the Notes module, which the activity depends on, goes first.
USAGE
}

case "${1:-}" in
  -h|--help) usage; exit 0 ;;
  ""|-*) demo_fail "Give the release to publish, 1 or 2, first: bash tools/demo/publish.sh <1|2> --host NAME (see --help)." ;;
esac
release="$1"
package="$(demo_staged_package "$release")" || exit 1
packages=("$package")
shift

feed=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) demo_need_value "$@"; feed="artifacts/demo/hosts/$2/feed"; [[ "$2" != "wb" ]] || packages+=("$(demo_staged_package "$release" Elsa.Samples.Nuplane.Notes.Activities)"); shift 2 ;;
    --feed) demo_need_value "$@"; feed="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) demo_fail "unknown argument '$1' (see --help)." ;;
  esac
done
[[ -n "$feed" ]] || demo_fail "--host or --feed is required (see --help)."

for package in "${packages[@]}"; do
  [[ -f "$package" ]] || demo_fail "Release $release is not staged (${package#"$demo_root"/} is missing). Run: bash tools/demo/prepack.sh"
done

feed="$(demo_dir "$feed")"
if compgen -G "$feed/Elsa.Persistence.EntityFramework.*.nupkg" >/dev/null; then
  demo_fail "$feed holds a copy of Elsa.Persistence.EntityFramework, and the host carries its own. Delete it (rm $feed/Elsa.Persistence.EntityFramework.*.nupkg) before publishing."
fi

# The temporary copy is beside the feed, not in it, so it is on the same volume (the rename is atomic) and never watched. Both
# copies are made before either is renamed, so the two packages of a release land in the feed within the watcher's debounce.
staging_copy="$(mktemp -d "$feed/../.publish.XXXXXX")"
trap 'rm -rf "$staging_copy"' EXIT
cp "${packages[@]}" "$staging_copy/"
for package in "${packages[@]}"; do
  mv -f "$staging_copy/$(basename "$package")" "$feed/"
done
for package in "${packages[@]}"; do
  echo "published $(basename "$package") to ${feed#"$demo_root"/}"
done
