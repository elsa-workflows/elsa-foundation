#!/usr/bin/env bash
# Everything slow, done before the audience arrives: builds the host and the `dotnet elsa` tool, warms the tool up, packs both
# releases of the Notes sample into a staging folder, and fills the closure feed. On stage, tools/demo/publish.sh then copies a
# staged release into a host's feed, which takes seconds where a live pack took 15 to 86 s on a loaded machine. For Act 3 it also
# builds the Workbench and packs both releases of the Add note activity beside the Notes ones.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/prepack.sh [--no-act3]

Builds Elsa.Foundation.Host and the elsa tool from this checkout, warms the tool up, packs release 1.0.0 and release 1.1.0 of
Elsa.Samples.Nuplane.Notes into artifacts/demo/staging/1 and artifacts/demo/staging/2, and fills the closure feed
artifacts/demo/closure. Run it once before presenting, and again after any change to the checkout: the staged packages are
the commit that was checked out when it ran.

Then, for Act 3, it builds Elsa.Workbench (Release) and packs the two releases of Elsa.Samples.Nuplane.Notes.Activities, the
Add note activity, into the same two folders. That part comes after everything Acts 1 and 2 use, which it leaves as it found it.
  --no-act3   skip it: Acts 1 and 2 only (the Workbench build takes about two minutes on a quiet laptop)

Then, on stage:  bash tools/demo/publish.sh <1|2> --host NAME
USAGE
}

act3=1
case "${1:-}" in
  "") ;;
  --no-act3) act3=0 ;;
  -h|--help) usage; exit 0 ;;
  *) demo_fail "unknown argument '$1' (see --help)." ;;
esac
[[ $# -le 1 ]] || demo_fail "unknown argument '$2' (see --help)."

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

staged=()
for release in 1 2; do
  staged+=("$(demo_staged_package "$release")")
done

if [[ "$act3" -eq 1 ]]; then
  act3_started=$SECONDS
  echo "== Act 3: building Elsa.Workbench (Release)"
  demo_quiet "Building Elsa.Workbench" dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release -nologo -v q -p:RestoreLockedMode=true
  # DemoVersion selects the release, as for the Notes project, and the Notes release the activity depends on with it.
  for release in 1 2; do
    echo "== Act 3: packing Elsa.Samples.Nuplane.Notes.Activities, release $release, into ${demo_staging#"$demo_root"/}/$release"
    demo_quiet "Packing the Add note activity" dotnet pack samples/Elsa.Samples.Nuplane.Notes.Activities/Elsa.Samples.Nuplane.Notes.Activities.csproj \
      -c Release -p:DemoVersion="$release" -o "$demo_staging/$release" -nologo -v q -p:RestoreLockedMode=true
    staged+=("$(demo_staged_package "$release" Elsa.Samples.Nuplane.Notes.Activities)")
  done
  echo "   Act 3's part took $((SECONDS - act3_started)) s"
fi

for package in "${staged[@]}"; do
  [[ -f "$package" ]] || demo_fail "A release was not staged: ${package#"$demo_root"/} is missing."
done

echo "== staged"
for package in "${staged[@]}"; do
  echo "     ${package#"$demo_root"/}"
done
[[ "$act3" -eq 1 ]] || echo "   Act 3 skipped (--no-act3): no Workbench build and no Add note packages"
echo "   ready in $((SECONDS - started)) s. On stage: bash tools/demo/publish.sh <1|2> --host NAME"
