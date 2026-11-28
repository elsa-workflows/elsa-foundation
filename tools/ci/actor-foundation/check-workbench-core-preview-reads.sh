#!/usr/bin/env bash
set -euo pipefail
configuration=${1:-Release}
case "$configuration" in
  Debug|Release) ;;
  *) echo "Choose Debug or Release." >&2; exit 2 ;;
esac
source_pin=23a7c4a718363bc4f60886426551f189ecc95505
checkout=$(cd "${2:-.}" && git -c core.fsmonitor=false rev-parse --show-toplevel)
if [[ "$(git -C "$checkout" -c core.fsmonitor=false rev-parse HEAD)" != "$source_pin" ]]; then
  echo "Supply a checkout at $source_pin as the second argument. See docs/docker.md." >&2
  exit 2
fi
cd "$checkout"
exec bash tools/ci/actor-foundation/check-workbench-core-preview-reads.sh "$configuration"
