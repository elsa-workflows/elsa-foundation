#!/usr/bin/env bash
# Everything slow, done before the audience arrives: builds the host and the `dotnet elsa` tool, warms the tool up, packs both
# releases of the Notes sample into a staging folder, and fills the closure feed. On stage, tools/demo/publish.sh then copies a
# staged release into a host's feed, which takes seconds where a live pack took 15 to 86 s on a loaded machine.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/prepack.sh

Builds Elsa.Foundation.Host and the elsa tool from this checkout, warms the tool up, packs release 1.0.0 and release 1.1.0 of
Elsa.Samples.Nuplane.Notes into artifacts/demo/staging/1 and artifacts/demo/staging/2, and fills the closure feed
artifacts/demo/closure. Run it once before presenting, and again after any change to the checkout: the staged packages are
the commit that was checked out when it ran.

Then, on stage:  bash tools/demo/publish.sh <1|2> --host NAME
USAGE
}

case "${1:-}" in
  "") ;;
  -h|--help) usage; exit 0 ;;
  *) demo_fail "unknown argument '$1' (see --help)." ;;
esac

demo_require_python
cd "$demo_root"
started=$SECONDS

# Both releases start from an empty staging folder, so a package left by an earlier pack (or an earlier commit) cannot be published.
rm -rf "$demo_staging/1" "$demo_staging/2"

# pack.sh builds the host first (release 1) and fills the closure feed; release 2 needs neither again.
bash tools/demo/pack.sh 1 --feed "$demo_staging/1" --closure "$demo_closure"
bash tools/demo/pack.sh 2 --feed "$demo_staging/2" --closure "$demo_closure" --no-host

# The tool is rebuilt (a stale build would silently not match the host), then run once so the first call on stage is a fast one.
echo "== building and warming up the elsa tool"
ELSA_DEMO_REBUILD=1 bash tools/demo/elsa.sh persistence status --help >/dev/null

for release in 1 2; do
  package="$(demo_staged_package "$release")"
  [[ -f "$package" ]] || demo_fail "Release $release was not staged: ${package#"$demo_root"/} is missing."
done

echo "== staged"
for release in 1 2; do
  echo "     $(demo_staged_package "$release" | sed "s|^$demo_root/||")"
done
echo "   ready in $((SECONDS - started)) s. On stage: bash tools/demo/publish.sh <1|2> --host NAME"
