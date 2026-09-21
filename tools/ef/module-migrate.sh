#!/usr/bin/env bash
# Out-of-process migration apply, validation and SQL scripting for every first-party EF module context.
#
# Usage:
#   bash tools/ef/module-migrate.sh pending [context-regex]
#   bash tools/ef/module-migrate.sh apply <Sqlite|SqlServer|PostgreSql|MySql> [--connection-env NAME|--connection-stdin] [modules]
#   bash tools/ef/module-migrate.sh validate <Sqlite|SqlServer|PostgreSql|MySql> [--connection-env NAME|--connection-stdin] [modules]
#   bash tools/ef/module-migrate.sh script <Sqlite|SqlServer|PostgreSql|MySql> <output-dir> [modules]
#   bash tools/ef/module-migrate.sh script-check <output-dir>
#
# `apply`, `validate`, `script` and `script-check` are thin shims (#1878) over the `dotnet elsa persistence`
# CLI (`src/Elsa/Cli`, spec 171): this script builds that CLI and the tooling project below — which
# references every first-party module and every provider engine, the same project `pending` already builds —
# and runs the CLI against that project's own build output as its `--host`. `pending` alone still drives
# `dotnet ef` directly: it needs no host closure, only the tooling project's own compiled model, and the new
# CLI has no equivalent command (migration generation still needs a source project either way).
#
# `modules` selects by the CLI's own canonical module names (`dotnet elsa persistence list`), comma-separated
# or repeated; the default is every module (`--all`). This replaces a regex over DbContext class names: the
# new CLI takes no such regex — a regex was never a name an operator could put in a deployment manifest or a
# support ticket — and nothing in this repository passed one.
#
# ELSA_EF_SCHEMA applies and scripts into the schema a host configured, same as before: the CLI itself reads
# it when `--schema` is not given, so this script does not have to.
#
# The connection is never a command-line argument: --connection-env NAME (default ELSA_EF_CONNECTION) reads
# it from that environment variable, and --connection-stdin reads it from this script's own stdin. Both flags
# travel straight through to the CLI's own identical flags, unexamined: this script never reads or holds the
# connection value itself, and neither does the CLI's front end — only the worker process does, inside the
# host closure. There is no --connection flag.
#
# script writes flat, numbered SQL files plus one migration-plan.json into <output-dir> (the CLI's own
# layout) — no longer nested under <Module>/<Provider>.sql, the layout this script wrote before #1878.
# SQLite is refused there, by the CLI, with the same alternative this script used to name itself.
# script-check regenerates <output-dir>'s own committed plan and diffs against it; the CLI decides the
# provider and the modules from that plan, so this command takes no provider or module selector of its own.
set -euo pipefail

invocation_dir="$PWD"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
tooling="tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj"
tooling_dir="tools/ef/Elsa.EntityFrameworkCore.Tooling"
cli_project="src/Elsa/Cli/Elsa.Cli.csproj"
cli_dir="src/Elsa/Cli"
# Required modules live under src/ and optional ones under extensions/ (#1815). Roots are filtered to
# the ones that exist: `find` exits non-zero on a missing directory, and under `set -e` that would end
# the run instead of reporting the one module it could not resolve.
module_roots=(src)
if [[ -d extensions ]]; then module_roots+=(extensions); fi
configuration="${ELSA_EF_CONFIGURATION:-Release}"

usage() {
  sed -n '4,10p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2
  exit 2
}

