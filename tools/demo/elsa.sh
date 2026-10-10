#!/usr/bin/env bash
# `dotnet elsa`, built from this checkout: builds src/essentials/Cli/Elsa.Cli and runs it with the arguments given.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/elsa.sh <dotnet elsa arguments>

  ELSA_EF_CONNECTION='Data Source=...' bash tools/demo/elsa.sh persistence apply \
    --host artifacts/demo/hosts/a --environment Development --provider Sqlite --modules Samples.Notes

The connection travels in ELSA_EF_CONNECTION, never as an argument. When it is unset the tool gets the default Sqlite file
(artifacts/demo/notes.db), the one tools/demo/run-host.sh gives a host; a PostgreSql database always needs it set.
A Workbench host directory that tools/demo/run-workbench.sh made (Act 3) keeps its own Sqlite file, artifacts/demo/hosts/NAME/elsa.db,
and the tool gets that one for --host artifacts/demo/hosts/NAME. ELSA_EF_CONNECTION must then be unset: a connection set in the
tab for another act would point the tool at a database that host does not use, so it is refused.

The tool runs inside the host's own dependency closure and reads the packages that host installed, so run it against a host
directory tools/demo/run-host.sh made, after that host has reconciled its feed at least once, or with --restore.
The tool is built once; delete src/essentials/Cli/Elsa.Cli/bin, or set ELSA_DEMO_REBUILD=1, after changing it.
USAGE
}

[[ $# -gt 0 ]] || demo_fail "Give the dotnet elsa arguments to run, e.g. persistence status (see --help)."
[[ "$1" != -h && "$1" != --help ]] || { usage; exit 0; }

cli="$demo_root/src/essentials/Cli/Elsa.Cli/bin/Release/net10.0/Elsa.Cli.dll"
if [[ ! -f "$cli" || -n "${ELSA_DEMO_REBUILD:-}" ]]; then
  demo_quiet "Building the elsa tool" dotnet build "$demo_root/src/essentials/Cli/Elsa.Cli/Elsa.Cli.csproj" -c Release -nologo -v q
fi
host_dir=""
previous=""
for argument in "$@"; do
  [[ "$previous" != "--host" ]] || host_dir="$argument"
  previous="$argument"
done
if [[ -n "$host_dir" ]] && demo_is_workbench_host "$host_dir"; then
  [[ -z "${ELSA_EF_CONNECTION:-}" ]] ||
    demo_fail "$host_dir is a Workbench host, which keeps its database in its own folder (elsa.db), and this tab has ELSA_EF_CONNECTION set (for another act). Run it where ELSA_EF_CONNECTION is unset (unset ELSA_EF_CONNECTION)."
  [[ "$host_dir" == /* ]] || host_dir="$demo_root/$host_dir"
  ELSA_EF_CONNECTION="$(demo_workbench_connection "$host_dir")"
fi
export ELSA_EF_CONNECTION="${ELSA_EF_CONNECTION:-$demo_sqlite_connection}"
# Relative paths in the arguments are relative to the repository root, as in every demo script.
cd "$demo_root"
exec dotnet exec "$cli" "$@"
