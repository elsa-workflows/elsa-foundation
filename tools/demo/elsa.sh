#!/usr/bin/env bash
# `dotnet elsa`, built from this checkout: builds src/essentials/Cli and runs it with the arguments given.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat >&2 <<'USAGE'
Usage: bash tools/demo/elsa.sh <dotnet elsa arguments>

  ELSA_EF_CONNECTION='Data Source=...' bash tools/demo/elsa.sh persistence apply \
    --host artifacts/demo/hosts/a --environment Development --provider Sqlite --modules Samples.Notes

The connection travels in ELSA_EF_CONNECTION, never as an argument. When it is unset the tool gets the default Sqlite file
(artifacts/demo/notes.db), the one tools/demo/run-host.sh gives a host; a PostgreSql database always needs it set.

The tool runs inside the host's own dependency closure and reads the packages that host installed, so run it against a host
directory tools/demo/run-host.sh made, after that host has reconciled its feed at least once, or with --restore.
The tool is built once; delete src/essentials/Cli/bin, or set ELSA_DEMO_REBUILD=1, after changing it.
USAGE
  exit 2
}

[[ $# -gt 0 && "$1" != -h && "$1" != --help ]] || usage

cli="$demo_root/src/essentials/Cli/bin/Release/net10.0/Elsa.Cli.dll"
if [[ ! -f "$cli" || -n "${ELSA_DEMO_REBUILD:-}" ]]; then
  demo_quiet "Building the elsa tool" dotnet build "$demo_root/src/essentials/Cli/Elsa.Cli.csproj" -c Release -nologo -v q
fi
export ELSA_EF_CONNECTION="${ELSA_EF_CONNECTION:-$demo_sqlite_connection}"
# Relative paths in the arguments are relative to the repository root, as in every demo script.
cd "$demo_root"
exec dotnet exec "$cli" "$@"