# A path an operator gave, resolved against the directory this script was invoked from — not against
# $root, which the `cd` above already changed to — without requiring the path to exist. `--output` and
# `script-check`'s directory travel to the CLI as-is; a relative one would otherwise resolve against this
# script's own working directory instead of the operator's.
absolute_path() {
  local path="$1"
  case "$path" in
    /*) printf '%s\n' "$path" ;;
    *) printf '%s\n' "$invocation_dir/$path" ;;
  esac
}

command="${1:-}"
case "$command" in
  pending) filter="${2:-.*}" ;;
  apply|validate)
    [[ $# -ge 2 ]] || usage
    provider="$2"
    shift 2
    # No positional connection argument (FR-050, FR-051): only its NAME travels here by default, and the
    # value itself is read from this process's own environment or its own stdin, never typed as an
    # argument. `--connection` is not accepted under any name — that shape is exactly what this converges
    # away from.
    connection_env="ELSA_EF_CONNECTION"
    connection_stdin=0
    while [[ $# -gt 0 && "$1" == --* ]]; do
      case "$1" in
        --connection-env) [[ $# -ge 2 ]] || usage; connection_env="$2"; shift 2 ;;
        --connection-stdin) connection_stdin=1; shift ;;
        --connection) echo "error: --connection is not accepted; use --connection-env or --connection-stdin." >&2; exit 2 ;;
        *) usage ;;
      esac
    done
    modules="${1:-}"
    ;;
  script)
    [[ $# -ge 3 ]] || usage
    provider="$2"
    destination="$(absolute_path "$3")"
    modules="${4:-}"
    ;;
  script-check)
    [[ $# -ge 2 ]] || usage
    destination="$(absolute_path "$2")"
    ;;
  *) usage ;;
esac

if [[ "$command" == "pending" ]]; then
  dotnet tool restore >/dev/null
  log="$(mktemp)"
  if ! dotnet build "$tooling" -c "$configuration" -v q -nologo >"$log" 2>&1; then
    grep -E " error " "$log" | sort -u >&2 || cat "$log" >&2
    exit 1
  fi
  rm -f "$log"

  ef() {
    local context="$1" project="$2"
    shift 2
    dotnet ef "$@" --context "$context" --project "$project" --startup-project "$tooling" \
      --configuration "$configuration" --no-build
  }

  failed=0
  count=0
  while IFS='|' read -r context _ assembly _; do
    [[ "$context" =~ ^($filter)$ ]] || continue
    project="$(find "${module_roots[@]}" -name "$assembly.csproj" -not -path '*/obj/*' | head -n 1)"
    count=$((count + 1))
    if ! ef "$context" "$project" migrations has-pending-model-changes >/dev/null 2>&1; then
      echo "pending model changes: $context" >&2
      failed=1
    fi
  done < <(dotnet run --project "$tooling" -c "$configuration" --no-build -- list)

  if [[ $count -eq 0 ]]; then
    echo "No module context matched." >&2
    exit 2
  fi
  if [[ $failed -ne 0 ]]; then
    exit 1
  fi
  echo "pending: $count module context(s) OK"
  exit 0
fi

# Everything below is the shim: `apply`, `validate`, `script` and `script-check` never call `dotnet ef`
# themselves any more (#1878). They build `dotnet-elsa` and this same tooling project, then run the CLI
# against the tooling project's own build output.

build_quietly() {
  local project="$1"
  local log
  log="$(mktemp)"
  if ! dotnet build "$project" -c "$configuration" -v q -nologo >"$log" 2>&1; then
    grep -E " error " "$log" | sort -u >&2 || cat "$log" >&2
    rm -f "$log"
    exit 1
  fi
  rm -f "$log"
}

# The directory beside the just-built project's one <application>.deps.json, whatever its target
# framework folder is named — so this never has to hardcode one.
build_output_dir() {
  local project_dir="$1"
  local deps
  deps="$(find "$project_dir/bin/$configuration" -mindepth 2 -maxdepth 2 -name '*.deps.json' -print -quit)"
  if [[ -z "$deps" ]]; then
    echo "error: '$project_dir' built with no *.deps.json under bin/$configuration." >&2
    exit 1
  fi
  dirname "$deps"
}

# Runs one `dotnet elsa` command. The connection, when this invocation opens a database, has already
# travelled here as --connection-env's NAME or as this process's own --connection-stdin flag, never as a
# value on this function's argument list.
dotnet_elsa() {
  build_quietly "$cli_project"
  dotnet exec "$(build_output_dir "$cli_dir")/Elsa.Cli.dll" "$@"
}

# --modules "$modules" when a filter was given, --all otherwise. An array, not a string, so a module
# list never round-trips through word-splitting.
selection=(--all)
[[ -z "${modules:-}" ]] || selection=(--modules "$modules")

case "$command" in
  apply|validate)
    build_quietly "$tooling"
    host="$(build_output_dir "$tooling_dir")"
    connection_flags=(--connection-env "$connection_env")
    [[ "$connection_stdin" -eq 0 ]] || connection_flags=(--connection-stdin)
    dotnet_elsa persistence "$command" \
      --host "$host" \
      --provider "$provider" \
      "${selection[@]}" \
      "${connection_flags[@]}"
    ;;
  script)
    build_quietly "$tooling"
    host="$(build_output_dir "$tooling_dir")"
    dotnet_elsa persistence script \
      --host "$host" \
      --provider "$provider" \
      "${selection[@]}" \
      --output "$destination"
    ;;
  script-check)
    build_quietly "$tooling"
    host="$(build_output_dir "$tooling_dir")"
    dotnet_elsa persistence script-check "$destination" --host "$host"
    ;;
esac
