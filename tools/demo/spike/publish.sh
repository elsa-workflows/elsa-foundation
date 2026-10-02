#!/usr/bin/env bash
# Spike only: publishes a staged release of the Notes sample and its Add note activity (1 = 1.0.0, 2 = 1.1.0) into the
# Workbench spike host's feed. Each package is copied beside the feed and renamed into it, so the folder watcher never
# sees a half-written file. Stage the releases first with tools/demo/spike/stage.sh.
#
#   bash tools/demo/spike/publish.sh 1|2 [FEED]
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
release="${1:?release 1 or 2}"
feed="${2:-$root/artifacts/spike/hosts/wb/packages}"
staged="$root/artifacts/spike/staging/notes-$release"
compgen -G "$staged/*.nupkg" >/dev/null || { echo "release $release is not staged: bash tools/demo/spike/stage.sh" >&2; exit 1; }
mkdir -p "$feed"
for package in "$staged"/*.nupkg; do
  cp "$package" "$feed/../.publishing-$(basename "$package")"
  mv -f "$feed/../.publishing-$(basename "$package")" "$feed/$(basename "$package")"
  echo "published $(basename "$package")"
done
