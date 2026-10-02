#!/usr/bin/env bash
# Spike only: builds what the designer spike runs and stages both releases of the Notes sample with its Add note activity.
#
#   bash tools/demo/spike/stage.sh
#
# - builds Elsa.Workbench (Release), the spike's backend;
# - packs Elsa.Samples.Nuplane.Notes and Elsa.Samples.Nuplane.Notes.Activities, release 1 (1.0.0) into
#   artifacts/spike/staging/notes-1 and release 2 (1.1.0) into artifacts/spike/staging/notes-2;
# - fills artifacts/spike/closure, the resolve-only feed the Notes module's EF engine comes from (tools/demo/pack.sh's
#   closure, which needs a built Elsa.Foundation.Host to read; it is built when missing).
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
cd "$root"
quiet() { local log; log="$(mktemp)"; if ! "$@" >"$log" 2>&1; then cat "$log" >&2; rm -f "$log"; exit 1; fi; rm -f "$log"; }

echo "== building Elsa.Workbench (Release)"
quiet dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release -nologo -v q -p:RestoreLockedMode=true
if [[ ! -f src/apps/Elsa.Foundation.Host/bin/Release/net10.0/Elsa.Foundation.Host.deps.json ]]; then
  echo "== building Elsa.Foundation.Host (Release), which the closure feed is read against"
  quiet dotnet build src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj -c Release -nologo -v q -p:RestoreLockedMode=true
fi

for release in 1 2; do
  staged="artifacts/spike/staging/notes-$release"
  rm -rf "$staged"
  echo "== packing release $release into $staged"
  quiet dotnet pack samples/Elsa.Samples.Nuplane.Notes/Elsa.Samples.Nuplane.Notes.csproj -c Release -p:DemoVersion="$release" -o "$staged" -nologo -v q -p:RestoreLockedMode=true
  quiet dotnet pack samples/Elsa.Samples.Nuplane.Notes.Activities/Elsa.Samples.Nuplane.Notes.Activities.csproj -c Release -p:DemoVersion="$release" -o "$staged" -nologo -v q -p:RestoreLockedMode=true
  ls "$staged"
done

echo "== filling the closure feed artifacts/spike/closure"
scratch="$(mktemp -d)"
quiet bash tools/demo/pack.sh 1 --feed "$scratch" --closure artifacts/spike/closure --no-host
rm -rf "$scratch"
echo "staged. Next: bash tools/demo/spike/up.sh"
