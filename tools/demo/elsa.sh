#!/usr/bin/env bash
# `dotnet elsa`, built from this checkout: builds src/essentials/Cli and runs it with the arguments given.
#
#   ELSA_EF_CONNECTION='Data Source=...' bash tools/demo/elsa.sh persistence apply \
#     --host artifacts/demo/hosts/a --environment Development --provider Sqlite --modules Samples.Notes
#
# The tool runs inside the host's own dependency closure and reads the packages that host installed, so run it against a host
# directory tools/demo/run-host.sh made, after that host has reconciled its feed at least once.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cli="$root/src/essentials/Cli/bin/Release/net10.0/Elsa.Cli.dll"
# Built once; delete the file, or set ELSA_DEMO_REBUILD=1, after changing the tool.
if [[ ! -f "$cli" || -n "${ELSA_DEMO_REBUILD:-}" ]]; then
  dotnet build "$root/src/essentials/Cli/Elsa.Cli.csproj" -c Release -nologo -v q >/dev/null
fi
exec dotnet exec "$cli" "$@"
